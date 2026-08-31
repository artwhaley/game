# Action Blocks Contract

An Action Block is an editor-only, versioned ActionSequence template stored in
the WPF authoring extension data. It is not a Card, runtime Action Type,
portable `GameContentDefinition` entity, or runtime execution frame.

- Every existing ActionSequence owner can save its whole sequence or a selected
  ordered subset as a Block.
- A compatible Block can be inserted into any existing ActionSequence editor.
- Insertion validates the complete template against the destination scope,
  including recursive PromptChoice descendants, before mutating the destination.
- Insertion deep-clones ordinary Actions, PromptChoice options, and nested
  sequences with fresh stable IDs. Resource, capability, and dialog-tag
  references remain references to the same authored catalog entities.
- Blocks have no live linkage. Renaming/deleting/editing a Block never changes
  Actions already inserted from it.
- RETURN has no Block-specific prohibition; destination legality controls it.
- A PhaseGoto Block captures its source Phase ID and is insertable only into a
  sequence owned by that same Phase in v1.
- SessionGoto is insertable only into a direct SessionDecision option sequence;
  each insertion gets a fresh Action ID and fresh unwired projected socket,
  with no copied graph edge.
- PromptChoice remains capped at three options, and the existing nested
  SessionGoto prohibition remains in force.
- The existing Phase ActionNode remains the direct scripted ActionSequence graph
  node. No Execute Specific Card node is added.
