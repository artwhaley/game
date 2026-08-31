# Nodify graph presentation and live execution follow

This document records the implemented scope of the graph/live-follow stack.

## Graph presentation

The Session and Phase editors use Nodify `StepConnection` wires. They preserve
orthogonal 90-degree routing and do not attempt automatic obstacle avoidance.
Node headers are color-coded by explicit node kind and include a small vector
icon. Output ports and established wires use colors for Normal, True, False,
Phase Exit, and Session GOTO outputs. Selected nodes and portal endpoints keep
their incident wires visually prominent; unrelated wires fade.

## Portal pairs

A portal pair is editor-only presentation metadata for one existing logical
graph edge. Inserting it from an edge creates two display-only endpoints; the
pair never becomes a runtime node or changes execution semantics. The endpoints
are independently draggable, the pair is removable, and undo/redo restores the
same pair and endpoint identities.

Portal pairs persist in schema migration 11, `wpf-graph-portal-pairs`, in
`wpf_session_edge_portal_pair` or `wpf_phase_edge_portal_pair`. Each row is
owned by its graph and edge, with foreign-key cascades and owner-consistency
triggers. Runtime snapshot loading ignores these WPF tables.

## Live execution follow

The portable engine emits `GraphEdgeTraversal` with the exact graph kind,
owner, edge id, source output id, and target node id. The Workbench consumes
that event to highlight the traversed wire and keeps a short fading trail of
recent edges and nodes. It calls Nodify `BringIntoView` for the active node.
When a card is drawn, the Workbench selects that card in the Card library and
opens it in the editor when the card is clean.

The Reference Player exposes real cooperative Pause and Resume controls. The
Workbench installs a pause gate at session-node, phase-node, and before-card-
actions checkpoints. If the drawn card is dirty, execution pauses before its
actions execute and explains that the card must be Saved or Reverted. Resume
is guarded until the dirty buffer is resolved. The Save button uses the same
highlighted visual treatment while the card is dirty.

## Deliberate exclusions

This pass does not add graph paper, a minimap, comments, frames, automatic
obstacle routing, runtime portal nodes, Unity portal rendering, or dependency
changes. Dialog playback remains aligned with the existing cutscene-style
reference-host behavior until Unity integration defines the final playback
contract.
