# Architecture contract — Conversation Performance V1

This source-audited revision supersedes earlier packet decisions. V1 proves one reusable performance concept through the existing content pipeline. No source implementation is requested by the document revision.

## 1. Canonical content boundary

Content/GameContent.db remains canonical authored game content. Game.Content.Sqlite reconstructs GameContentDefinition; Game.Core executes that in-memory snapshot. WPF is the primary writer. Unity must consume the same schema and loader rather than extending the temporary ScriptableObject content bridge.

The loader already exposes Load(connection, ensureSchema: false). Unity uses read-only connections and checks schema compatibility; migrations remain an authoring operation. Use a consistent short read transaction for all snapshot queries, then release database locks before playback. Verify the provider's transaction behavior with the existing helper methods; do not hand-wave consistency across separate queries.

V1 development runs open the configured canonical DB path directly. Play/Repeat loads newly committed edits while WPF stays open. No active-file filesystem copies, automatic runtime migrations, game-content JSON, duplicate Card/graph DTOs, closure exporter or publication protocol.

The repository has no configured Unity SQLite provider. Ticket 00 must establish a compatible DbConnection provider and integrate the existing mapping assembly/source without duplicating portable types or schema SQL. New dependencies require approval under agents.md. A reproduced incompatibility is a review blocker: report exact provider/version, native dependency, assembly/compiler/runtime error and smallest reproduction. Stop the affected path for review; do not substitute a new architecture. This documentation revision does not assert provider feasibility.

Existing whole-snapshot validation remains the baseline. Invalid game content may prevent a run; display the actual error. Do not promise unrelated broken Cards are ignored. Evaluate validation changes separately only if observed workflow requires them.

## 2. Authored ownership and exchange

| Item | Editable owner |
|---|---|
| Performance Tag definitions and stable IDs | WPF/SQLite, represented in Game.Content |
| Conversation Performance Events and queries | WPF/SQLite |
| Cards, dialogue, graphs and gameplay state definitions | Existing WPF/SQLite surfaces |
| Ingredient tag membership and factual compatibility | Unity |
| Clips/masks/facial bindings, anchors and transition bindings | Unity |
| PresentationCatalog | Generated read-only projection of Unity ingredients and stage capabilities |

Performance Tags remain separate from Card Tags and Dialog Tags. They express semantics such as playful, tease, stern or comforting. V1 introduces no Happy/Neutral/Mad enum or persistent presentation mood. Existing Happiness/temperatures retain their gameplay semantics; automatic mappings are outside V1.

Unity editor tag pickers read the authored tag catalog through the shared SQLite path. Refresh on inspector focus/open and before ingredient validation/generation, so new tags and renames appear without ID copying. Ingredient membership preserves stable IDs across renames.

WPF reads the generated PresentationCatalog automatically. It contains minimal semantic descriptors and stable ingredient IDs, not Unity objects, coordinates, bone names or a second game-content representation. A small versioned JSON file is acceptable for this presentation-only artifact. Generate atomically outside Assets when valid ingredient data changes. Catalog errors must remain visible; do not represent a failed generation as current.

Cross-store references require explicit validation. Tag renames preserve identity; event references are protected by SQLite relations. For V1 retire referenced tags rather than hard-delete them; retired tags retain identity, remain resolvable for existing content and are hidden from new selections. Show known ingredient usage from the catalog with its freshness. This avoids claiming database foreign keys can protect Unity assets.

At run start Unity validates current enabled ingredient bindings/tag references and supplies the current catalog to Core. WPF absence of a presentation catalog limits planning diagnostics but does not prevent unrelated authoring. New gestures require no event/Card save to enter Unity's next run.

## 3. Minimal ingredient intake

V1 needs foundation, body gesture and face ingredients, plus anchor/transition descriptors. Prefer a focused registry and standard inspector controls over an asset-authoring framework.

Normal body gesture:

- Stable ID: generated once; duplication creates a new one, reimport preserves it.
- Display name: inferred from clip, editable.
- Enabled: defaults off until explicitly enabled after inspection.
- Semantic Performance Tags: chosen from the SQLite vocabulary.
- Compatible posture/foundation: artist confirms; never infer universal compatibility from retargeting.
- Body channel/mask: standard tested gesture mask supplied by the registry preset.
- Clip/binding: assigned in Unity.

Infer clip duration/loop information from the asset. Put the ordinary blend behavior in the rig/kind preset, not mandatory per-ingredient fields. Additional location/prop/head ownership constraints are optional and appear only for an asset that actually needs them. Do not add a universal region-bitmask taxonomy, breath suppression form or every possible future capability.

Use a verified nonlooping gesture with return to foundation as the default. Exceptional head-owning gestures suspend gaze through a simple explicit claim. Unity validates that the mask/binding matches the declaration. An enabled broken binding is an error.

Intake acceptance: from New Gesture to enabled usable ingredient, normally edit no more than four fields—clip, tags, compatible posture and Enabled. Generated ID/name and standard mask require no typing. Count actual interactions separately; do not hide a dozen settings in a required setup wizard. Creating the rig preset once is separate from per-gesture cost.

## 4. One action and one event

Add Perform(eventId), a blocking ordinary activity with no authored blocking toggle or action-local overrides. It waits for host readiness, then returns while Unity retains accepted presentation state. It is not an endless task.

Conversation Performance Event fields:

- ID and name.
- Semantic ALL/ANY Performance Tag query.
- Staging policy: Stay (default), Choose compatible, Different location, or Named location.
- Optional allowed postures/anchors; empty means no extra restriction.
- Refresh at dialogue start: on by default.

No layered query tables, rest probabilities, driver kind, duration settings, gaze cadence, persistent mood, public Refresh Expressions or Set Player State in V1. Starting player target/context belongs to the Unity test setup.

Tag selection applies to expressive ingredients. The required foundation is selected from posture/anchor compatibility. Face/body candidates satisfy the event query; a face is required, while absence of a matching body gesture means the explicit V1 default of remaining at foundation. Show that rest choice in diagnostics; do not silently replace a missing face or incompatible requested destination. No implicit semantic tag fallback. A small usable event needs matching face content; an unrelated neutral face must not be substituted.

Select a viable foundation/face/body combination for a destination before dispatch. The small body channel and optional head claims determine compatibility; no generalized mixer model in Core. Empty mandatory coverage fails before movement.

Perform always establishes fresh compatible acting. Dialogue-boundary refresh, when enabled, changes expressive selections while retaining location/posture. Reuse current acting when disabled. Another Perform is the way to change intent.

## 5. Core plans; Unity renders

Core owns current anchor/posture, active event, candidate filtering, operation planning, semantic/ingredient selections, presentation RNG and desired versus committed state.

Extend CoreServices with IPerformanceHost and keep the existing async service approach. The minimal interaction is:

1. Core resolves a finite plan and selected ingredient IDs.
2. Core submits a correlated semantic request.
3. Unity executes move/pose and accepts the selected acting.
4. Host readiness acknowledges the committed semantic result; failure/cancellation leaves no false claim of arrival.
5. Unity continues rendering that accepted state until replaced or stopped.

A request may expose operation completion only where needed for committing intermediate state. Do not create a general streaming command protocol. A readiness task/result and a small refresh/stop boundary are sufficient starting points; finalize exact signatures against the spike.

Core receives no delta time, mixer weights, blend progress or frame ticks. Unity owns Animator/Playables, continuous blends, IK, gaze execution, blink/breath and purely visual ambient lifetime. V1 uses finite gestures returning to foundation and simple persistent face/gaze; no automatic idle content reroll loop.

Random semantic selection occurs at Perform and actual dialogue boundaries only. Use a dedicated performance RNG domain, stable candidate order and immediate repeat exclusion when another legal expressive candidate exists. Preserve existing Card and Dialogue RNG consumption. Diagnostic seed/choice records are not saved performances.

## 6. Dialogue boundary and lifetime

Retain Direct Dialog and Dialog From Tags through IDialogService. No voice/timing columns or low-level Core speech scheduler are introduced. Tagged dialogue is selected synchronously before host awaits, as current source requires.

V1 proves automatic refresh on the representative Card's ordinary blocking dialogue path. Dialog From Tags selects its snippet synchronously before any host await. Immediately before calling the existing IDialogService.ShowAsync(text, cancellationToken) for a blocking Direct Dialog or selected Dialog From Tags line, Core asks the active Performance Director to select and await acceptance of compatible expressive acting when RefreshAtDialogueStart is enabled. Then ShowAsync presents the line. WPF uses a simulated performance host for the same sequence. No active event, or disabled refresh, leaves dialogue presentation unchanged.

Preserve existing nonblocking dialogue behavior and RNG ordering. Exact refresh synchronization to a later host-side visual start is deferred. Do not add a Unity-to-Core presentation-start callback protocol, stale dialogue callback generation system, speech scheduler or queue coordinator for V1. If implementation reveals that preserving existing nonblocking behavior requires even a tiny callback seam, stop that expansion and document the concrete case for review before changing the contract.

Text pacing belongs to the host. The Unity tester may hold each line until acknowledgement so the two-line proof is visible; final WaitForContinue remains the ordinary graph yield. Do not silently change WPF's existing presentation semantics.

Persistent acting is excluded from BackgroundActionTracker and WaitForAll. Continue-wait leaves Unity ambience active; application pause is host rendering behavior. End/cancel/unload stops presentation, invalidates old requests and releases resources. Integrate the existing GameSessionEngine shutdown path so one service's failure cannot skip another. A host fault after readiness must reach the run/UI even during Continue-wait through a narrow failure notification; no polling Core loop.

## 7. Factored physical state and plans

State is (AnchorId, Posture). Begin with standing and sitting on the fixture. Facing is Unity calibration unless a concrete semantic need appears. Lying and broader posture support can follow observed needs.

Anchors declare supported postures and connections. Reusable capabilities express MoveWhileStanding, Stand and Sit, with explicit preconditions/results. Stand is supported explicitly; do not infer a clip can run backward. Shared rig transitions bind to compatible furniture calibration. Exceptional anchor transitions may override a binding without forcing unique state nodes everywhere.

Example: sitting at Chair A → Stand → MoveWhileStanding along connected anchors → Sit at Chair B. Core finds a shortest legal sequence of positive-cost operations with stable-ID tie breaks. Generated search states may be anchor/posture pairs internally; they are not separately authored records.

Unity owns actual route coordinates, obstacle/path feasibility for authored connections, facing and contact calibration. Missing required operation/binding excludes that plan; no teleport fallback. Commit semantic arrival only after the host has settled. Choose rig-appropriate tolerances during the spike and record them rather than mandating arbitrary universal numbers.

Different location excludes the current anchor. Stay preserves the anchor while permitting a compatible posture change if explicitly constrained. Named requires the named anchor. No viable result reports why.

The bedroom names are fixtures, never Core enums. Adding an equivalent sit-capable anchor should require an anchor, connections and calibration, not copies of every posture conversion.

## 8. Rig technology decision

Ticket 01 is an experiment on the actual or representative humanoid rig. Start with Animator/layers and ordinary rig facilities. Compare a targeted Playables or Animation Rigging solution only where the observed problem justifies it. Animation Rigging is not installed and needs approval if required.

Judge travel/pose transitions, masked gesture, face preset and player gaze together. Choose the smallest implementation that works and document evidence/tradeoffs. Do not mandate a fixed PlayableGraph, preallocate driver layers or build a general arbitration engine.

## 9. WPF and visual iteration

Reuse catalog editors, searchable chips, Card Save/Revert, semantic undo, clone/equality and Action Blocks. The event editor is a simple form. Show the current factored state, eligible destinations, chosen operations/ingredients and exclusion reasons in existing diagnostics.

A generated read-only Nodify reachability/plan view is allowed if it cheaply reuses current facilities; textual diagnostics pass V1. No comprehensive state-by-mood dashboard. No authorable choreography graph. Future semantic behavior graphs remain open to evidence.

Unity's small Play/Stop/Repeat panel selects a real test Session and existing profile/seed with starting scene context. The Session draws the representative Card through GameSessionEngine. A one-eligible-Card fixture makes repeated testing convenient without a second Card interpreter.

On Repeat, stop/dispose the old run, load fresh committed SQLite content and current catalog, reset the known scene start and rerun while keeping scene/rig loaded. Snapshot load failure blocks the new run visibly rather than silently replaying old data. WPF Save followed by Unity Repeat requires no export/import steps.

## 10. V1 completion and deferred work

V1 passes when a WPF-authored SQLite Card moves/poses the actor, selects compatible acting, shows two ordinary tagged dialogue lines with automatic refresh, gazes at the player and retains coherent state through Continue. Repetition shows alternatives. A new ordinary Unity gesture joins existing content without event/Card edits. WPF explains the same plan through a fake host.

Performance Phase 2 / Driven Motion is a separate post-acceptance packet: oscillator, scalar-window playback, funscript and body/prop motion. Do not reserve APIs, database tables or layers for it now.

Also deferred: player posture/visibility actions, voice/lipsync, broad state repertoire, sophisticated idle cadence/arbitration, comprehensive coverage dashboards and standalone packaging. Never reintroduce takes, per-line clip assignments, saved combinations or performance timelines.
