# Pre-Milestone C Reliability — Ticket 08 Exact Edge Undo/Redo

## Implemented contract

- Session and Phase disconnect commands capture and restore the complete
  persisted edge identity: edge ID, source socket, target node, and graph owner
  supplied by the command.
- Connect replacement commands preserve the replaced edge exactly on Undo and
  keep the newly authored replacement edge identity stable across Redo.
- Node-deletion Undo restores every captured incident edge with its original
  ID. Projected SessionGoto and PhaseExit compound restore paths already carry
  and reinsert exact edge IDs; the tests now assert that behavior.
- Regenerated-edge behavior/comments were removed from the semantic command
  paths. New GUIDs are used only for genuinely authored replacement edges.

## Verification

- Focused graph command suite: 20 passed.
- Assertions cover Session node delete, connect replacement Undo/Redo,
  disconnect, and projected PhaseExit restoration.
- WPF build: passed with 0 warnings and 0 errors.
- The rebuilt WPF app is running for interactive Undo/Redo verification.
