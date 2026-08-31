# Final Milestone C Readiness Hotfix — Baseline

Date: 2026-08-30

## Repository state

- Required base branch: `final-pre-milestone-c-corrections`
- Required and actual base commit:
  `602bd205c8dd08625b41243a3e97bfbed7b5d9ac`
- Working branch: `milestone-c-readiness-hotfix`
- `git status --short` was empty immediately before the branch was created.
- The previously running WPF process was closed to release its output DLLs and
  canonical database handle. On shutdown it flushed three existing
  `wpf_viewport_state` changes. A logical SQLite dump comparison against HEAD
  confirmed that those three viewport rows are the only database differences;
  they are preserved as authored UI state.
- Current canonical safety backup:
  `C:\Users\artwh\AppData\Local\Temp\GameContent.milestone-c-readiness.20260830-164151.bak`
  (SHA-256
  `55A22AC7DAB407EE272C07A8C79E1E11216EB3D7334585E58B441740E79BC198`,
  708,608 bytes).

## Baseline gates

| Gate | Result |
|---|---:|
| `Game.Core.Tests` | 161 passed |
| `Game.Content.Sqlite.Tests` | 134 passed |
| `Game.Profile.Sqlite.Tests` | 11 passed |
| `Game.ReferenceHost.Wpf.Tests` | 36 passed |
| Total | 342 passed |
| WPF project build after releasing old process lock | 0 warnings, 0 errors |
| Canonical schema ledger | v8 |
| `PRAGMA integrity_check` | `ok` |
| `PRAGMA foreign_key_check` | zero rows |

Canonical authored counts are 1 Session, 1 Phase, 3 Cards, 1 Resource,
0 Dialog Tags, 0 Dialog Snippets, and 1 Smart Toy Capability. No WAL/SHM
companion files were present.

## Exact implementation map before behavior changes

### ActionSequence editor and lookup snapshots

- `DotNet/Game.ReferenceHost.Wpf/ActionEditorRegistry.cs`
  - `ActionSequenceEditorViewModel` begins at line 169.
  - Its constructor copies Resource, Toy Capability, Toy Pattern, and Dialog Tag
    choices into per-editor `List<>` instances at lines 190–195.
  - `CreateDefaultInstance` assigns initial toy capability/pattern IDs at lines
    251–284.
  - `RefreshPicker` removes Wait For All from PromptChoice descendants at lines
    287–305.
- `DotNet/Game.ReferenceHost.Wpf/GraphViewModels.cs`
  - `GraphEditorViewModel.ConfigureActionCatalog` constructs graph-editor
    catalog options at lines 781–804.
  - Card, Phase ActionNode, PhaseDecision, SessionDecision, and recursively
    nested PromptChoice editors are constructed from those snapshots in this
    file and `MainWindow.MilestoneB.cs`.

### Set Toy Pattern validation and action creation

- `DotNet/Game.ReferenceHost.Wpf/GraphViewModels.cs`
  - `ActionRowData.ValidationMessage` is at lines 299–328.
  - Timed Toy Pattern validates Capability at lines 318–320.
  - Both toy actions validate Pattern Resource at lines 321–323.
  - Set Toy Pattern does not currently validate Capability.
- `DotNet/Game.ReferenceHost.Wpf/MainWindow.xaml.cs`
  - Normal `+ Action` creation guard is at lines 469–510 and only checks
    `ToyActivity` at lines 484–488.
  - Action Browser/drop append is at lines 796–836 and only checks
    `ToyActivity` at lines 804–808.

### Smart Toy Capability usage and deletion

- `DotNet/Game.Content.Sqlite/CatalogRepositories.cs`
  - `CountSmartToyCapabilityUsage` at lines 287–296 counts only Card and Session
    Type relation tables; it omits both toy Action subtype tables.
- `DotNet/Game.ReferenceHost.Wpf/MainWindow.MilestoneB.cs`
  - usage display is at lines 347–352;
  - deletion preflight is at lines 506–579 and uses only that aggregate count.
- `DotNet/Game.Content.Sqlite/AuthoringCommands.cs`
  - `DeleteCatalogEntryCommand` at lines 1627–1678 performs a raw Capability
    delete, so the command layer currently relies on FK failure.
- `DotNet/Game.ReferenceHost.Wpf/CardEditBuffer.cs`
  - recursive sequence traversal exists for sequence lookup at lines 272–291,
    but no unsaved Resource/Dialog Tag/Capability reference inspector exists.

### Resource and Dialog Tag CRUD

- `DotNet/Game.Content.Sqlite/ResourceRepository.cs` owns Resource create,
  rename, usage, and safe delete.
- `DotNet/Game.Content.Sqlite/DialogCatalogRepository.cs` owns Dialog Tag CRUD,
  usage, and safe delete.
- `DotNet/Game.Content.Sqlite/FinalPreMilestoneCCommands.cs` owns semantic
  Resource and Dialog Tag commands.
- `DotNet/Game.ReferenceHost.Wpf/MainWindow.FinalPreMilestoneC.cs` owns Resource
  browser create/rename/delete callbacks.
- `DotNet/Game.ReferenceHost.Wpf/MainWindow.MilestoneB.cs` owns Dialog Tag and
  Smart Toy catalog create/update/delete callbacks.

### PromptChoice blocking and Wait For All restrictions

- `Assets/Scripts/Portable/Game.Core/ActionTypeRegistry.cs`
  - PromptChoice metadata is at lines 263–274. It defaults to blocking and is
    not configurable, but does not declare `IsAlwaysBlocking`.
  - `ValidateScope` at lines 67–76 checks owner scope only.
- `Assets/Scripts/Portable/Game.Core/ActionExecutor.cs`
  - always-blocking validation currently happens during dispatch at lines
    108–116 rather than at the registry validation boundary.
- Wait For All is specially prohibited in four authoring/validation paths:
  - `ActionSequenceScopeValidator.cs` lines 87–91;
  - `ActionEditorRegistry.cs` lines 293–294;
  - `MainWindow.xaml.cs` lines 799–803 (append);
  - `MainWindow.xaml.cs` lines 864–868 (copy/drop).
- SessionGoto remains excluded from nested PromptChoice scope by
  `ActionOwnerScopes.NestedPromptChoice` plus normal registry scope validation.
- PromptChoice authoring remains capped at three options in
  `MainWindow.xaml.cs` lines 900–919.

### Snapshot/reference validation

- `DotNet/Game.Content.Sqlite/GameContentSnapshotLoader.cs` reconstructs the
  full snapshot and currently runs only `ActionSequenceScopeValidator.Validate`
  at lines 75–80.
- `Assets/Scripts/Portable/Game.Core/GameSessionEngine.cs` constructs
  `ContentCatalog` at lines 57–73 and has no explicit recursive reference
  validation boundary.
- `Assets/Scripts/Portable/Game.Core/ContentCatalog.cs` indexes core entities and
  detects duplicate/missing IDs, but it does not validate Action Resource kinds,
  toy Capability references, DialogFromTags references, or Dialog Snippet tags.

This map completes Ticket 00's hard gate. No application behavior was modified
before it was recorded.
