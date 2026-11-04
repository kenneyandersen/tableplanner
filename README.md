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
| `src/TablePlanner.Core` | Model, seat geometry, validation, editing operations and undo/redo, project JSON, example project |
| `src/TablePlanner.Solver` | Seating algorithm (not yet implemented) |
| `src/TablePlanner.Export` | CSV/Excel import/export and print templates (not yet implemented) |
| `src/TablePlanner.Web` | Blazor UI (MudBlazor): projects, floor layout editor, groups editor |
| `tests/*` | xUnit tests per project |

Dependencies point one way: Web → Solver/Export → Core.

## Build, test, run

Requires the .NET 10 SDK.

```sh
dotnet build
dotnet test
dotnet run --project src/TablePlanner.Web
```

## Publishing to GitHub Pages

```sh
dotnet publish src/TablePlanner.Web -c Release -o publish
```

Then, in `publish/wwwroot`:

- set `<base href="/your-repo-name/">` in `index.html`
- add an empty `.nojekyll` file, so Pages serves the `_framework` folder
- copy `index.html` to `404.html`, so deep links work
