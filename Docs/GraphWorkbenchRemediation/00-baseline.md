# Graph Workbench Remediation — Ticket 00 Baseline

Recorded from the local checkout before behavior changes on 2026-08-27/28.

## Git baseline

- Starting branch: `sqlite-content-graph`
- Starting SHA: `42d834f` (`fix: reject input-socket edge sources when connecting nodes`)
- Remediation branch: `graph-workbench-remediation`
- Pre-existing working-tree change: `Content/GameContent.db` is modified. It was preserved and not reset, cleaned, or staged by this stack.
- Baseline diff stat: `Content/GameContent.db` binary change, 557056 bytes before and after.
- No remote push performed.

## Repository surfaces inspected

Read the current Graph Workbench documentation, SQLite migrations/schema, portable content model, Core graph VMs and continuation types, action executor/registry, WPF XAML/code/viewmodels, layout persistence, authoring repositories/commands/undo, Unity host scripts, and .NET/Unity test projects.

The current implementation is the SQLite → provider-neutral snapshot → `Game.Core` boundary with separate Session and Phase graph VMs, reusable Phases, stable PhaseExits, owned Action Instances, GOTO/RETURN continuation frames, and Nodify. Those foundations are retained by the remediation stack.

## Baseline verification

| Check | Result |
|---|---|
| `dotnet test Game.Workbench.sln --no-restore` | **204/204 passed** — 102 `Game.Core.Tests`, 102 `Game.Content.Sqlite.Tests` |
| WPF build (`Game.ReferenceHost.Wpf.csproj --no-restore`) | **Succeeded**, 0 errors, 12 existing CS0067 unused-event warnings in `GraphViewModels.cs` |
| WPF smoke launch | Host launched/responded for 5 seconds from the built executable, then was stopped; no crash observed |
| Unity editor availability | Unity `6000.5.9f1` is installed and the project compiles scripts in batch mode |
| Unity EditMode CLI suite | **Not measured**: two batch invocations exited 0 after asset/script refresh but produced no `-testResults` XML and no test-run summary. The exact logs are `Logs/GraphWorkbenchRemediation-Ticket00-EditMode-20260827224456.log` and `Logs/GraphWorkbenchRemediation-Ticket00-EditMode-Retry-20260827224520.log`. |
| SQLite `PRAGMA integrity_check` | `ok` on `Content/GameContent.db` |
| SQLite `PRAGMA foreign_key_check` | no rows on `Content/GameContent.db` |

The database reports migrations 1 (`core-schema-v1`), 2 (`core-graph-schema-v2`), and 3 (`wpf-authoring-layout`). Current counts include 11 Session graph nodes, 34 Phase graph nodes, 12 PhaseExits, 34 Action Instances, and 0 persisted WPF node-layout rows.

## Reproduced/documented gaps

### PhaseExit deletion error 19

On a temporary copy of the canonical database, with foreign keys enabled, deleting an exit referenced by an `action_instance_phase_goto` failed as:

```text
Error: stepping, FOREIGN KEY constraint failed (19)
```

This is the current `phase_exit`/`action_instance_phase_goto` `RESTRICT` path. The WPF delete handler asks for confirmation but does not first unassign referencing GOTO instances, so it can surface this failure and leave the UI refresh path incomplete.

### Layout reset/initial layout

`wpf_session_node_layout` and `wpf_phase_node_layout` are empty in the canonical DB. `SessionGraphViewModel.LoadFromDefinition` and `PhaseGraphViewModel.LoadFromDefinition` fall back to collection-order grid positions (`column * 220`, `row * 150`) rather than a connection-aware first layout. New nodes are saved at an offset based on node count. This means a graph without saved rows is not durably arranged according to topology; reconstruction can also re-use fallback positions for nodes that have no rows.

### Phase drag/drop

`OnSessionListMouseMove` is attached to `SessionList` in XAML and checks for a `PhaseDefinition`, while `PhaseList` has no corresponding mouse-move handler. The Session canvas drop target and graph-coordinate conversion exist, but the normal Phase-library drag source is miswired, so the requested Phase drag workflow is not reliable.

### Undo history lifetime

`OnSessionListChanged` and `OnPhaseListChanged` call `_stack.Clear()`. Selecting another Session or Phase therefore clears authoring history. The current implementation also reloads whole snapshots after undo/redo; this is safe for display but is not yet the requested one-history-per-open-database behavior across panes and selections.

### Runtime pacing and Card continuation

`GameSessionEngine.AdvanceOneCardAsync`, `SessionGraphVm.AdvanceAsync`, and `PhaseGraphVm.AdvanceAsync` still teach/enforce one Card per request through a local `cardBudget = 1`. There is no code-defined `WaitForContinue` Action Type. In `PhaseGraphVm`, `CardFinished` is raised immediately after `ExecuteSequenceAsync` returns, before transfer-aware Card continuation semantics are resolved; a Card GOTO therefore does not yet satisfy the remediation contract.

### Authoring coverage

The WPF graph rows currently expose checks, GOTO rows, decision prompt/options, and labels, but no complete explicit Action Instance type picker/list/reorder/clone editor. New Phase GOTO rows also select the first PhaseExit rather than starting Unassigned. `Show Sessions` currently opens a modal message instead of switching/filtering the Library.

## Hard-gate posture

No runtime, schema, WPF, Unity, or test behavior was changed for this baseline. Ticket 01 contract work may begin only after this record is committed and the contract is inspected against the current source.
