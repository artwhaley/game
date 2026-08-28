# Graph Workbench Finish — Implementation Summary

This finish pass applies the supplied patch stack to the existing WPF Graph
Workbench without changing the frozen Session/Phase runtime architecture.

## Authoring behavior

- SessionDecision and PhaseDecision options now construct the same reusable
  owner-scoped `ActionSequenceEditorViewModel` used by Phase ActionNodes.
- The editor exposes an explicit, searchable Action Type registry, typed
  parameter controls, add/delete/duplicate/reorder operations, and inline
  validation for malformed numbers, missing resources/temperatures, and
  unassigned PhaseExits.
- SessionGoto remains routed through its specialized repository commands so
  its stable instance-backed projected socket and connected edges are preserved.
- PhaseExit shared-topology edits use a literal `Make Unique` / `Cancel` dialog.
  Follow-up deletes use the clone's explicit mapped PhaseExit ID, including
  duplicate display names.
- PhaseExit in-use deletion uses an explicit `Delete Anyway` / `Cancel` dialog
  and reports the GOTO and Session-connection impact.
- Session/Phase library panes are mode-driven, one full-width list at a time;
  Show Sessions applies a temporary usage filter without changing the selected
  Phase canvas.
- Center row splitter ratios are measured from the actual WPF row heights on
  drag completion and reapplied to those rows on load.
- SessionStart and PhaseEntry palette-add controls are removed; structural
  nodes remain created by their parent and available for movement.

## Safety and structure

- Merged authoring edits clear stale redo history and raise the stack change
  notification.
- SessionGoto undo restores the original projected edge IDs instead of minting
  replacement IDs.
- Whole-graph `ReplaceGraph` methods are `internal`; production authoring uses
  granular repositories and commands.
- WPF responsibilities now include focused `ActionEditorRegistry`,
  `LibraryPaneController`, and explicit dialog classes while the existing
  startup and preview flow remain intact.

## Scope retained

The pass does not add Kinks, Equipment, Smart Toys, card-library authoring,
Unity Action binding/Timeline authoring, packaging, or persistent user
profiles. SQLite remains the canonical authored-content store and layout stays
in WPF extension tables.
