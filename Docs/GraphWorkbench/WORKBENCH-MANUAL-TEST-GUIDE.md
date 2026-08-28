# Graph Workbench — Manual Test Guide (Ticket 21)

This is the human walkthrough for the WPF Graph Workbench
(`DotNet/Game.ReferenceHost.Wpf`, run the exe or F5 from `Game.Workbench.sln`).
Everything persists immediately to the canonical SQLite DB
(`Content/GameContent.db` via `--db <path>` override) — **the database is the
source of truth**. To avoid dirtying the committed DB during testing, run with
`--db <tempfile.db>` and let the app migrate it, or restore the DB afterwards.

## Layout & dark mode

- Four panes: **Library** (sessions + phases), **Session Graph**, **Phase
  Graph**, **Inspector** — all dark; splitters drag and the ratios persist.
- Toolbar: Run Session (legacy player window), **Undo/Redo** (Ctrl+Z / Ctrl+Y),
  **Preview ▸** (live debugger strip), Reset Layout.

## Session authoring (T13)

1. **New Session** already contains its singular **Start** node; add **+ Phase**
   (pick a phase in the Library first — drag one from the Phase list onto the canvas too),
   **+ Decision**, **+ End**.
2. **Connect** nodes by dragging from an output socket to a node body. A
   PhaseReference node shows **one projected exit socket per exit** of the
   referenced phase (live projection).
3. **Move** nodes (undo restores the original spot with one Ctrl+Z), **delete**
   with Delete / the Delete button (undo restores node + edges).
4. Inspector: selecting Start edits the session title/type; selecting a
   PhaseReference **opens that phase below** (placement context).

## Phase authoring (T14–T16)

1. **New Phase** already contains its singular **Entry** node; add **+ Draw**,
   **+ Check**, **+ Action**, **+ Decision**, **+ Return**; connect them.
2. **Exits strip** (above the phase canvas): rename any exit; add/delete while
   the phase has ≤1 placement. Renaming never breaks wiring.
3. Inline editors inside nodes: **Check** (source/operator/literal),
   **Action** → **+ exit action** (GOTO with an exit dropdown), **Decision** →
   options with their own GOTO rows (session: label + unique socket; phase:
   exit dropdown).
4. Double-click a phase in the Library to open it; double-click a
   PhaseReference on the Session canvas to jump to its phase.

## Reuse & port lock (T17)

- **Copy Session** (toolbar or button) deep-clones with shared phase
  references kept shared.
- **Duplicate Phase** deep-clones exits, graph, action instances, layout.
- **Make Unique** detaches the selected placement to its own phase clone.
- With a phase placed in 2+ sessions, adding/deleting an exit is blocked —
  the popup offers **Make Unique** to apply the edit to the clone. **Undo
  that whole operation with ONE Ctrl+Z** (placement returns to the shared
  phase, clone is removed).

## Undo/redo (T18)

- Drag a node, type a title/prompt, change a check, add/delete an exit, copy a
  session, make unique — then **Ctrl+Z** repeatedly: each action undoes as one
  step (typing/dragging coalesce). **Ctrl+Y** redoes. Buttons reflect state.
- Switching sessions/phases preserves the shared history; edits remain undoable
  across the two graph panes.

## Live preview / debugger (T19)

1. **Preview ▸** opens the strip. **Start ▶** runs the CURRENT session through
   the real Core engine on a **fresh snapshot** — edit the DB during a run and
   the running snapshot is unaffected.
2. **Continue** resumes after an authored WaitForContinue; cards without Wait
   flow automatically. **Restart ↺** takes a fresh snapshot;
   **Stop ■** halts.
3. Watch the amber **node rings** on both canvases follow the run; the
   transfer edge into the current session node highlights; check nodes light
   the taken true/false branch; prompts/cutscenes render in the strip.
4. Readouts: temperatures, phase progress vs target, active phase + placement
   (PhaseRun identity), current node, card, continuation stack depth + frames,
   and runtime errors in red.

## Persistence

- Restart the app: sessions, phases, graphs, layouts, viewport, exits, ports,
  and undo history state (in-memory) all reload from the DB.
- **Reset** (if the toolbar has it) restores default pane ratios.

## Known UX gaps (from the review gates)

- Library has no tabs/search-yet-scale story for thousands of entries (filter
  boxes exist; tabs are future work).
- The legacy Run Session window has no progress/temperature debug readouts —
  the Preview strip is the debugging surface.
- Action instances can be added, edited by type, reordered, and deleted inline;
  PhaseGoto exposes an explicit Unassigned state with a red warning.
