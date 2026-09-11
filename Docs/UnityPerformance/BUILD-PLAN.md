# Procedural performance build plan

Revision: September 11, 2026. This replaces the previous performance-authoring proposal. These are implementation requirements, not claims of completed features. The [architecture contract](../../Tickets/UnityPerformanceExecutionStack/ARCHITECTURE-CONTRACT.md) resolves detailed behavior; the [ticket stack](../../Tickets/UnityPerformanceExecutionStack/TICKET-STACK.md) supplies execution order.

## Product and authoring goal

The game draws a Card and executes its actions. A procedural event changes the presentation context; dialogue actions select lines; Core combines compatible animation ingredients. Repeated executions can look different without anyone authoring another animation sequence.

    Perform: Conversation variety, Mad, Different location
    Dialogue from tags: challenge
    Refresh expressions
    Dialogue from tags: instruction
    Wait for Continue

An event can refresh expressions automatically at each dialogue start, removing even the explicit refresh row. Most new content should mean writing cards and dialogue using existing events. A recognizable behavior is a semantic query in a reusable event, not a specific clip assigned to a line.

The end of this stack must deliver a playable Unity session using actual card selection, movement, layered expression, dialogue, Continue/choice and procedural motion. It must also make adding more content pleasant. A fixed demonstration sequence alone does not pass.

## Three responsibilities, no duplicate authoring

| Surface | What someone authors |
|---|---|
| Unity | Animation ingredients, masks, facial presets, stage anchors/routes, motion windows and compatibility metadata |
| WPF | Cards/dialogue and procedural Performance Event rules: moods, allowed positions, tag filters, refresh policy and scalar drivers |
| Core | Nothing through a separate editor: it evaluates those inputs and owns runtime selection/state |

An artist marks a gesture as suitable for sitting and Mad beside the actual animation in Unity. WPF reads that fact from an automatically generated catalog. The writer chooses rules such as Mad-compatible body expressions. Do not register the same animation again in SQLite.

Use stable Unity-generated semantic IDs and searchable names/tags. A disabled ingredient can remain unfinished. Enabling a broken required ingredient is a visible error. New enabled compatible ingredients automatically join existing queries.

The WPF matrix is a computed view of event coverage across legal states and moods, with candidate counts and reasons for exclusion. It edits event filters, never asset metadata. It does not enumerate every possible combination as authored rows.

## What a Performance Event contains

A reusable rule definition with two flavors:

- Conversation: destination and mood policies, body/face queries, gaze/cadence and expression-refresh triggers. These rules persist across cards until changed.
- Driven motion: common context policies plus motion-family query, oscillator or imported scalar curve, scalar range, finite duration and blends. Existing conversation rules continue by default on compatible regions.

Actions are Perform, Refresh expressions and Set Player State. Perform references an event with optional mood/destination/duration overrides. It always waits for travel and pose readiness. A nonblocking driven event backgrounds only its finite driver completion. Refresh expressions samples under current rules without movement. Player posture and body visibility are independent.

Keep the existing Direct Dialogue and Dialogue From Tags actions and selection behavior. Dialogue carries text, identity, optional voice and timing, with no authored acting track. Session setup supplies initial presentation state and the default event once; cards inherit live state.

Do not create a new graph per card, a cue editor, per-line gesture assignments, takes, audition/pin controls or persisted random combinations.

## Runtime composition

Core first finds viable destination states and joint layer combinations. Pose, location, mood, props, player posture, region claims and directed route reachability constrain choices before sampling. Empty required coverage is an error; an explicitly permitted resting body is valid. A single candidate is usable but cannot provide variety.

Use independent seeded random domains for presentation and existing gameplay selection. Avoid immediate presentation repeats when alternatives exist. Automatic refresh follows semantic boundaries or scheduled dwell intervals, never frame-by-frame random rolls.

Unity uses one fixed animation graph per actor. Standing, sitting and lying are alternative foundations. Body expression, finite driven motion, compatible additive breath, gaze and a single facial composer provide the remaining channels. Masks and declared ownership resolve overlap. A driver can own pelvis/legs and a prop together; it is not restricted to the upper body.

Core owns requested versus committed state and correlated requests. Unity owns actual coordinates, clip evaluation and arrival acknowledgement. Persistent ambience is host-ticked state, excluded from WaitForAll. Finite motion is tracked work. Speech uses one ordered channel. Pause, cancellation, fatal faults and teardown have explicit semantics in the contract.

## Fixed room and asset budget

Use six named locations: Room center, Door, Window, Chair, Side of bed, Foot of bed. Standing at all six, sitting at Chair/Side of bed, and lying at Side of bed give nine initial legal states.

Author directed standing routes through Room center and stand/sit/lie transitions: sixteen directed edges, not sixteen unique clips. Reuse travel assets. Core chooses routes; Unity handles anchor calibration and movement. No general navigation or teleport fallback.

Prove the actual rig early. Minimum content for the final slice:

- A real usable character, standing/sitting/lying foundations, travel and pose transitions.
- Enough compatible body and facial alternatives to show different executions of at least one event, including Happy, Neutral and Mad.
- Gaze, blink, speech mouth and compatible breath.
- A visible prop motion and a motion involving pelvis/body, using a reversible path.
- Optional short voice audio, plus explicit text-only delivery timing.
- Four player posture representations with independent visibility.

Asset absence is a concrete dependency, not justification to claim a text log or empty mannequin is the visual milestone. Continue independent software work while the dependency is resolved; report what remains unverified.

## Scalar-driven motion

The artist marks a usable reversible clip window in Unity, for example frames 25–85. The event selects a semantic motion family and supplies a normalized driver.

The writer-facing rate is seconds per one-way traversal, randomized within a range at endpoints. Known clip-window details may show equivalent source frames/second. There is only one rate authority. Use smooth endpoint easing for oscillation and absolute time so host frame rate does not change the result.

Import the small, validated single-axis funscript subset defined in the contract into SQLite. Use timestamped linear interpolation and finite end behavior. Preserve curve identity when replacing its source; validate affected events. No hardware/device integration is part of this stack.

Unity samples clip time manually, maps scalar range into the marked window and blends entry/exit. Scrubbed animation events do not execute gameplay.

## Everyday workflows

### Write more cards

Open the existing Card editor, choose an existing event when needed, and write or select dialogue. Save normally. Existing transactional editing, undo and Action Blocks remain useful. No separate shared-draft framework.

WPF automatically publishes a development snapshot after valid committed edits. Unity keeps the scene loaded. Choose a Card and use Play or Repeat; each run loads the latest committed snapshot and generates a fresh performance. There is no manual export/import dialog for each edit and no WPF command queue directing Unity one line at a time.

WPF's Reference Player tests the same logic with simulated presentation. Unity tests the appearance. Authors do not need to bounce between applications to arrange every gesture because they never arrange every gesture.

### Tune variety

Edit one reusable event's mood, filters or cadence in WPF. Read the computed coverage matrix and diagnostic candidate reasons. Save, then repeat a representative card in Unity. This changes rules shared by cards, not a chosen execution.

### Add an animation

Import and bind it in Unity, set its factual compatibility/tags, inspect the clip and relevant layers on the rig, then enable it. Catalog generation refreshes WPF automatically without losing unsaved card edits. Existing matching events and cards now include it. No second asset registration.

## Integration with the existing project

Physical portable source belongs under Assets/Scripts/Portable and is linked by the DotNet projects. Keep Core independent of Unity, WPF and database/serializer details.

- Extend Core action models, execution, host contracts, snapshot validation and the session-scoped director.
- Separate ActionExecutor blocking classification from graph-control transfer reduction. Preserve ordinary graph semantics.
- Extend SQLite typed mappings/migrations for event rules, curves and new action fields. Do not create a second editable ingredient library.
- Extend WPF Card clone/equality/Save/Revert, action pickers, event editor and Reference Player.
- Add conservative scoped export before global validation can block unrelated incomplete content.
- Bootstrap Unity from the actual exported game content, not only legacy ScriptableObject sample decks.
- Keep Unity assets, masks, graph, movement, face composition and developer card playback in the Unity host.

Use explicit versioned portable field DTOs, reconstructed and validated by Core. Include all current actions and reachable choices/branches. Development JSON stays outside Assets to avoid per-edit reimport. A standalone build packages a validated snapshot and registry.

Do not replace the graph editor, selector, consent/capability checks, progress accounting, explicit Continue or existing tagged dialogue logic.

## Delivery and acceptance

Follow tickets 00–10. Prove ingredient feasibility first, expose a real card test in Unity early, then complete composition and driven motion. Workflow checks are requirements throughout, not a polish phase after architecture is fixed.

Final demonstration:

1. Start the real session and draw an eligible card.
2. Move to a compatible location, settle, adopt layered expression and deliver selected dialogue.
3. Refresh expressions, deliver another line and move on a later event.
4. Exercise Continue/choice and another card with inherited state.
5. Demonstrate oscillator and imported curve, including a pelvis-owning path and compatible ongoing dialogue.
6. Repeat a card enough times to observe alternatives and verify every result is valid.
7. Save a dialogue/rule change in WPF and play it in the loaded Unity scene without manual transport.
8. Add a compatible Unity ingredient and observe existing cards gain it without edits.
9. Run a standalone Windows build using packaged content.

Provide the runnable scene/build, useful seed/error diagnostics, meaningful tests, short authoring instructions and honest evidence of visual acceptance. Do not build a performance recording system, generic choreography platform or monthly pack distribution pipeline.
