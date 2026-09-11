# Architecture Contract — Performance Director and Authoring System

This file is the authoritative contract for Tickets 00–10, revised after the September 11 workflow review. Detailed rationale and edge cases live in [`Docs/UnityPerformance/BUILD-PLAN.md`](../../Docs/UnityPerformance/BUILD-PLAN.md).

## Product boundary

The project remains one portable game engine with two hosts:

```text
WPF Workbench -> SQLite canonical content -> validated runtime export
                                                |
Existing graph VM -> PerformanceDirector ------> semantic host requests
                                                |
                      WPF simulated host / Unity stage and actor renderer
```

The graph VM still owns narrative flow, card selection, consent/capability eligibility, explicit Continue, GOTO/RETURN and session completion. The new session-scoped performance director owns presentation state and semantic acting choices. It is not another general-purpose graph VM.

## Authority boundaries

| Authority | Owns | Must not own |
|---|---|---|
| Portable `Game.Content` | Stable semantic definitions and action data | Unity objects, transforms, clip paths, bone names |
| Portable `Game.Core` | Compatibility, routes, selected cue IDs, requested/committed state, clocks, region arbitration, deterministic RNG and traces | SQL, WPF controls, Unity timing/render evaluation |
| SQLite provider | Canonical persistence, migrations, loaders, commands, semantic undo data | Runtime host assets or spatial transforms |
| WPF | Writer UX, matrix/diagnostics, simulated preview, export/audition commands | Duplicate compatibility rules or editable Unity binding values |
| Unity | Anchors/routes in space, PlayableGraph, masks, clips, face, gaze, props, audio, camera and acknowledgement | Narrative decisions or content eligibility |

SQLite is the sole editable content source. The Unity JSON snapshot and technical binding manifest are generated, versioned transport artifacts. They are never hand-edited authoring stores.

## State and lifetime

- Character `StageState` is a legal `(LocationId, PoseId)` with an associated base resource. It prevents impossible Cartesian combinations.
- Performance state contains requested and committed stage state, persistent profile/mood, player posture/visibility, active finite work, channel claims, and pending semantic request IDs.
- Arrival is committed only after Unity acknowledges route completion, endpoint tolerance and target-pose settlement. Failure/cancel leaves no false committed state.
- Performance state is session-global and survives Phase GOTO/RETURN. It is reconstructed on a new run and cannot leak into replay.
- Persistent base, face, gaze, blink and breath do not enter `BackgroundActionTracker` and cannot hold `WaitForAll` open.
- Finite nonblocking motion and dialogue use bounded tracked work. Fatal presentation faults become visible session failures even while Core waits for Continue.
- Teardown is idempotent: cancel requests, stop audio/motion, reject stale acknowledgements, release props/regions, destroy Unity graphs, then complete. Failure of one cleanup service does not skip the rest.

## Typed performance vocabulary

Persist stable-ID definitions for Stage, Location, Pose, StageState, directed Transition, PerformanceCue, PerformanceProfile, MotionRecipe, MotionCurve and session presentation setup. Extend direct and tagged dialogue with optional voice, explicit text duration and line performance intent.

Start moods: Happy, Neutral, Mad. They are authored intent. Do not infer them from Happiness, text sentiment, an LLM or Unity state.

Typed constraints cover stage state/location/pose, mood, player posture/visibility, required prop and occupied body regions. Tags cover semantic suitability. Eligibility is the intersection of profile query and cue constraints. Empty restrictions are invalid unless represented by an explicit Any mode.

Closed region vocabulary for this slice: world root; pelvis/legs; torso; left arm/hand; right arm/hand; neck/head; eyes; facial upper; mouth/jaw; eyelids; named prop. Unity maps these semantics to actual masks/curves and validates binding signatures.

Profiles are reusable references. They can query cues or contain explicit cue IDs. The authoring matrix is computed from the same Core predicate and is never a persisted Cartesian table.

## Actions and ordering

Add three typed Action Instances:

- `Set Performance`: independent profile and mood changes, each defaulting to `Keep current`; destination policy defaults to `Stay`, with `Specific` and `DifferentLocation` alternatives. Optional pose restriction. Blocking activity; commits after travel/settle. Session setup explicitly supplies the initial profile/mood/state. Changing only mood never requires reselecting the profile; changing only location never resets mood. Selecting a new profile keeps mood unless explicitly changed; an incompatible result errors without partial application. An entirely unchanged action is shown as a harmless no-op in the editor.
- `Play Motion`: recipe and finite duration, blocking by default, optionally nonblocking. Completes after exit blend and claim release.
- `Set Player State`: posture plus independent body visibility. Blocking activity.

Extend `Dialog` and `Dialog From Tags` with resolved structured presentation: text, optional voice, duration, mood/cue handling (`Auto`, `None`, specific). Preserve existing tagged-dialogue selection and RNG.

These are ordinary activity actions in existing appropriate action scopes and create no graph ports. `IsAlwaysBlocking` must not imply `ReduceFlow`; classify transfer actions explicitly.

Ordering rules:

- One actor movement request and one speech channel serialize accepted commands.
- Dialogue following Set Performance begins only after target readiness.
- Nonblocking dialogue is FIFO and never overlaps speech.
- A context-changing Set Performance/Player State waits for previously accepted speech.
- Finite motion may overlap speech only when compatible.
- A second overlapping finite motion is an explicit error in this slice.
- Auto line cues revalidate claims at onset and may be suppressed with trace reason; explicit incompatible cues error.
- Line-local mood restores persistent mood at line end; gestures cannot leak into the next line.
- Wait/choice keep compatible ambience alive; application pause freezes all presentation clocks/audio/movement.

Dialogue's default is `Inherit` for mood and `Auto` for acting. `None` means deliberately no cue; it is never overloaded to mean keep/inherit. The editor shows the effective inherited values and their source. In a reusable Card that can have multiple calling contexts, show `From caller` rather than guessing a single resolved profile. Presentation state changes validate together before any field is committed.

## Variation and determinism

Core chooses intent-level destination, gesture, face, gaze/cadence and procedural-rate values using separate seeded streams, independent from card and dialog selection. Sort candidates by stable ID and use a stable cross-host seed derivation.

Use presentation-only immediate anti-repeat: exclude the last cue in the channel when another eligible cue exists. One candidate is legal with a warning. Optional `None` is explicit weighted content. Required base/face coverage fails loudly.

Core advances on elapsed presentation time and semantic host events. Tests use a manual clock. RNG is consumed on scheduled decisions, never per rendered frame. Same seed plus same event/clock sequence reproduces semantic traces; a seed alone does not reproduce different human wait timing.

## Stage and route contract

Initial locations: Room center, Door, Window, Chair, Side of bed, Foot of bed. Initial legal character states:

- Standing at all six locations;
- Sitting at Chair and Side of bed;
- LyingDown at Side of bed.

Initial directed route topology:

- two directions between Room center standing and each other standing location (10 edges);
- Chair standing↔sitting (2 edges);
- Side of bed standing↔sitting and sitting↔lying (4 edges).

Core chooses least authored-cost route with stable-ID tie breaks. Reverse routes must be explicit. Missing routes fail. `DifferentLocation` excludes the committed location and guarantees a real location change.

Unity owns in-place locomotion spatial paths and final facing. One mover owns world root. Pose transitions are calibrated at station origins; actor-local pelvis motion remains distinct from world-root travel. Arrival requires root within 2 cm and 3 degrees plus pose settlement, followed by visual contact review.

## Animation composition

Build one fixed-topology body PlayableGraph per actor. Do not create an Animator state/Timeline per combination and do not rebuild the graph every frame.

- Foundation: full base/locomotion/pose transition.
- Body overlays: at most one ambient/line gesture plus one compatible finite motion.
- Breath: verified additive input, suppressible on torso conflict.
- Gaze: post-body limited head/eye aim, attenuated on head ownership.
- Face composer: sole writer for expression, speech mouth/jaw and blink eyelids.

Region priority: transition > finite motion > line gesture > ambient gesture. Higher priority suppresses/fades lower conflicting ambience; accepted finite authored work is not surprise-canceled. Normal gestures are masked override clips unless explicitly authored and visually proven additive.

Ticket 01 freezes the actual rig contract: import settings, scale/axis/origin, reference pose, required bones/blendshapes, masks, curve coverage, facial channel separation, prop attachment and useful scrub intervals. Metadata does not substitute for visual proof.

## Procedural motion

One normalized scalar drives a calibrated motion clip interval and all regions/prop channels owned by that recipe:

```text
u(t) = uMin + p(t) * (uMax - uMin)
sampleSeconds = u(t) * clipDuration
```

Unity disables automatic clip time and sets playable local time before evaluation. No world-root extraction or animation-event gameplay from scrubbed clips.

Oscillator rates are displayed as source frames/second with derived traversal time/cycles per minute. Draw one rate per half-cycle, retain it for that segment, use smoothstep endpoint easing, process elapsed endpoints deterministically with a guard, and never reroll per frame.

Accepted `.funscript` subset: JSON; version absent or `1.0`; at least two actions; strictly increasing nonnegative integer `at` milliseconds; integer `pos` 0–100; apply `inverted` once on import. `range` is provenance only. Reject invalid input with point context; never sort/clamp/repair silently. Persist imported immutable points in SQLite/export. Linear interpolation; hold before/after; default duration is last timestamp; loops and multi-axis are deferred.

## WPF authoring contract

### Write, try, adjust

The primary loop is edit a line or intent, press **Audition**, watch it, and continue typing. Saving a Card, exporting for a build, switching apps, finding a Unity button, and replaying a whole session are not prerequisites to trying a line. The command labels below describe a small toolbar on the existing editor; they are not a new authoring application.

- **Audition** plays the selected line or contiguous presentation passage using current editor buffers. With no row selection, use the active line. On a profile/cue/recipe, audition that item in the displayed test context. Reuse the last seed; a text edit alone must not deliberately reroll the acting.
- **Replay take** repeats the last audition's immutable snapshot, resolved choices, context and event schedule. This can show the previous take even if the editor has since changed; label it as the last take. It reports a stale/missing asset rather than claiming identical replay after a binding change.
- **New take** uses current edits and changes only the presentation seed. It does not redraw cards, change a chosen dialogue snippet or alter choices/stats. Reuse runtime event/selection trace data; no saved-replay product is required.
- **Use on this line** pins a chosen body/face cue through the existing explicit line override. It changes only the owning buffered action. **Auto** removes that override. Do not store the entire randomized take as a new choreography resource.
- **Stop** cancels the audition promptly, releases claims/audio and returns to the displayed starting context. Repeated Audition replaces the previous request after cancellation acknowledgement; intermediate requests are superseded, never accumulated into a playback queue.

Provide keyboard access without stealing normal text-editing shortcuts. Ctrl+Enter inserts/focuses the next Dialogue row; Shift+Enter inserts a newline inside one line. An explicit **Paste dialogue** command converts nonempty paragraphs to direct Dialogue actions as one undo unit at the insertion point, preserving paragraph-internal line breaks. It inserts no hidden waits, stat changes or mood resets. Default advanced fields stay collapsed. Keep the existing Card body field separate from performed dialogue; show which text is actually spoken.

Record the line's text fingerprint when attaching voice audio. Editing voiced text marks that association **Voice needs update**; never play old wording while implying it matches the new subtitle. The audition toolbar offers a persistent **Text only** mode so writing can continue before recording new audio; this is explicit preview intent, not a missing-asset fallback. Publishing voiced content requires a current association, or the author explicitly clears voice and uses text-only delivery. This is a small field/validation rule, not an audio-production system.

### Starting context and a useful first document

Ship the configured test stage, validated starter cues and one Conversation profile as reviewed starter content during Tickets 03–04; incomplete new cues are disabled as described below. A writer must not configure nine states and sixteen edges to write their first card.

Add one **New conversation** authoring command using existing semantic commands: create a normal Session, private Phase and Card, wire a single CardExecutor path followed by an ordinary EndSession action, attach configured stage/profile defaults, and put one empty Dialogue row before an explicit WaitForContinue. Give the Card a fresh ordinary CardTag and the Phase a required-tag filter for that ID, so this scaffold cannot accidentally draw some other library Card. Name/link that selector clearly in advanced context; do not introduce a direct-Card runtime node or hide a special selection rule. Mint normal IDs and persist/undo the entire scaffold/tag as one operation. If starter bindings are unavailable, show the exact setup dependency; never manufacture dummy assets. Re-running starter installation is keyed by stable fixture identities and cannot overwrite authored edits.

For local audition, display a small context strip: stage state, profile, mood, player state and test profile. Default from the chosen Session setup; optionally copy the last player's settled state at an idle/Continue boundary. If a reusable Card has no unique caller, show the chosen test context explicitly. Do not execute preceding arbitrary graph actions to guess it. **Test arrival** includes the requested route; ordinary line audition starts already settled at its displayed pose so the writer need not watch the walk every time. This initialization is explicitly sandbox setup, never an acknowledged gameplay movement or evidence that routes passed.

The local harness uses the production ActionExecutor/director, with disposable run state and clock. It accepts only the presentation actions/Continue barriers in the selected passage. Reject selections containing choices, stat/progress changes or flow with a link to **Play session**, which uses the real graph VM and authored eligibility from the selected start. Never silently skip such actions. Simulated testing retains the same scope and visible context when Unity is unavailable.

### Buffers and shared edits

SQLite holds committed authoring content. Card Save/Revert remains unchanged. Profiles, cues and recipes get focused edit buffers with **Apply** and **Revert**; no database mutation per slider drag. Audition overlays only the owning Card and explicitly participating shared-definition buffers on a cloned, scoped snapshot. Label it **Unsaved changes** and list the included buffers. It neither saves the DB nor bypasses the normal Reference Player's dirty-Card gate. Unrelated dirty buffers stay outside that audition.

Show `Shared · used by N` in shared editors. **Apply to shared definition** is one validated transaction/undo unit, with a before/after coverage diff and usage links visible beside the button, not a modal prompt on each edit. **Make unique here** copies the current edited values and rebinds only the selected action, within its buffer if it is unsaved. **Revert** restores the last applied values. No profile inheritance and no hidden automatic cloning.

Each audition gets fresh director state. A playing session keeps its immutable snapshot until explicit restart. Saving/applying new content must not mutate an active run. Preview copies are ephemeral, not a second editable database.

### Register vocabulary once

Add a simple intake list for Unity assets. Unity publishes read-only asset descriptors for unregistered clips/presets, identified by GUID plus subasset local ID, with duration/sample rate and inspected curve coverage. WPF **Register selected** mints the semantic Resource/Cue/Recipe IDs and sends a binding-registration request referencing those descriptors; Unity's Editor helper resolves assets and writes its own registry. Acknowledge the request before the binding is usable; retries reuse IDs and never duplicate records. No copied GUIDs or duplicate name entry. Runtime snapshots contain semantic IDs only.

Technical fields remain owned by Unity. Author-reviewed pose/mood/prop applicability stays in WPF. Bulk assign tags/moods/compatible states; duplicate a cue's rules for a sibling clip; preview before enabling. New cues default to **Not in rotation** (`Enabled=false`), are excluded from Auto selection, and can be explicitly auditioned if locally valid. **Enable** validates the semantic definition and required binding, then includes it in querying profiles. Already-enabled required content with a broken binding errors; do not silently disable or skip it. Intake is a convenience over existing typed definitions, not a generalized import platform.

### Diagnostics that lead back to writing

The computed matrix shows coverage and counts by legal state/mood/channel, with optional-none and required gaps distinct. Select a cell to see candidates, typed exclusion reasons and binding state; click a problem to open the exact field/source. Keep that cell/test context selected while fixing it. Show changes in candidate counts before applying shared edits; provide a simple **Try eligible cues** audition list, one cue at a time with Stop, to inspect the newly added vocabulary. Testable seeds and traces stay in a details panel.

Minimal eligible counts, use sites and per-item errors ship with basic editors in Ticket 04; Ticket 09 adds bulk matrix/coverage comparison and intake conveniences. Support exact-ID undo, nested choices/Action Blocks, motion range plots/scrub, identity-preserving script **Replace source** with affected recipe validation, and safe focus/dirty-buffer refresh. Actions inherit recipe duration until explicitly overridden; label overrides and provide Reset to recipe. Rate and range remain recipe settings, without extra per-action override fields.

### One-command Unity audition

Configure the local project/preview connection once. An already-running development audition host accepts the WPF Audition command as the explicit user action and automatically consumes its atomic request. No second Unity Run click. This is the first visual-loop deliverable in Ticket 05, connected to Ticket 01's real proof renderer and a basic text-only presenter; Ticket 06 expands travel, Ticket 07 expands voice/player, Ticket 08 expands motion. Ticket 09 must not be the first time a writer watches an edited line.

Use versioned filesystem request/response and receiver-ready status, monotonic request IDs, snapshot hash, required-binding signature and cancellation acknowledgement. Ordinary edits alone trigger nothing. Reload the small immutable preview snapshot from a local generated directory **outside Assets** at an audition boundary; avoid Editor asset reimport/domain reload/build per line. Keep the scene/rig loaded. Connection setup explains how to start the audition scene once; show Ready/Busy/Unavailable and a retryable error without losing edits. UI stays responsive and focus returns to the edited line.

Development preview files are excluded from source control/builds. Publish/build export is a separate operation. Target warm-start response within two seconds from command to first preview frame, excluding intentional travel/entry blends/audio import; measure on the user's machine and fix avoidable export/reimport delays. Cold Editor launch is measured separately. Do not implement a network service or in-flight content mutation.

### Validation scope and unfinished work

An unrelated unfinished Card or disabled unbound cue must not prevent auditioning a valid line. Share provider reconstruction/mapping and Core validators, but separate authoring diagnostics from strict execution validation. A global `Load()` that validates the entire library before roots are selected cannot implement this workflow; add a scoped load/snapshot builder rather than catching and suppressing its errors.

For local audition, roots are selected presentation rows plus the displayed context; include every candidate that their profile queries may select, their explicit references, required base/face/prop/voice data, and routes if Test arrival is requested. For publishing selected Sessions, traverse all reachable graph branches, all tag-matching Cards/Dialog snippets regardless of current RNG/consent weighting, all selectable profiles/cues, states, required routes and assets. Keep action DTO coverage complete for every subtype. Do not validate only a lucky sampled path. Disabled cues outside explicit references are not candidates. Library-wide diagnostics remain available without blocking unrelated scoped runs; corrupt storage/FKs always fail loudly.

Content hash and asset signature are separate: changing text requires a new snapshot, not rebinding every animation. Unity regenerates its technical manifest automatically on actual binding/asset changes; WPF refreshes it automatically. Validate signatures for the required asset set, not global revision equality. Adding an unrelated asset cannot stale an otherwise identical take. No hash that depends on a second hash that in turn depends on the first. Snapshot/schema incompatibility, absent required references or changed required clip/rig signatures remain hard failures with a direct repair link.

## Unity runtime/export contract

Replace the new playable path's legacy ScriptableObject/deck bootstrap with an explicit complete runtime DTO export and snapshot-based session selection. Retain old fixtures only until deliberately retired.

Use flat field DTOs with explicit action discriminators and arrays, complete for every current action including nested sequences; Action Blocks remain editor-only and their inserted actions use those same mappings. Export the selected scope from one consistent DB read, validate/read back, hash, atomically replace. Failure leaves the old artifact visibly stale. Unity reconstructs and strictly validates existing portable definitions. Build export uses committed data only; audition uses the explicitly labelled buffer overlays above.

Unity binding registry uses serialized asset references keyed by semantic resource ID. Technical manifest reports kind, duration/sample rate, region/curve signature, rig and targets. Missing/duplicate/wrong-kind/stale required bindings block the selected preview/build/session. Package exports include a visible list of selected Sessions and their complete dependency closure; a separate Check library operation reports unfinished work outside that set. Never silently omit a broken included dependency.

Standalone Windows development player contains the snapshot/assets and works with WPF closed. Runtime errors appear in game UI. Use a labeled in-memory acceptance profile; do not claim persisted WPF profile parity. Reachable unsupported host capabilities block preflight.

## Required exclusions

Do not add generalized performance graphs, profile inheritance, arbitrary expressions/scripts, mood AI/text analysis, motion matching, NavMesh/general navigation, multi-character direction, arbitrary rig/furniture retargeting, a contact solver, cinematic camera director, mid-motion save/load, live session content replacement, multi-axis/video/device synchronization, cloud TTS, phoneme generation, hardware integration, pack download/dependency systems, or per-scene Timelines.
