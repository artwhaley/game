# Milestone B authoring-readiness implementation

Implemented on branch `milestone-b-authoring-readiness`.

## Authoring

- Card action inspection now uses the shared `ActionSequenceTemplate`, so Card rows render as real typed action editors instead of the host view-model `ToString()` value.
- The Inspector has a persistent Action Browser with category labels, search, focused sequence/scope, double-click append, and drag/drop. Same-sequence drops reorder by ordinal; cross-owner copies deep-clone PromptChoice trees with fresh IDs; invalid scope drops are rejected through `ActionTypeRegistry`.
- PromptChoice rows expose prompt text, expand/collapse, 1–3 named options, deletion, and recursively nested action sequences. Nested sequences inherit the enclosing scope and reuse the same editor/browser/undo paths.
- Cards and Phase ALL/ANY tag queries use the stable-ID `RelationPickerControl`; duplicate display titles do not collapse into one selection. Search, keyboard Enter/arrow/Escape, and removable chips are supported.
- Catalogs expose searchable Session Type, Card Tag, Kink, Equipment, and Smart Toy editors. Title/description/category/required-capability edits are persisted through one semantic command, and existing referenced-delete blocking remains in place.

## Runtime and host

- `PhaseGoto` no longer has `CardSequence` in its legal scope.
- Card selection publishes the same evaluated candidate set used for weighted selection, including the selected card.
- Action execution reports typed START/FINISHED/FAILED lifecycle messages through `IGameLog`; background faults remain observed and logged.
- The WPF runner shows full Card BodyText, candidate eligibility/rejection reasons and weights, selected Card, node/check traces, action flow, and explicit Halt/Close. WPF cutscene playback is a typed two-second asynchronous stand-in with START/FINISHED/CANCELED logs; completion leaves the runner open.

## Data safety

Nested sequence replacement clears and rewrites owned contents transactionally while preserving the root sequence identity and stable existing IDs. The pre-existing `Content/GameContent.db` working-tree modification was not reset, edited, or used as a verification fixture.
