# Table Planner

A browser app for seating groups of guests at long tables across one or more floors.
Groups are kept close together and near the groups they'd like to sit with. Plans can be
adjusted by hand, saved, and printed as posters and table cards.

It is a Blazor WebAssembly app that runs entirely in the browser, so it can be hosted on any
static site host (e.g. GitHub Pages). Project data stays in the browser and in exported files.

Design decisions are recorded in [DECISIONS.md](DECISIONS.md).

## Projects

| Project | Contents |
|---|---|
| `src/TablePlanner.Core` | Model, seat geometry, seat graph, validation, editing operations and undo/redo, project JSON, example project |
| `src/TablePlanner.Solver` | Seating algorithm: scoring, plan summary, search |
| `src/TablePlanner.Export` | CSV and Excel (.xlsx) import/export of groups; HTML print templates for table cards and the poster |
| `src/TablePlanner.Web` | Blazor UI (MudBlazor): projects, floor layout editor, groups editor with import/export, plan page with manual edits, locks and saved plans, print page |
| `tests/*` | xUnit tests per project |

Dependencies point one way: Web → Solver/Export → Core.

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

## Publishing to GitHub Pages

Release builds are compiled ahead of time (AOT), which makes the solver much faster in the browser.
This needs the WebAssembly build tools once, and publishing takes several minutes:

```sh
dotnet workload install wasm-tools
dotnet publish src/TablePlanner.Web -c Release -o publish
```

Add `-p:RunAOTCompilation=false` for a quick publish without AOT.

Then, in `publish/wwwroot`:

- set `<base href="/your-repo-name/">` in `index.html`
- add an empty `.nojekyll` file, so Pages serves the `_framework` folder
- copy `index.html` to `404.html`, so deep links work
