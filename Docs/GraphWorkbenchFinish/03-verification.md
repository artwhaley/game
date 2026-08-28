# Graph Workbench Finish — Verification

All commands below were run from the repository root after the implementation
changes, sequentially to avoid shared build-output contention.

## HARD gates

```text
dotnet build DotNet/Game.ReferenceHost.Wpf/Game.ReferenceHost.Wpf.csproj --no-restore --nologo --verbosity minimal
```

Passed with 0 warnings and 0 errors.

```text
dotnet test DotNet/Game.Content.Sqlite.Tests/Game.Content.Sqlite.Tests.csproj --no-restore --nologo --verbosity minimal
```

Passed: 112; skipped: 1; failed: 0. The skipped canonical playback test is an
existing guard for the known unconnected projected PhaseExit in the committed
database.

```text
dotnet test Game.Workbench.sln --no-restore --nologo --verbosity minimal
```

Passed: 105 Core tests, 112 SQLite tests, and 4 WPF tests; skipped: 1; failed:
0. The WPF build completed as part of this solution gate.

## Canonical content checks

`Content/GameContent.db` remains tracked and unchanged by this pass. The final
checks returned:

- `PRAGMA integrity_check`: `ok`.
- `PRAGMA foreign_key_check`: no rows.
- Core migration ledger: versions 1 through 4, ending at
  `phase-goto-nullable-exit`.
- Counts: 2 sessions, 6 phases, 8 cards, 34 action instances, 11 session
  layout rows, and 34 phase layout rows.
- No `GameContent.db-wal` or `GameContent.db-shm` sidecars.

## Unity and human gates

The Ticket 00 Unity batch attempts exited successfully but did not emit the
requested Test Runner XML, so Unity tests remain unavailable rather than
counted as passed. Interactive WPF acceptance is still a human gate. Use the
finish walkthrough in [`../GraphWorkbench/WORKBENCH-MANUAL-TEST-GUIDE.md`](../GraphWorkbench/WORKBENCH-MANUAL-TEST-GUIDE.md)
to check layout persistence, mixed decision sequences, Make Unique, dialogs,
and the live preview.
