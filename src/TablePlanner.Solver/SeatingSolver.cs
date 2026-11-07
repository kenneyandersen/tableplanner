using System.Diagnostics;
using TablePlanner.Core.Model;
using TablePlanner.Core.Validation;

namespace TablePlanner.Solver;

public enum SolveMode
{
    /// <summary>Make a new plan from scratch.</summary>
    Fresh,

    /// <summary>Start from the current plan and move as few people to other tables as possible.</summary>
    KeepCloseToCurrent,
}

public enum SolveStatus
{
    /// <summary>Everyone is seated and no hard rule is broken.</summary>
    Success,

    /// <summary>A plan was made, but not everyone is seated or a hard rule is broken.</summary>
    Partial,

    /// <summary>No plan could be made; see the issues.</summary>
    Failed,
}

public enum StopReason
{
    TimeBudget,
    MaxIterations,
    NoImprovement,
    Cancelled,
    NothingToSearch,
    NotRun,
}

public sealed record SolverOptions
{
    /// <summary>Overrides <see cref="SeatingPriorities.TimeBudgetSeconds"/>.</summary>
    public TimeSpan? TimeBudget { get; init; }

    public int MaxIterations { get; init; } = 5_000_000;

    /// <summary>Stop when the best plan has not improved for this many iterations.</summary>
    public int StallIterations { get; init; } = 50_000;

    public SolveMode Mode { get; init; } = SolveMode.Fresh;

    /// <summary>Overrides <see cref="SeatingPriorities.Method"/>.</summary>
    public SolverMethod? Method { get; init; }

    /// <summary>For comparisons: with beam search, skip the order search that improves the beam's plan.</summary>
    internal bool BeamOnly { get; init; }

    /// <summary>Fixed seed for repeatable results (tests); null = random. With a seed, cooling follows <see cref="MaxIterations"/> only.</summary>
    public int? Seed { get; init; }

    /// <summary>How often the solver hands control back (to keep a single-threaded browser responsive) and reports progress.</summary>
    public TimeSpan YieldInterval { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>How the solver hands control back. Defaults to a 1 ms delay, which lets the browser render and handle clicks.</summary>
    public Func<CancellationToken, Task>? Yield { get; init; }
}

public sealed record SolveProgress(TimeSpan Elapsed, TimeSpan TimeBudget, long Iterations, double BestScore)
{
    public double Fraction => TimeBudget <= TimeSpan.Zero ? 1 : Math.Min(1, Elapsed / TimeBudget);
}

public sealed record SolveResult(
    SolveStatus Status,
    SeatingAssignment? Assignment,
    PlanSummary? Summary,
    IReadOnlyList<PlanIssue> Issues,
    long Iterations,
    TimeSpan Elapsed,
    StopReason StoppedBecause);

/// <summary>
/// Seats the groups. A plan is an order of the groups that are not pinned to a table or seats; it is turned
/// into seats by filling a fixed seat order (<see cref="SeatSequence"/>), so every group is a compact block.
/// Simulated annealing then improves the order; when it gets stuck it goes back to the best order and searches on from
/// there. Finally <see cref="PlanPolisher"/> makes direct seat swaps the order cannot express. The solver always stops
/// (time budget, iteration limit, no improvement or cancellation) and returns the best plan found so far.
/// </summary>
public static class SeatingSolver
{
    private const double StartTemperature = 40;

    /// <summary>Restarts from the best plan after <see cref="SolverOptions.StallIterations"/> without improvement, before stopping.</summary>
    private const int MaxRestarts = 4;

    /// <summary>The share of the time budget for the order search; polishing the seats gets the rest.</summary>
    private const double SearchShare = 0.85;

    /// <summary>With beam search: the share of the search time for the beam; the order search improves its plan in the rest.</summary>
    private const double BeamShare = 0.4;

    /// <summary>The start temperature of the order search after a beam search.</summary>
    internal const double AfterBeamTemperature = 3;

    public static async Task<SolveResult> SolveAsync(EventProject project, SolverOptions? options = null,
        IProgress<SolveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        options ??= new SolverOptions();
        var clock = Stopwatch.StartNew();
        var budget = options.TimeBudget ?? TimeSpan.FromSeconds(Math.Max(0.1, project.Priorities.TimeBudgetSeconds));
        // The order search gets most of the time; polishing the seats (PlanPolisher) gets the rest.
        var searchBudget = budget * SearchShare;

        var failure = PreCheck(project);
        if (failure.Count > 0)
            return new SolveResult(SolveStatus.Failed, null, null, failure, 0, clock.Elapsed, StopReason.NotRun);

        var problem = SeatingProblem.Create(project.Setup, project.Priorities);
        var evaluator = new PlanEvaluator(problem);
        var random = options.Seed is { } seed ? new Random(seed) : new Random();

        var fixedPlan = PlacePinned(problem);
        var sequence = new SeatSequence(problem, fixedPlan);
        var free = Enumerable.Range(0, problem.Groups.Count)
            .Where(g => problem.SeatPin[g] is null && problem.TablePin[g] < 0)
            .ToArray();

        int[] current;
        if (options.Mode == SolveMode.KeepCloseToCurrent)
        {
            var previous = problem.ToPlan(project.CurrentAssignment);
            evaluator.SetPreviousPlan(previous);
            current = OrderFromPlan(sequence, previous, free, problem);
        }
        else
            current = InitialOrder(problem, free);

        var plan = problem.EmptyPlan();
        sequence.Decode(current, plan);
        var currentScore = evaluator.Score(plan);
        var best = (int[])current.Clone();
        var bestScore = currentScore;

        // Also try the plain "largest first" order; keep whichever start is better.
        if (options.Mode == SolveMode.Fresh)
        {
            var bySize = free.OrderByDescending(g => problem.Size[g]).ThenBy(g => g).ToArray();
            sequence.Decode(bySize, plan);
            var score = evaluator.Score(plan);
            if (score > currentScore)
                (current, currentScore, best, bestScore) = (bySize, score, (int[])bySize.Clone(), score);
        }

        var candidate = new int[current.Length];
        long iterations = 0;
        long lastImprovement = 0;
        var restarts = 0;
        var stop = free.Length < 2 ? StopReason.NothingToSearch : (StopReason?)null;
        var lastYield = clock.Elapsed;
        var yield = options.Yield ?? (ct => Task.Delay(1, ct));
        var startTemperature = StartTemperature;
        var temperature = startTemperature;
        // Where the order search starts (after a beam search, if any), for its cooling.
        var (searchFrom, iterationsFrom) = (TimeSpan.Zero, 0L);

        // Hands control back now and then (and reports progress); swallows cancellation, which the caller checks.
        async Task Pause()
        {
            if (clock.Elapsed - lastYield < options.YieldInterval)
                return;
            progress?.Report(new SolveProgress(clock.Elapsed, budget, iterations, bestScore));
            try
            {
                await yield(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            lastYield = clock.Elapsed;
        }

        // Beam search first: beams of growing width while its share of the time allows (fixed widths with a seed, so
        // results repeat), keeping the best plan. The order search then goes on from that plan, starting cooler.
        int[]? beamPlan = null;
        var beamBudget = !options.BeamOnly ? searchBudget * BeamShare : searchBudget;
        if (options.Mode == SolveMode.Fresh && (options.Method ?? project.Priorities.Method) == SolverMethod.Beam && free.Length > 0)
        {
            var beam = new BeamSearch(problem, sequence, evaluator, free, random);
            int[] widths = options.Seed is null ? [4, 8, 16, 32, 64, 128, 256, 512, 1024] : [4, 16, 64];
            stop = StopReason.MaxIterations;
            foreach (var width in widths)
            {
                var started = clock.Elapsed;
                var result = await beam.RunAsync(width,
                    () =>
                    {
                        iterations = beam.Evaluations;
                        // The first beam always finishes (unless cancelled), so there is a plan.
                        return cancellationToken.IsCancellationRequested || (beamPlan is not null && options.Seed is null && clock.Elapsed >= beamBudget);
                    },
                    Pause);
                iterations = beam.Evaluations;
                if (result is null)
                {
                    stop = cancellationToken.IsCancellationRequested ? StopReason.Cancelled : StopReason.TimeBudget;
                    break;
                }
                var score = evaluator.Score(result);
                if (beamPlan is null || score > bestScore)
                    (beamPlan, bestScore) = (result, score);
                // A beam twice as wide takes about twice as long; don't start one that cannot finish.
                if (options.Seed is null && clock.Elapsed + 2.2 * (clock.Elapsed - started) > beamBudget)
                {
                    stop = StopReason.TimeBudget;
                    break;
                }
            }
            if (cancellationToken.IsCancellationRequested)
                stop = StopReason.Cancelled;
            else if (!options.BeamOnly && beamPlan is not null)
            {
                // Then improve it with the order search, from the order the beam seated the groups in.
                current = OrderFromPlan(sequence, beamPlan, free, problem);
                sequence.Decode(current, plan);
                currentScore = evaluator.Score(plan);
                Array.Copy(current, best, current.Length);
                bestScore = currentScore;
                stop = free.Length < 2 ? StopReason.NothingToSearch : null;
                lastImprovement = iterations;
                // Cooler, so it refines the beam's plan rather than shuffling it; cooling over the time that is left.
                (startTemperature, temperature) = (AfterBeamTemperature, AfterBeamTemperature);
                (searchFrom, iterationsFrom) = (clock.Elapsed, iterations);
            }
        }

        while (stop is null)
        {
            iterations++;
            Array.Copy(current, candidate, current.Length);
            Mutate(candidate, problem, random);
            sequence.Decode(candidate, plan);
            var score = evaluator.Score(plan);

            if (score >= currentScore || (temperature > 1e-3 && random.NextDouble() < Math.Exp((score - currentScore) / temperature)))
            {
                (current, candidate) = (candidate, current);
                currentScore = score;
                if (score > bestScore + 1e-9)
                {
                    Array.Copy(current, best, current.Length);
                    bestScore = score;
                    lastImprovement = iterations;
                    restarts = 0;
                }
            }

            if ((iterations & 63) != 0)
                continue;

            // Reading the clock is slow in the browser, so it is only done every 64 iterations.
            // Cooling follows the time budget, or the iteration limit if that comes first. With a seed it follows only
            // the iterations, so the same seed gives the same plan on any machine.
            var byIterations = (double)(iterations - iterationsFrom) / Math.Max(1, options.MaxIterations - iterationsFrom);
            var progressFraction = options.Seed is null ? Math.Max((clock.Elapsed - searchFrom) / (searchBudget - searchFrom), byIterations) : byIterations;
            temperature = startTemperature * Math.Pow(Math.Max(0, 1 - progressFraction), 2);
            if (cancellationToken.IsCancellationRequested)
                stop = StopReason.Cancelled;
            else if (clock.Elapsed >= searchBudget)
                stop = StopReason.TimeBudget;
            else if (iterations >= options.MaxIterations)
                stop = StopReason.MaxIterations;
            else if (iterations - lastImprovement >= options.StallIterations)
            {
                // Stuck: go back to the best plan and search on from there. Give up after a few restarts that find nothing better.
                if (++restarts > MaxRestarts)
                    stop = StopReason.NoImprovement;
                Array.Copy(best, current, best.Length);
                currentScore = bestScore;
                lastImprovement = iterations;
            }
            else if (clock.Elapsed - lastYield >= options.YieldInterval)
            {
                progress?.Report(new SolveProgress(clock.Elapsed, budget, iterations, bestScore));
                try
                {
                    await yield(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    stop = StopReason.Cancelled;
                }
                lastYield = clock.Elapsed;
            }
        }

        sequence.Decode(best, plan);
        // The beam's own plan, unless the order search made a better one from it.
        if (beamPlan is not null && evaluator.Score(beamPlan) >= evaluator.Score(plan))
            Array.Copy(beamPlan, plan, plan.Length);
        if (stop != StopReason.Cancelled)
        {
            var polisher = new PlanPolisher(problem, sequence, evaluator);
            await polisher.PolishAsync(plan,
                () => cancellationToken.IsCancellationRequested || (options.Seed is null && clock.Elapsed >= budget),
                Pause);
            if (cancellationToken.IsCancellationRequested)
                stop = StopReason.Cancelled;
        }
        var summary = evaluator.Summarize(plan);
        progress?.Report(new SolveProgress(clock.Elapsed, budget, iterations, summary.Score));

        var status = summary.PeoplePlaced < summary.PeopleTotal || summary.Groups.Any(g => g.BreaksHardRule)
            ? SolveStatus.Partial
            : SolveStatus.Success;
        return new SolveResult(status, problem.ToAssignment(plan), summary, summary.Issues, iterations, clock.Elapsed, stop.Value);
    }

    /// <summary>Problems that make solving pointless: setup errors, no seats, no groups, a group too big for any floor while that is a hard rule.</summary>
    private static List<PlanIssue> PreCheck(EventProject project)
    {
        var setup = project.Setup;
        var issues = SetupValidator.Validate(setup).Errors.Select(e => new PlanIssue(IssueSeverity.Error, e.Message)).ToList();
        if (issues.Count > 0)
        {
            issues.Insert(0, new PlanIssue(IssueSeverity.Error, "Fix the errors in the setup first."));
            return issues;
        }
        if (setup.AllTables.Sum(t => t.AvailableSeatCount) == 0)
            issues.Add(new PlanIssue(IssueSeverity.Error, "There are no seats. Add tables first."));
        if (setup.Groups.Count == 0)
            issues.Add(new PlanIssue(IssueSeverity.Error, "There are no groups to seat. Add groups first."));
        if (project.Priorities.NeverSplitAcrossFloors && setup.Floors.Count > 0)
        {
            var largestFloor = setup.Floors.Max(f => f.Tables.Sum(t => t.AvailableSeatCount));
            foreach (var group in setup.Groups.Where(g => g.PeopleCount > largestFloor))
                issues.Add(new PlanIssue(IssueSeverity.Error,
                    $"'{group.Name}' ({group.PeopleCount}) is larger than any floor ({largestFloor} seats), but groups may never be split across floors.", group.Id));
        }
        return issues;
    }

    /// <summary>Seats groups pinned to seats, then groups pinned to a table (in the table's fill order), and returns that fixed part of the plan.</summary>
    private static int[] PlacePinned(SeatingProblem problem)
    {
        var plan = problem.EmptyPlan();
        for (var g = 0; g < problem.Groups.Count; g++)
            if (problem.SeatPin[g] is { } seats)
                foreach (var s in seats)
                    if (plan[s] < 0)
                        plan[s] = g;

        var fillOrder = SeatSequence.FillOrder(problem.Graph);
        for (var g = 0; g < problem.Groups.Count; g++)
        {
            if (problem.TablePin[g] is not (var t and >= 0))
                continue;
            var left = problem.Size[g];
            foreach (var s in fillOrder)
            {
                if (left == 0)
                    break;
                if (problem.Graph.Seats[s].TableIndex == t && plan[s] < 0)
                {
                    plan[s] = g;
                    left--;
                }
            }
        }
        return plan;
    }

    /// <summary>
    /// Largest group first, then chains of preferences: after each group come the groups it prefers or that
    /// prefer it (strongest first), so they end up next to each other in the seat order.
    /// </summary>
    private static int[] InitialOrder(SeatingProblem problem, int[] free)
    {
        var left = free.ToHashSet();
        var order = new List<int>(free.Length);
        var weights = problem.Priorities.PreferenceRankWeights;
        double Affinity(int a, int b)
        {
            var w = 0.0;
            var ra = Array.IndexOf(problem.Preferences[a], b);
            var rb = Array.IndexOf(problem.Preferences[b], a);
            if (ra >= 0 && ra < weights.Count)
                w += weights[ra];
            if (rb >= 0 && rb < weights.Count)
                w += weights[rb];
            return w;
        }

        while (left.Count > 0)
        {
            var last = left.OrderByDescending(g => problem.Size[g]).ThenBy(g => g).First();
            while (true)
            {
                order.Add(last);
                left.Remove(last);
                var next = left.Select(g => (g, w: Affinity(last, g))).Where(x => x.w > 0)
                    .OrderByDescending(x => x.w).ThenByDescending(x => problem.Size[x.g]).ThenBy(x => x.g)
                    .Select(x => (int?)x.g).FirstOrDefault();
                if (next is not { } n)
                    break;
                last = n;
            }
        }
        return order.ToArray();
    }

    /// <summary>The groups in the order their seats come in the current plan; groups without seats go last, largest first.</summary>
    private static int[] OrderFromPlan(SeatSequence sequence, int[] plan, int[] free, SeatingProblem problem)
    {
        var position = new Dictionary<int, int>();
        for (var i = 0; i < sequence.Length; i++)
            if (plan[sequence.Seats[i]] is var g and >= 0)
                position.TryAdd(g, i);
        return free
            .OrderBy(g => position.TryGetValue(g, out var p) ? p : int.MaxValue)
            .ThenByDescending(g => problem.Size[g])
            .ThenBy(g => g)
            .ToArray();
    }

    /// <summary>One random change to the order: swap two groups, move one, move one next to a group it prefers, or move or reverse a short run.</summary>
    private static void Mutate(int[] order, SeatingProblem problem, Random random)
    {
        var n = order.Length;
        switch (random.Next(6))
        {
            case 0:
            case 1:
                {
                    var i = random.Next(n);
                    var j = random.Next(n);
                    (order[i], order[j]) = (order[j], order[i]);
                    break;
                }
            case 2:
                Move(order, random.Next(n), random.Next(n));
                break;
            case 3:
                {
                    // Move a group next to one of its preferred groups (or a group that prefers it).
                    var i = random.Next(n);
                    var g = order[i];
                    var prefs = problem.Preferences[g];
                    if (prefs.Length == 0)
                        goto case 2;
                    var target = Array.IndexOf(order, prefs[random.Next(prefs.Length)]);
                    if (target < 0)
                        goto case 2;
                    Move(order, i, target < i ? target + 1 : target);
                    break;
                }
            case 4:
                {
                    // Move a run of 2–4 groups elsewhere: rotating the stretch between the run and its target.
                    var length = Math.Min(n, 2 + random.Next(3));
                    var from = random.Next(n - length + 1);
                    var to = random.Next(n - length + 1);
                    if (to < from)
                        Rotate(order, to, from + length, from - to);
                    else if (to > from)
                        Rotate(order, from, to + length, length);
                    break;
                }
            default:
                {
                    var i = random.Next(n);
                    var j = Math.Min(n - 1, i + 1 + random.Next(4));
                    Array.Reverse(order, i, j - i + 1);
                    break;
                }
        }
    }

    /// <summary>Rotates order[start..end) left by <paramref name="shift"/> places.</summary>
    private static void Rotate(int[] order, int start, int end, int shift)
    {
        Array.Reverse(order, start, shift);
        Array.Reverse(order, start + shift, end - start - shift);
        Array.Reverse(order, start, end - start);
    }

    /// <summary>Takes the element at <paramref name="from"/> out and inserts it so it ends up at <paramref name="to"/>.</summary>
    private static void Move(int[] order, int from, int to)
    {
        if (from == to)
            return;
        var item = order[from];
        if (from < to)
            Array.Copy(order, from + 1, order, from, to - from);
        else
            Array.Copy(order, to, order, to + 1, from - to);
        order[to] = item;
    }
}
