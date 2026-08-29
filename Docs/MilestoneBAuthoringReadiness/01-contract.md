# Milestone B frozen authoring contract

These contracts are intentionally narrow and stable across the WPF authoring host and the portable runtime.

1. Action type keys and owner scopes are explicit registry data. `PhaseGoto` is legal only in Phase action and inherited choice-option sequences; it is not legal in a Card sequence. `SessionGoto` is legal only in Session Decision option sequences.
2. Every action instance and PromptChoice option has a stable ID. Reorder preserves IDs. Copying an action or card across an owner creates fresh IDs while preserving the typed payload.
3. PromptChoice option sequences inherit the enclosing owner scope. Options may contain all actions legal in that scope, including nested PromptChoice, and are persisted recursively.
4. Card tags, kinks, equipment, smart-toy capabilities, and phase tag queries persist relation IDs, never display titles or comma-separated free text. Duplicate titles remain independently selectable.
5. Catalog deletion is blocked while the stable ID is referenced. Catalog editing is title/description/category/capability aware and undoable.
6. Host services are typed boundaries. WPF `PlayAsync` logs START, waits about two seconds, logs FINISHED, and completes its returned `Task`; cancellation and failures propagate.
7. The session runner is observational: it logs phase/card/action/selection/check/flow events without changing Core semantics. Natural completion leaves the runner open; Halt cancels and closes it.
8. Final human acceptance remains a separate gate: authoring the requested card, playing it, selecting PromptChoice options, checking logs, and exercising undo/redo must be performed in the WPF UI.
