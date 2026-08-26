# Deferred From the Extraction/Remediation Passes

Deliberately NOT done in extraction milestone 0.1 or its remediation pass.
Each entry names **when it comes due** so we remember it at the right time —
do not pull these forward casually; most belong inside a design decision
rather than as bolt-on patches.

| # | Item | Comes due | What it is | Why it was deferred |
|---|---|---|---|---|
| 1 | **Content validator** (semantic validation before play/export) | Before the WPF host becomes the authoring workstation | Errors like: session with zero phases; null phase/card entries where not tolerated; null card tags; malformed/null choice options; unknown action discriminator beyond JSON layer; cutscene resource id with no known binding. Runs on `ContentDocument`/definitions before engine construction. | Validation must be designed against the *final* content contract, not bolted onto the spike shape. Syntactically-valid-but-semantically-bad JSON currently fails at engine construction (WPF now surfaces that in-UI instead of crashing, but real validation is owed). |
| 2 | **Choice reference-cycle handling** | With item 1 | `ChoiceAction.ToDefinition` recursion stack-overflows on authoring cycles (A→B→A). Baseline never traversed unchosen branches; conversion does. Fix belongs inside the validator/design (depth guard or ID-graph), not sprinkled checks. | Cycles are authoring garbage either way; correct fix depends on the identity model below. |
| 3 | **Stable content IDs / reference semantics** | First design decision of the authoring milestone (AD-20 deferral) | Decide whether choice children/actions stay inline-nested or become reusable entities referenced by stable ID; this determines persistence AND what the validator validates. | Packet AD-20 locked the deferral; building GUI around the wrong model would be expensive to undo. |
| 4 | **Replace `CutsceneBindingRegistry` opaque keys** | Same design moment as item 3 | The runtime counter-key bridge is explicitly an extraction compatibility shim, not the production resource-ID system. | Works exactly for what it was built; polishing now would be throwaway. |
| 5 | **Canonical content path: JSON ↔ Unity** | Authoring milestone | Decide whether JSON becomes the source of truth imported into SOs, an export target, or both; Unity deliberately does not consume `Game.Content.Json` yet. | Explicitly out of scope for the serialization spike. |
| 6 | **PlayMode scene-glue test** (boot real Game scene through one choice card) | Next test-investment pass | Repo overview §6.2 already flagged it; the two current smoke tests cover adapters, not scene wiring. | Needs interactive-editor iteration; automation coverage exists for everything below scene glue. |
| 7 | **Authored Timeline assignment + human playback verification** | Whenever content work resumes | Pre-existing repo Ticket 2 open item: assign a hand-authored timeline to `Cutscene_Intro.asset`, verify playback. Carried through extraction unchanged by design (AD-25). | Content authoring, not extraction parity. |

Historical note: items surfaced by the external review of milestone 0.1
(2026-08). The remediation pass fixed cancellation commit boundaries,
fail-noisy conversion (`ToDefinition` abstract), optional DirectorPlayer
wiring, drain quiesce semantics, WPF registration leaks, and stale root
documentation. Everything above was consciously left for the right moment.
