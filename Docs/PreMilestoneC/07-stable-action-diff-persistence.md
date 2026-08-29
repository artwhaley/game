# Pre-Milestone C Reliability — Ticket 07 Stable Action Diff Persistence

## Implemented contract

- Normal ActionSequence saves now apply a stable-ID diff rather than clearing
  or deleting the sequence and rewriting it.
- Surviving Action Instance rows are updated in place, including typed subtype
  parameters, blocking state, and ordinal. New IDs are inserted and removed
  IDs alone are deleted.
- PromptChoice options and nested ActionSequences follow the same recursive
  rule, preserving surviving PromptChoice, option, and nested Action IDs.
- An incompatible type change on an existing ID fails loudly and requires a
  new Action Instance ID, avoiding silent subtype replacement.
- The card editor snapshot clone now preserves existing nested IDs.

## Verification

- Focused extension-safety test passed: 1 test. It attaches rows in a fake
  `unity_test_action_binding` table with `ON DELETE CASCADE`, then covers card
  text/relations, root parameter edits, reorder, option-label edits, nested
  parameter edits, undo, and redo without losing surviving bindings.
- Full SQLite suite: 124 passed, 1 skipped (the pre-existing canonical human
  playback skip); `foreign_key_check` remains part of the SQLite gate.
- WPF build: passed with 0 warnings and 0 errors.
- The rebuilt WPF app is running for interactive authoring verification.
