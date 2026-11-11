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
  CI and Pages themselves. No GitHub Actions workflow in the repo for now (confirmed 2026-11-07: deploying stays
  manual, with `publish.ps1`). The site is served from a separate repo (`TableAssigner`, branch `gh-pages`), so an
  Actions workflow here would need a token for that repo.
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
- **Columns:** `Name`, `People`, `Contact person`, `Dietary info`, `Colour`, `Notes`, `Preference 1` …
  `Preference 5`. Only `Name` and `People` are required. Headers are matched loosely (case, spaces, a few
  alternatives like `Group`, `Size`, `Color`, `Pref 1`); unknown columns are ignored with a warning.
  - *Changed (step 4):* preferences are five columns of group names rather than one `Preferences` column,
    so no separator inside a cell is needed and the file is easy to edit in Excel.
- **CSV:** semicolon separator, UTF-8 with BOM (Excel-friendly with æøå). On import the separator (`;`, `,`,
  tab) is detected from the header line; files that are not valid UTF-8 are read as Latin-1 (Excel's
  "CSV" save in the Windows code page).
- **Excel (.xlsx):** same columns as CSV, first sheet only. Old `.xls` files are rejected with a message.
  - *Changed (step 4):* no Excel library. A small reader/writer in `TablePlanner.Export` handles the zip/XML
    parts needed for one sheet (shared and inline strings, numbers, booleans, cached formula values).
    It keeps the WebAssembly download small, needs no lazy loading, and has no trimming or licence concerns.
    The written file has a bold, frozen header row and `People` as numbers; it opens in Excel without repair.
- CSV and Excel readers produce rows that go through one shared pipeline (`GroupFile` → `GroupImport`):
  validation, then an import preview, then apply as one undoable edit.
- **Merge** (default): groups are matched by name, ignoring case and surrounding spaces, and keep their IDs,
  so pins, plans and preferences stay valid. New names are added; groups not in the file are kept.
  **Replace** also removes groups that are not in the file (with their pins and seats).
- For an existing group, only the columns present in the file are changed; a present column with an empty
  cell clears the value. If any preference column is present, preferences are replaced.
- **Row errors** (missing name, people not a whole number ≥ 1, a name used twice in the file) skip that row;
  the rest can still be imported. In Replace mode a group whose row has errors is kept as it is.
- **Warnings** (invalid colour → automatic colour; preference for an unknown group or itself → left out)
  do not block the import. Preferences may refer to groups further down the same file.
- Export of an empty project gives a header-only file, which doubles as a template.

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
- **Algorithm** (*changed in step 5*, from "floor bin packing, then greedy, then local search over seats"):
  - All free seats are put in one fixed **fill order**: floor by floor; within a floor from table to the
    nearest table (a snake through rows); within a table column by column (top seat, then the one across),
    end seats first and last. Each table is walked in whichever of its 4 directions starts closest to where
    the previous one ended, so the joint between back-to-back rows is close.
  - A plan is an **order of the groups**. Decoding fills the seats in fill order, group after group, so every
    group is a compact block (across the table and along it, spilling onto the next adjacent table when big).
    A group that does not fit in the rest of a table (or floor) starts at the next one when there are seats
    to spare and it then fits entirely, which puts empty seats at table ends. Groups that do not fit at all
    are skipped (unplaced), and later, smaller groups still get seats.
  - The start order is the better of "largest first, followed by the groups it prefers (and that prefer it)"
    and plain "largest first". **Simulated annealing** then changes the order (swap, move, move next to a
    preferred group, move or reverse a short run), with full re-scoring of each candidate.
  - This searches a small space in which every plan is already sensible; there is no seat-level search.
    Floor assignment falls out of the order, so floor bin packing is not a separate step.
- **Score** (`PlanEvaluator`, also used for plans edited by hand): points for same-group pairs that are next to,
  across, diagonal or back to back, plus per pair at the same table and at adjacent tables; preference
  points by rank × closeness (same table 1, adjacent table 0.6, same floor 0.2) plus the mutual bonus;
  minus penalties per extra table, per part not adjacent to the rest, per extra floor, per small part, per
  empty seat between people, per person moved (re-optimize only). Unplaced people (10,000 each) and broken
  hard rules (100,000 per person: pins, never-split-floors) outweigh everything else.
- **Default preference weights 200 / 60 / 20 / 6 / 2** (*changed in step 5* from 100 / 30 / 10 / 3 / 1): with the
  lower weights the solver sometimes gave up a group's 1st choice for slightly more compact big groups.
- **Seat graph** thresholds: tables are adjacent when their tops are at most 2.0 m apart (on the same floor);
  seats at different tables are next to each other when facing the same way within 1.3 m (tables end to end),
  and back to back when facing away from each other within 1.5 m.
- **Pins:** seat pins are placed as given; table pins take that table's first free seats in fill order before
  the search; floor pins are a hard-rule penalty, so the search moves those groups onto their floor.
- **Re-optimize** starts from the current plan's group order and adds the moved-person penalty.
- **Repeatability:** with a fixed seed, cooling follows the iteration count, so tests get the same plan on any machine.
- **Speed:** WebAssembly normally runs .NET in an interpreter, about 100 times slower than native for the solver
  (700 people: ~750 plans/s). Release builds are therefore **AOT-compiled** (~12,700 plans/s for 700 people;
  the compressed download grows from 3.3 to 7.9 MB, and publishing takes ~7 minutes). F5/`dotnet run` stay
  interpreted for fast builds, so the solver is slower there.
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
  - *Changed (after step 9):* two options, see the notes below: a card per **seat** as well as per group at a table,
    and **folded tent cards**.
- **Poster:** A3 (A4 option). A floor plan page per floor, with group names on their areas, plus an
  alphabetical "Find your table" list (group → floor and table).

## Later

- ~~A mobile page~~: done as the guest page (step 9).
- ~~A drag-and-drop editor for the table cards~~: done, see "card designer" below.
- ~~Round tables~~: done (2026-11-07). Free rotation angles (wanted, but later), per-group priority overrides,
  "should not sit near" rules. (~~Partial group moves~~: done as moving one person, Shift+drag.)
- **Side-by-side comparison of saved plans:** postponed (2026-11-05); not sure it is needed, since each saved plan
  already shows its key figures in the list.

Wishes noted 2026-11-10, after testing (not started):
- ~~**Copy a project**~~: done 2026-11-11 (a copy button on each project; images are copied too).
- ~~**Labels on top of element drawings**~~: done 2026-11-11. Labels get a thin white outline (dark on a pillar), so
  they read over any drawing, and a selected element's label can be dragged (`LabelOffsetX/Y`; "Reset label" in the
  panel).
- ~~**Rotate elements directly on the canvas**~~: done 2026-11-11. A selected element has a round handle 0.5 m above it
  (turned with it): drag to turn in 15° steps, Alt for any angle. Tables got the same handle (beyond the chairs on their top side),
  turning them 90° at a time (tables keep quarter turns only).
- ~~**Free rotation of elements**~~: done 2026-11-11 (elements only, not tables). `FloorElement.Angle` in degrees; the old
  `Rotation` (R0…R270) stays for older files and sets the angle when read (in files it comes before the angle, so a
  saved angle wins). Boxes, drawings, labels, wall gaps and bounds follow the angle; R and the button still add 90°;
  "Angle (°)" in the panel. At an angle that is not a quarter turn the resize handles are hidden (width and depth in
  the panel). Copying an element (Ctrl+D) now also keeps its round shape and label size and position.
- ~~**A text element**~~: done 2026-11-11 (kind Text: no box, the label fills its height; outlined only when selected).
- ~~**Font size for element labels**~~: done 2026-11-11 ("Label size (cm)" in the panel; empty = fitted to the element).
- **The interface for group levels needs more work** (noted 2026-11-11, after the first level view): how levels are
  shown and switched on the plan page, and how they are built on the groups page. Collect specifics while testing.
- ~~**Looking at the plan level by level**~~: done 2026-11-11. "Show: groups / group – level 1 / group – level 2" on the
  plan page (only the levels the groups have) sets colours, the names on the tables (one label per group at that level),
  the group list (the groups at that level with their tables) and the warnings (about those groups). A department's
  label selects it but is not dragged. Was: see how the top-level groups were seated, then switch to the next level
  and see how their subgroups were seated, and so on. "Colour by" is a start, but the display should follow the level
  as a whole: group names on the tables, the group list, highlighting and warnings at the chosen level (e.g. one label
  "Sales" over all its tables at level 1, its teams at level 2).
- ~~**The plan page's group list as a foldable tree**~~: done 2026-11-11 (an arrow on each parent group; the tree had
  not been showing at all, because the list was not given the setup; that also made the person search there work). Was: it already shows parent groups in tree order with indentation
  (when the plan has them), and selecting one highlights all its groups; folding parts open and closed is missing.
- ~~**A warning on duplicate guest names**~~: done 2026-11-11 (a setup warning naming the groups).
- ~~**A poster of guest names and their tables**~~: done 2026-11-11. "Names list" on the print page (shown when there
  are names) adds a "Find your table by name" page: the named people alphabetically, with their group (as chosen under
  "Group names") and its tables. People without a name are not listed. Templates get `People` and `PeopleLetters`.
- ~~**Naming of levels**~~: done 2026-11-11 ("Group – level 1" … in the plan page and import wizard). Was: "Group", "Level 1" and "Level 2" (e.g. "Colour by level 1", the import wizard's "Level 1,
  e.g. department") reads badly: subgroups are groups too. Decided 2026-11-11: "Group – level 1", "Group – level 2" …
  for now (project-specific level names maybe later).
  Then (2026-11-11) **level names per project**: `EventSetup.LevelNames` (e.g. "Departments", "Teams"), set on the Groups
  page ("Levels are called:", one field per level) and taken from the level columns' headings when groups are imported
  (unless the project has names already). Unnamed levels are "Main groups", "Subgroups", "Sub-subgroups"
  (`GroupTree.LevelName`). The plan page shows "Show: all groups / Show: Departments / …" and the group list's heading;
  the import wizard's column roles are "Main group (e.g. department)", "Subgroup (e.g. team)", "Sub-subgroup".
- ~~**Group names on tables overlap**~~: done 2026-11-11. Long tables have two lines for names (a group on the top side
  only uses the upper, otherwise the lower); a name that would overlap another, or the table number, moves to the other
  line, else is made smaller or shortened to fit the gap. Names sit over the group's longest unbroken run of seats (a
  group in two pieces was labelled between them). On round tables a clashing name moves outwards, then is shortened.
- ~~**Better alignment when moving tables**~~: done 2026-11-11 (`TableSnapping`): the dragged table lines up its edges,
  centre and seats with tables within 4 m (within 25 cm; closest match per axis), with pink guide lines; an axis without
  a match snaps to the grid as before; Alt turns snapping off. Was: the grid snaps a table's centre, so tables of different lengths end up with
  their ends and seats out of line with the next table, and seats no longer sit back to back. Snap to neighbouring
  tables instead: line up ends and edges, and put seats in line with the seats of the table next to it (with guide lines
  while dragging).
- **A better experience for planning by hand** (to look at as a whole; details to be collected while testing). Ideas:
  a list of groups without seats to drag from; while dragging, show where the group fits and how the score and
  warnings would change; "best place for this group" suggestions; moving a whole department or several selected
  groups at once; making room (pushing neighbours along) instead of only swapping; Re-optimize only a part (a floor,
  some tables) around what was placed by hand; touch-friendly moves (Shift+drag needs a keyboard).
  - Noted 2026-11-11 (user, testing): **dropping a group from the list onto the plan should place it where it is
    dropped**. Dragging Sales (50) onto the right end of table 103: the preview starts at that seat, but then follows the
    solver's fill order (4 seats at the end of 103, 6 at the end of 104, all of 105 and 106) instead of growing from
    the drop point into the nearest free seats (the rest of 103, then 102/106).
- **For tomorrow (noted 2026-11-11 evening)**:
  - Solver rethink (see the level-by-level method above). Care: keeping large groups together must not split a main
    group because its subgroups want to sit together (or near other groups); a main group's unity comes first.
  - Free seats in the middle of the room (beam search especially): gather them at the outside.
  - The user's clarification on free seats: they need not be at the outside of the room (fine if they are). The goal is
    groups together, main groups and their subgroups, without many empty seats scattered all over. By hand the user
    would: place the main groups first, then see whether the subgroups fit within those bounds, and if not, try placing
    the main groups differently. (Points to the level-by-level method, with a retry loop at the top level.)
  - It must still work without levels (a flat list of groups, the common case): then the groups themselves are the
    "main groups" and the method reduces to placing them well, at least as good as Search today.
  - CI: when the GitHub Actions deploy (on the remote master, to be merged) is in, consider running all tests
    (`dotnet test`) before deploying, and failing the deploy when they fail. The solver's quick checks take ~40 s.
- **Emoji picker for elements** (noted 2026-11-11): a small picker with the most common emojis (🎤 🍽️ 🍸 🎂 🎁 🚻 🚪
  🎵 📸 ☕ 🅿️ ♿ …) next to an element's label, for an element shown with just an emoji instead of text (the label font
  size can already be raised).
- **Continuing a wall should be easier to find** (noted 2026-11-11): the user expected an edit mode on a selected wall
  that adds pieces at its last (or first) corner, as when drawing. It exists since 2026-11-11 (Walls / W, then click a
  loose end of an open wall), but is not discoverable: offer it on the selected wall, e.g. "Continue from this end"
  handles or buttons in the wall panel that start drawing from the first or last corner.
- **A level-by-level solver, as an alternative method** (to design next, noted 2026-11-11). The user's idea: place the
  main groups first, each as one area (split only when it must, and then along its subgroups), then place the next
  level inside each area, and so on, handing each part to the existing algorithms. Possible shape: (1) divide the
  tables into compact areas per main group, sized to its people (growing areas over the table adjacency, keeping
  preferences between main groups next to each other); a main group too big for one area is split into whole
  subgroups (a bin-packing step); (2) within each area, run the existing order search + polisher for its groups only;
  (3) a final pass over the whole plan (polisher, short search) for what crosses areas. Offered as a third method next
  to Search and Beam, compared with the test plans.
- ~~**"Create test projects" in the app**~~: done 2026-11-11 (7 projects each time, `TestProjects` in Core, with
  fills from about 0.85 to 1.07; the test plans use the same generators with fixed seeds). Was: (projects page): makes several different setups at once (long and round
  tables, several floors and separate areas, disabled seats, group levels, names, preferences), with about as many
  people as seats (a little under, exactly, a little over). For testing the solver by hand, so they are deliberately
  not checked against it. The generators in `TestPlanGenerator` (tests) could move to Core and be reused, with a
  random seed each time.
- ~~**Zoom with the scroll wheel**~~: done 2026-11-11, in the layout editor and on the plan page, around the pointer.
  A mouse wheel and a touchpad pinch zoom; two-finger scrolling on a touchpad and Shift+wheel still scroll. Panning:
  drag the empty background (grid, locked picture, or a floor element on the plan page), or drag with the middle button
  anywhere; a pan does not clear the plan page's selection (in the editor pressing the background still does).
- ~~**Importing a project whose name (or ID) already exists**~~: done 2026-11-11. It asks: import as a copy (new ID,
  name "… (copy)"), replace (same project) or keep the name (same name, other project), or cancel.

Order agreed 2026-11-11:
1. Small fixes: copy a project, import as a copy, duplicate guest names warning, test projects in the app, scroll
   wheel zoom.
2. Layout editor: labels not over drawings, label font size, text element, snapping tables to their neighbours.
3. Group levels: naming ("Group – level x"), the level-by-level view, the foldable tree.
4. The names poster.
5. Element rotation: a handle on the canvas, then free rotation (elements only).
6. Planning by hand (collect specifics while testing first).
Overlapping group names on tables: when convenient.

## Defaults chosen without explicit discussion (change if wrong)

- Adjacency covers both side-by-side (back to back) and end-to-end tables.
- ~~No individual guest names, only group name and size.~~ Changed 2026-11-10: optional names per group (person lists).
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

Status (2026-11-07): steps 1–9 done, plus the extras noted after step 9 and the changes after testing (end of file).
Postponed: side-by-side comparison, free rotation angles.

Notes from step 3:
- Undo/redo stores a snapshot of the project before each edit (`EditHistory`); the edit logic itself lives in
  `SetupOperations` in Core. Undo restores into the same project object, so the UI refers to items by ID.
- Resizing a table keeps its left end in place; disabled seats, seat pins and seat assignments follow their seats.
- The Checks panel also warns when tables on a floor overlap.
- Not yet in the editor: rubber-band (drag-box) selection, and showing which tables count as adjacent
  (comes with the seat graph in step 5).

Notes from step 4:
- Groups page has Import (CSV/Excel) and Export (CSV or Excel). The import dialog shows new / updated /
  unchanged / removed groups with what changed, plus row errors and warnings, before anything is applied.

Notes from step 5:
- New **Plan** page: Make plan, Re-optimize (keep close to the current plan), Stop, Priorities, Clear plan. A new
  plan is shown as a proposal with its summary and only replaces the current plan on "Use this plan" (undoable).
  The solver runs on a copy of the project.
- The plan view colours seats by group, writes group names on their part of each table, and highlights a group
  when it is clicked (in the drawing or in the group list). Richer visualization and manual edits come in step 6.
- The priorities dialog edits all weights as numbers; presets and sliders are not done yet.
- The layout editor can show which tables count as adjacent ("Adjacent tables" switch).
- Automatic group colours are spread around the colour wheel; with many groups some look alike.

Notes from step 6:
- **Manual edits** on the Plan page: click a seat or name to select a group, press **Move** (or M), then click a seat.
  - An empty seat (or the group's own): the group takes free seats along the solver's fill order from that seat,
    then the free seats just before it, **on that floor only**. If the floor lacks room, nothing changes and the
    user is told to swap instead. (A first version wrapped around to other floors and split groups across floors.)
  - A seat of another group: the two groups **swap**. Equal sizes trade places exactly; otherwise the moved group
    starts where the other sat and the other takes the remaining seats of both, so the larger one ends up in two places.
  - The seat choice lives in `Solver.PlanEditing` (it needs the fill order); the change itself is
    `Core.Editing.PlanOperations.SeatGroups`, one undoable step. Score and warnings update immediately.
- **Locks and pins:** Lock = a seat pin on the group's current seats (it follows the group when moved by hand).
  Pin to table / floor from the group panel; Locks menu has "Lock all seated groups" and "Unlock all" (table and
  floor pins stay). Locked or pinned groups have a dashed outline and a lock icon in the list.
  "Take out" removes a group from the plan (and its lock).
- Edits apply to the current plan only; while a proposed plan is shown, editing is disabled.
- **Saved plans** tab: save the current plan with a name; each saved plan stores its priorities and key figures
  (score, seated, split groups, 1st choices met), shown in the list. Restore makes it the current plan (undoable);
  seats or groups that no longer exist are left out and reported, as are groups whose size changed.
- **Tables** tab: per table, seats taken and the groups sitting there.
- Automatic colours now start with a 20-colour palette (Tableau 20) before falling back to the colour wheel.
- Not done: drag and drop, side-by-side comparison of saved plans, presets/sliders for priorities.
  *(Later: drag and drop and presets were added; side-by-side comparison is postponed.)*

Notes from step 7:
- **Print page** (Print in the top bar): Table cards or Poster, which plan (current or a saved one), poster paper
  (A3/A4, landscape or portrait, floor plans and/or group list), preview size, and **Print**, which opens the
  browser's print dialog (printer or "Save as PDF").
- **Templates** use a small Mustache subset, implemented in `Export.Printing.HtmlTemplate` (no library):
  `{{Name}}` (HTML-escaped), `{{{Name}}}` (not escaped), `{{#List}}…{{/List}}` (repeat, or show if true/not empty),
  `{{^Name}}…{{/Name}}` (show if false/empty), `{{! comment }}`. Names are case-insensitive; inner sections see outer
  values. Unknown placeholders give an empty text and a warning in the preview; a broken template (unclosed section
  etc.) shows the error with its line and keeps the last good preview.
- The data a template gets is built by `TableCards.BuildData` / `Poster.BuildData`; the placeholder help next to the
  editor comes from the same code (`Placeholders`), and a test checks they match. Texts that depend on numbers
  ("person"/"people", "table"/"tables") are flags (`OnePerson`, `SeveralOtherTables`), so the wording stays in the template.
- **Built-in templates** are real HTML files (`Export/Printing/Templates/*.html`, embedded). The user's own templates
  are kept in localStorage per kind (not in the project), can be loaded from / saved to an .html file, and reset.
- **Preview:** an iframe with `srcdoc` and `sandbox="allow-same-origin allow-modals"`: scripts in templates do not run;
  same-origin lets the page call `print()` on the frame. The preview size scales the iframe, not the document, so
  printing is unaffected. Templates have `@media screen` styles that show pages as sheets.
- **Cards:** one per group per table, in floor/table order and along the table; `Sheets` gives two per A4 sheet.
  A split group's card shows "12 of 32 people · also at table 102".
- **Poster:** a page per floor with an SVG floor plan (`FloorPlanSvg`: cropped to the tables and elements, colours as
  presentation attributes so template CSS can override them by class), then "Find your table" in columns, by first
  letter. Floor names in the list only when there is more than one floor with tables.
- Group label placement moved to Core (`GroupLabels`), shared by the plan view and the poster. Long names on a short
  part of a table are drawn smaller (down to 65 %) before they are shortened with "…".
- Printing reads the plan leniently (`PrintedPlan`): seats that no longer exist and deleted groups are left out;
  the page warns when groups are not fully seated.
- Not done: folded tent cards, a fit-to-width preview, the mobile page (step 9).

Notes from step 8:
- **Background image per floor**, in the floor's panel of the layout editor (nothing selected): Add image, Set scale,
  opacity, Left/Top, Locked, Show on the poster, Replace, Remove. It is drawn under the floor in the layout editor and
  on the Plan page, and (faded to 60 % of its opacity) on the poster when chosen.
- **Model:** `Floor.Background` holds only settings (image ID, pixel size, top-left position, metres per pixel,
  opacity, locked, show on poster), edited through `BackgroundOperations` and undoable. The **image itself is not in
  the model**, because every undo step serializes the whole project; images are `ProjectImage(Id, DataUrl)`.
- **Storage:** images are in **IndexedDB** (`tableplanner` / `images`, key `projectId/imageId`), the project stays in
  localStorage. Deleting a project deletes its images. Images no longer used are deleted when the project is next
  opened (not during editing, since undo may bring a removed background back).
- **Project file:** `ProjectSerializer.SerializeFile` adds an `images` array (ID + data URL) for the images in use;
  `Deserialize` returns them and accepts only `data:image/…` URLs. Older files simply have no images. Undo snapshots
  and the localStorage copy use `Serialize`, without images.
- **Upload:** PNG or JPG (up to 30 MB), downscaled in the browser to at most 2,000 px on the longest side and stored
  as JPEG (quality 0.88, transparency on white). A new image is placed at the top left, as wide as the floor's
  contents (at least 30 m). A replaced image of the same pixel width keeps position, scale and settings.
- **Set scale:** click two points on the floor, enter their real distance; the image is scaled around the first point.
  The canvas now extends up/left when something (such as a scaled image) lies at negative coordinates.
- **Moving:** drag the image when it is unlocked (or type Left/Top); a locked image ignores the mouse, so clicks go to
  the floor as before.
- Not done: rotating the image, cropping, several images per floor.

Notes from step 9:
- **Guest page:** a third kind on the Print page (renamed **Print & share**). **Download page** saves one
  self-contained HTML file (about 50 KB for the example; more with background images) to put on any web host,
  e.g. GitHub Pages, and share as a link or QR code. Nothing is sent anywhere by the app.
- On the page, guests type their group's name (case and accents ignored) and tap it: the table number(s), floor and
  the floor plan with their tables and seats highlighted, zoomed to their tables ("Show the whole floor" to zoom out).
  The group goes in the address (#…), so the phone's back button returns to the list and links can point at a group.
- It is a **template** like the cards and poster (`Templates/guest.html`, user-editable, same placeholders as the
  poster's group list plus `GroupKey`). The floor plan SVG now carries `data-table` on tables and `data-group` on
  seats and group names, so a script can highlight a group.
- **Preview sandbox:** the guest page needs its script, so its preview frame has `sandbox="allow-scripts"` *without*
  `allow-same-origin`: the script runs but cannot reach the app or its data (checked in the browser test). The prints
  keep the script-free sandbox. Switching kinds makes a new frame, as a frame keeps its sandbox until it reloads.
- In the sandboxed preview `history.pushState` is not allowed; the page then works without the address change.
- The downloaded file name is URL-friendly (e.g. `example-party-find-your-table.html`).
- Not done: a QR code generator, a hosted version that updates itself.

Notes after step 9 (table card options):
- **One card per:** "Group at each table" (as before) or **"Seat"**: an A5 card for every seated person, to put on
  their seat. Seat cards come per table in **seat order around the table**, so they can be laid out one after the
  other: numbered from 1 along the top side left to right, the right end, the bottom side right to left, the left end
  (in the table's own frame; every seat counts, taken or not, so numbers do not depend on the plan). The card shows
  "Seat N" small in a corner, for the staff. *Changed:* seat cards show the group's people count too, like the
  other cards (it was left out at first); a template can still hide it with `{{^PerSeat}}…{{/PerSeat}}`.
- **Folded tent cards** (switch): each A4 sheet holds one card twice, the top half turned 180°; folded across the
  middle it stands on the table and reads from both sides. The group's colour stripes meet at the fold. Without it,
  cards are flat, two to a sheet, cut apart. Both options combine (231 seat tents = 231 sheets for the example).
- The data gives the halves of each sheet (`Sheets` → `Cards`, with `Flipped`), so a template never repeats its card
  markup for tents. New placeholders: `PerSeat`, `Folded`, `SheetCount`, `SeatNumber`, `SeatNumbers`, `Flipped`.
  The page shows how many cards and sheets the choice takes.

Notes: A5 card paper and QR codes (2026-11-05):
- **Card paper:** A4 (two A5 cards per sheet, cut or folded) or **A5** (one card per A5 sheet, landscape, nothing to
  cut). Tent cards need A4 (an A5 tent would make A6 cards), so the switch is off on A5. The template gets the page
  size from the data (`PageSize`, `SheetWidthMm`, `SheetHeightMm`, `CardHeightMm`) instead of fixed A4 sizes.
- **QR code for the guest page:** the guest page's web address is entered on the Guest page tab and saved in the
  project (`EventProject.GuestPageUrl`, undoable; only http/https addresses with a host count). The tab then shows
  the QR code with SVG and PNG downloads, and the poster's "Find your table" page shows it. Every template gets
  `{{GuestPageUrl}}` and `{{GuestPageQr}}` (an SVG to size with CSS), e.g. for cards.
- The QR code is made with **QRCoder** (MIT, pure C#, works in WebAssembly): writing a QR encoder (Reed-Solomon,
  masks, versions) is not worth the risk. The SVG is drawn from its module matrix (one path, crisp edges); the PNG
  is QRCoder's own. Error correction level M.
- Found while testing: undo did not restore the new address, because `EditHistory.Restore` copies the project's
  properties one by one. Fixed, and a test now lists every `EventProject` property, so a new one cannot be missed.

Notes: drag and drop on the plan (2026-11-05):
- On the Plan page, a group can be **dragged by any of its seats or its name** onto a seat. It does exactly what
  Move + click does: onto a free seat (or its own) it moves along the fill order from there on that floor, onto
  another group's seat the two swap. One undoable step; the group stays selected. Move (M) + click still works.
- While dragging, a small marker with the group's name follows the pointer and the seats it would take are ringed;
  a seat where it does not fit is ringed red. Dropping away from a seat (or a refused move) changes nothing.
- A press becomes a drag only after 5 px of movement, so a click still selects. The pointer is then captured by the
  SVG, positions come from its own offsets (no JS call per move), and the canvas skips re-rendering unless the
  marker moved noticeably or the seat under the pointer changed (plain mouse moves over ~700 seats cost nothing).
- Dragging is off while a proposed plan is shown, like the other edits. On the plan itself a group cannot be dragged
  to another floor (only one floor is shown); drag it from the list instead (below).
- **From the group list:** a row can be dragged onto a seat of the shown floor, with the same preview and result.
  This also places groups that have no seats yet and moves a group to another floor. It uses the browser's own drag
  and drop (built-in threshold and drag image, no page-wide pointer tracking); app.js sets the drag data some
  browsers need. Positions come from the drag events' screen coordinates and the SVG's position, read when the drag
  enters it; the drop position is used again on drop, since the last dragover can lag behind the pointer.

Notes: card designer (2026-11-05):
- **Design cards** (Print & share → Table cards) opens a designer: the A5 card (210 × 148 mm) with items that are
  dragged into place and resized by a corner handle (1 mm snap, Alt for 0.1 mm), nudged with the arrow keys, and
  set in a properties panel. Items: **text** (with placeholders, e.g. "Welcome, {{GroupName}}!"; size, bold,
  italic, capitals, alignment, colour or the group's colour), **colour boxes** (fixed or group colour, rounded
  corners), the guest page **QR code** and an **image** (logo, downscaled to 800 px). Ready-made fields (group name,
  table, floor, seat number, people, dietary info) and a placeholder menu help with the texts. One font per design.
- The card shows **real cards from the plan** (step through them), so long names and split groups are visible while
  designing; a made-up card when nothing is seated. The designer has its own undo/redo.
- **Save and use** turns the design into an ordinary table-card template (`CardDesign.ToTemplate`) and makes it the
  table-card template, so printing is unchanged: paper, tent cards and a card per seat still apply. The design is
  stored separately (localStorage, `tableplanner:carddesign`), so it opens again for changes. The template editor
  shows "From the card designer" while the template is the design's; editing the HTML by hand is still possible, but
  saving the design again replaces it (the designer says so).
- Literal text is HTML-escaped (placeholders are left as they are), colours must be hex, images must be
  `data:image/…` URLs, so a design cannot inject markup or load anything from elsewhere.
- The designer lays the card out in real millimetres and points, like the print, and scales it with CSS; transparent
  hit areas on top of the rendered items take the mouse.
- Not done: snapping to other items / guides, rotating items, several designs to choose from, poster designer.

Notes: exploring the floor plan on the guest page (2026-11-05):
- The guest page has two tabs: **Find your group** (search, as before) and **Floor plan**: every floor, whole and in
  full colour, without choosing a group first. Tapping a seat or a group name opens that group; tapping a table with
  several groups shows a small chooser ("Table 101: Hansen family, Support"). Back returns to where the guest came
  from (the list or the plan), also with the phone's back button (#plan in the address).
- Each floor plan has **+ / −** and **Whole floor**; a zoomed plan can be dragged to look around. Only a zoomed plan
  takes touch drags (touch-action: none), so at whole-floor size the page still scrolls normally. The page's own
  pinch-zoom keeps working too.
- Group pages and the plan tab share one set of floor plans: with a group, its tables are highlighted and zoomed
  in, as before.

Notes: priority presets (2026-11-05):
- The Priorities dialog starts with presets: **Balanced** (the defaults), **Keep groups together** (splitting costs
  about three times as much, preferences half), **Preferences first** (preferences about twice as much, splitting a
  little cheaper), **Few changes** (moving a person to another table costs 100 instead of 20, for re-optimizing a
  plan that is already shared). The preset that matches the current numbers is highlighted; after a change by hand
  it says "Custom".
- A preset sets only the weights; the time limit, "never split across floors" and the small-part size stay as the
  user set them. "Balanced" replaces the former "Reset to defaults" button.

Notes: no small parts of large groups (2026-11-05):
- Problem: a large group was often split into full tables plus a remainder, e.g. Marketing (24) as 20 + 4, and in the
  large benchmark a group of 41 as 20 + 20 + 1 (one person alone). Packing tightly gives more "same table" pair
  points than an even split (20 + 4 beats 12 + 12 by about 116), and the old small-part penalty was a flat 30 per
  part, only for parts under 4.
- Now a part counts as small below **5** people (`MinimumFragmentSize` = 5, so 1–4), and the penalty grows with how
  far short it is: **150 per missing person** (`SmallFragmentPenalty`), so 4 people cost 150 and one person alone
  600. "Keep groups together" uses 400.
- Measured over 5 seeds each (FragmentDiagnostics): small parts 5 → 0 (example), 5 → 0 (nearly full),
  10 → 0 (large); first choices unchanged or better (large 251 → 254 of 290; example 40/40). The big groups now take
  even blocks over adjacent tables (Marketing 104 + 105 instead of 20 + 4). A solver test checks this.
- Saved projects keep the priorities they were saved with (old numbers show as "Custom"); choose a preset such as
  Balanced to get the new values.

Notes: several named templates (2026-11-05):
- Each kind (table cards, poster, guest page) has the **built-in** template plus any number of **named templates**;
  a **Template** picker on Print & share chooses which one is used. The choice and the templates are kept in the
  browser (`TemplateLibrary`, localStorage `tableplanner:templatelibrary`), separately from projects.
- The built-in template is read-only. **Make a copy** asks for a name and uses the copy; copies are edited in the
  template editor (saved as you type), **Rename**d and **Delete**d (then the kind goes back to the built-in one).
  Loading a file saves it under the file's name. Names are unique per kind ("Gala (2)"); "Built-in" is taken.
- The **card designer** opens the selected card template when it was made in the designer (its design is kept with
  it), otherwise it starts a new design from the built-in card and leaves the selected template alone. **Save**
  updates the template being designed, **Save as new…** stores it under a new name; both make it the one used. A
  designed template whose HTML was edited by hand since is marked, and the designer says Save replaces those edits.
- Templates from the earlier version (one per kind plus one card design) are moved into the library on first use as
  "My template" / "My card design", still selected; the old keys are removed. Tests cover the library logic.

Notes: tidying (2026-11-05):
- Print & share has two toolbar rows: what is printed and the action (kind, template, Design cards, Edit template,
  Print / Download page), then its settings (plan, the kind's options, card count, preview size).
- A MudSelect's outer wrapper grows to fill a toolbar, which spread toolbar items apart; toolbars now keep selects at
  their own (minimum) width.

Notes: changes after testing (2026-11-07):
- **Quick add of groups:** a row under the groups table: name, Tab, people, Enter (or Add). The fields clear and the
  cursor returns to Name. People is required; a name already in use is refused with a message. Each add is one undo step.
- **Resizing tables from both ends:** a handle at each end; the other end stays in place, and seats are added or
  removed at the dragged end. Seats keep their place in the room, so disabled seats, seat pins and plan seats follow
  them (`ResizeTable(…, keepRightEnd)`; `RemapSeatIndex` takes a shift from the left).
- **Resizing floor elements by dragging:** handles on the corners and sides of a selected element; the opposite side
  stays put, edges snap to the grid (Alt: no snapping), at least 0.5 m (`SetElementBounds`).
- **Layer order of elements:** Bring to front / forward / backward / Send to back in the element's panel. The order is
  the order of `Floor.Elements` (no new field), so the plan view, poster and guest page draw it the same way. Tables
  are always drawn above elements.
- **Round tables** (`TableShape.Round`, `Table.RoundSeats`, default 8, at least 2): the diameter follows from the
  seats (each seat gets `SeatSpacing` of the edge, at least 0.9 m: 8 seats ≈ 1.5 m, 10 ≈ 1.9 m). Seats are numbered
  clockwise from the top; that is also the seat number on seat cards. Rotation only moves where seat 1 is.
  - Seat graph: only the neighbours on either side are "next to" (also the last and first seat); seats across a
    round table are too far apart to talk, so they count only as "same table". Seats face the centre, so back-to-back
    edges between close tables work as before.
  - Adjacency and distances use the circle (`TableGeometry.Distance`, `Overlap`), not the square around it.
  - Fill order: around the ring, from the seat nearest the previous table, either way. The empty-seat gap rule works
    around the ring too.
  - Group names sit towards the group's seats, half-way out from the middle; the table number is in the middle.
  - Editor: "Round table, 8/10 seats" presets, a Shape switch (clears the table's seats in the plan, since seat
    numbers mean something else), a seat count field and one resize handle on the right of the circle.
  - Drawn as a rectangle with corner radius = half its size, so the three renderers needed no new shape.
- **Groups split far apart** (*changed*): new `ApartPersonPenalty` (default 200): per person not in the group's largest
  connected part (tables that are the same or adjacent), × (1 + d / 10 m), d being the distance from their table to
  the main part, capped at 30 m; another floor counts as 30 m. So 6 people at a table 2 m from the rest cost about 1,400, across the
  room up to 4,800. Presets: "Keep groups together" 600, "Preferences first" 100. Old projects get 200 when loaded.
  - The decoder also treats a jump in the fill order to a table that is not adjacent (another area of the room) like
    the end of a floor: a group that does not fit before the jump starts after it when there are seats to spare.
  - Found with a new "scattered" benchmark (main hall, side room, two single tables): at 99 % full with a short search
    (5,000 iterations, like a slow browser) 12 people in 2 groups were apart over 5 seeds; now none. The regular
    benchmarks did not show the problem, since all their tables are adjacent. First choices unchanged or slightly better.
  - The plan's warnings say how many of a split group sit away from the rest.
- **Nearly full venues** (*changed*), found with a real project in `tests/testplans` (676 people, 677 seats, 37 groups,
  three floors, ten separate areas of adjacent tables, groups of up to 47 at 50-seat tables). With one seat to spare
  every area has to be filled by whole groups exactly, which the plain "fill groups one after another" decoder only
  managed by luck. Four changes:
  - **Fill gaps with a group that fits:** when the next group would run past the end of an area, the decoder first
    seats the next group in the order that fits in what is left of the area. Likewise when it would run on to the next
    table with fewer than `MinimumFragmentSize` people on one of the two. The order search still decides which group
    that is, so it can steer it.
  - **Restart from the best instead of stopping:** after `StallIterations` without improvement the search goes back to
    the best order and goes on; it stops only after 4 such restarts in a row. Before, it stopped after 50,000
    iterations (about 1 s of the 8 s budget natively) while still hot.
  - **Polishing:** after the search (which now gets 85 % of the time), `PlanPolisher` takes each small part of a split
    group (a run of up to 16 seats at one table, not the group's largest) and tries swapping it with every equally long
    run of seats in the fill order, keeping the best swap that raises the score, until none does. This fixes splits
    the order search cannot express. With a seed (tests) it runs to the end, so results stay repeatable.
  - On that project over 5 seeds: before, 8 / 11 / 7 warnings (20k / 100k / 500k iterations) with people seated away
    from their group; after, 1 / 3 / 2, none away from their group, only "small part at a table", and scores about
    1,500–2,000 higher. The other benchmarks are unchanged within noise.
  - What is left: a 47-person group at a 50-seat table leaves 3 seats, and there are no groups of 2–4, so someone's 3
    people sit there unless the big group itself splits 42 + 5 over two adjacent tables. The search finds that in some
    runs but not all.
  - Considered: A* and other exact searches do not fit the whole problem (no goal state, no useful optimistic
    estimate, too many partial plans). An exact search may still pay off for the sub-problem "which whole groups fill
    each area exactly" (subset sums).
  - Diagnostic: `dotnet test tests/TablePlanner.Solver.Tests -c Release --filter TestPlanDiagnostics --logger "console;verbosity=detailed"`
    runs every `*.tableplanner.json` under `tests/testplans` and prints the warnings with the groups at each table.
- **Two search methods** (`SeatingPriorities.Method`, chosen next to Make plan, saved with the project; Re-optimize
  always uses Search, since it starts from the current plan):
  - **Search** (default): the order search above.
  - **Beam search** (`BeamSearch`): builds the plan group by group along the seat order and keeps the best W partial
    plans at each step. Tried next at each step: one group of each size still to seat (groups of the same size are
    interchangeable for packing) and the groups linked by a preference to the last one seated. A partial plan is rated
    by its score so far (groups still to come cost nothing for missing seats, `PlanEvaluator.ScorePartial`), plus an
    optimistic rest (each remaining group as if seated on its own at the start of the largest area, all its
    preferences met), minus about 750 when no combination of the remaining groups fills what is left of the current
    area exactly (a subset sum, within the seats that can be spared). This was the user's suggestion of rating "how
    close we are to fitting people together without gaps". Not A*: the estimate is not a guaranteed bound, and keeping
    only the best W makes memory and time predictable.
  - The beam follows the decoder's rule (no group may run past an area end while a smaller one would fill the gap), so
    its plan is exactly what decoding its order gives. Beams of width 4, 8, 16, … run while 40 % of the search time
    allows (4, 16, 64 with a seed); then the order search goes on from the best one, starting cooler (temperature 3
    instead of 40, so it refines rather than reshuffles) and cooling over the rest of the time; then polishing.
  - On the Party 2025 project (5 runs each, 2 s native, time limit only): Search: average score 28,867–29,192,
    3 warnings; beam alone: 28,731, 0 warnings, the same plan every time, but fewer first choices met (4 of 8); beam then
    search: 29,055, 1 warning. Differences between runs are about as large as between methods, hence the choice in the
    app rather than one fixed method.
  - Beam estimate (*changed* after the test plans below): a preference between a seated group and one still to come
    counts fully only while the seated group is in the area being filled; otherwise as "same floor" (0.2). Before, the
    beam counted it as achievable long after the area was full, and met clearly fewer preferences than Search on every
    plan (Party 2025: 6 of 24 first choices against 20).
- **Test plans** (`tests/testplans`): the user's Party 2025 and five made by `TestPlanGenerator` (seeded, so they can be
  made again): Wedding 176 (17 round tables of 8–10 and a head table with 2 disabled seats, 178 seats, 35 groups of
  2–12 with many preferences); Company dinner 430 (2 floors, long tables, a side room of round tables, a pillar
  blocking 4 seats, 458 seats, departments of 25–60); Club party 220 full (every seat taken, three separate areas incl.
  a rotated row, 3 disabled seats); Conference lunch 96 (16 round tables of 6/8, 110 seats, groups of 1–6, many mutual
  preferences; the tables are just over 2 m apart, so not adjacent, and a preference is only met at the same table);
  Festival 1080 (3 floors, 54 long and 12 round tables, 1165 seats, groups up to 89, larger than any table).
  Normal test runs check that both methods seat everyone there with no one away from their group.
  - Comparison (5 runs each, time limit only; 0.5 s native is about a slow browser, 2 s about 8 s in the browser):
    no one was away from their group in any run, and everyone was seated. Search had the higher score on 5 of 6 plans
    and met more preferences (e.g. Festival 138 vs 119 of 165 first choices, Company dinner 74 vs 67 of 80); beam search
    was best on Party 2025 (the nearly full real one: 0 warnings in 5 runs, 38 of 40 first choices, against 3 warnings
    and 34 for Search). Club party (exactly full) and Conference lunch: both without warnings. So Search stays the
    default; beam search is worth trying for very full venues with many separate areas.
  - Runs of the same method differ by several hundred points, often more than the methods do: compare over 5 or more
    runs. Clicking Make plan again is a cheap way to get a better plan.
  - Seen in the worst runs: polishing swaps can put a single person between two other groups at a table, or interleave
    two small groups; the score accepts it because it removes something worse, but it looks untidy. A group larger
    than its tables (89 at tables of 21–22) may still leave 3 at a fifth table.
- **Poster in A2 and A1** (plus A4, A3): the template gets `PageSize` in millimetres (browsers do not all know A1/A2 by
  name) and `Scale` (1 on A3/A4, 1.41 on A2, 2 on A1); the built-in poster multiplies its text and marks by it.
  The page notes that A2/A1 are best saved as PDF and printed at a print shop.
- **Guest page floor plan:** the plan sits in a viewer that fits the screen (about 60 % of its height) instead of growing
  past it. Pinch zooms around the fingers, one finger drags, a mouse wheel zooms; the page itself does not move on the
  plan. Zoom stays between 3 m wide and the whole floor (+30 %), and the middle stays on the floor. A group that spans
  more than 14 m (or several floors) starts zoomed on its first table, with a button per table and "All".
  Coming back from a group to the Floor plan tab returns to the same zoom and position.
- **Publishing:** `publish.ps1 -BasePath /<repo>/ [-Target <gh-pages checkout>] [-NoAot]` does the GitHub Pages steps:
  base href with trailing slash, `.nojekyll`, `.gitattributes` (`* -text`: with `core.autocrlf` git changed line
  endings in `dotnet.js`, so the browser blocked it for a wrong integrity hash), `404.html`, and drops the stale
  `index.html.br/.gz`. The target keeps `.git`, `pages/` and `CNAME`.
- **Warnings select their group:** on the plan page, clicking a warning about a group selects that group (as clicking
  it in the group list does: it is highlighted and its floor shown; clicking again deselects). More than 6 warnings
  can be shown in full ("… and N more" / "Show fewer").
- **Moving one person** (Shift+drag a seat on the plan): only that person moves; onto an empty seat they move there,
  onto someone else's seat the two swap (`PlanOperations.MovePerson`, one undo step). Seat pins (locks) follow the
  people. The plan's warnings update as usual, so a person moved away from their group shows up there. Without Shift a
  drag moves the whole group as before; the seat tooltip and the move hint mention Shift.
- **Several attempts in Make plan** (*changed*): runs of the same search differ a lot, and in tests the best of four
  short runs usually beat one run four times as long (Party 2025: 5 warnings over 5 tries against 3). Make plan now
  makes up to 4 independent attempts within the time limit and keeps the best. How many is measured, not fixed: 200
  plans are tried first to see how fast this machine is (a browser is much slower than a test run), and each attempt
  should get about 1,000 plans per group (`IterationsPerGroup`); so a large plan in a slow browser gets one long attempt.
  An attempt still improving when its time runs out also sends the rest of the time to one longer attempt.
  - Choosing: the highest score, but among plans within 1 % of it the one with fewest warnings (`CloseScoreShare`):
    the score already weighs warnings, but they are what people notice.
  - Every attempt starts from the same starting order (a shuffled start wasted time on large plans); the random search
    soon takes them apart. Each attempt is search (or beam then search) plus polishing, as before.
  - Re-optimize and seeded runs (tests) make one attempt, so they behave as before; `SolverOptions.Attempts` overrides.
    The new plan's note says "the best of N attempts".
  - Test plans, 5 runs at 2 s native (about 8 s in the browser), one attempt → adaptive: Party 2025 warnings 3 → 1 and
    no one away from their group (2 before); Company dinner and Club party higher scores; Festival and Wedding equal
    within noise.
- **Group levels** (*new*, 2026-11-10): groups can have a parent group (`Group.ParentId`), up to 3 levels, e.g.
  department › team › subteam. Optional: a flat list of groups behaves exactly as before.
  - Only bottom groups (without subgroups) have people and are seated. A parent group has `PeopleCount` 0 and its size
    is the sum of its bottom groups (`GroupTree.PeopleIn`), so existing totals stay right. When a bottom group gets
    subgroups, its own people, seats and seat/table pins move to a new subgroup "<name> – other" (its floor pin stays).
    Validation: a parent with people of its own, an unknown parent, a loop, or more than 3 levels is an error.
  - Groups page: tree order with indentation; parent rows show their people total. Select groups (checkboxes) and
    **Group…** puts them in a new parent group (under their common parent, if any); **Move to** another parent or the
    top level; **Ungroup** removes a parent and moves its subgroups up; deleting a parent also keeps its subgroups.
    Quick add can add straight into a parent group. Sorting by column is off while there are levels (it is a tree).
  - Solver: `ParentApartPenalty` (40) per person away from the main part of their parent group (the largest set of its
    tables that are the same or adjacent), × (1 + d / 10 m) as for groups; another floor counts as 30 m. A parent a
    level below the top counts double, so teams stay closer than whole departments; both far below a bottom group's
    200, so a large department can spread over areas and floors when it must, and then splits between whole teams.
    "Keep groups together" uses 120.
    Added 2026-11-11 (the user's example, `tests/testplans/Example party levels`: department "a" sat in a ring around
    another top-level group, which counted as together since the ring's tables are all next to each other):
    - **Sister groups next to each other**: two seat neighbours (next to, across, diagonal, back to back) from different
      groups with a parent group in common get a share of the weight two people of one group get: parent apart penalty
      / 100 (0.4 by default), × 1.5 with two parents in common, at most 0.8. A department then forms one block, with
      other groups at its edge rather than inside it.
    - **A mild pull towards the main table**: per person and metre (between table middles; another floor is 30 m) from
      the table where most of the parent group sits, 0.02 × the parent apart penalty (0.8 by default). Keeps the block
      compact rather than a long strip.
    - **Polisher**: also tries moving whole small groups (up to 16) that sit on another floor than most of their
      top-level group (the order search seldom moves them, as that shifts everyone after them).
    - Result: on the example, people away from their parent group went from 6 (search) and 30 (beam) to 0, and seat
      neighbours from different top-level groups from 51-53 to about 31-40. Plans without levels are unchanged; on
      "Company levels 440" no difference in warnings over 16 runs (that plan varies a lot between runs).
    - **Free seats together** (same day, the user: free seats were left in the middle, 3 here and 6 there; better all
      at one end, so part of a table can be taken away): two free seats next to each other (any neighbour relation)
      get 0.3 × that neighbour's weight. On the example the 11 free seats went from 3 tables to 2 (9 at the corner
      table's end). Neutral on the other test plans (within the run-to-run spread).
    - **Free seats at the outside** (same day): a free seat is worth up to 3 points by how far it is from the middle of
      its floor's seats, and the polisher tries swapping the 3 runs of free seats furthest inside with people elsewhere.
      Not enough yet: beam runs on the example still leave 6 free at the inner end of 108 (next to 107). To look at
      tomorrow, as part of the solver rethink.
  - Preferences can name a parent group, and a parent group can have preferences; met when any of its groups sits at
    the same or the next table. Floor/table pins on a parent hold its groups that have no pin of their own; locking a
    parent locks each of its groups (no seat pin on a parent).
  - Search: the start order goes department by department (largest first); two extra moves gather a parent group's
    groups into one block, or move that block; beam search links siblings. Without levels the moves and random draws
    are the same as before, so seeded results did not change.
  - Plan page: "Colour by group / level 1 / level 2" (`GroupColours.ForLevel`); the group list is a tree, and selecting
    a parent group highlights all its groups; its panel locks, unlocks, pins or takes out its groups (no Move).
    Warnings when a parent group is spread over parts of the room that are not next to each other, or over floors.
  - Group export/import: a "Parent group" column, written only when there are levels, in tree order with an empty
    number for parent groups.
  - Tests: a Departments benchmark (5 departments, two floors with separate areas): no one away from their department
    or team over 3 seeds, against 13 people without the penalty; a department of 160 on floors of 152 splits between
    whole teams. Test plan "Company levels 440" (6 departments with teams and subteams, department preferences):
    at 2 s native over 5 runs, Search 2 warnings, beam search 1. A first version counted groups instead of people per
    table for parent groups (about ten times too weak); this test plan found it.
- **Person lists** (*new*): a group can have names (`Group.Members`, a list of `Person`). Optional and need not be
  complete: the number of people (`PeopleCount`) stays what is seated, and "8 people, 5 named" means 3 unnamed guests.
  People do not get seats of their own.
  - Adding names beyond the number raises it; it never drops by itself. Removing a person makes the group one smaller
    (they are not coming); moving a person to another group takes one from the old and adds one to the new.
  - A parent group has no people, so names added to it go to its subgroup "<name> – other" (made when needed), which
    is kept with the department's teams like any other subgroup. A bottom group that gets subgroups hands its names to
    its "– other" group along with its people.
  - Validation: more names than people is a warning; names on a parent group are an error.
  - Groups page: a Names column ("5 of 8", or "Add names") opens a dialog to paste names (one per line, e.g. a column
    from Excel), rename, move to another group or remove; for a parent group it lists the names in all its groups. The
    search also finds people and shows the matching names.
  - Plan page: the group panel lists the names; the group list has a search for a group or a person, showing the
    matching names next to their group (so where someone sits is one search away).
  - Group export/import: a "Names" column (semicolon-separated), written when any group has names; on import, people
    with the same name keep their ID, a row without a number gets one per name, and names for a parent group go to its
    "– other" group. The import wizard (next) will also read one row per person.
- **Import wizard** (*new*): Import on the Groups page first asks where the list is and what each column holds, then
  shows the usual preview of what changes (added, updated, removed) before anything is applied.
  - Step 1: the sheet (Excel files with several sheets; it starts on the first one with a table) and the row with the
    headings, guessed as the row with the most known column names (or the fullest row), so titles and blank rows above
    the table are fine. CSV: the separator is guessed from the first 20 lines (a title line no longer confuses it);
    Danish Excel CSV in Windows-1252 keeps its æøå.
  - Step 2: a role per column, guessed from the heading in English and Danish (Afdeling, Team, Underteam, Navn,
    Fornavn/Efternavn, Antal, Kost, Bemærkninger, Ønske …); "Name/Navn" is a person next to a group column or two level
    columns, otherwise the group. A live preview shows the groups as a tree, the counts, and rows that will be left out.
    The choices are remembered in this browser for files with the same headings.
  - How rows become groups (`ImportMapping.Build`): with level columns and/or person names, a row's group is its path
    (level 1 › level 2 › level 3, then the group name if given). A row with a person's name adds that person, and a
    number on such a row is their party ("2" = them and a guest); a row without one is the group with its number. Rows
    may mix both. A group with subgroups that also has people of its own (someone only in a department) puts them in
    "<name> – other". A name used under two parents gets the parent's name added ("North (Sales)"). Per-person dietary
    info and notes are kept as "Name: text" in the group's. Without levels or person names (e.g. the app's own export)
    the regular reader is used, so such files import exactly as before.
  - Example files in `tests/testdata/imports` (made by `ImportExamples`; `TABLEPLANNER_GENERATE=1` writes them): the
    app's own format, people with 3 levels and an updated version (moved, added, removed people, a +1), mixed people and
    groups, a messy sheet (2 sheets, title, header on row 3, first/last name, a column to ignore), a Danish Excel CSV,
    errors on purpose, and 700 people. Each has a test.
- **Levels and names in print and on the guest page** (*new*): with group levels, the print page has "Group names":
  group only ("North", the default), department · group ("Sales · North") or the full path ("Sales › North"), for cards,
  the poster's list (sorted by the name as shown) and the guest page. Every template gets `OwnName`, `GroupPath`,
  `TopGroupName`, `ParentGroupName`, `HasParent` and `Names` (`GroupFields`), so own templates and the card designer
  can show e.g. the department as a small heading.
  - Names: cards always have them (printed, not published); the poster's data has them (the built-in poster does not
    show them). The guest page has them only with "Include people's names" (off by default, with a note that the page
    is public); then guests can search for their own name, and the matching name shows next to the group. The guest
    page also finds a group by its path, so searching a department lists its teams.
- **Drawings of floor elements** (*new*): instead of a plain box, each kind has a simple drawing (`ElementDrawing`),
  the same in the layout editor, on the plan page, and on the poster and guest page: stairs with a step every 30 cm and
  an arrow up; a bar's counter with stools in front; dishes on a buffet; a stage's thick front edge; a light chequered
  dance floor; a door and its swing for an entrance (and a green arrow out for an exit); WC figures; a cloakroom rail
  with hangers. Kinds with a direction turn with the element (the bottom side is the front); icons stay upright, with
  the label moved below them. Drawings bring their own colours (attributes), so the poster needs no stylesheet; the
  editor's CSS only styles the box's outline (`> rect`).
  - New kinds: Pillar (filled grey; square, or round with the "Round" switch), DJ booth (turntables and a mixer) and
    Photo booth (a camera). Existing "Custom" elements stay as they are.
  - `ElementGallery` (with TABLEPLANNER_GALLERY set to a file path) writes all kinds, upright and turned, to one page.
- **Walls** (*new*): for venues without a picture, walls can be drawn (`Floor.Walls`; a `Wall` is corner points,
  open or closed into a room outline). Only a drawing: the solver ignores them (tables either side of a wall still count
  as next to each other, as the user wanted).
  - Layout editor: "Walls" (W) draws: each click a corner, the next piece straight across or down from the last
    (Shift: any angle; Alt: off the grid) with its length shown; double-click or Enter ends the wall, a click on its first
    corner closes the room, Esc stops. "Room" adds a 20 × 12 m rectangle at the origin to adjust. A selected wall shows
    its corners (drag; Alt+click removes one), a "+" in the middle of each piece (drag to add a corner) and each piece's
    length; the panel shows corners, total length and a "Closed" switch. Walls move, copy, rotate (a quarter turn around
    their middle), nudge and delete with the selection like tables and elements, and are copied with a floor.
  - Drawn as thin dark lines (`WallDrawing`, the same SVG in the editor, plan page, poster and guest page) under
    everything else. An Entrance or Exit element placed on a wall cuts a gap in it (an SVG mask), so a door needs no
    tool of its own. Walls count towards the canvas and poster extent.
  - A finished open wall can be **continued**: start drawing walls (W) with a click on one of its loose ends, and the new
    corners are added to that wall (from that end); a click on its other end closes it into a room. Undo takes it back
    in one step ("Extend wall" / "Close wall").
- **Unavailable seats by clicking** (replaces the "Disable seats" switch): with one table selected, a click (press and
  release without dragging) on one of its seats makes it unavailable, and a click on it again frees it. Dragging from a
  seat still moves the table, and the first click on a table only selects it, so seats are not blocked by accident.
- **Group list on the plan page**: a group spread over many tables no longer squeezes its name. The table numbers are
  shortened (runs of three or more as a range, e.g. "108, 111–114", `TableNumberText`), cut short with "…" when there
  is still no room, and shown in full in a tooltip.
