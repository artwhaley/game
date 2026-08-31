# Action Blocks Baseline

## Packet/base orientation

The packet expected `final-pre-milestone-corrections` at `602bd205`. The
workspace instead started this run on `milestone-c-readiness-hotfix` at
`5eea5ed`, which contains the current tested Milestone C readiness and card
folder work. The local implementation branch is `action-block-editor-templates`,
branched from that current state.

## Baseline gates

- Core: 174 tests passed.
- SQLite: 145 tests passed.
- Profile: 11 tests passed.
- WPF: the baseline build was blocked by the already-running WPF host holding
  its output DLLs; the WPF test assembly therefore did not run in that pass.
  The host remained responsive and will be stopped only long enough to build,
  then relaunched after application changes.
- Canonical `Content/GameContent.db`: migration ledger v9; `integrity_check`
  returned `ok`; `foreign_key_check` returned no rows; 1 Session, 1 Phase,
  4 Cards, 2 card folders.

## Reuse points

- `ActionSequenceEditorViewModel` and `ActionRowData` in
  `DotNet/Game.ReferenceHost.Wpf/ActionEditorRegistry.cs` and
  `GraphViewModels.cs` are the shared typed ActionSequence editor model.
- `ActionSequenceTemplate` in `MainWindow.xaml` is used by Card, Phase
  ActionNode, PhaseDecision option, SessionDecision option, and recursive
  PromptChoice option hosts.
- `MainWindow.MilestoneB.cs` buffers Card edits in `CardEditBuffer`; Card Save
  uses `ReplaceCardSequenceCommand`. Graph-owned sequences persist through
  `AddActionInstanceCommand`, `UpdateActionInstanceCommand`, reorder commands,
  and `ReplaceActionSequenceContentsCommand`.
- The lower Inspector Action Browser is populated by
  `ActionEditorRegistry.PickerChoices` and drag-start/drop handlers in
  `MainWindow.xaml.cs`.
- `ActionInstanceCloneUtility` recursively creates fresh IDs for normal Actions
  and PromptChoice option/nested sequences. `SequenceSnapshotUtility` clones a
  sequence while preserving existing IDs for undo and dirty-buffer snapshots.
- Phase ActionNodes and decision options are built in
  `GraphViewModels.BuildPhase`; decision options already use the shared
  `ActionSequenceEditorViewModel` through `PopulateDecisionEditing`.
- `ActionTypeRegistry.ValidateScope` is the existing legal-scope authority.
  SessionGoto authoring uses `AddSessionGotoCommand` and its projected socket
  persistence in the SQLite session-decision repositories.
- `AuthoringCommandStack` provides compound semantic undo/redo and immediate
  transactional SQLite persistence.

## Constraints carried forward

Action Blocks must remain editor-only templates. Core, the portable content
snapshot, and runtime execution must not load or execute a Block reference.
Inserted Actions become ordinary stable-ID Action Instances immediately.
