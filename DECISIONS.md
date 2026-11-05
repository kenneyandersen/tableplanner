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

- A **mobile page:** a generated, self-contained HTML page (like the poster) that can be hosted
  separately, so guests can look up their group on their phone.
- Round tables, free rotation angles, partial group moves, per-group priority overrides,
  "should not sit near" rules, side-by-side plan comparison.
- A **drag-and-drop editor for the table cards** (place and size the name, table number, QR code etc. on the card
  without writing HTML), producing a card template. Wanted, but deliberately left for later (2026-11-05).

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

Status (2026-11-05): steps 1–9 done.

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
  "Seat N" small in a corner, for the staff; the people count is left out on seat cards.
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
