# Unity performances: build plan for the first playable slice

Planning baseline: September 10, 2026. This is a design proposal and implementation handoff, not a report of implemented or verified features. Read the repository's `agents.md` when turning this into execution tickets.

## 1. The outcome

Build a short, replayable game session, authored in WPF and played in Unity, featuring one character in one room. The character walks to a named place, settles into a pose, delivers dialogue with expressions, gaze, blinking and breathing, changes position at a story beat, and demonstrates a bounded procedural motion. The player can make a choice, continue the session, change between four presentation postures, and finish and replay the game.

The larger purpose is an authoring system in which writing another conversation mostly means writing dialogue. Animation specialists add reusable performance vocabulary; writers express intent and occasionally direct a specific beat. Adding dialogue must not require creating a Timeline, Animator state, mask, transition, or new matrix row for every combination.

Use the requested game's conversational presentation as a product reference, not as an implementation specification. This plan makes no claims about its internal technology and does not require its assets or source.

**Central decision:** add a small, session-scoped `PerformanceDirector` to portable Core. It coordinates persistent presentation state alongside the existing Session/Phase graph VM. Unity renders its decisions. Do not replace the graph VM or introduce another general-purpose graph editor.

The feeling of variety comes from coherent choices, independent timing and restrained repetition. Multiplying the number of clips is not a meaningful quality metric if the combinations look wrong.

## 2. What the inspected repository changes about this plan

The root overview and codemap correctly identify the major boundaries. The inspected code adds several implementation constraints:

| Existing area | Consequence for this work |
|---|---|
| Portable files live in `Assets/Scripts/Portable/Game.Content` and `Game.Core`, linked by the .NET projects | Put new portable definitions and director logic there; do not create a competing engine under `DotNet`. |
| SQLite snapshot loader, semantic authoring commands, typed Action Instances, shared sequence editor | Extend these paths. Performances are first-class reusable content with ordinary action references. |
| `GameManager` still uses `UnityContentGraphBuilder`, a legacy deck and SO session selection | A current-content Unity bootstrap is required. Adding an animation service alone will not make WPF-authored content playable. |
| `IDialogService.ShowAsync(string, token)` only carries text; Unity does not wire a dialog service in `GameManager` | Add a structured dialogue request and Unity presenter. Existing direct/tagged dialogue must converge on the same presentation path. |
| `ActionExecutor` currently treats most `IsAlwaysBlocking` actions as control transfers | Separate transfer classification from blocking policy before adding blocking performance or player-state actions. Test this directly. |
| Background actions and `WaitForAll` already exist | Persistent ambience must stay outside that tracker. Only finite motion/speech work belongs in it. |
| `ShutdownAsync` currently cleans up toy output | Expand session teardown to performance, movement, audio and pending presentation tasks; one failed cleanup must not prevent the others. |
| Action Blocks deep-copy sequences into consumers | Use them for insertion convenience. They are not shared runtime performance profiles. |
| No character prefabs, animation clips/controllers, FBX models or speech audio appeared in the inspected `Assets` inventory | Real animation content is a prerequisite and a scheduled deliverable, not something the last integration ticket can assume exists. |
| Unity 6000.5.9f1 is pinned; Timeline/Cinemachine are installed | Keep the editor version. Use built-in Playables, animation and audio; no new runtime package is required by this plan. |

The documentation reports 384 passing .NET tests at the previous gate; they were not rerun for this planning task. Existing Unity test counts are not evidence that these new features work. Existing schema files reach v11; implementation must inspect the actual database before choosing the next migration number.

## 3. Decisions to preserve when generating tickets

1. **One pose foundation, not one layer per posture.** Standing, sitting and lying are alternatives within the foundation. Overlay gestures, procedural motion, gaze and facial channels above it.
2. **A persistent performance is the normal mode.** It continues across dialogue, cards, choices and graph transfers until explicitly changed or the session ends.
3. **Mood is authored presentation intent.** Start with Happy, Neutral and Mad. Do not silently derive it from Happiness, sentiment analysis or an LLM. Game actions can explicitly request a mood when game state warrants it.
4. **Tags express suitability; typed constraints express physical validity.** Location, pose, occupied body regions, required prop and required player state are not loose strings.
5. **Profiles select vocabulary; writers do not enumerate combinations.** The WPF matrix is a computed view of eligibility and coverage, with explanations and preview.
6. **Core chooses the semantic performance and route; Unity owns spatial execution.** No transforms, bone names, clip objects, Unity enums or scene paths in Core.
7. **Use a small programmatically built PlayableGraph for body animation.** Do not grow an Animator Controller state for every profile/gesture/mood combination. Facial composition and gaze have explicit Unity owners.
8. **Normal movement uses authored routes and in-place locomotion in this fixed room.** No NavMesh dependency or general crowd/pathfinding system in the slice.
9. **Procedural motion is a normalized scalar driving a calibrated clip interval.** Random oscillation and imported single-axis funscript feed the same sampler. Support both arm/prop motion and a body motion that includes the pelvis.
10. **SQLite stays canonical; Unity consumes an exported build artifact.** This supersedes the previous direct-SQLite-in-Unity plan for this milestone. JSON is a generated transport, never another authoring store.
11. **Target a Windows desktop player first.** Produce a launchable Unity build as well as Editor Play Mode. Cross-platform packaging is outside this stack.
12. **No silent compatibility fallback.** Explicitly optional silence/idle is valid authored behavior. Missing required bindings, impossible travel and invalid required performances are errors.

These decisions are sufficiently concrete to implement. The early animation proof may expose an asset limitation; fix the binding, clip or affected decision explicitly and update this document before dependent work. Do not let a ticket quietly replace the architecture with per-scene Timelines.

## 4. Architecture and ownership

```text
WPF Workbench -> SQLite -> production snapshot loader -> GameContentDefinition
                                     |
                           validate + export build DTOs
                                     |
                              Unity runtime snapshot

Existing graph VM -> Set Performance / Dialog / Play Motion / Set Player State
                                     |
                     Core PerformanceDirector + selectors
                       + route planner + scalar sampler
                                     |
                        semantic requests and completions
                                     |
               Unity stage bindings / actor renderer / speech / camera

WPF Reference Player uses the same Core director with a simulated host.
```

Core owns current desired mood/profile, committed stage state, transition status, eligible candidates, selected cue IDs, route IDs, procedural configuration, independent random streams and structured traces. Unity owns the actual actor transform, clip mixing, masks, blendshapes, gaze limits, anchors, routes' spatial paths, props, audio playback and camera.

Core stores both requested and committed stage state. A new destination becomes committed only after Unity acknowledges arrival and pose settling. A failed or canceled move does not claim success. On an unrecoverable presentation error, stop the session and report the failed request; restarting reconstructs the initial stage.

The director is session-global, like Temperatures, not part of a Phase continuation. GOTO/RETURN do not restore old poses, moods or performance RNG. This avoids rewinding the character when narrative control returns to a caller.

Use one serialized host execution context. Unity calls into presentation updates on its main thread; WPF uses its dispatcher. Host acknowledgements carry request IDs and are marshaled back to that context. Do not mutate director state from worker threads or recursively advance the graph from animation callbacks.

## 5. Content vocabulary: small, explicit and reusable

Use stable IDs for persisted references, editable names for people, and typed definitions with relational membership tables. Names below are proposed type/table families, not a demand for one class per concept.

| Definition | Required meaning |
|---|---|
| Stage / Location | A named room and its semantic places. Location IDs resolve to Unity anchor bindings; they contain no coordinates. |
| Pose | Standing, Sitting, LyingDown for the character in this slice. A pose is a compatibility family, not a universal animation. |
| StageState | A legal `(LocationId, PoseId)` with its base-loop resource and optional semantic tags. Sitting on the bed and sitting on the chair may use different base loops. |
| Transition | Directed source/destination StageState IDs, transition resource ID, authored nominal duration/cost and watchdog bound. |
| PerformanceCue | Resource ID, role (body gesture or face), allowed stage states/poses/locations, allowed moods, semantic tags, occupied regions, prop/player requirements, duration, blend timing and gaze/breath suppression declarations. |
| PerformanceProfile | A named reusable palette: allowed stage states, mood-specific cue queries, gaze targets, gesture/rest cadence, face dwell and optional-channel policy. |
| MotionRecipe | Compatible stage states/moods/player states, motion resource, occupied regions, normalized interval, driver (oscillator or script), finite duration, entry/exit blend and optional prop requirement. |
| MotionCurve | Immutable imported timed positions in portable units, source filename/hash and importer version; no device behavior. |
| Dialogue presentation fields | Existing direct line/snippet text plus optional voice resource, text-only delivery duration and line-local mood/cue override. |
| Session presentation setup | Stage ID, initial StageState ID, default PerformanceProfile ID and initial player posture/body visibility. Required for sessions declaring performance presentation. |

Resource remains the identity of a bindable asset. Add explicit kinds for the needed base/gesture/transition/motion/face/voice resources rather than storing Unity references in profile rows. A motion curve is portable data; do not reuse a toy-pattern resource as a visual animation merely because both may originate from funscript.

**Region vocabulary:** world root, pelvis/legs, torso, left arm/hand, right arm/hand, neck/head, eyes, facial upper region, mouth/jaw, eyelids, and a named prop channel. It is deliberately small and closed in code for this milestone. Unity maps it to actual masks/bones/curves. Combining regions is allowed; creating arbitrary user scripts or a general constraint language is not.

**Compatibility predicate:** a candidate must meet its typed stage/mood/player/prop restrictions AND every required tag AND at least one any-tag when that set is nonempty. Empty explicit allowed-state/mood sets are invalid; an explicit `Any` mode means unrestricted. Profile queries and cue restrictions intersect. There is no specificity ranking or hidden rule precedence.

A scene binding manifest reports which resources, targets and props exist and their technical signatures. WPF imports this as read-only technical metadata. It never edits transforms or masks. Unity validates that declared region ownership agrees with actual mask/curve behavior. The manifest belongs to Unity; semantic rules belong to SQLite. No property has two editable masters.

## 6. The writer-facing actions

Add three action kinds and extend the existing two dialogue kinds. Avoid a duplicate dialogue container or nested performance graph.

| Action | Parameters and completion |
|---|---|
| **Set Performance** | Required profile; mood defaults to that profile's default; destination policy `Stay`, `Specific`, or `DifferentLocation`. Optional pose restriction. Always await arrival and initial expression settling, then finish. The resulting ambience persists. |
| **Play Motion** | Required reusable recipe and finite duration (script duration can be the default). Blocking by default; optional nonblocking to allow dialogue concurrently. Completion includes exit blend and release of claimed regions. |
| **Set Player State** | Posture: Standing/Sitting/Kneeling/LyingDown; Body visible: yes/no. Always await the presentation change. |
| **Dialog / Dialog From Tags** | Keep text or tag selection. Default performance handling is `Auto`; optional line-local mood and `Auto`, `None`, or specific gesture/face cue. Existing blocking flag remains. Optional voice/duration data travels with the resolved line. |

`Stay` preserves the committed stage state and errors if the selected profile cannot run there. `Specific` names a legal target state or a location with an eligible pose selected by Core. `DifferentLocation` excludes the current location, then chooses among reachable eligible target states. It guarantees a location change, not merely another idle animation. If none exists, show the precise constraint failure; do not stay in place silently.

New sessions start in their explicitly configured stage/profile. A dialogue line never needs to guess the initial pose. A Set Performance action replaces the profile/mood policy, but does not move unless asked. Repeat calls to the same profile with `Stay` do not restart the base loop gratuitously.

The three new actions are legal in the existing ordinary activity scopes, including nested choice sequences; they create no graph ports. Add their scope/clone/template behavior explicitly to the registry and tests.

Example card/phase sequence, as it should read in the editor:

```text
Set Performance  Conversation / Happy / Side of bed / Sitting
Dialog           "Come sit with me."
Dialog           "How was your day?"
Wait for Continue
Set Performance  Conversation / Mad / Different location
Dialog           "I wanted you to listen."       [gesture: Auto]
Prompt Choice    [two authored options, one changes the mood]
Set Performance  Conversation / Neutral / Chair / Sitting
Play Motion      Prop demonstration / 8 seconds / nonblocking
Dialog           "Watch the rhythm."
Wait for All
Set Player State Kneeling / Body visible
Wait for Continue
End Session
```

This is an illustration of authoring vocabulary, not the complete acceptance session. Ordinary writers see names, filtered pickers and concise row summaries, never resource GUIDs or layer indexes.

### Precise lifetime and ordering rules

- Set Performance, Set Player State and blocking motion are ordinary asynchronous presentation actions, not graph transfers. Correct the existing executor classification accordingly.
- Persistent base pose, face, blink, gaze and breathing are not background Action tasks and never make `WaitForAll` hang.
- Nonblocking Play Motion registers one finite task. A subsequent Set Performance that needs a conflicting region or relocation waits for that motion to release it. A second overlapping motion request is an explicit conflict error in this slice, not an unbounded queue.
- One character has one speech channel. Resolve tagged dialogue and its cues synchronously in authored order before awaiting presentation. Speech requests are FIFO; two nonblocking dialogue actions never speak over one another.
- A Set Performance or Set Player State request waits for already accepted speech to finish before changing the context. New lines following it begin after readiness. A finite motion can run during speech when compatible.
- One-shot cues selected for a line start at line onset, do not loop accidentally, and release their claims at completion. A line-local mood returns to the persistent mood when that line finishes. Subsequent queued lines resolve against the persistent state plus their own overrides, not the preceding line's temporary state.
- Revalidate a queued cue's region availability at speech onset. If a finite motion has since claimed that region, an Auto gesture may be suppressed with an explicit trace reason; do not reroll it and disturb deterministic ordering. An explicitly requested incompatible cue is an error. End or fade out unfinished line gestures when their line ends; they do not leak into the next line.
- `WaitForContinue` keeps the actor alive: idle, gaze, blink and breathing continue. It does not restart a line or issue movement. A choice similarly leaves compatible ambience running.
- Application pause freezes presentation time, finite motions, travel, audio and timed delivery; Continue-wait is not application pause. Cancel/stop aborts them. Skip-audio controls are outside the slice.
- End/error/menu exit/scene unload use the same idempotent cleanup path. Cancel pending requests, stop audio, discard stale acknowledgements, release props/regions and destroy graphs. No run state leaks into replay.
- New presentation failures must not be swallowed by the existing background-action logging wrapper. Latch a fatal presentation error in the session and notify the host immediately, including while waiting on Continue; stop acceptance of further commands and invoke cleanup. Keep the failed request/action ID in the surfaced error. This does not require changing the failure policy of unrelated legacy actions.
- Legacy Cutscene remains separate. Performance-enabled sessions may not invoke it in this slice: preflight rejects that combination until an explicit ownership handoff is implemented. Do not add a hidden competing Animator writer.

## 7. Conversation direction and the feeling of variety

### Choose intent first, details second

On Set Performance, choose a destination if requested, execute its route, commit the state, then select an initial compatible face/gaze. On each dialogue onset, choose at most one body gesture and one face adjustment. While idle, schedule occasional gestures and gaze shifts at authored intervals. Do not reroll every frame or randomize locomotion during an ordinary sentence.

Starting profile values for tuning: idle gesture opportunity every 6–12 seconds; face dwell 4–8 seconds; gaze dwell 2–5 seconds; a 50% chance of no body gesture on a line. These are starting content values, not universal constants. A running line gesture suppresses idle gesture opportunities until it releases the channel; missed opportunities are discarded, not queued for a burst.

Respect minimum face dwell across short lines: Auto may retain the current compatible face instead of choosing a new one. An explicit cue or mood change can replace it immediately with a blend. Give dialogue precedence over idle facial changes for the line's duration.

Happy permits the happy face/body vocabulary, Mad the mad vocabulary, Neutral its own vocabulary. A wistful face can later be tagged as a Neutral variant without creating a mood simulation. Hands-on-hips is Mad-only because its cue explicitly says so. Do not invent automatic intent classification from dialogue text.

Implement a small presentation-only anti-repeat rule now: remove the last selected cue in that channel when another eligible cue exists. Choose uniformly among the remainder. A one-candidate pool is legal but displays a variety warning. `None` is an explicit weighted alternative for optional gestures; required face/base coverage cannot vanish. Do not extend this into the deferred card-selection anti-repeat feature.

Profiles contain an authored neutral/rest behavior per allowed state. Missing required coverage errors. An optional gesture intentionally yielding no gesture is logged as such; it is not a rescue for bad content.

Use separate seeded domains for destination, gesture, face, gaze/cadence and motion speed, separate from card and dialogue selection. Seed derivation must be stable across .NET and Unity; do not use runtime string hash codes or `UnityEngine.Random` for content decisions. Sort candidates by stable ID before sampling.

### Time and deterministic testing

Core accepts elapsed presentation time and semantic events through a small update method; it does not own a forever-running Task or a render loop. Unity and the WPF simulated host advance it while the graph awaits speech, movement or Continue. Automated tests use a manual clock.

Discrete scheduled events process in timestamp order, with a stable tie order: completed releases, context changes, speech start, then idle opportunities. Use absolute due times so frame rate does not change oscillator speed draws. Idle opportunities during a busy channel are skipped consistently. No RNG is consumed by bone evaluation or every rendered frame.

Seed plus the same clock/event sequence should reproduce selections and motion values. A seed alone cannot reproduce a player waiting a different amount of time. Record presentation events/times for diagnostic replay; do not promise identical visual frames across hosts or add a full replay product.

## 8. Unity animation composition

Build one body PlayableGraph per actor with a fixed topology and reusable mixer inputs. Replace/connect selected clips and crossfade weights. Do not rebuild the graph every frame. Unity's layer mixer supports masks and additive flags; these mechanisms support this design, but do not establish that an arbitrary clip is safe to layer. See [layer masks](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimationLayerMixerPlayable.SetLayerMaskFromAvatarMask.html) and [additive layers](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Animations.AnimationLayerMixerPlayable.SetLayerAdditive.html).

| Evaluation responsibility | Contract |
|---|---|
| Foundation | One base idle or locomotion/pose-transition blend. Full skeletal pose; speech facial curves are excluded. |
| Body overlay composition | One ambient/line gesture plus at most one finite motion. They may coexist only on disjoint claimed regions or an explicitly supported additive breath channel. |
| Breath | Low-amplitude additive motion authored against a known reference pose. Suppressed if an active full-body cue already contains incompatible breathing/torso motion. |
| Gaze | After body evaluation, apply limited head/eye aim toward a named target, attenuated when the active cue owns the head. |
| Facial composer | One writer combines face preset, speech mouth/jaw and blink eyelids. Skeletal clips must not overwrite its channels. |

Semantic roles need not correspond one-for-one to Unity Animator layers. Separate facial channels are often direct blendshape composition; they should not become several controllers racing to write the same property.

### Region arbitration

Priority is transition > finite motion > line gesture > ambient gesture. Higher priority suppresses/fades lower-priority conflicting cues; it does not erase the persistent profile. On release, select a fresh eligible ambient cue rather than resuming a half-finished gesture.

For this slice, transition requests wait for finite motion and speech as specified above. Priority governs which ambient overlays are suppressed during actual execution, not surprise cancellation of authored finite work. Gaze yields to a head-owning nod/point cue; blink can continue unless a cue explicitly owns eyelids.

An arm motion can reserve one arm/hand and a prop while other compatible body parts animate. A seated rhythmic body motion can reserve pelvis/legs/torso while leaving face, gaze and compatible arms available. The foundation is overridden in those regions. Never promise that all body movement belongs to an upper-body-only expression mask.

Normal gestures are masked **override** clips by default. Only assets authored and verified as offsets use additive mixing. A shoulder shimmy built against a standing reference is not automatically valid while lying down. Hand contacts, collision clearance and pose-specific silhouettes still require content work.

### Clip and rig contract

Use one approved humanoid rig for the proof, with a face that exposes at least jaw/mouth opening, eyelids and expression controls. The asset gate records rig import settings, scale, forward axis, local origin, reference pose and required bone/blendshape bindings. Do not defer rig choice until the rendering ticket.

Unity technical bindings include clip, mask, actual curve coverage, duration/sample rate, additive reference if relevant, loop flag, blend bounds, gaze suppression and prop attachment. The semantic cue declares its requirements; binding validation catches mismatches. Handwritten metadata alone cannot prove an imported mask works.

One Unity gaze component evaluates after body animation, using that frame's animated local rotations as its baseline; never accumulate yesterday's correction. Clamp yaw/pitch, smooth target changes and verify eyes/head while nodding. No new IK package is required for the fixed, calibrated room. If reliable contact requires one, demonstrate the failure before proposing a dependency change.

## 9. Six locations, poses and transitions without an explosion

Adopt these initial display names: Room center, Door, Window, Chair, Side of bed, Foot of bed. IDs stay stable if names change. These are initial content choices, not hardcoded engine enum values.

Initial legal character states:

| Location | Supported poses |
|---|---|
| Room center / Door / Window / Foot of bed | Standing |
| Chair | Standing, Sitting |
| Side of bed | Standing, Sitting, LyingDown |

Thus there are nine legal stage states, not every product of location and pose. Author standing travel connections through Room center, with directed paths each way to the other five locations. Add Chair standing↔sitting and Side of bed standing↔sitting↔lying transitions. This supplies 16 directed edges, using shared locomotion and pose clips where appropriate; it does not require 16 unique clips.

Core finds the least authored-cost route with stable-ID tie breaks. A move from chair sitting to bed lying therefore exits the chair, travels via standing anchors, sits on the bed, then lies down. Missing routes are validation/runtime errors. Never teleport as a normal success path. There is no inferred reverse transition: reversibility must be authored and visually approved.

Unity stores spatial waypoint paths and final facing for travel edges. One stage mover owns the world root, with in-place walking and turn blends matched to route speed. No scene physics moves that root concurrently. Keep routes clear in the fixed room; dynamic obstacles and general navigation are deferred.

Pose transitions execute around a calibrated station origin with authored local body movement. Keep travel/world-root motion separate from pelvis displacement. A chair/bed binding must align the transition's stand and contact endpoints with its base poses. For the proof use one rig and fixed furniture dimensions; adjustable furniture, arbitrary retargeting and procedural contact solving are deferred.

Each movement request has an ID, progress/arrival status and a timeout from validated edge metadata. Arrival means within 2 cm and 3 degrees of the bound root endpoint, with the target pose blended in. Check visual hand/seat/foot contact separately; a root within tolerance does not prove contact quality.

Transitions suppress incompatible expressive body layers. Face/blink may continue where permitted. Do not deliver the next line until the requested place and pose are ready. The initial milestone does not need walk-and-talk choreography.

## 10. Procedural motion: one sampler, two drivers

A motion clip defines a useful one-dimensional pose path. It must be deliberately authored so scrubbing it both directions produces the intended motion. Reversing an arbitrary action such as a grasp or footstep is not equivalent to creating a usable procedural motion.

Store interval endpoints as normalized clip positions `uMin/uMax`. WPF can display and edit frames using Unity manifest sample rate and clip duration. Frame labels are zero-based relative to the imported clip, not an assumed DCC timeline start; show the equivalent seconds. Validate against the actual clip on reimport.

```text
driver value p(t) in [0,1]
u(t) = uMin + p(t) * (uMax - uMin)
clip sample time = u(t) * clip duration in seconds
```

Unity keeps the clip's automatic time advancement disabled and sets sample time before graph evaluation. This is supported by the built-in [Playable timing API](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Playables.PlayableExtensions.SetTime.html) and [manual-time example](https://docs.unity3d.com/6000.0/Documentation/Manual/Playables-Examples.html). Do not reverse the whole Animator or use animation events to drive gameplay from a repeatedly scrubbed interval.

### Random oscillator

Make rate units unambiguous. The first editor uses **source frames per second**, with a read-only traversal duration and cycles/minute display. For frames 25–85, the one-way distance is 60 frames; 10–30 source frames/second means one-way duration 6–2 seconds. This is different from 10–30 complete cycles/minute.

At each endpoint, draw the next half-cycle's rate from its seeded stream. Compute the segment duration from interval distance/rate; retain that rate throughout the segment. Use smoothstep progress for eased turnarounds. Thus the displayed rate is average source-frame traversal rate, not instantaneous speed. Runtime stores normalized distance per second, so changing frame-display units cannot change playback.

Clock catch-up processes all elapsed endpoints in order with a bounded pathological-event guard. Pause preserves segment time; resume continues it. Do not reroll rates every frame.

### Recorded script driver

Import single-axis `.funscript` JSON into portable `MotionCurve` samples: `at` in milliseconds, `pos` in 0–100. This is a narrow importer, not a device integration. Source implementations demonstrate the timed-position representation and duration/position interpretation; see [funscript-utils source](https://github.com/defucilis/funscript-utils/blob/main/src/funMapper.ts).

Define the accepted subset explicitly: version absent or `1.0`; at least two points; nonnegative, strictly increasing integer timestamps; integer positions within 0–100. Apply `inverted` once on import when true. Treat `range` as source metadata, not an extra visual scaling factor; the recipe's interval determines travel. Preserve these decisions in import diagnostics. Reject duplicate timestamps, invalid versions and out-of-range data with line/point context; do not silently sort, clamp or repair it.

Sample with linear interpolation. Before the first timestamp hold its position; after the last, hold the last until finite completion. Default duration is the final timestamp; explicit shorter duration truncates sampling and blends out. A longer duration requires the explicit hold-end policy. Looping scripts and multi-axis files are deferred. Do not silently ease recorded points and change their timing.

Script samples live in SQLite and the exported snapshot so Unity and WPF use the same immutable curve, independent of the original file's availability. Import metadata/unknown non-motion fields can be retained as provenance but have no runtime effect.

### Entry, exit and concurrent motion

Fade into the initial sampled pose over a recipe-defined duration, then start the driver clock. Driver duration excludes entry and exit blends; action completion includes them. Fade back to the current compatible foundation at completion/cancel. No world-root motion is extracted from scrubbing. Pelvis translation inside the actor is allowed if explicitly claimed and calibrated.

One recipe may drive both an arm and its attached prop from the same scalar. A single recipe owning several regions is one coordinated motion, not several unrelated phase clocks. No hardware output, video sync, arbitrary multitrack choreography or physics simulation is included.

## 11. Dialogue, facial motion and the player

Replace the text-only host call with a structured request carrying request/line identity, resolved text, optional voice resource, text-only delivery duration and selected performance cues. Preserve the existing tagged-dialogue RNG and selection rules. Add presentation fields to both direct dialogue and catalog snippets; do not force writers to make every direct line a snippet.

Text-only dialogue uses an explicit duration when set, otherwise a documented estimate `max(1.5 seconds, wordCount / 2.5)` in Core. This duration controls delivery/gesture lifetime, not a player acknowledgement. Keep subtitles visible until replaced or session end. `WaitForContinue` remains the explicit player-paced action.

For voiced lines, completion follows actual audio completion. Use a precomputed amplitude envelope for basic jaw/mouth movement in the initial proof. Label it approximate audio-driven mouth animation, not phoneme-accurate lipsync. Include real short voice clips in the acceptance scene so this path is exercised. A future viseme track can replace that one input without changing authored performance actions. No cloud TTS or new lipsync package is needed.

One facial composer writes expression, mouth and blink outputs. Expression presets in this slice avoid the speech-controlled jaw/mouth channels; smiles can use approved separate corner shapes. Clamp final blendshape weights and inspect combinations. If the rig cannot separate these channels, resolve that at the early rig gate instead of accepting broken expressions as normal.

Blink scheduling and breathing are lightweight Unity presentation details driven by the shared pause state and their own cosmetic seeds. They do not consume content selection RNG. WPF can report them as simulated; it cannot certify a face looks lifelike.

Player posture and body visibility are independent fields, not eight unrelated states. Bind four static player poses/camera anchors and toggle the body renderer independently. For now change posture under a short fade, not bespoke transitions for every pair. Update the character's named PlayerFace gaze target after the camera/posture change. A simple visible player body is sufficient, but it must demonstrate all four postures without the camera looking through its head.

Player posture is presentation state. It does not change consent, equipment or gameplay stats implicitly. Cues that physically require a posture declare it as a typed requirement. Validate the upcoming performance before committing an incompatible player change; error with the blocking cue/recipe rather than rendering a bad pose.

## 12. The WPF authoring experience is part of the deliverable

Add a **Performances** library area with profiles, cues, motion recipes and the stage overview. Keep ordinary writing in the existing card/action editor. Use dedicated controls and small view models; do not expand the main-window partials into another monolithic editor or use reflection to generate property grids.

### Writer path

1. Open a card and write several Dialogue rows using keyboard-friendly add-next-line behavior.
2. Choose a performance profile at the beginning of a section; select mood and movement intent in that row.
3. Leave per-line acting on Auto. Open the advanced line controls only for a particular emphasis.
4. Preview with a seed; read the selected cues and reasons. Send the same selection to Unity for visual audition.
5. Publish a fresh runtime snapshot and replay the scene. See content revision/binding revision in both apps.

Dialogue edits, row insertion and performance pickers must retain selection/focus and dirty buffers. Do not rebuild the entire open card on each catalog change. Reuse the current semantic undo and deep-clone rules, including nested PromptChoice sequences and Action Blocks.

### Performance profile editor

Give authors a compact palette editor: allowed places/poses; three mood tabs; body/face queries; rest probability; gaze choices; timing ranges. Show eligible counts and example names immediately. Provide editable defaults in the profile, with explicit advanced overrides in actions. Do not build profile inheritance in v1.

Profiles are shared by reference. Show where-used counts and a usage list before broad edits. Duplicate creates a new profile ID and copies its selection policy while continuing to reference the same cues. A contextual Make Unique rebinds only the selected action. New gestures matching a shared query become available to all its consumers; a profile that needs frozen membership can use explicit cue IDs instead.

The matrix is calculated from the same Core eligibility code used at runtime:

- Rows: valid location/pose states. Columns: mood and selected channel.
- Cells: eligible count, required-coverage error or optional-none indicator.
- Click: list included/excluded candidates with typed rejection reasons and resource binding status.
- A selected motion/gesture also shows which other channel candidates conflict with its region claims.
- Travel view shows reachability from the selected state; it is not another general graph authoring canvas.
- Edit rules on the relevant cue/profile; do not create a persisted table containing every Cartesian combination.

### Motion recipe editor

Show clip name, scrub range in frames/seconds/normalized units, duration, occupied regions, compatible poses, rate units, and a small scalar-time plot. Import a script with validation and inspect points without opening raw JSON. Preview at a specific time or play the recipe. Bulk stage/mood/tag assignment to selected cues is one undoable command.

### Preview that can actually assess authoring

WPF offers a simulated live inspector showing requested/committed location, pose, mood, selected cues, claimed regions, motion scalar and compatibility explanations. Its Reference Player uses the same director and timing contracts, including finite simulated transition/speech durations. A named fast-test mode may use a manual clock; it must not masquerade as real visual preview.

Add a small filesystem audition handoff, not embedded Unity or a network service. WPF writes a versioned preview request with monotonic request ID, snapshot hash, seed, source/target state, profile and optional line/motion. Unity in the audition scene loads it only on explicit Run/Replay. It returns validation status and selected trace to a response file. Clear stale success when issuing a new request; display results only when IDs/hashes match. File writes are atomic. Preview files are generated, outside the canonical content tables.

This lets the author keep both apps open, alter a profile and audition it without editing an Animator. Live hot replacement of assets/content during an active game session is deferred; Reload/Restart establishes a fresh immutable snapshot.

### Measurable authoring acceptance

After the vocabulary is configured, the user must be able to:

- Author six lines, two moods and a required location change in WPF without opening Unity asset configuration.
- Add one gesture's metadata/binding once, then see it eligible in two existing conversations without editing those cards.
- Remove a sitting permission and immediately identify every affected required profile cell and referencing action.
- Duplicate a profile, change it locally and undo/redo without altering the source or losing IDs.
- Import a short script, adjust its range/rate, preview it, save, close/reopen, and reproduce the saved configuration.
- Export and replay edited content in Unity without hand-copying IDs, editing JSON or making a Timeline.

Do these with the user operating the GUI. A test that inserts the same rows directly into SQLite is valuable but does not satisfy authoring acceptance.

## 13. Current content into a real Unity player

Keep the production SQLite loader and portable reference validator. Add an explicit runtime DTO mapping/export step after loading. Include all currently supported content/action subtypes in the transport; unsupported mappings fail export with the source ID. Never silently omit actions outside the demo.

Use flat, serializable field-based DTO records with explicit discriminators and reference arrays, then reconstruct the existing definitions. WPF can serialize with framework `System.Text.Json`; Unity can deserialize with built-in `JsonUtility`. Do not serialize the polymorphic `GameContentDefinition` object graph directly. Unity's JSON serializer has field/type restrictions and does not support dictionaries as an ordinary serialization feature; the transport must be shaped accordingly. See [Unity JSON serialization](https://docs.unity3d.com/6000.0/Documentation/Manual/json-serialization.html).

Export from one consistent DB read transaction. Envelope includes transport version, schema version, deterministic payload hash and required semantic resource IDs. Write to a temporary file, validate/read it back and atomically replace the previous artifact. Export failure keeps the old artifact but explicitly shows it as stale; Unity launch/preview must display the loaded revision.

Use a generated JSON TextAsset under `Assets/GeneratedContent` for Editor import and player inclusion. The build step requires a fresh validated artifact and includes it in the player. WPF settings choose the project's export target once; no developer paths are hardcoded into content. Unity bindings use serialized asset references keyed by semantic IDs, not runtime `AssetDatabase` lookups. AssetDatabase may assist Editor validation/export only.

Create a Unity binding registry asset plus stage bindings for clips, masks, facial presets, voice clips, props, gaze targets and transitions. Export its technical manifest for WPF. Validate resource kind, duplicates, missing bindings, actual clip bounds, region signature and rig compatibility. Hash/revision mismatches block visual audition/build until manifests and content agree.

Replace the legacy SO launcher path for the new player with session selection from the imported snapshot and the existing Core eligibility rules. Keep the legacy bridge only for old fixtures until retired deliberately. Do not add a third sample-only engine.

For this slice use a labeled in-memory test profile chosen at launch, via existing portable spawn/profile contracts; do not silently pretend to load the persisted WPF profile. The sample has no device requirements. Persistent cross-host user-profile storage is a separate concern. If a reachable action requires an unavailable host capability (including hardware toy actions), preflight blocks the session with a precise error. Supporting its DTO does not claim Unity implements its hardware service.

A standalone build must contain content/assets and work with WPF closed. Menu: choose the acceptance session, start, choose/continue, return/replay. Disable duplicate advance clicks while a graph advance is running. Surface runtime failure in the game UI, not only the Unity console.

## 14. Ordered implementation work packages for Sol

Split these into appropriately sized tickets, preserving dependencies and gates. A ticket must name files, database impact, visual/content work, failure behavior and verification. Do not estimate completion based only on how many classes compile.

Existing extension points, relative to the repository root:

| Area | Starting files/directories |
|---|---|
| Definitions and action types | `Assets/Scripts/Portable/Game.Content/GameContentDefinition.cs`, `ActionInstances.cs`, `ActionTypeKeys.cs`, `DialogCatalogDefinitions.cs`, `ResourceKinds.cs` |
| Runtime and validation | `Assets/Scripts/Portable/Game.Core/ActionExecutor.cs`, `ActionTypeRegistry.cs`, `CoreServices.cs`, `IDialogService.cs`, `GameSessionEngine.cs`, `ContentCatalog.cs`, `ContentReferenceValidator.cs`, `BackgroundActionTracker.cs` |
| Persistence | `DotNet/Game.Content.Sqlite/CoreMigrations.cs`, `GameContentSnapshotLoader.cs`, `AuthoringCommands.cs`, `AuthoringUndo.cs`, `ActionSequenceWriter.cs`, new typed repositories/migrations |
| WPF authoring | `DotNet/Game.ReferenceHost.Wpf/ActionEditorRegistry.cs`, `ActionInstanceCloneUtility.cs`, `SequenceSnapshotUtility.cs`, new dedicated performance controls; existing library/shell wiring |
| WPF reference play | `DotNet/Game.ReferenceHost.Wpf/ReferencePlayerWindow.xaml.cs`, `DialogHostService.cs`, `MainWindow.Preview.cs` |
| Unity | `Assets/Scripts/Game/GameManager.cs`, `UnityHostAdapters.cs`, session-selection/UI components, new stage/actor bindings and renderer; do not extend the SO bridge as the permanent content path |
| Tests | Existing Core/SQLite/WPF test projects plus `Assets/Tests/EditMode` and `Assets/Tests/PlayMode` |

Verify exact helper filenames before writing the tickets; the table identifies responsibility, not an exhaustive edit list. New DTOs must compile in the same portable/Unity arrangement as existing shared definitions. Keep host serialization adapters outside Core.

### A. Baseline and real asset proof — first, before broad tooling

**Deliver:** inspect current DB/migrations and builds; list required services; choose the actual test rig; obtain or create the minimum animation/audio assets; build a small Unity proof in the pinned editor. The proof combines a standing and sitting base, one gesture, a face, gaze, blink, breath, one voice clip and one scrubbed motion including pelvis movement. Demonstrate an actual stand-to-sit transition.

**Gate:** human visual review on the real rig establishes which masks/channels combine, clip origin conventions and useful scrub intervals. Save the rig contract and reusable binding assets. Primitive/capsule motion or log output does not close this gate. Missing character/animation assets must be called out as an asset-production dependency with a concrete owner and list.

**Scope:** retain useful renderer proof code but do not build an entire WPF matrix against untested rig assumptions. Do not acquire paid assets or add packages without the repository's required approval.

### B. Portable content, director and action semantics

Depends on A's rig/region findings.

**Deliver:** definitions, profile/cue predicates, stage routing, seeded selectors, request/committed state, finite-motion arbitration, clock/events and structured trace. Add the three action types; extend dialogue requests and session setup. Correct blocking-versus-transfer classification. Connect shutdown and failure propagation. Use a deterministic fake host to exercise the full sequence now.

**Gate:** ordinary blocking actions do not route through `ReduceFlow`; same event stream/seed produces same decisions; changing performance RNG never changes card/dialog draws; impossible routes and region conflicts explain themselves; persistent ambience never enters `WaitForAll`; failures and cancellation cannot commit arrival or hang a run.

### C. SQLite authoring and portable persistence

Depends on B's model.

**Deliver:** the next migrations, typed repositories/commands, snapshot loader and reference validation. Persist profile/cue/state/transition/recipe data, dialogue presentation fields and imported curve points. Extend action writer, clone/duplicate paths, Action Blocks and undo. Add where-used/delete protection for shared resources.

**Gate:** close/reopen round trip including nested choices and template insertion; profile/cue edits undo atomically; direct and tagged dialogue retain presentation fields; invalid references fail at load/export. Use disposable DB fixtures. Follow canonical DB backup/checkpoint/integrity rules before any migration of authored content.

### D. WPF basic writing and simulated play

Depends on C.

**Deliver:** performance/session/player action controls and dialogue overrides in the existing sequence editor; basic profile/cue editors and live simulated director inspector. Make the fake host a real WPF reference service with explicit timing and pause/stop.

**Gate:** author the first conversation through the GUI, run the actual Core engine, exercise both mood branches and a movement request; restart without leftover motion/tasks. This establishes the first complete nonvisual path before further editor polish.

### E. Runtime export and current Unity bootstrap

Depends on C and B. Can precede advanced WPF tools.

**Deliver:** complete explicit DTO transport, transactional export, round-trip validator, Unity runtime loader/binding registry and snapshot-based session launcher. Include nested action coverage and build freshness checks. Replace demo SO selection for this path.

**Gate:** the same exported conversation produces the same semantic action/selection trace in WPF and Unity under controlled clock events. Missing bindings are caught before starting. Load in a development player, not only Editor, to expose serialization or Editor-only references early.

### F. Production movement and layered actor renderer

Depends on A, B and E.

**Deliver:** integrate the reusable proof renderer with semantic requests; all six station bindings, nine legal states, 16 directed transition edges, arrival validation and timeouts. Implement region suppression, face composition, gaze, breath and blink. Calibrate chair/bed contacts.

**Gate:** a real character travels through all locations, sits and lies down and returns via valid paths. Run dialogue gestures in both standing and sitting, demonstrate suppressed head aim during a nod, and verify no foundation reset after a gesture. No teleport success, no unrelated controllers writing the same body.

### G. Dialogue and player presentation

Depends on E/F and B's dialogue contracts.

**Deliver:** subtitles, FIFO speech, actual voice completion, approximate audio mouth motion, line-local cues/mood restoration, text-only timing, choice/Continue UI, four player posture/camera/body configurations and fade changes. Test application pause and cleanup.

**Gate:** dialogue starts after arrival, nonblocking lines queue without overlap, choice/Continue leaves ambience alive, player visibility can change in each posture, head aim follows the new PlayerFace target, and replay has no retained audio or pose overrides.

### H. Procedural motion and importer

Depends on B/C/F. Implement the portable sampler first, then bind it to the already-proven renderer.

**Deliver:** oscillator, single-axis script import/curve persistence, calibrated clip intervals, finite blocking/nonblocking execution, entry/exit blends and region ownership. Support the arm/prop demonstration and the pelvis-involving seated body demonstration.

**Gate:** check exact sampled values at controlled times, rate units, reverse boundaries, pause/resume and end behavior. Visually demonstrate each motion concurrently with speech/blink and compatible gaze. Verify motion releases its claims before relocation and `WaitForAll` finishes. Do not conflate this with existing toy hardware actions.

### I. Computed matrix and fast visual audition

Depends on D and functioning F/G/H.

**Deliver:** compatibility/reachability/region-conflict views, readable exclusion reasons, bulk metadata editing, usage links/Make Unique, motion plot/scrub controls, manifest import and filesystem Unity audition requests/results. All changes persist through existing undo infrastructure.

**Gate:** complete every authoring acceptance task in section 12. Verify stale manifest/preview-result detection. Adding a cue changes eligible profile pools without touching consumers. The matrix never becomes a second rules implementation.

### J. Playable content, regression and handoff

Depends on all above. Populate and refine acceptance content throughout earlier packages; this ticket completes integration, it does not begin asset production.

**Deliver:** a hand-authored 3–5 minute session with mood branches, repeated conversational lines, movement, all requested presentation mechanisms, a route/pose tour and procedural demonstrations. Produce a Windows development build, run instructions, authoring guide and limitations/evidence report; update README and the relevant stale Unity-plan documentation.

**Gate:** the user plays the entire session and authors a small new conversation through WPF. Run the tests below, record visual review findings, repair material animation/authoring failures, and leave the appropriate apps running. A backend-only implementation is not completion of this stack.

## 15. Asset/content budget and acceptance sequence

Asset work is likely to dominate the first visual milestone. Budget it explicitly:

| Asset/content | Minimum deliverable |
|---|---|
| Character | One working humanoid with facial controls and one documented rig contract. |
| Room | One fixed room with six named stations and unambiguous paths; simple furniture is acceptable. |
| Foundation | Standing idle, chair sitting idle, bed sitting idle, bed lying idle; walk and turn blends. |
| Pose transitions | Chair stand/sit both directions; bed stand/sit both directions; bed sit/lie both directions. Reverse playback counts only if it looks correct. |
| Body vocabulary | At least six short gestures across Happy/Neutral/Mad, including a head-owning nod, point, shimmy and Mad-only hands-on-hips. At least two eligible body gestures per mood in the main standing/sitting conversation states; pose variants may be required. |
| Face vocabulary | At least two distinct presets per mood; verify them with speech and blinking. |
| Procedural vocabulary | One arm/prop interval and one seated body interval that moves the pelvis. One short valid recorded script and one oscillator recipe. |
| Player | Four visible static posture variants with camera anchors and independent visibility. |
| Dialogue | At least twelve lines, including three voiced lines and text-only lines, two mood branches and a clear position-change beat. |

Gestures need not cover every state: a lying state may intentionally have no body gesture pool. Its base/face/gaze policy still requires declared coverage. Do not label nonexistent variation as complete just to fill green cells.

Acceptance play path:

1. Launch the built player with WPF closed; select the session and see the character at the configured initial state.
2. Walk to Side of bed, sit, deliver Happy dialogue with visible face variation and at least one gesture while gaze/blink/breath continue.
3. Wait on Continue long enough to see restrained ambience without a flood of gestures.
4. Choose a mood branch; request DifferentLocation; observe a real transition/travel/settle before the next line.
5. Visit Chair, run a nonblocking arm/prop motion with a spoken line, and finish through WaitForAll.
6. At Side of bed, run the pelvis-involving motion and the recorded-script driver. Then settle, lie down, get up and move again.
7. Exercise the remaining stations in a short directed tour; demonstrate every supported player posture and both visibility values through an optional in-session test choice/menu. Do not require restarting the Editor to change these states.
8. Finish and replay with another seed. Story/selection rules stay valid; incidental acting visibly changes where pools permit.
9. Back in WPF, write a small additional conversation and audition/export it without creating Unity choreography.

## 16. Verification that proves the intended system

### Automated tests worth writing

- Pure Core candidate and route tests: forbidden mood/pose, required player/prop, missing route, different-location guarantee, stable tie order, anti-repeat singleton behavior and explicit optional-none behavior.
- Core lifecycle tests: requested versus committed state, stale completion IDs, timeout/failure, nested choices/GOTO/RETURN, finite motion conflicts, speech FIFO and line-local override restoration.
- Clock/sampler tests: seeded oscillator boundaries across unequal frame steps; script interpolation/inversion/hold/end; invalid input; entry/exit clock semantics; pause and catch-up. Use a common sample grid for WPF/Unity parity.
- Action tests: blocking presentation versus flow actions, tracker/barrier behavior, background faults reaching visible session failure, and cleanup after error/cancel with another cleanup service failing.
- SQLite/transport tests: every action discriminator including nested/template sequences, stable IDs, revision hashes, transactional export, reimport/reopen, semantic undo/redo and referenced-definition deletion guards.
- WPF tests: picker values survive refresh, shared edits preserve dirty card buffers, matrix uses the Core result, bulk undo and stale audition responses cannot show success for a new request.
- Unity EditMode tests: missing/duplicate/wrong-kind bindings, invalid clip interval, required facial channels, manifest region signatures and runtime DTO reconstruction.
- Unity PlayMode tests: arrival and pose readiness, movement cancel, speech completion, layer suppression/release, exact-time motion samples, teardown/replay and no advancing twice from repeated clicks.

Run the existing relevant .NET suite and Unity suites in the pinned Editor; also run a built-player smoke test. Compare normalized semantic traces only under controlled clocks and host completions, excluding frame timestamps, audio duration measurements and cosmetic blink differences. Do not compare animation pixels to the WPF simulator.

### Human visual and usability checks

Review feet sliding, chair/bed penetration, expression changes while speaking, head/gaze fights, pops at clip boundaries, robotic cadence, excessive gesturing, camera/body clipping and visible range extremes. Watch the scripted and oscillator motion at slow/fast limits, including reverse and release. A legal mask combination that looks bad is a content defect to fix, not a passing performance.

Record pass/fail evidence and limitations separately for engine, WPF authoring, Unity rendering and built player. No count of passing unit tests replaces these visual and authoring gates.

## 17. What deliberately waits

No generalized performance graph, profile inheritance, arbitrary rule expressions, mood AI, semantic text analysis, motion matching, general navigation, multiple independently directed characters, arbitrary rig/furniture retargeting, full-body contact solver, procedural player transitions, cinematic camera director, mid-motion save/load, live asset hot reload, multi-axis script synchronization, video/hardware integration, phoneme generation, plugin marketplace or pack downloader.

Monthly content production does require stable IDs, reusable profiles/cues, where-used visibility, explicit snapshot/binding revisions and complete export now. It does not yet require a pack dependency resolver or Patreon delivery system. Begin with one content database and one asset registry. Later distribution can package immutable snapshots/assets without changing how a writer directs a line.

## 18. Handoff instructions for ticket generation

Treat this document as the intended design, with sections 3, 6–10 and 12 as the contracts. Tickets may split work packages but must preserve their behavior and the final playable/authoring gates. Name concrete acceptance artifacts and asset-production work, not just classes or UI placeholders.

Maintain the existing graph selection, consent, progress and explicit Continue semantics. Do not reinterpret a presentation feature as a rewrite of gameplay. Preserve the new performance domain's separate RNG and lifetime. Keep all compatibility decisions in Core, all spatial/rig decisions in Unity, and all ordinary content authoring in WPF.

The repository allows an approved execution packet to batch coherent work and override its interactive file-count/per-step-go defaults. If the user authorizes the generated stack for execution, state that scope explicitly in the packet. This planning request itself does not authorize implementing the stack or purchasing dependencies/assets.

The final ticket must say, in substance: **the user can write a new conversation conveniently in WPF and then play it in Unity with the real character performing layered, varied animation.** Neither an animation sandbox disconnected from authored content nor a sophisticated authoring matrix attached to a logging-only host satisfies the goal.
