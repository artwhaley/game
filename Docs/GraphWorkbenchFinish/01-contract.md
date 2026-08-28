# Graph Workbench Finish — Ticket 01 Contract

This finish pass is a narrow correctness/authoring pass on the existing
Graph Workbench. It does not redesign the Graph VMs or reopen the accepted
run-until-yield runtime contract.

## Frozen decisions

- Session and reusable Phase remain distinct graph levels.
- SessionDecision and PhaseDecision options own ordinary ordered
  `ActionSequence` instances. They are not GOTO-only structures.
- One reusable WPF ActionSequence editor/view-model is used for Phase
  ActionNodes, SessionDecision options, and PhaseDecision options.
- Action Types remain code-defined; owner-scoped Action Instances retain their
  own configured values. No reflection property grid and no EAV layer.
- PhaseExit identity is stable and separate from its display name. Phase GOTO
  stores nullable `PhaseExitId`; unassigned is valid authored state and is a
  visible danger condition.
- Make Unique carries an explicit `oldPhaseExitId -> newPhaseExitId` mapping.
  Follow-up edits/deletes target the mapped stable ID, never a display-name
  lookup or first-match fallback.
- SessionGoto remains a distinct Action Type with an instance-backed unique
  projected socket; labels are display text only.
- Undo restores exact pre-existing row IDs, edge IDs, projected socket IDs, and
  PhaseExit/action assignments. A new edit after Undo invalidates Redo,
  including after a coalesced merge.
- The Library uses Sessions/Phases modes with one full-width list. Show
  Sessions changes the Library mode/filter without changing the selected Phase
  canvas or clearing history.
- SessionStart and PhaseEntry are structural nodes created with their parent,
  not palette-addable nodes; they remain movable and nondeletable.
- WPF coordinates and viewport/layout metadata remain authoring concerns and
  stay outside Core semantics. Existing Nodify two-way node-location binding
  must remain intact.
- No Unity Action binding/Timeline system is added in this pass. No Kinks,
  Equipment, Smart Toys, monetization, packaging, or user-profile work.

## Implementation boundaries

- Reuse the current `Game.Content.Sqlite` persistence and semantic command
  layers; do not create parallel Decision-action tables or runtime semantics.
- Keep SQL in repositories, game rules in `Game.Core`, and WPF focused on
  authoring/presentation.
- Extract focused WPF responsibilities where needed, but retain the current
  application shape and startup flow.
- Every exposed Action Type must either have a small, semantically correct
  editor descriptor or be removed from the picker until one exists.
- Human gates remain separate from automated verification. No human acceptance
  will be claimed unless actually performed.

## Source review

The contract was reviewed against the current source after the Ticket 00
baseline. Existing SQLite ownership, nullable PhaseGoto persistence, Nodify
location binding, run-until-yield engine, and stable-ID command foundations are
preserved. The deficiencies listed by the stack are implementation gaps, not
architecture conflicts.

## HARD gate

Passed. This contract is frozen before implementation tickets 02–09.
