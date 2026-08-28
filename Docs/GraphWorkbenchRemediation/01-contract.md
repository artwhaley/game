# Graph Workbench Remediation — Ticket 01 Contract

This contract freezes the remediation direction before code changes. It applies to the existing `sqlite-content-graph` architecture and is not a redesign.

## Current-source inspection

The Ticket 00 audit was checked against the source at `42d834f`:

| Contract area | Current source | Remediation requirement |
|---|---|---|
| Runtime API | `GameSessionEngine.AdvanceOneCardAsync`, `SessionGraphVm.AdvanceAsync`, and `PhaseGraphVm` use a one-card budget | Add canonical run-until-yield execution; CardExecutor completion is not a yield |
| Explicit pacing | No `WaitForContinue` instance, key, registry entry, or executor case exists | Add a code-defined, owner-scoped no-parameter Action Instance |
| Card events | `PhaseGraphVm` fires `CardFinished` immediately after the current sequence call returns | Keep Card identity/continuation through GOTO, and fire exactly once only after true sequence completion |
| Phase GOTO authoring | `MainWindow.OnAddGoto` and decision GOTO creation choose `Exits[0]` | New GOTO must persist nullable/unassigned and render warning styling |
| PhaseExit deletion | `phase_exit` is referenced by GOTO with `RESTRICT`; projected sockets cascade, but GOTO assignments are not cleared | Implement counted warning, force-delete transaction, refresh, and exact undo |
| Workbench layout | Four horizontal columns and horizontal graph headers/tool strips | Stack Session above Phase in the center and move graph-local tools to left vertical toolbars |
| Layout persistence | Existing tables/load hooks exist, but no-layout fallback is collection-order grid placement and rows are empty in the canonical DB | Add deterministic connection-aware initial layout and partial-layout preservation |
| Library workflow | Phase drag handler is attached to `SessionList`; Show Sessions is modal-only | Fix Phase drag source and make Show Sessions a Library mode/filter |
| Undo lifetime | Selection handlers clear `_stack` | Keep one history for the open DB, including cross-pane selection changes |
| Action authoring | Inline check/GOTO/decision rows exist, but no complete explicit Action Instance editor | Add explicit registry-driven type/list/reorder/delete/clone/parameter editing |

## Frozen runtime contract

- The graph is authoritative. There is no one-card-per-`Advance` gameplay rule.
- `CardExecutor` is not a pause boundary. One canonical `RunUntilYieldAsync` call continues through nodes, transfers, and multiple Cards until an explicit yield, SessionEnd, cancellation, awaited host/input operation, or clear runtime/content error.
- A configurable transition/action safety budget applies per run, defaulting to approximately 10,000. Exceeding it throws a contextual `GraphExecutionException`; it never inserts an invisible gameplay pause.
- `WaitForContinue` is a code-defined Action Type with an owner-scoped Action Instance and no v1 parameters. It preserves the next Action position, does not push a GOTO frame, and does not fire `CardFinished`.
- A conventional new Card has ordinary owned Action Instances in this order: `WaitForContinue`, then `IncrementPhaseProgress` amount `10`. Authors may delete, reorder, or edit them. No migration blindly mutates arbitrary future Cards.
- `CardStarted` fires once before the first Card Action. `CardFinished` fires once only after the entire Action sequence completes. It does not fire on GOTO transfer, WaitForContinue, cancellation, or SessionEnd that prevents RETURN.
- Card GOTO preserves Card identity, owner/list, next Action index, active PhaseRun, graph locus, progress, RNG, history, and future Phase-local state. RETURN resumes the exact remaining actions, then completes the Card and follows the CardExecutor normal edge.
- GOTO always captures resumable continuation. RETURN restores the newest exact frame. Nested and recursive same-Phase calls are legal. SessionEnd is absolute and discards all suspended continuations.
- Session-global Temperatures persist across GOTO/RETURN. PhaseRun-local progress, graph locus, Card/action continuation, Card-selection RNG, Card history/cooldowns/anti-repeat state, and future Phase-local variables belong to the suspended PhaseRun and resume unchanged.
- Progress changes only through authored Action Instances; there is no implicit Card progress.
- VariableCheck remains structured numeric v1: PhaseProgress, named Temperature, or existing numeric runtime stat; operators `< <= == != >= >`; literal numeric comparison; True/False outputs.

## Frozen PhaseExit and authoring contract

- `PhaseExit` remains stable first-class reusable-Phase interface data. Names are display labels; wiring uses stable IDs. Multiple internal GOTO instances may reference one exit.
- The user-facing spelling is **Phase Exit** / **Phase Exits**. The low-level Phase canvas has no declaration nodes; its left toolbar owns the ordered Phase Exits list, add button, rename, and delete command.
- New Phase GOTO instances start Unassigned (`NULL`), appear red/warning-styled, and have an inline ComboBox with `Unassigned` plus current exits. No freeform exit names and no implicit first-exit selection.
- If placement count is greater than one, add/delete PhaseExit topology is locked and offers Make Unique / Cancel. Rename and reassignment among existing exits remain allowed. An unused exit is not auto-deleted.
- If placement count is zero or one, add/delete topology is allowed.
- Deleting an in-use exit on an editable Phase warns with GOTO and Session-edge use counts. Delete Anyway snapshots all affected data, sets referencing GOTO IDs to NULL, disconnects Session edges projected from that exit, deletes the exit, commits once, refreshes both graphs/dropdowns, and is undoable. Cancel changes nothing. No SQL error 19 path remains.

## Frozen Workbench/layout contract

- Preserve Nodify and the two graph levels. The shell is Library left (~18%), stacked Session graph upper-center and Phase graph lower-center (~64% combined), Inspector right (~18%). Left/right splitters and the Session/Phase vertical splitter are draggable and enforce useful nonzero minimums.
- Each graph has a narrow left vertical toolbar; inputs are on the left and outputs on the right. Session node types remain SessionStart, PhaseReference, SessionDecision, SessionEnd. Phase node types remain PhaseEntry, CardExecutor, VariableCheck, ActionNode, PhaseDecision, and Return/equivalent.
- Node X/Y coordinates are durable editor metadata keyed by stable graph-node ID. First load with no layout uses one deterministic connection-aware left-to-right layout and persists it. Partial layouts preserve every saved coordinate and place only missing nodes. Adding/deleting nodes, edges, or PhaseExits never moves an already-positioned node. Navigation and restart restore exact coordinates. A drag is one undo command.
- The Library supports Sessions/Phases modes or tabs, search/filter, Phase drag/drop that creates a new placement referencing the original Phase, and Show Sessions that switches to Sessions mode with a `Uses Phase X` filter. Make Unique remains placement-scoped and undoable.

## Explicit preservation list

The following existing decisions are out of scope for reconsideration:

- Persistence remains `GameContent.db -> Game.Content.Sqlite -> portable snapshot -> Game.Core`; Core never issues SQL.
- Session and Phase remain separate graph layers; they are not merged.
- Phases remain reusable low-level graphs. Copy Session keeps references shared. Make Unique clones only the selected placement's Phase.
- Action Types remain code-defined. Action Instances remain owner-scoped, own their parameter values, and use no EAV model.
- Stable-ID persistence is transactional and normal authoring never truncates/rebuilds the whole DB or deletes/reinserts all entities.
- SQLite remains canonical authored content. Portable snapshot boundaries remain provider-neutral. WPF/Unity stay hosts and do not move authoring into Unity.
- PhaseSlot, query-selected Phases, min/max-card progression, implicit Card increment, reusable configured Action entities, embedded Phase copies, JSON canonical persistence, Unity-first authoring, and the old one-card-per-Advance pacing rule are not to be restored.
- Scope exclusions remain: no Kinks, Equipment, Smart Toys, Card-selection weighting, full speculative Card editor, update/monetization systems, user-profile mechanics, or new Unity presentation work.

## Gate

Ticket 01 is documentation-only. The contract has been inspected against the current Core, SQLite, WPF, and Unity surfaces above. Implementation begins with Ticket 02 only after this document is committed.
