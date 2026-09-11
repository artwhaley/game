# Architecture Contract — Procedural Performance Events

This contract supersedes all earlier versions of this packet. The user correction is decisive: this is a card game whose presentation is assembled procedurally from reusable layers. Authors create ingredients and selection rules, not recorded performances or a sequence of chosen clips.

## 1. What the game does

Draw a Card through the existing selection engine. Execute its ordinary actions. A performance action establishes or changes the character's procedural behavior. Dialogue actions select and deliver text. The director assembles compatible acting as those actions run.

Example Card:

    Perform              Conversational variety / Mad / Different location
    Dialogue from tags   [challenge]
    Refresh expressions
    Dialogue from tags   [instruction]
    Wait for Continue

The card contains no animation IDs, facial presets, recorded combinations, or choreography tracks. Refresh expressions chooses another legal combination. An event can instead refresh automatically at each dialogue boundary, so most cards omit that action.

The other event flavor drives a reusable motion path with an oscillator or recorded scalar curve while the remaining compatible layers continue.

    Perform              Prop rhythm / Use event duration / nonblocking
    Dialogue from tags   [encouragement]
    Wait for All

There is no persisted result of the random selection. The next execution can produce a different valid performance.

## 2. One editable owner for each fact

| Owner | Authored data or responsibility |
|---|---|
| Unity | Animation ingredients, clips, masks, rig setup, facial presets, props, spatial anchors, pose/transition assets, named gaze targets, motion windows, and ingredient compatibility metadata |
| WPF / SQLite | Cards, dialogue, existing game graphs and eligibility, reusable procedural Performance Events, event queries/timing/mood/movement policies, driver settings and imported scalar curves |
| Portable Core | All runtime selection, compatibility evaluation, state, routing, layer arbitration, event lifetime, RNG, scheduling and host requests |
| Unity runtime host | Execute Core requests using the bound scene/rig/assets; report actual arrival, completion and errors |
| WPF reference host | Run the same Core logic with clearly labelled simulated presentation |

Compatibility metadata describes an ingredient: this clip supports sitting, is appropriate for Mad, uses the right arm, requires this prop. It is authored beside the asset in Unity, where someone can actually inspect it. Unity does not use that metadata to make independent game decisions.

An event rule describes intent: choose a Mad-compatible gesture, restrict destination to these locations, change expressions on each line, drive a motion family at this rate. It is authored in WPF and evaluated by Core.

Do not duplicate ingredient records or editable compatibility facts in SQLite. WPF reads Unity's generated catalog as read-only data. Do not create a cross-application asset-registration protocol or require anyone to copy resource IDs by hand.

SQLite is canonical for game logic/content. Unity assets are canonical for presentation ingredients. Their generated projections are not independent authoring stores.

## 3. Unity ingredient library

Use a small serialized catalog/binding asset, or ingredient assets with a registry. Each ingredient has a stable semantic ID minted and retained in Unity, editable display name, kind, Enabled flag, compatibility descriptors and the appropriate Unity references. Duplicating an ingredient creates a new semantic ID; ordinary reimport retains it.

Required kinds:

- Base: a standing, sitting or lying foundation appropriate for a legal stage state.
- Body expression: a gesture, nod, lean, shimmy or held posture overlay.
- Face: an expression preset with declared facial-channel ownership.
- Motion: a reusable scalar-driven path, possibly controlling body and attached prop together.
- Transition/locomotion: pose changes and travel ingredients.
- Voice: optional audio used by dialogue, with duration metadata.
- Stage binding: location/state/route/target/prop descriptions for the fixed room.

The implementation need not create a distinct framework or file type for every row. Prefer a few focused assets and editor controls. Reuse serialized references and built-in Unity inspection.

Each ingredient declares applicable pose/state/location, permitted moods, performance tags, required player posture/prop, occupied regions, blend/duration/loop data, and any gaze/breath suppression. Technical facts measured from assets are displayed read-only; the artist reviews semantic applicability. No automatic assertion that every retargeted clip works in every pose.

Performance tags are a separate catalog from existing Card/Dialogue tags. Unity supplies stable tag IDs and display names; WPF chooses them through searchable chips. Mood starts as Happy, Neutral, Mad. Locations and performance tags remain content identities, not six hardcoded engine coordinates.

New ingredients can remain disabled while being authored. Disabled ingredients do not enter random selection. An enabled ingredient with a broken required binding is a validation error, not silently skipped.

The artist plays clips and layered combinations in Unity using a small developer test panel on the real rig. This is asset verification owned by Unity, not a feature for staging dialogue performances in WPF.

## 4. Read-only catalog exchange

Unity generates a versioned portable PresentationCatalog containing only semantic descriptors: IDs, kinds, tags, supported states, ownership/requirements, nominal timings and motion-window metadata. It contains no Unity objects, bone names or coordinates needed by Core.

Write the generated catalog atomically to the project's configured generated-content location. WPF loads it automatically and refreshes pickers/diagnostics after changes without discarding dirty Card edits. No manual matching of two inventories. One-time project path configuration is sufficient.

A valid catalog refresh also regenerates the selected development snapshot from committed game content, so newly enabled candidates reach Unity testing without a dummy Card edit. Never include dirty Card buffers in that export.

For testing/builds, combine the committed SQLite game snapshot with a compatible PresentationCatalog. Keep content hash and required-asset signatures independent: changing dialogue does not require rebinding clips; an unrelated asset addition does not invalidate an unchanged Card dependency set.

The runtime manifest validates the actual required Unity bindings against the exported descriptors. A changed required clip window, missing prop or incompatible rig blocks the affected run with a useful error. Missing catalog blocks performance execution, not opening the editor or authoring unrelated game content.

## 5. Portable Performance Event definitions

A PerformanceEventDefinition is a reusable rule set referenced by ordinary Card actions. Store it in SQLite using typed tables/relations and existing authoring commands. It is not an ordered collection of animation cues.

Two kinds share the common rule fields:

| Kind | Purpose and additional fields |
|---|---|
| Conversation | Establish ongoing procedural layers and the rules for their refresh |
| Driven motion | Apply common context rules, select a compatible motion ingredient, run a finite scalar driver, and release its claims |

Common fields:

- Stable ID, display name and optional authoring folder.
- Mood policy: Keep current or Set Happy/Neutral/Mad.
- Destination policy: Stay, Choose compatible, Different location, or Named location; optional allowed pose/location filters.
- Body and face queries: required/all tags and optional any-tags, plus explicit body-rest probability.
- Gaze-target policy and dwell/cadence settings.
- Expression refresh triggers: on event entry, on dialogue start, and optionally at idle intervals.
- For a driven event, conversation-rule policy: Keep the active rules by default, or explicitly replace them with this event's rules.
- For motion: a semantic motion-family/tag query, scalar range, driver kind, rate/duration or curve reference, and entry/exit blending.

The WPF editor uses a simple form with a layer-rules table and searchable chips. Typical users edit mood, allowed positions, expression rules, and motion rate; advanced numeric fields need not dominate.

A Card's Perform action references an event and may override mood/destination/duration. Overrides default to Use event. Event fields that say Keep preserve active state. Show effective defaults and overrides; no repeated profile selection or implicit mood reset.

Do not add per-line animation IDs, exact facial-cue selectors, sequence tracks, or a facility to save chosen combinations. To express a recognizable behavior, author a semantic tag/query in the event; adding another compatible ingredient can expand it without changing Cards.

## 6. Actions and ordinary card execution

Add three action kinds:

| Action | Contract |
|---|---|
| Perform | Execute a referenced procedural event. Conversation events wait for readiness, then leave persistent rules active. Driven events wait for readiness, then either await finite motion completion or register that completion as nonblocking work. |
| Refresh expressions | Request fresh compatible body/face choices under the active event rules. It does not move the actor, change mood or name clips. Wait for the new selection/blend to be accepted, not for all ambience to finish. |
| Set Player State | Set Standing/Sitting/Kneeling/LyingDown and independent body visibility; wait for the presentation change. |

Keep Direct Dialogue and Dialogue From Tags. Extend their host request only with line identity, text, optional voice resource and delivery timing. Acting is selected by the current event's rules at line start, not stored on the dialogue line.

Session presentation setup explicitly supplies stage, initial state, initial mood, default Conversation event and player state. This is one session setup, not a new setup per Card. Later Cards normally inherit live presentation state.

Fix the existing ActionExecutor distinction: always-blocking activity does not mean graph transfer. Only actual GOTO/RETURN/end control actions enter transfer reduction. New actions are legal in existing ordinary activity scopes and do not create graph ports.

For nonblocking driven events, readiness is still awaited before continuing to the next Card action. Only the finite driver completion is background work. Otherwise dialogue could start while the character is still traveling.

Preserve the existing Card selector, consent/equipment/capability filters, weighting, tagged-dialogue RNG, explicit Continue, progress and graph continuations. No automatic gameplay mutation is attached to a pose or mood.

## 7. Selection is a procedural composition, not a saved sequence

At an event boundary:

1. Resolve event plus action overrides against the current state.
2. Filter destination states by pose/location requirements, mood, player/prop compatibility and route reachability.
3. For driven events, also require an available compatible motion ingredient and its claimed regions.
4. Require viable mandatory base/face coverage under the event queries; body-rest is a legitimate explicit possibility.
5. Select a valid state and motion if needed; execute route and settle.
6. Select compatible body/face/gaze inputs and establish refresh scheduling.

Do not independently roll several layers and then discover that they cannot coexist. Apply region/requirement constraints before sampling each dependent choice. If a candidate destination has no valid required combination, exclude it with a typed reason. If the total set is empty, error; do not teleport or substitute unrelated behavior.

At dialogue start, select/refresh expressions only if the active event enables that trigger. An explicit Refresh expressions always requests a refresh but still respects region ownership. Each triggered body gesture is finite or an explicitly held pose with bounded release rules, never an accidental looping action.

Respect a minimum face dwell for ordinary automatic refresh; explicit mood/refresh requests can replace it with a blend. A single eligible result is legal and appears as a variety warning. A refresh cannot manufacture variety from one asset.

Use separate stable seeded domains for card, dialogue, destination, body, face, gaze/cadence and motion rate. Sort candidates by stable ID. Presentation-only immediate anti-repeat excludes the last candidate if another exists; do not change the existing Card selection policy.

The diagnostic log can record chosen IDs/reasons/seeds for a bug report. It is not a user-authored recording or another content artifact.

## 8. Layer ownership and runtime state

Standing, sitting and lying are alternative foundation states. They are not three simultaneously active layers.

Use one fixed-topology PlayableGraph per actor and a few stable channels:

- Foundation: base idle, locomotion or pose transition.
- Body expression overlay.
- Finite driven-motion overlay, possibly including pelvis/legs/torso.
- Verified additive breath where compatible.
- Head/eye gaze after body evaluation.
- One facial composer for expression, speech mouth and blink.

Closed logical regions: world root, pelvis/legs, torso, left arm/hand, right arm/hand, head/neck, eyes, upper face, mouth/jaw, eyelids and named prop channels. Unity maps these to actual masks/curves. Default skeletal overlays are override clips; only verified offset assets are additive.

Priority: transition > driven motion > expressive body gesture > ambient body gesture. Gaze yields to a head-owning expression; breath yields to incompatible torso animation. Disjoint channels may coexist. A pelvis-involving path must be supported; never restrict all motion to an upper-body mask.

The director is session-scoped and stores desired and committed state, active rules/mood, claims and correlated requests. GOTO/RETURN does not restore old presentation state. Commit arrival only after host success. On failure, stop the run visibly; do not claim the desired location was reached.

Persistent ambience is not an endless Action task and is excluded from WaitForAll. Finite driven motion belongs in background tracking when requested. A second overlapping driven event is an explicit conflict in this slice; a context-changing event waits for existing finite motion to release.

One speech channel queues nonblocking dialogue FIFO. Context changes wait for already accepted speech. Refresh during a driven event selects only remaining compatible regions; optional body-rest is valid. Reserve speech/claims before host awaits to preserve action order.

Core accepts elapsed presentation time and semantic host events; it does not spawn a perpetual Task. Hosts tick it while awaiting movement, speech and Continue. Use absolute scheduled times and stable event order; no per-frame content RNG.

Application pause freezes presentation clocks/audio/travel. Continue-wait and choices leave ambience alive. End/cancel/error/unload stop all finite work and audio, release claims/props, reject stale completion IDs and dispose graphs. Cleanup is idempotent, and one failing service must not skip the others. Fatal presentation faults reach game UI even while waiting for Continue.

## 9. Fixed-room movement

Initial location names are Room center, Door, Window, Chair, Side of bed and Foot of bed. Unity owns their anchors and names/IDs in the catalog.

Initial legal states: Standing at all six, Sitting at Chair and Side of bed, LyingDown at Side of bed. Nine states total. Start with two-way standing routes through Room center (ten directed edges), Chair stand/sit (two), and bed stand/sit plus sit/lie (four). Shared locomotion/transition assets may supply several edges; this does not imply sixteen unique clips.

Core finds the least authored-cost directed route with stable-ID tie breaks. Unity executes spatial waypoints/facing and calibrated pose transitions. No automatic reverse edge, general navigation system or teleport success.

One mover owns world root and uses in-place locomotion. Pose/driver pelvis motion is actor-local. Require arrival within 2 cm and 3 degrees plus target-pose settling; inspect actual furniture/foot contacts independently. Watchdogs and request IDs cover failure/cancel.

Different location excludes the committed location. Stay retains it only if the requested rules can run there. Choose compatible may choose the current location. WPF displays these distinctions, not a hidden “best effort” fallback.

## 10. Driven motion

Unity defines the usable clip window and compatible body/prop bindings. For example, the artist marks frames 25–85 as a reversible motion path and inspects both directions on the rig.

WPF defines the event's driver: oscillator or imported single-axis curve, scalar range within 0–1, duration and speed. Its ordinary rate control is one-way traversal duration in seconds. This remains meaningful across compatible clip variants. A detail display may translate it to average source frames/second for a known window; a 60-frame path in 2–6 seconds corresponds to 30–10 source frames/second. Do not expose a conflicting second rate authority.

    p(t) in [0,1]
    q(t) = scalarMin + p(t) * (scalarMax - scalarMin)
    u(t) = usableWindowStart + q(t) * (usableWindowEnd - usableWindowStart)
    sampleSeconds = u(t) * clipDuration

Oscillator: choose one seeded half-cycle duration from the event range at each endpoint; retain it through that segment; use smoothstep endpoint easing. Advance elapsed endpoints deterministically with a pathological-event guard. Never reroll each frame.

Recorded curve: import .funscript actions with nonnegative strictly increasing integer at timestamps in milliseconds and integer pos in 0–100; accept absent/1.0 version, at least two points, apply inverted once. Treat range as provenance, not extra amplitude. Reject malformed/duplicate/out-of-range points with context. Persist immutable points/source hash in SQLite. Replace source preserves curve ID and validates dependent event durations/ranges. Use linear interpolation, hold before first/after last, finite end behavior; no hidden smoothing, loops or multi-axis/hardware integration.

Unity disables automatic clip time and samples the selected playable. Do not reverse the whole Animator, extract world-root motion or emit gameplay from scrubbed animation events.

Entry blend precedes the driver clock; exit blend follows it. Completion includes claim release. Driven events inherit existing conversation rules by default; on completion compatible ambience resumes. The same scalar can coordinate body and prop as one ingredient.

## 11. WPF authoring and the matrix

Use the existing Card/ActionSequence editor and catalogs. Add a Performance Events library with a searchable event picker on Perform. Author ordinary Cards; do not generate one Session/Phase graph per conversation.

The event editor exposes:

- Kind, mood and movement policies.
- Allowed pose/location choices from the Unity catalog.
- Body/face tag queries, rest probability, refresh triggers and simple cadence.
- Driven-event motion query, curve or oscillator settings, duration and scalar range.
- A computed coverage matrix: legal state by mood, showing eligible base/body/face/motion counts and missing required coverage.
- Included/excluded candidate names and reasons, route reachability and region conflicts as read-only diagnostics.

The matrix edits event policy, never ingredient compatibility. If an ingredient's metadata is wrong, diagnostics name the Unity ingredient to fix. Ingredient previews and frame/mask editing stay in Unity.

Reuse existing transactional authoring commands, Card Save/Revert, semantic undo and Action Block insertion. Event edits commit through ordinary commands with grouped text/number edits; show where-used and duplicate when an independent rule variant is intended. Do not add an experimental shared-draft/overlay system. Catalog refresh preserves Card buffers, focus and selections.

A new event can be duplicated from a useful preset and changed with a few dropdowns. Typing more dialogue or adding a tag-matched snippet uses the existing dialogue workflow. Ctrl+Enter may add the next Dialogue row, and a simple multi-paragraph paste command can insert rows as one undoable operation; neither introduces pacing or movement automatically.

A new compatible enabled Unity ingredient becomes available to matching events without editing those events or any Card. This is a required acceptance test.

## 12. Testing and content iteration

Test logic in the WPF Reference Player with the same Core director and simulated host. Test animation ingredients and visual Card outcomes in Unity.

Add a small Unity development panel: choose an exported Card or Session, choose a test profile/starting session presentation context, Play, Stop, Repeat. Repeat executes the Card again with fresh performance randomness; an optional diagnostic seed is for reproducible failures, not for authoring a fixed result. It neither stores nor pins the chosen combination.

A development Test Card runner uses the production action/director path with disposable game state and normal consent/capability checks. If a Card requires enclosing graph exits/continuations unavailable in standalone mode, fail with “Test in session”; never silently skip its actions. Full Session tests use the unmodified graph VM and real selection path.

Keep the character/scene loaded between tests. On the next Play/Repeat, load the latest successfully generated committed game snapshot, validate it against current required ingredients and reset the test run. WPF automatically regenerates the development snapshot after valid Save/committed rule changes; batching avoids per-keystroke churn. Show export failures/staleness so Unity cannot silently test older logic as if it were current.

No WPF-to-Unity per-line command queue, remote actor controls or request/response choreography protocol. The workflow is save game rules in WPF, play the Card in Unity; repeated visual tests happen in Unity. There is no unsaved multi-editor overlay snapshot or selective graph seek.

Development JSON lives outside Assets to avoid asset reimport/domain reload per rule edit. A build step packages a fresh validated snapshot and serialized Unity registry for a standalone Windows player. Production has no development test file receiver or external-path dependency.

## 13. Validation and transport

The provider reuses existing typed reconstruction/mappings and portable validators. Runtime transport covers every current action discriminator, nested choice and reachable graph definition. Action Blocks are editor-only; inserted actions use normal mappings.

Export an explicitly selected test Card/Session or published Session set with its conservative dependency closure. Include all reachable branches, tag-matching Cards/dialog snippets and all enabled candidate ingredients/rules that could be selected, regardless of a particular seed or test-profile weighting. Validate mandatory compatibility and required bindings across that set, not just a lucky execution.

Keep authoring diagnostics separate from execution validation. An unrelated incomplete Card or disabled ingredient does not block a valid selected Card test. Corrupt storage/FKs and broken included dependencies remain errors. Do not make a global fail-fast loader a prerequisite to scoped loading or suppress its errors afterward.

Use explicit versioned flat field DTOs and arrays, not direct polymorphic serialization. WPF can use framework JSON serialization; Unity can use its built-in field serializer. Portable shared DTOs carry no serializer-specific runtime dependency. Serialize atomically, reconstruct/validate and hash before replacing the previous generated snapshot.

Presentation descriptors remain generated read-only files, not writable SQLite ingredient tables. Validate references from SQLite event rules into the current catalog by Core/domain checks because a cross-store foreign key cannot enforce them.

Canonical DB backups and Git checkpoints require closed writers and no WAL/SHM; ordinary authoring/export uses proper transactions while WPF stays open. Preserve meaningful authored content throughout development.

## 14. Explicit exclusions

Do not implement saved performances, takes, audition/pin controls, exact per-line gesture assignment, New conversation scaffolding, a WPF animation ingredient editor, cross-app asset registration, shared-draft overlays, presentation timeline tracks or per-Card Animator/Timeline assets.

Also excluded: generalized performance graphs, profile inheritance, mood/text AI, general navigation, arbitrary rig/furniture retargeting, multiple actors, full contact solvers, video/device sync, cloud TTS, phoneme generation, content pack distribution and mid-motion save/load.

Keep the early rig feasibility check and final playable/authoring acceptance. The key metric is many coherent executions from the same Card/event rules and an ingredient addition benefiting existing Cards automatically.
