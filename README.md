# Table Planner

A browser app for seating groups of guests at long tables across one or more floors.
Groups are kept close together and near the groups they'd like to sit with. Plans can be
adjusted by hand, saved, and printed as posters and table cards.

It is a Blazor WebAssembly app that runs entirely in the browser, so it can be hosted on any
static site host (e.g. GitHub Pages). Project data stays in the browser and in exported files.

Design decisions are recorded in [DECISIONS.md](DECISIONS.md).

## What it does

- **Layout:** floors with long tables (0–2 end seats) and round tables, floor elements (bar, stage, …) in layers, and
  a background image of the venue, with move, resize, rotate, snap to grid and undo.
- **Groups:** a groups editor with quick add and up to 5 ranked preferences per group; CSV and Excel import/export.
- **Plan:** the solver seats groups close together and near the groups they prefer, without leaving small parts of
  a group alone at a table; two search methods (search, beam search); priority presets; re-optimizing with few changes; manual moves and swaps (also by drag
  and drop, from the plan or the group list), locks and pins, saved plans.
- **Print & share:** table cards (per group at a table or per seat, flat or tent cards, A4 or A5), a poster with floor
  plans and a "find your table" list, and a guest page for phones (search or explore the floor plan) with a QR code.
  All are HTML templates: built-in ones, named copies, or table cards drawn in the card designer.

## Projects

| Project | Contents |
|---|---|
| `src/TablePlanner.Core` | Model (incl. floor background settings), seat geometry, seat graph, validation, editing operations and undo/redo, project JSON, example project |
| `src/TablePlanner.Solver` | Seating algorithm: scoring, plan summary, order search, beam search, polishing |
| `src/TablePlanner.Export` | CSV and Excel (.xlsx) import/export of groups; HTML templates for table cards, the poster and the guest page |
| `src/TablePlanner.Web` | Blazor UI (MudBlazor): projects, floor layout editor with background images, groups editor with import/export, plan page with manual edits, locks and saved plans, print and share page (cards, poster, guest page), card designer |
| `tests/*` | xUnit tests per project |

Dependencies point one way: Web → Solver/Export → Core. Besides MudBlazor, the only package is QRCoder (MIT) for QR codes.

## Build, test, run

Requires the .NET 10 SDK.

```sh
dotnet build
dotnet test
dotnet run --project src/TablePlanner.Web
```

Solver diagnostics for the benchmark setups (scores, preference fulfilment, iterations per second):

```sh
dotnet test tests/TablePlanner.Solver.Tests -c Release --filter SolverDiagnostics --logger "console;verbosity=detailed"
```

and how many small parts (1–4 people of a group alone at a table), or people away from the rest of their group,
the solver leaves, over several seeds:

```sh
dotnet test tests/TablePlanner.Solver.Tests -c Release --filter FragmentDiagnostics --logger "console;verbosity=detailed"
dotnet test tests/TablePlanner.Solver.Tests -c Release --filter ApartDiagnostics --logger "console;verbosity=detailed"
```

and for real projects: put an exported `*.tableplanner.json` anywhere under `tests/testplans` (five generated ones
are there too, made by `TestPlanGenerator`) and run the comparison of both search methods (a few minutes):

```powershell
$env:TABLEPLANNER_DIAGNOSTICS=1; dotnet test tests/TablePlanner.Solver.Tests -c Release --filter TestPlanDiagnostics --logger "console;verbosity=detailed"
```

Without the variable, normal test runs only check that every test plan gets everyone seated with no one away from their
group. To regenerate the generated plans: `$env:TABLEPLANNER_GENERATE=1; dotnet test tests/TablePlanner.Solver.Tests --filter TestPlanGenerator`.

## Publishing to GitHub Pages

Release builds are compiled ahead of time (AOT), which makes the solver much faster in the browser.
This needs the WebAssembly build tools once, and publishing takes several minutes:

```sh
dotnet workload install wasm-tools
```

Then publish with the script, giving the path the site is served under (`/<repo-name>/` for a GitHub project
site, `/` for a custom domain) and, optionally, a checkout of the `gh-pages` branch to copy the result into:

```powershell
./publish.ps1 -BasePath /TableAssigner/ -Target C:\git\tableplanner-ghpages
```

Add `-NoAot` for a quick publish without AOT. The script sets `<base href>` (with its trailing slash), adds
`.nojekyll` (so Pages serves `_framework`), `.gitattributes` (so git does not change line endings, which breaks
Blazor's integrity checks) and `404.html` (so deep links work). In the target it replaces everything except `.git`,
`pages/` and `CNAME`. It does not commit or push.
