# Decisions

Design decisions for the table planner. Keep this file up to date when decisions change.
All entries below were agreed on 2026-11-04 unless a different date is given.

## Goal

A web app for seating **groups** of guests at long tables across one or more floors of a venue.
Groups are kept as close together as possible, and seated near the groups they'd like to sit with.
The result can be adjusted by hand, saved, and printed as posters and table cards.

## Platform and hosting

- **Blazor WebAssembly (standalone)** on **.NET 10 (LTS)**. Runs entirely in the browser.
- **Static hosting** (e.g. GitHub Pages), with no backend. The app must work under a sub-path
  (`<base href>` set at publish), and Pages needs `.nojekyll` and a `404.html` SPA fallback.
- **Repository:** a local git repo only. The user creates the public GitHub repo, pushes, and sets up
  CI and Pages themselves. No GitHub Actions workflow in the repo for now.
- **UI library:** MudBlazor.
- **UI language:** English. No localization framework. Text on cards and posters comes from the
  user-editable templates, so it can be in any language.
- **Devices:** desktop first (mouse and keyboard).
- **Scale:** up to ~700 people. Design for 700, benchmark up to ~1,000 for headroom.

## Architecture

Strict separation of UI and logic: a model describes the setup, the UI edits the model, and the
algorithm works on setup + assignment. UI code never goes into model or algorithm code.

| Project | Contents | Depends on |
|---|---|---|
| `TablePlanner.Core` | Models, seat geometry, seat graph, validation, edit commands (undo/redo), project JSON, example project | — |
| `TablePlanner.Solver` | Scoring, floor assignment, greedy placement, local search | Core |
| `TablePlanner.Export` | CSV and Excel import/export, HTML template rendering (cards, poster) | Core |
| `TablePlanner.Web` | Blazor UI, browser storage, file upload/download, printing | all of the above |
| `tests/*.Tests` | xUnit tests per project | |

- Dependency direction is always **Web → Solver/Export → Core**, never the reverse.
- No Blazor or browser references outside `TablePlanner.Web`.

## Domain model

- **EventProject** (one file per event): Setup + Priorities + CurrentAssignment + SavedAssignments[].
  - **Setup:** floors, tables, floor elements, groups, pins.
- **Floor** → **Table** → **Seat**, plus **FloorElement**s and an optional background image per floor.
  A separate room can be modelled as its own floor (the name is free text).
- **Group:** Name (required, unique), PeopleCount (required, > 0), Notes, Colour, Dietary info,
  Contact person, PreferredGroups (ordered, max 5). There are no individual guest names.
- **Stable IDs** (not list positions) for floors, tables, seats, groups and elements.
- JSON with a **`schemaVersion`** field from day one, so older files can be migrated.

## Tables and floors

- v1 supports **long rectangular tables** with seats on both long sides, plus an optional per-table
  setting for **end seats** (0, 1 or 2; default 0).
- A table-shape concept with a **geometry generator per shape**. Round tables may be added later
  without changing the solver, visualization or printing.
- Individual seats can be **disabled** (pillar, wheelchair space, reserved).
- **Table numbering:** automatic per floor (101, 102 … on floor 1, 201 … on floor 2), each number editable.
- **Adjacency:** auto-detected when tables are close and parallel (side by side, giving back-to-back
  seats) or end to end. Shown visually in the editor, with manual override. Adjacency exists only
  within a floor.

## Setup editor

- Tables and elements use **free placement on a snap-to grid** (e.g. 0.5 m).
- Rotation snaps to **0°, 90°, 180° and 270°**. Free angles are out of scope for now.
- Drag to move. Resize handles, with table length and seat count linked. Rotate with a button or `R`.
- Duplicate (`Ctrl+D`), multi-select to move or duplicate rows, copy a whole floor.
- Table presets, a properties panel for exact values, arrow-key nudging.
- Undo/redo through the same command system as plan editing.
- **Floor elements** (Bar, Buffet, Stairs, Entrance, Exit, Toilets, Stage, Dance floor, Cloakroom,
  Custom): label, position, size and rotation. No seats, and ignored by the solver.
- **Background image per floor:**
  - PNG or JPG upload, downscaled in the browser (e.g. max 2,000 px, JPEG) and stored in IndexedDB
  - scale calibration: click two points and enter the real distance
  - opacity slider and lock option
  - embedded as base64 in the exported project JSON
  - optionally shown, faded, on the poster

## Groups editor and import/export

- A table view with inline edit, add/delete, sort/search, and validation (empty name, count of 0 or
  less, duplicate names).
- Live totals: groups, people, seats, shortfall.
- Preferences are picked from the other groups and reordered by dragging (max 5).
- **CSV:** semicolon separator, UTF-8 with BOM (Excel-friendly with æøå). The separator is detected
  automatically on import. Header row; optional columns may be missing. A `Preferences` column lists
  group names in order.
- **Excel (.xlsx):** same columns as CSV. The Excel library (MiniExcel or ClosedXML) is lazy-loaded.
- CSV and Excel readers produce rows that go through one shared pipeline: validation, then an import
  preview with row-level errors, then apply.
- When groups already exist, the import **merges by name** (keeping IDs, so pins and saved plans stay
  valid) and adds new groups. Replace-all is an option.

## Seating rules and solver

- The unit of assignment is the **group** (sizes from ~4 to ~50). Placement is **seat-level**.
- Groups are seated **as close together as possible**: next to each other, across the table, or back
  to back at adjacent tables. Filling half of 2 adjacent tables beats spreading out.
- Avoid splitting groups **across floors** (an explicit penalty per extra floor, stronger than any
  table split).
- **Seat graph** (derived in Core from the geometry): seats are nodes, and edges carry a relation
  (next to, across, diagonal, back to back, same table, adjacent table) and a weight. The solver only
  sees the seat graph and the groups.
- **Score:** the sum over all same-group pairs of their relation weight, plus preference bonuses, minus
  penalties.
- **Group preferences:** each group has an ordered list of groups it would like to sit with.
  - **Fairness:** every group's first choices come before anyone's lower choices. Weights fall steeply
    per rank (e.g. 100 / 30 / 10 / 3 / 1).
  - Placing mutual preferences together earns a **bonus**.
  - Closeness between groups is measured where they meet: sharing a table > adjacent tables >
    same floor > different floors.
  - By default, keeping a group itself together ranks above sitting near preferred groups.
  - "Should not sit near" rules: later.
- **Empty seats** are gathered at table ends, not scattered between groups.
- **Configurable priorities** (`SeatingPriorities`, saved in the project): seat-closeness weights, table
  split penalty, adjacent vs. distant split, floor split penalty, minimum fragment size, preference
  weights, fill vs. spread. Each penalty can be soft (a weight) or hard (a rule). Presets plus sliders.
  Per-group overrides: later.
- **Algorithm:** 1) assign groups to floors (bin packing), 2) greedy placement per floor, largest groups
  first, in the most compact free region, 3) local search (move/swap groups and blocks) with
  **incremental (delta) scoring**. AOT compilation is the fallback if it's too slow.
- **Minimal-change re-optimize:** after last-minute changes, rearrange as little as possible.
- A group that can't be kept together is **split as compactly as possible, with a warning**.

## Error handling and termination

- **Pre-validation** before solving: more people than seats, pinned group larger than its target,
  overlapping pins, group larger than any floor while the floor-split rule is hard, pins exceeding
  floor capacity. Results are errors (can't run) or warnings (can run, some rules can't be met).
- **Not enough seats:** place as many groups as possible, list the rest as unplaced, and show a big warning.
- **Guaranteed termination:** a time budget (default ~5–10 s, configurable), a max number of iterations,
  and an early stop when nothing improves. It is an anytime algorithm that returns the best plan so far.
- **Responsive UI in single-threaded WASM:** an async solver that yields regularly (`await Task.Yield()`),
  with a `CancellationToken` (Cancel button) and `IProgress<T>` (progress and best score).
- **`SolveResult`:** Success / Partial / Failed plus a list of issues. No exceptions for expected
  problems. The solver works on a copy, and the result replaces the plan only when the user accepts
  it. Unexpected exceptions are caught at the UI boundary.

## Visualization and editing

- A floor plan per floor (tabs), drawn as **SVG in Blazor**: tables, floor elements, background image,
  seats coloured by group with short labels, hover details, click-to-highlight a group.
- A group legend showing size, number of tables and floors, split warnings, and preference fulfilment
  (e.g. "34 of 38 groups got their 1st choice").
- Further views: per table and per group.
- Core computes all seat coordinates. The seat graph and every drawing share that geometry, and the
  UI only draws it.
- **Pins** (constraints in the setup) target a floor, a table or exact seats. The solver keeps them fixed.
- **Manual edits:** move or swap **whole groups** in v1 (partial moves later). Score and warnings
  update live. Edits that break hard rules are allowed but flagged.
- **Lock** turns a placement into a pin. **Re-optimize** keeps pins. **Lock all** plus unlocking
  individual groups gives partial re-optimization.
- Edit operations are **commands in Core** (MoveGroup, SwapGroups, Pin, Unpin, …), which gives
  undo/redo, shared validation and tests.

## Saving

- Data lives in the browser: localStorage for project data, IndexedDB for images.
- **Project list** in the browser for several events, with a **"last exported"** reminder against
  browser data loss.
- **Export/import** of the whole project as one JSON file. Sharing between planners works by
  sending this file.
- **Saved assignments (snapshots):** named, with a timestamp, the priorities used and a score summary.
  They can be restored, deleted, or used as a starting point. Shown as a list with scores
  (side-by-side comparison later). Restoring validates against the current setup and reports conflicts.

## Printing (cards and poster)

- Both are produced as **HTML from user-editable HTML/CSS templates** with placeholders
  (e.g. `{{GroupName}}`, `{{PeopleCount}}`, `{{TableNumber}}`), and printed with the browser's
  print dialog ("Save as PDF"). No C# PDF library is needed.
- Template rendering (placeholder substitution) lives in `TablePlanner.Export`. The Web project
  previews and prints the result in a **sandboxed iframe** (no scripts), since templates may come
  from other people.
- Templates are stored separately from projects, so they can be reused across events. A default
  template for each is built in.
- **Table cards:** A5, 2 per A4 sheet, flat. One card per group area at a table (a group split over
  2 tables gets 2 cards). Each card shows the group name, the count, and the table number. A split
  group's card shows "30 of 50". A folded tent-card option comes later.
- **Poster:** A3 (A4 option). A floor plan page per floor, with group names on their areas, plus an
  alphabetical "Find your table" list (group → floor and table).

## Later

- A **mobile page:** a generated, self-contained HTML page (like the poster) that can be hosted
  separately, so guests can look up their group on their phone.
- Round tables, free rotation angles, partial group moves, per-group priority overrides,
  "should not sit near" rules, side-by-side plan comparison, folded table cards.

## Defaults chosen without explicit discussion (change if wrong)

- Adjacency covers both side-by-side (back to back) and end-to-end tables.
- No individual guest names, only group name and size.
- Separate rooms are modelled as separate floors.
- Individual seats can be disabled (pillar, wheelchair space, reserved).
- Sharing between planners is done by sending the project file.
- A built-in **example project** (a few floors, tables and groups), also used as test and benchmark data.
- Tests: unit tests per project, plus solver benchmark setups (small, large, nearly full, impossible).

## Build order

1. **Scaffold:** git repo, solution and projects, MudBlazor, `.gitignore`, README
2. **Core model:** model classes, seat geometry, validation, project JSON, example project, unit tests
3. **Setup UI:** floor and table editor, floor elements, groups editor, project list, save/load
4. **CSV/Excel** group import and export
5. **Seat graph and solver**, including benchmarks
6. **Visualization and editing:** floor plan view, pins, manual moves, snapshots
7. **Printing:** HTML templates for table cards and poster
8. **Background images**
9. Later: the mobile page

Status (2026-11-04): steps 1–2 done.
