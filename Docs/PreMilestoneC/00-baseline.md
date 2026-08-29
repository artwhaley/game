# Pre-Milestone C Reliability — Ticket 00 Baseline

Recorded from the actual local checkout before implementation work. The
working branch was created from the required Milestone B base.

## Checkout and content state

- Required base branch: `milestone-b-authoring-readiness`
- Base HEAD: `ba0cd3e1ad1ffc4bcc6c4c536aa79104a469a29b`
  (`Place new graph nodes in viewport center`)
- Working branch: `pre-milestone-c-reliability`
- Remote tracking: base branch was even with `origin/milestone-b-authoring-readiness`
  before the local branch was created. This stack is local-only until explicitly
  pushed.
- Initial working tree: `Content/GameContent.db` modified; no other files
  modified. This is authored user content and is preserved as-is.
- Canonical DB sidecars: no `GameContent.db-wal` or `GameContent.db-shm` files
  were present.

## Automated baseline gates

| Check | Result |
|---|---|
| `dotnet test Game.Workbench.sln --no-restore --verbosity minimal` | 285 passed, 1 skipped, 0 failed (135 Core, 122 SQLite + 1 known skip, 11 Profile, 17 WPF) |
| `dotnet build DotNet/Game.ReferenceHost.Wpf/Game.ReferenceHost.Wpf.csproj --no-restore --verbosity minimal` | Passed; 0 warnings, 0 errors |
| `Content/GameContent.db` core migration ledger | Versions 1 through 5 applied; latest `milestone-b-cards-profile-selection` |
| `PRAGMA integrity_check` on canonical DB | `ok` |
| `PRAGMA foreign_key_check` on canonical DB | zero rows |

## Current implementation map inspected

- Nested action scope and registry: `Assets/Scripts/Portable/Game.Content/ActionOwnerScope.cs`, `Assets/Scripts/Portable/Game.Core/ActionTypeRegistry.cs`, `Assets/Scripts/Portable/Game.Core/ActionExecutor.cs`.
- WPF nested PromptChoice construction: `DotNet/Game.ReferenceHost.Wpf/GraphViewModels.cs`, especially `ToActionRow`; the nested sequence currently receives the enclosing sequence owner scope.
- Session runtime transfer ownership: `Assets/Scripts/Portable/Game.Core/SessionGraphVm.cs`; phase action execution: `Assets/Scripts/Portable/Game.Core/PhaseGraphVm.cs`.
- Catalog UI/search: `DotNet/Game.ReferenceHost.Wpf/MainWindow.xaml` and `DotNet/Game.ReferenceHost.Wpf/MainWindow.MilestoneB.cs`.
- Runner log/profile/selection surfaces: `DotNet/Game.ReferenceHost.Wpf/ReferencePlayerWindow.xaml(.cs)`, `DotNet/Game.ReferenceHost.Wpf/MainWindow.Preview.cs`, `DotNet/Game.ReferenceHost.Wpf/MainWindow.xaml.cs`, and `DotNet/Game.ReferenceHost.Wpf/SelectionDiagnosticsWindow.xaml(.cs)`.
- Action persistence: `DotNet/Game.Content.Sqlite/ActionSequenceWriter.cs`, `ActionInstanceRepository.cs`, and `AuthoringCommands.cs`; the card editor uses `ReplaceCardSequenceCommand`.
- Graph persistence and undo: `DotNet/Game.Content.Sqlite/GraphRepositories.cs`, `AuthoringCommands.cs`, and `AuthoringUndo.cs`; current node-delete undo comments/paths regenerate edge IDs.
- Canonical schema/migrations: `DotNet/Game.Content.Sqlite/CoreMigrations.cs` and embedded `SQLITE-SCHEMA-V1.sql` through `SQLITE-SCHEMA-V5-MILESTONE-B.sql`.

## Baseline gate decision

The required branch, build, test, schema, and canonical-DB checks passed. The
only pre-existing working-tree change is the user-authored canonical database;
it must not be reset, replaced, or included accidentally in code-only commits.
Implementation may proceed under Ticket 01's frozen scope contract.
