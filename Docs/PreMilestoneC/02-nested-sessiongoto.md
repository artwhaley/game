# Pre-Milestone C Reliability — Ticket 02 Nested SessionGoto

## Implemented contract

`SessionGoto` is legal only in the direct ActionSequence owned by a
`SessionDecision` option. A PromptChoice nested below that option cannot author
or execute `SessionGoto`, including when PromptChoice is nested again. Other
actions retain the containing context's legal behavior.

## Root cause and fix

The WPF nested-row builder reused the enclosing `SessionDecisionOptionSequence`
scope, and the runtime executor reused the same context. That made a nested
SessionGoto appear legal and gave it access to the containing decision's
projected socket lookup. A dedicated
`SessionDecisionPromptChoiceSequence` scope now preserves session-safe actions
while excluding direct-only SessionGoto ownership. The same derived scope is
used by WPF picker rows, runtime execution, and snapshot validation.

Existing malformed content is rejected by
`ActionSequenceScopeValidator` during full snapshot load with the owning
Session/Decision/PromptChoice path and Action Instance ID in the error.

## Verification

- Core registry test: direct SessionGoto remains legal; nested scope rejects it
  and allows a general action.
- Core executor test: a nested SessionGoto under a SessionDecision option fails
  loudly instead of returning a transfer request.
- WPF binding test: nested PromptChoice rows receive the dedicated scope and
  omit SessionGoto from their picker.
- SQLite snapshot test: malformed persisted nested SessionGoto content fails
  during load.
- Focused results: Core 14 passed; WPF 8 passed; SQLite snapshot suite 5
  passed.
