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

    /// <summary>
    /// Independent attempts within the time budget, keeping the best. Null: <see cref="SeatingSolver.DefaultAttempts"/>
    /// for a new plan without a seed, otherwise 1. An attempt that is still improving when its time runs out makes the
    /// rest of the time go to one longer attempt.
    /// </summary>
    public int? Attempts { get; init; }

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
    StopReason StoppedBecause)
{
    /// <summary>Whether this was a new plan or re-optimizing.</summary>
    public SolveMode Mode { get; init; }

    /// <summary>How many attempts were made; the plan is the best of them.</summary>
    public int Attempts { get; init; } = 1;

    /// <summary>The run stopped before its time limit: searching on found nothing better (see SolveAsync).</summary>
    public bool StoppedEarly { get; init; }
}

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

    /// <summary>Attempts by default when making a new plan (<see cref="SolverOptions.Attempts"/>).</summary>
    public const int DefaultAttempts = 4;

    /// <summary>With a long time limit (over <see cref="LongBudgetSeconds"/>), up to this many attempts, so a run can stop early.</summary>
    private const int MaxAttempts = 12;

    private const double LongBudgetSeconds = 10;

    /// <summary>When choosing between attempts, plans within this share of the best score count as equally good, and the one with fewer warnings wins.</summary>
    internal const double CloseScoreShare = 0.01;

    /// <summary>An attempt whose best plan was found after this share of its search time was still improving when time ran out.</summary>
    private const double StillImprovingShare = 0.7;

    public static async Task<SolveResult> SolveAsync(EventProject project, SolverOptions? options = null,
        IProgress<SolveProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        options ??= new SolverOptions();
        var clock = Stopwatch.StartNew();
        var budget = options.TimeBudget ?? TimeSpan.FromSeconds(Math.Max(0.1, project.Priorities.TimeBudgetSeconds));

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
        if (options.Mode == SolveMode.KeepCloseToCurrent)
            evaluator.SetPreviousPlan(problem.ToPlan(project.CurrentAssignment));

        var run = new Run(project, problem, evaluator, sequence, free, options, clock, budget, progress, cancellationToken);

        // Several independent attempts, keeping the best: runs of the same search differ a lot, and a fresh start often
        // beats searching longer. Re-optimizing and seeded runs (tests) make one attempt, unless told otherwise.
        var attemptsLeft = options.Attempts
            ?? (options.Mode == SolveMode.Fresh && options.Seed is null ? AffordableAttempts(problem, evaluator, sequence, free, budget, clock) : 1);
        // Stopping early (a new plan, by time): when the best plan has no warnings and the last two attempts did not beat
        // it, more attempts would most likely only repeat it. Each attempt still gets its full share of the time.
        var mayStopEarly = options.Mode == SolveMode.Fresh && options.Seed is null && options.Attempts is null;
        var stoppedEarly = false;
        var notBetter = 0;
        var results = new List<Attempt>();
        while (attemptsLeft > 0)
        {
            var start = clock.Elapsed;
            if (results.Count > 0 && (start >= budget || cancellationToken.IsCancellationRequested))
                break;
            var end = start + (budget - start) / attemptsLeft;
            var bestBefore = results.Count > 0 ? Choose(results).Score : double.NegativeInfinity;
            var attempt = await run.AttemptAsync(start, end, random);
            results.Add(attempt);
            attemptsLeft--;
            if (attempt.Stop == StopReason.Cancelled)
                break;
            // Still improving when its time ran out (a large plan or a slow browser): the rest goes to one long attempt.
            if (attempt.StillImproving)
                attemptsLeft = Math.Min(attemptsLeft, 1);
            notBetter = results.Count > 1 && attempt.Score <= bestBefore + 1e-6 ? notBetter + 1 : 0;
            if (mayStopEarly && attemptsLeft > 0 && notBetter >= 2 && Choose(results).Warnings == 0)
            {
                stoppedEarly = true;
                break;
            }
        }

        // Also when the attempts ended on their own before the time limit (none of them found anything better for long).
        stoppedEarly |= mayStopEarly && !cancellationToken.IsCancellationRequested && clock.Elapsed < budget * 0.9;
        var chosen = Choose(results);
        var summary = evaluator.Summarize(chosen.Plan);
        progress?.Report(new SolveProgress(clock.Elapsed, budget, run.Iterations, summary.Score));

        var status = summary.PeoplePlaced < summary.PeopleTotal || summary.Groups.Any(g => g.BreaksHardRule)
            ? SolveStatus.Partial
            : SolveStatus.Success;
        var stop = results.Any(r => r.Stop == StopReason.Cancelled) ? StopReason.Cancelled : results[^1].Stop;
        return new SolveResult(status, problem.ToAssignment(chosen.Plan), summary, summary.Issues, run.Iterations, clock.Elapsed, stop)
        {
            Attempts = results.Count,
            StoppedEarly = stoppedEarly,
            Mode = options.Mode,
        };
    }

    /// <summary>Plans an attempt should be able to try per group to search the order well (from the test plans).</summary>
    internal const int IterationsPerGroup = 1000;

    /// <summary>
    /// How many attempts fit in the budget: plans are tried for a moment to see how fast this machine is (a browser is
    /// much slower than a test run), and each attempt should get about <see cref="IterationsPerGroup"/> per group.
    /// Between 1 and <see cref="DefaultAttempts"/>.
    /// </summary>
    private static int AffordableAttempts(SeatingProblem problem, PlanEvaluator evaluator, SeatSequence sequence, int[] free,
        TimeSpan budget, Stopwatch clock)
    {
        if (free.Length < 2)
            return 1;
        var order = (int[])free.Clone();
        var plan = problem.EmptyPlan();
        var random = new Random(1);
        var started = clock.Elapsed;
        const int Probe = 200;
        for (var i = 0; i < Probe; i++)
        {
            Mutate(order, problem, random);
            sequence.Decode(order, plan);
            evaluator.Score(plan);
        }
        var perSecond = Probe / Math.Max(1e-4, (clock.Elapsed - started).TotalSeconds);
        var affordable = perSecond * (budget - clock.Elapsed).TotalSeconds * SearchShare / (IterationsPerGroup * (double)free.Length);
        // A long time limit is better spent on more attempts than on longer ones (and lets the run stop early).
        var most = budget.TotalSeconds > LongBudgetSeconds ? MaxAttempts : DefaultAttempts;
        return (int)Math.Clamp(Math.Floor(affordable), 1, most);
    }

    /// <summary>The best attempt: the highest score, except that of plans within <see cref="CloseScoreShare"/> of it the one with fewest warnings wins.</summary>
    internal static Attempt Choose(IReadOnlyList<Attempt> results)
    {
        var best = results.MaxBy(r => r.Score)!;
        var margin = Math.Abs(best.Score) * CloseScoreShare;
        return results.Where(r => r.Score >= best.Score - margin)
            .OrderBy(r => r.Warnings)
            .ThenByDescending(r => r.Score)
            .First();
    }

    /// <summary>The outcome of one attempt.</summary>
    internal sealed record Attempt(int[] Plan, double Score, int Warnings, StopReason Stop, bool StillImproving);

    /// <summary>What the attempts of one solve share: the problem, the clock, progress reports and the iteration count.</summary>
    private sealed class Run(EventProject project, SeatingProblem problem, PlanEvaluator evaluator, SeatSequence sequence, int[] free,
        SolverOptions options, Stopwatch clock, TimeSpan budget, IProgress<SolveProgress>? progress, CancellationToken cancellationToken)
    {
        private readonly Func<CancellationToken, Task> _yield = options.Yield ?? (ct => Task.Delay(1, ct));
        private TimeSpan _lastYield;

        /// <summary>Plans tried over all attempts.</summary>
        public long Iterations;

        /// <summary>The best score over all attempts, for progress reports.</summary>
        private double _bestScore = double.NegativeInfinity;

        /// <summary>Hands control back now and then (and reports progress); swallows cancellation, which the caller checks.</summary>
        private async Task Pause()
        {
            if (clock.Elapsed - _lastYield < options.YieldInterval)
                return;
            progress?.Report(new SolveProgress(clock.Elapsed, budget, Iterations, _bestScore));
            try
            {
                await _yield(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            _lastYield = clock.Elapsed;
        }

        /// <summary>
        /// One attempt between <paramref name="attemptStart"/> and <paramref name="attemptEnd"/> on the clock: the order
        /// search (after a beam search, if that is the method), then polishing.
        /// </summary>
        public async Task<Attempt> AttemptAsync(TimeSpan attemptStart, TimeSpan attemptEnd, Random random)
        {
            // "By levels": the order changes one level at a time, keeping every parent group in one run (LevelOrder).
            // Without levels (no parent groups) it is the same as the order search.
            var levels = options.Mode == SolveMode.Fresh && (options.Method ?? project.Priorities.Method) == SolverMethod.Levels
                && problem.ParentGroups.Count > 0 ? new LevelOrder(problem, free) : null;
            sequence.BalanceSplits = levels is not null;
            // The order search gets most of the time; polishing the seats (PlanPolisher) gets the rest.
            var searchEnd = attemptStart + (attemptEnd - attemptStart) * SearchShare;
            var iterationsAtStart = Iterations;

            int[] current;
            if (options.Mode == SolveMode.KeepCloseToCurrent)
                current = OrderFromPlan(sequence, problem.ToPlan(project.CurrentAssignment), free, problem);
            else
                // Every attempt starts from the same good order; the random search soon takes them different ways.
                current = levels?.Initial(free) ?? InitialOrder(problem, free);

            var plan = problem.EmptyPlan();
            sequence.Decode(current, plan);
            var currentScore = evaluator.Score(plan);
            var best = (int[])current.Clone();
            var bestScore = currentScore;
            var bestFoundAt = clock.Elapsed;

            // Also try the plain "largest first" order; keep whichever start is better.
            if (options.Mode == SolveMode.Fresh && levels is null)
            {
                var bySize = free.OrderByDescending(g => problem.Size[g]).ThenBy(g => g).ToArray();
                sequence.Decode(bySize, plan);
                var score = evaluator.Score(plan);
                if (score > currentScore)
                    (current, currentScore, best, bestScore) = (bySize, score, (int[])bySize.Clone(), score);
            }
            _bestScore = Math.Max(_bestScore, bestScore);

            var candidate = new int[current.Length];
            long lastImprovement = Iterations;
            var restarts = 0;
            var stop = free.Length < 2 ? StopReason.NothingToSearch : (StopReason?)null;
            var startTemperature = StartTemperature;
            var temperature = startTemperature;
            // Where the order search starts (after a beam search, if any), for its cooling.
            var (searchFrom, iterationsFrom) = (attemptStart, iterationsAtStart);

            // Beam search first: beams of growing width while its share of the time allows (fixed widths with a seed, so
            // results repeat), keeping the best plan. The order search then goes on from that plan, starting cooler.
            int[]? beamPlan = null;
            var beamEnd = options.BeamOnly ? searchEnd : attemptStart + (searchEnd - attemptStart) * BeamShare;
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
                            Iterations = iterationsAtStart + beam.Evaluations;
                            // The first beam always finishes (unless cancelled), so there is a plan.
                            return cancellationToken.IsCancellationRequested || (beamPlan is not null && options.Seed is null && clock.Elapsed >= beamEnd);
                        },
                        Pause);
                    Iterations = iterationsAtStart + beam.Evaluations;
                    if (result is null)
                    {
                        stop = cancellationToken.IsCancellationRequested ? StopReason.Cancelled : StopReason.TimeBudget;
                        break;
                    }
                    var score = evaluator.Score(result);
                    if (beamPlan is null || score > bestScore)
                        (beamPlan, bestScore, bestFoundAt) = (result, score, clock.Elapsed);
                    _bestScore = Math.Max(_bestScore, bestScore);
                    // A beam twice as wide takes about twice as long; don't start one that cannot finish.
                    if (options.Seed is null && clock.Elapsed + 2.2 * (clock.Elapsed - started) > beamEnd)
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
                    lastImprovement = Iterations;
                    // Cooler, so it refines the beam's plan rather than shuffling it; cooling over the time that is left.
                    (startTemperature, temperature) = (AfterBeamTemperature, AfterBeamTemperature);
                    (searchFrom, iterationsFrom) = (clock.Elapsed, Iterations);
                }
            }

            while (stop is null)
            {
                Iterations++;
                Array.Copy(current, candidate, current.Length);
                if (levels is not null)
                    levels.Mutate(candidate, random);
                else
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
                        lastImprovement = Iterations;
                        restarts = 0;
                        bestFoundAt = clock.Elapsed;
                        _bestScore = Math.Max(_bestScore, bestScore);
                    }
                }

                if ((Iterations & 63) != 0)
                    continue;

                // Reading the clock is slow in the browser, so it is only done every 64 iterations.
                // Cooling follows the time budget, or the iteration limit if that comes first. With a seed it follows only
                // the iterations, so the same seed gives the same plan on any machine.
                var maxIterations = iterationsAtStart + options.MaxIterations;
                var byIterations = (double)(Iterations - iterationsFrom) / Math.Max(1, maxIterations - iterationsFrom);
                var progressFraction = options.Seed is null ? Math.Max((clock.Elapsed - searchFrom) / (searchEnd - searchFrom), byIterations) : byIterations;
                temperature = startTemperature * Math.Pow(Math.Max(0, 1 - progressFraction), 2);
                if (cancellationToken.IsCancellationRequested)
                    stop = StopReason.Cancelled;
                else if (clock.Elapsed >= searchEnd)
                    stop = StopReason.TimeBudget;
                else if (Iterations >= maxIterations)
                    stop = StopReason.MaxIterations;
                else if (Iterations - lastImprovement >= options.StallIterations)
                {
                    // Stuck: go back to the best plan and search on from there. Give up after a few restarts that find nothing better.
                    if (++restarts > MaxRestarts)
                        stop = StopReason.NoImprovement;
                    Array.Copy(best, current, best.Length);
                    currentScore = bestScore;
                    lastImprovement = Iterations;
                }
                else if (clock.Elapsed - _lastYield >= options.YieldInterval)
                {
                    progress?.Report(new SolveProgress(clock.Elapsed, budget, Iterations, _bestScore));
                    try
                    {
                        await _yield(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        stop = StopReason.Cancelled;
                    }
                    _lastYield = clock.Elapsed;
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
                    () => cancellationToken.IsCancellationRequested || (options.Seed is null && clock.Elapsed >= attemptEnd),
                    Pause);
                if (cancellationToken.IsCancellationRequested)
                    stop = StopReason.Cancelled;
            }

            var finalScore = evaluator.Score(plan);
            _bestScore = Math.Max(_bestScore, finalScore);
            var stillImproving = stop == StopReason.TimeBudget && bestFoundAt - attemptStart > (searchEnd - attemptStart) * StillImprovingShare;
            return new Attempt(plan, finalScore, evaluator.Summarize(plan).Issues.Count, stop!.Value, stillImproving);
        }
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
        if (problem.ParentGroups.Count > 0)
            return TreeOrder(problem, free);
        var left = free.ToHashSet();
        var order = new List<int>(free.Length);
        var weights = problem.Priorities.PreferenceRankWeights;
        double Affinity(int a, int b)
        {
            var w = 0.0;
            var ia = Array.IndexOf(problem.Preferences[a], b);
            var ib = Array.IndexOf(problem.Preferences[b], a);
            var ra = ia >= 0 ? problem.PreferenceRanks[a][ia] : -1;
            var rb = ib >= 0 ? problem.PreferenceRanks[b][ib] : -1;
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

    /// <summary>
    /// With levels: the groups department by department (largest first), and within each its teams and their groups,
    /// so every parent group's groups are next to each other in the seat order.
    /// </summary>
    private static int[] TreeOrder(SeatingProblem problem, int[] free)
    {
        var setup = problem.Setup;
        var freeSet = free.ToHashSet();
        var order = new List<int>(free.Length);
        int People(Group group) => setup.BottomGroupsOf(group)
            .Sum(b => problem.IndexOfGroup(b.Id) is { } i && freeSet.Contains(i) ? problem.Size[i] : 0);
        void Visit(IEnumerable<Group> siblings)
        {
            foreach (var group in siblings.OrderByDescending(People).ThenBy(g => setup.Groups.IndexOf(g)).ToList())
                if (setup.HasChildren(group.Id))
                    Visit(setup.ChildrenOf(group.Id));
                else if (problem.IndexOfGroup(group.Id) is { } i && freeSet.Contains(i) && !order.Contains(i))
                    order.Add(i);
        }
        Visit(setup.Groups.Where(g => g.ParentId is not { } p || setup.FindGroup(p) is null));
        order.AddRange(free.Where(g => !order.Contains(g)));
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
        // With levels, two more moves keep parent groups together; without, the same moves (and random draws) as always.
        switch (random.Next(problem.ParentGroups.Count > 0 ? 8 : 6))
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
                    // Move a group next to one of its preferred groups (or a sibling or a group of a preferred parent group).
                    var i = random.Next(n);
                    var g = order[i];
                    var prefs = problem.MoveTargets[g];
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
            case 5:
                {
                    var i = random.Next(n);
                    var j = Math.Min(n - 1, i + 1 + random.Next(4));
                    Array.Reverse(order, i, j - i + 1);
                    break;
                }
            case 6:
                // Gather a parent group's groups into one block where the first of them is.
                MoveParentBlock(order, problem, random, relocate: false);
                break;
            default:
                // Move a parent group's groups, as one block, elsewhere.
                MoveParentBlock(order, problem, random, relocate: true);
                break;
        }
    }

    /// <summary>
    /// Takes the groups of a random parent group out of the order and puts them back as one block (in their order):
    /// where the first of them was, or with <paramref name="relocate"/> at a random place.
    /// </summary>
    private static void MoveParentBlock(int[] order, SeatingProblem problem, Random random, bool relocate)
    {
        var members = problem.ParentMembers[random.Next(problem.ParentGroups.Count)];
        var block = new List<int>(members.Length);
        var rest = new List<int>(order.Length);
        var insertAt = -1;
        foreach (var g in order)
        {
            if (Array.IndexOf(members, g) >= 0)
            {
                if (insertAt < 0)
                    insertAt = rest.Count;
                block.Add(g);
            }
            else
                rest.Add(g);
        }
        if (block.Count == 0)
            return;
        if (relocate)
            insertAt = random.Next(rest.Count + 1);
        rest.InsertRange(insertAt, block);
        rest.CopyTo(order);
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
