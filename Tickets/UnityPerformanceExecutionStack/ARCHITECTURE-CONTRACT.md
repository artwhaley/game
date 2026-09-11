# Architecture Contract — Performance Director and Authoring System

This file is the compact, non-negotiable contract for Tickets 00–10. Detailed rationale and edge cases live in [`Docs/UnityPerformance/BUILD-PLAN.md`](../../Docs/UnityPerformance/BUILD-PLAN.md).

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

- `Set Performance`: profile, mood, destination policy (`Stay`, `Specific`, `DifferentLocation`), optional pose. Blocking activity; commits after travel/settle.
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

Ordinary writer flow stays in Card/ActionSequence editing: write dialogue, add Set Performance at meaningful changes, leave lines on Auto, occasionally pin a cue, preview/export/replay.

Add a Performances library for profiles, cues, stages and motion recipes. Use dedicated controls/view models; do not create reflection property grids or indefinitely expand monolithic main-window files.

The computed matrix shows eligible counts by legal state/mood/channel, optional-none, required gaps, typed exclusion reasons, binding status, route reachability and region conflicts. Editing changes source profile/cue rules. It never stores cells.

Support shared usage counts, usage navigation, duplicate and contextual Make Unique, semantic undo, nested choice/action-block clone paths, stable picker focus/dirty buffers, bulk cue metadata editing and motion curve plot/scrub.

Visual audition is an explicit filesystem request/response handshake with monotonic IDs and snapshot/binding hashes, atomic writes and stale-result rejection. It is not a live network service or a second content store.

## Unity runtime/export contract

Replace the new playable path's legacy ScriptableObject/deck bootstrap with an explicit complete runtime DTO export and snapshot-based session selection. Retain old fixtures only until deliberately retired.

Use flat field DTOs with explicit action discriminators and arrays, complete for every current action including nested sequences/templates. Export from one consistent DB read, validate/read back, hash, atomically replace. Failure leaves the old artifact visibly stale. Unity reconstructs and validates existing portable definitions.

Unity binding registry uses serialized asset references keyed by semantic resource ID. Technical manifest reports kind, duration/sample rate, region/curve signature, rig and targets. Missing/duplicate/wrong-kind/stale bindings block preview/build/session start.

Standalone Windows development player contains the snapshot/assets and works with WPF closed. Runtime errors appear in game UI. Use a labeled in-memory acceptance profile; do not claim persisted WPF profile parity. Reachable unsupported host capabilities block preflight.

## Required exclusions

Do not add generalized performance graphs, profile inheritance, arbitrary expressions/scripts, mood AI/text analysis, motion matching, NavMesh/general navigation, multi-character direction, arbitrary rig/furniture retargeting, a contact solver, cinematic camera director, mid-motion save/load, live session content replacement, multi-axis/video/device synchronization, cloud TTS, phoneme generation, hardware integration, pack download/dependency systems, or per-scene Timelines.
