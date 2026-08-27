# WPF Workbench Authoring Specification

> Installed from packet `Game_GraphVM_WPF_Authoring_Stack/WPF-AUTHORING-SPEC.md` (Ticket 01). The WPF reference host evolves into the Workbench; no second desktop app.

## Technology

- Existing `.NET 10` WPF reference host evolves into the Workbench; do not build a second desktop app.
- Add NuGet `Nodify` pinned initially to `7.3.0`.
- Use MVVM enough to keep graph state/testability sane, but do not introduce a broad framework rewrite solely for ideology.
- Existing explicit repositories remain the persistence pattern. Add explicit graph/action repositories/services; no generic repository abstraction.

## Four-pane shell

Default Grid weights:

```text
Library 18* | Session 32* | Phase 32* | Inspector 18*
```

Three `GridSplitter`s.

All panes have practical MinWidth (choose values during implementation so a pane cannot collapse/disappear). Persist ratios as WPF/user workspace state, not core content.

### Pane 1 — Library

Initial sections/tabs/filter modes:

- Sessions
- Phases

Cards/Actions/Equipment/etc can appear later as those authoring surfaces are built.

Library supports fast text filter.

`Show Sessions` from Phase header activates a temporary Sessions filter for uses of that Phase and provides a clear 'clear filter/back' action.

### Pane 2 — Session Graph

Header includes current Session title and a compact node palette.

Palette node types:

- SessionStart
- Phase
- SessionDecision
- SessionEnd

SessionStart is singular; palette creation becomes disabled if one exists.

Node movement/pan/zoom/connect/delete use Nodify.

### Pane 3 — Phase Graph

Header:

- current Phase title;
- usage `N sessions · M placements`;
- `Show Sessions`;
- `Make Unique`;
- Exits strip + button.

Node palette:

- PhaseEntry
- CardExecutor
- VariableCheck
- ActionNode
- PhaseDecision
- Return

PhaseEntry is singular and creation disabled once present.

Provide an Action-Type palette/command surface appropriate to current owner scope so Action Types can be dragged into ActionNode/Decision option Action lists. An inline `+ Action` searchable menu is also acceptable as a complementary fast path; do not require inspector navigation.

### Pane 4 — Inspector

Detailed properties for current selection.

Frequent graph-local controls stay inline when specified. The Inspector is not a dumping ground that forces repetitive scrolling.

## Inline Action editing

Action rows inside ActionNode/Decision nodes render their primary parameters directly where practical.

Mandatory:

### Phase GOTO row

```text
GOTO  [ Success ▼ ]
```

A real WPF `ComboBox` inside the Nodify node. Items are current PhaseExit records. It stores selected stable exit ID.

No `Create Exit` button in this row. Exits are managed only by Phase header strip.

Ensure ComboBox pointer interaction does not initiate node dragging/connection gestures.

### SessionGoto row

```text
SESSION GOTO  [ Bonus Path ]
```

Text/label edit is inline. The Action Instance owns a unique output socket rendered on the containing SessionDecision node immediately.

### Increment progress

```text
Progress  [+10]
```

### Modify temperature

```text
Happiness  [+10]
```

### Cutscene

Render Resource selection inline if compact; detailed binding status can remain inspector/future work.

## Session node rendering

### SessionStart

- title `Session Start`;
- one output connector;
- selecting opens Session metadata in Inspector.

### PhaseReference

- show referenced Phase title;
- one input connector;
- output connectors dynamically/projected from Phase exits;
- connector display labels come from PhaseExit.Name;
- stable Session placement port IDs preserve Session edges independently of rename.

Selecting it loads referenced Phase into Phase pane and establishes current-placement context for Make Unique.

### SessionDecision

- prompt visible;
- up to 3 options;
- each option's Action rows visible/expandable enough for direct editing;
- common normal output;
- every SessionGoto instance creates its own labeled additional output socket.

### SessionEnd

- input only;
- no outputs.

## Phase node rendering

### PhaseEntry

- singular;
- one normal output;
- selecting edits Phase metadata.

### CardExecutor

- one input, one normal output;
- clearly labeled `Draw / Execute Card`.

### VariableCheck

Compact inline editor:

```text
[Temperature ▼] [Happiness ▼] [< ▼] [10]
```

or

```text
[Phase Progress ▼] [>= ▼] [100]
```

Outputs labeled True/False.

### ActionNode

- ordered Action rows;
- drag/reorder;
- one normal output.

### PhaseDecision

- prompt;
- max 3 options;
- each option owns Action Instance sequence;
- one common normal output.

### Return

- terminal node;
- no normal output.

## Live cross-canvas propagation

When a Phase exit name changes:

- every visible Session PhaseReference socket label updates immediately;
- persisted Session edge stays intact.

When an exit is added/deleted on a Phase with <=1 placement:

- projected Session placement ports update transactionally;
- visible high-level node updates immediately.

When shared topology mutation triggers Make Unique:

- Session canvas PhaseReference title/reference changes to clone;
- Phase pane switches to clone;
- high-level wiring stays connected via remapped projected ports;
- attempted edit is then applied.

## WPF layout tables

Use `wpf_*` extension tables, not core schema columns, for at least:

- session node X/Y;
- phase node X/Y;
- per-graph viewport zoom/pan;
- workspace pane ratios if persisted.

Core migrations must ignore these.

## Continuous persistence

Each semantic edit commits to SQLite immediately/transactionally.

Status may show `Saved` / error. Do not build a fake document Save model on top of SQLite.

## Human review requirements

The implementation stack deliberately pauses for user review after:

1. four-pane/Nodify shell;
2. real Session canvas;
3. real Phase canvas;
4. GOTO exits + live cross-canvas propagation;
5. Make Unique / undo behavior;
6. live Core preview/debug flow.

The executor should present screenshots and the exact interactions ready to test at each review gate.
