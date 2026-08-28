# Graph Workbench Finish — Ticket 00 Baseline

Date: 2026-08-28  
Base branch: `graph-workbench-remediation`  
Finish branch: `graph-workbench-finish`

## Repository state

- Worktree was clean before the branch was created.
- Starting SHA on `graph-workbench-remediation`: `8e33503362959ad07418cfc688a650df973233a3`.
- Finish branch was created locally from that SHA.
- No user changes were reset, cleaned, or staged.

## Baseline verification

Command:

```text
dotnet test Game.Workbench.sln --no-restore --nologo --verbosity minimal
```

Result:

- `Game.Core.Tests`: 105 passed, 0 failed.
- `Game.Content.Sqlite.Tests`: 109 passed, 1 skipped, 0 failed.
- `Game.ReferenceHost.Wpf.Tests`: 2 passed, 0 failed.
- Total: 216 passed, 1 skipped, 0 failed.
- The existing skipped canonical playback test preserves the known unconnected projected PhaseExit in the authored database.

Command:

```text
dotnet build DotNet/Game.ReferenceHost.Wpf/Game.ReferenceHost.Wpf.csproj --no-restore --nologo --verbosity minimal
```

Result: succeeded with 0 warnings and 0 errors.

SQLite checks on `Content/GameContent.db`:

- `PRAGMA integrity_check`: `ok`.
- `PRAGMA foreign_key_check`: no rows.
- Core schema ledger: v1 `core-schema-v1`, v2 `core-graph-schema-v2`, v3 `wpf-authoring-layout`, v4 `phase-goto-nullable-exit`.
- Content counts: 2 Sessions, 6 Phases, 8 Cards, 34 Action Instances.
- Layout counts: 11 Session node rows, 34 Phase node rows.
- No `GameContent.db-wal` or `GameContent.db-shm` sidecars were present.

Unity 6000.5.9f1 baseline commands were run for EditMode and PlayMode. Both processes exited with code 0 and compiled/imported scripts, but neither emitted the requested Test Runner XML result file. Unity tests are therefore unavailable, not passed. Logs:

- `Logs/GraphWorkbenchFinish-Ticket00-EditMode-20260828034921.log`
- `Logs/GraphWorkbenchFinish-Ticket00-PlayMode-20260828034921.log`

## Source inspection recorded

- Decision option persistence already owns ordered ActionSequences in the current schema/model; the remaining specialization is primarily in WPF view-model/editor construction.
- The current Action editor is inline and type-aware for Phase ActionNodes, but Decision options still use GOTO-specific rows instead of the same reusable sequence editor.
- PhaseExit references are nullable and stable in the persistence model; UX/dialog and clone-follow-up paths require audit.
- `AuthoringCommandStack.PushOrMerge` currently executes merged state, but its merged path returns before clearing redo or firing `Changed`.
- `ReplaceGraph` APIs remain publicly callable and require import/restore-only naming or visibility.
- The WPF shell still has simultaneous Library Session/Phase columns, Start/Entry palette buttons, and substantial authoring code in `MainWindow`.
- Nodify node location binding from the prior finish fix is present and covered by the existing 2-test WPF binding suite; it must not regress.

## HARD gate

Passed. Baseline is recorded before finish-stack implementation changes.
