# Ticket Stack — Unity Performance Playable Slice

Read every ticket before starting Ticket 00. Execute in order. Each ticket repeats its local context so it can be handed to a fresh worker, but the architecture contract and build plan remain binding.

## Ticket 00 — Baseline, Protection, and Focused Implementation Map

### Goal

Create a reproducible execution base, protect authored content, inventory the real presentation assets and map each contract to current code before behavior changes.

### Context

The repository currently reports schema v11 and 384 passing .NET tests, but these are documentation claims to verify. Unity still uses the legacy SO/deck bridge. The working directory may contain untracked database backups that must remain untouched. Ticket 01 cannot close without a real rig and animation/audio inputs.

### Contract

1. Confirm the exact user-approved packet commit, create the implementation branch, and record Git/SDK/Unity/package state.
2. Run the full .NET baseline, available Unity EditMode/PlayMode baseline, WPF build/start smoke, and current Unity batch compile. Report observed totals and limitations rather than copying README numbers.
3. Close canonical DB writers normally, prove no WAL/SHM companions, make/hash a timestamped byte copy outside the commit, and record integrity, FK, migration ledger and semantic table row counts.
4. Create disposable DB copies for migration/authoring tests. Never use the canonical DB for exploratory work.
5. Inventory actual character/room/animation/voice/player assets and their licenses/provenance. Record missing items against Ticket 01's manifest.
6. Trace and document exact current extension points for portable definitions/Core, action execution, services/shutdown, SQLite migration/loader/writer/undo/clone, WPF sequence/library/reference player, Unity bootstrap/bindings, and all relevant tests.
7. Create `Docs/UnityPerformance/Execution/IMPLEMENTATION-MAP.md` and `RUNNING-REPORT.md`. Include expected file groups and any justified ticket split needed for reviewable commits.

### Guardrails

- No runtime/schema/canonical-content behavior change.
- No package install, asset purchase, Unity version change or placeholder asset presented as sufficient.
- Preserve all unrelated/untracked work and existing backups.
- Do not launch a writer on the canonical DB merely to inspect it.
- If the approved base is not available/clean or an essential real rig cannot be identified, complete all other preflight and report the exact blocker.

### HARD acceptance

- Baseline commands and exact outcomes are recorded.
- Canonical DB has a verified backup hash and clean integrity/FK checks.
- Actual schema version/migration names and next available version are known; later tickets do not guess `v12`.
- The implementation map names concrete files/types/tests and flags monoliths to split.
- Asset inventory identifies the real proof inputs or the precise acquisition/creation gate.

### Commit and handoff

Commit only reports/docs created by this ticket. Handoff includes base SHA, backup path/hash, test totals, missing tools/assets, running process state and whether Ticket 01 can begin.

---

## Ticket 01 — Real Rig and Animation Proof

### Goal

Prove the risky visual assumptions on the actual intended test rig before building persistence and broad tooling.

### Context

This proof decides the layer/region contract. It must combine a standing and sitting foundation, a transition, gesture, face, speech mouth, gaze, blink, breath and manually scrubbed arm/prop and pelvis motion. Built-in Playables are the planned implementation; no package is approved.

### Contract

1. Import/configure or locate the real humanoid rig, face controls, room/furniture, minimum clips and short voice sample. Preserve `.meta` files and provenance/license notes.
2. Build a small reusable Unity proof scene/component with one fixed-topology PlayableGraph and one facial composer. It may be dev-only but must use patterns that can graduate into Ticket 06.
3. Demonstrate: stand idle; walk/turn or controlled travel; stand↔chair sit; masked gesture while sitting; Happy/Neutral/Mad face presets; mouth plus expression plus blink; gaze during ordinary body motion and suppression during a head-owning nod; additive/suppressed breath; arm/prop clip scrub both directions; seated pelvis/body scrub.
4. Inspect actual imported curve coverage. Define masks, region claims, additive reference requirements, facial ownership, gaze limits, prop attachment and clip endpoint/origin conventions from evidence.
5. Record calibrated useful scrub intervals in normalized position plus source frame/seconds display, with notes on reverse quality.
6. Add focused EditMode validation tests for proof bindings where useful. Run the pinned Editor and leave the proof scene open for review.
7. Write `Docs/UnityPerformance/Execution/RIG-AND-ANIMATION-CONTRACT.md` with screenshots/evidence locations, actual asset IDs and approved constraints.

### Guardrails

- A primitive, capsule, generic Mixamo stand-in without the required facial rig, or log-only fake does not close this ticket.
- No paid acquisition or new dependency without explicit approval.
- Do not proliferate Animator states, per-combination controllers or Timelines.
- Do not assert an additive/masked combination is safe without watching it on the real pose/rig.
- Do not manually rewrite broad scene YAML.
- If assets are missing, prepare the exact manifest (rig/clip/face/audio requirements, formats and license constraints) and block the HUMAN gate.

### HARD acceptance

- Unity compiles in 6000.5.9f1; focused tests pass.
- Proof can be run repeatedly without leaked PlayableGraphs or duplicate writers.
- Actual curve/mask/face ownership is documented and machine-checkable where possible.
- Both procedural examples scrub smoothly enough to proceed, or affected architecture/content assumptions are explicitly amended and reviewed.

### HUMAN acceptance

User watches the proof in the pinned Editor and approves foundation/gesture layering, stand↔sit quality, face/speech/blink composition, gaze behavior, breath, and both scrub motions as a viable basis. Record corrections; do not claim approval before the user gives it.

### Commit and handoff

Commit the reusable proof code/assets, tests, `.meta` files and rig contract after the HUMAN gate. Handoff enumerates assets, licenses, limitations, accepted masks/regions and any build-plan amendment.

---

## Ticket 02 — Portable Performance Domain and Action Semantics

### Goal

Implement the host-neutral performance definitions, session director, deterministic selectors/routes/clocks, lifecycle and three action kinds using a fake host.

### Context

Portable source lives under `Assets/Scripts/Portable` and is linked into .NET projects. Existing `ActionExecutor` conflates many always-blocking actions with transfers. Background tracking logs faults, but new fatal presentation faults must reach visible session failure. Performance state is session-global and persists across graph continuations.

### Contract

1. Add semantic definitions and stable resource kinds for stages, legal states, directed transitions, cues/profiles, motion recipes/curves, session setup, player presentation and structured dialogue fields.
2. Add closed moods/regions/destination policies/line-cue policies with explicit validation. Keep extensible catalog IDs where the architecture contract says names are content, not engine enums.
3. Add `IPerformanceService` or equivalent semantic host boundary and a session-scoped `PerformanceDirector`. Separate requested/committed state and correlate host acknowledgements by request ID.
4. Implement one serialized execution context, route calculation, typed compatibility/exclusion diagnostics, region arbitration, scheduled update API, manual-clock test seam and structured traces.
5. Add independently seeded stable-ID-sorted destination, gesture, face, gaze/cadence and motion-rate RNG domains. Prove card/dialog selections do not change.
6. Implement anti-repeat, explicit optional None, minimum face dwell, line-local mood restoration and queued-cue claim revalidation exactly as the architecture contract states.
7. Add `Set Performance`, `Play Motion`, and `Set Player State` instances, registry metadata/defaults/scopes and execution. Classify transfer actions explicitly; blocking activity must never call `ReduceFlow`.
8. Extend direct/tagged dialog resolution to produce structured requests while preserving existing tag selection/order/RNG. Define text-only duration and voiced completion contracts.
9. Expand session shutdown/fatal error handling. Fatal nonblocking presentation errors latch once, surface to the host even at Continue, reject later work and trigger complete cleanup. Preserve unrelated legacy failure semantics unless a root-cause fix requires a documented change.
10. Build deterministic fake performance/dialog hosts and manual clock tests for the complete example sequence, nested choices, GOTO/RETURN and replay.

### Guardrails

- No SQL, WPF or Unity types in portable projects.
- No forever-running Core task and no per-frame RNG.
- Persistent ambience never enters the background tracker.
- No sentiment/mood inference, general expression language, fallback route, teleport, silent missing coverage, or global `UnityEngine.Random` decisions.
- Do not change consent, card selection, phase progress or Continue semantics.
- Prefer focused new files over making `ActionExecutor`/`GameSessionEngine` monolithic.

### HARD acceptance

- Full Core suite passes, including new tests for all contracts above.
- Same seed plus same clock/events gives identical semantic trace; different frame-step partitions produce identical scheduled decisions/samples where specified.
- Impossible route, explicit cue conflict, stale acknowledgement, host timeout/failure, cancellation and replay are loud and leave valid committed state.
- Separate RNG-domain tests prove existing card/dialog traces are unchanged.
- `WaitForAll` observes only finite accepted work and cannot wait on ambience.
- WPF/.NET build and Unity portable compile succeed.

### Commit and handoff

Commit portable definitions/Core/tests and update architecture docs/README status. Handoff includes new public contracts, exact failure semantics, RNG seed derivation and host responsibilities for Tickets 04–08.

---

## Ticket 03 — SQLite Persistence, Import, Clone, and Undo

### Goal

Persist and reconstruct the performance domain with the repository's typed migration/repository/authoring-command model.

### Context

SQLite is canonical; the snapshot loader must reconstruct fully validated portable content. Action Blocks are copied templates, profiles are shared references. Existing semantic undo, recursive PromptChoice sequences, session/phase/card duplicate paths and resource delete protection all need explicit coverage.

### Contract

1. Use Ticket 00's actual next migration number. Create embedded additive/rebuild SQL and transforms as required for typed normalized definitions/relations, actions, dialogue fields and motion curve points. Add constraints/indexes/FKs that express real invariants without hiding semantic validation.
2. Implement typed repositories and semantic commands for all definitions, relations, usage queries, bulk cue metadata changes and importer writes.
3. Extend snapshot loading and portable reference validation. Unknown future resource kinds may load according to existing policy, but missing/wrong-kind required performance references fail.
4. Extend action sequence writer, type mapping, default creation, recursive PromptChoice persistence, Action Blocks, Card duplication, Session/Phase clone/Make Unique where relevant, and undo/redo identity restoration.
5. Implement the narrow `.funscript` importer in the provider/authoring boundary. Parse accepted subset, report point context, apply inversion once, retain provenance/hash/importer version, and persist immutable normalized points transactionally.
6. Implement usage counts/navigation data and RESTRICT/explicit delete behavior for shared profiles, cues, recipes, resources and curves. No orphan repair fallback.
7. Write migration idempotence, load/roundtrip, invalid schema/content, recursive clone, undo, bulk edit and importer tests on disposable DBs.
8. Do not migrate or populate the canonical DB in this ticket. Commit code/tests first.

### Guardrails

- Never hand-edit ledger rows or silently choose a migration number after collision.
- No EAV parameter bags, JSON blobs standing in for relational authoring, reflection mapping or direct SQL from WPF/Core.
- Do not treat a motion curve as a toy hardware resource.
- Reject duplicate timestamps/out-of-range values; never sort/clamp/fix invalid scripts silently.
- Preserve stable IDs on undo; duplicates mint full stable IDs.
- Do not add sample/demo rows to canonical content.

### HARD acceptance

- Full Core and SQLite suites pass.
- Every current and new action subtype round-trips, including nested PromptChoice and Action Block insertion.
- Close/reopen retains profiles, legal states, transitions, dialogue fields, recipe settings, imported points and provenance exactly.
- Migration applies once/idempotently to multiple representative prior-version disposable DBs; integrity/FK checks pass.
- Invalid references/load/imports fail with source IDs and actionable context.
- Deleting/in-use/undo/redo/duplicate/Make Unique semantics are covered and deterministic.

### Commit and handoff

Commit migration code, provider code, tests and docs. Handoff states reserved migration version/name, schema diagram/table inventory, canonical DB untouched, and exact WPF APIs for Ticket 04/09.

---

## Ticket 04 — Basic WPF Writing and Simulated Performance Play

### Goal

Give a writer the smallest pleasant GUI needed to author and execute the first complete nonvisual performance conversation before advanced matrix work.

### Context

The existing shared ActionSequence editor, explicit editor registry, semantic undo and Reference Player are the foundation. This ticket proves ordinary authoring flow; Ticket 09 later adds the full performance library/matrix/audition experience.

### Contract

1. Add explicit editors for Set Performance, Play Motion and Set Player State in every legal root/nested scope, with filtered name-based pickers and concise row summaries.
2. Extend Direct Dialog and Dialog Snippet/From Tags editing for voice/duration/mood/cue handling. Keep simple fields visible and advanced acting intent collapsible.
3. Add session presentation setup editing for required stage, initial state, profile/mood and initial player state.
4. Add minimal focused Performances library editors sufficient to create stage/location/pose/state/transition, cue/profile and motion recipe records. Reuse semantic commands/undo; do not build matrix yet.
5. Implement a real WPF simulated performance/dialog host with explicit durations, manual/fast-test clock mode and live inspector: requested/committed stage, mood, selected cues, claims, route, scalar and diagnostics.
6. Preserve active card selection, focus, dirty buffer and nested sequence editors during catalog refresh. Add keyboard-friendly “add next dialogue line” behavior suitable for writing six lines quickly.
7. Show fatal performance errors and missing coverage/routes in both Workbench preview and Reference Player. Stop/restart leaves no simulated work alive.
8. Author the first disposable acceptance conversation through the GUI, including two moods, a DifferentLocation request, dialogue, choice, motion and player-state change.

### Guardrails

- WPF must call Core selectors/validators; do not duplicate compatibility logic.
- No generic reflection property grid or arbitrary rule editor.
- No direct SQLite editing in code-behind and no replacement Save-document model.
- Do not expand `MainWindow.xaml.cs`/existing giant partials when a focused control/view model/service can own the feature.
- Simulated preview must be labeled and cannot claim visual correctness.
- Do not refresh an open editor by discarding dirty buffers or selection.

### HARD acceptance

- Core/SQLite/WPF suites and WPF build pass.
- GUI-authored disposable content survives close/reopen and produces the expected structured Core trace.
- Recursive choice/action-block authoring, duplicate and undo/redo work.
- Fast-test and real-time simulation have explicit distinct labels and deterministic tests.
- Stop/error/replay do not retain queues, claims, clocks or state.

### HUMAN acceptance

With WPF left running on a disposable DB, user authors six lines, two moods, movement intent, one choice and a motion action without touching IDs/SQL. User judges the basic sequence editing flow understandable enough to continue. Capture friction for Ticket 09.

### Commit and handoff

Commit WPF controls/services/tests/content fixtures and docs after gate corrections. Handoff includes the authored fixture/export expectations, UI friction list and any deferred matrix work.

---

## Ticket 05 — Runtime Export and Current Unity Bootstrap

### Goal

Load complete WPF-authored current content in Unity Editor and a Windows development player through a validated explicit transport.

### Context

Unity currently constructs content from legacy ScriptableObjects and lacks the current dialogue host path. Unity should not gain a SQLite provider for this milestone. The transport must cover the whole current action vocabulary, not only demo actions.

### Contract

1. Define a versioned flat field-based runtime DTO envelope with explicit discriminators/arrays for all current definitions/actions, including recursive choices. Keep serializer adapters outside Core.
2. Export from one consistent SQLite read transaction via the production loader/validator; include schema/transport/binding versions, deterministic payload hash and required semantic resource IDs.
3. Write temp, deserialize/reconstruct/validate, then atomically replace the prior generated artifact. On failure retain old bytes but mark/display them stale.
4. Add complete roundtrip tests comparing semantic snapshots and action trees. Unsupported mappings fail export naming source ID/type.
5. Add Unity loader/reconstructor compatible with built-in JSON serialization constraints and a serialized binding registry keyed by semantic resource ID.
6. Generate/import the Unity technical manifest for WPF: resource kind, clip duration/sample rate, rig/signature/regions, targets/props and binding revision. Validate duplicate/missing/wrong-kind/stale binding before run/build.
7. Add snapshot-based session selection and labeled in-memory acceptance profile using existing eligibility/spawn contracts. Reachable unsupported host capability blocks preflight with an in-game error.
8. Wire current Core services including structured dialog/performance fakes sufficient for semantic execution; do not yet claim production animation.
9. Keep legacy SO fixtures isolated for old tests. The new playable path must not call `UnityContentGraphBuilder`.
10. Produce and smoke-test a Windows development player with WPF closed; show loaded content/binding revision and visible runtime errors.

### Guardrails

- No direct SQLite in Unity and no hand-edited JSON content.
- Do not serialize the polymorphic `GameContentDefinition` graph directly.
- No runtime `AssetDatabase`; Editor tooling may use it only to build/validate serialized bindings.
- No sample-only parallel engine or silent omission of older action kinds.
- Do not claim persisted WPF user-profile parity.
- Generated artifacts/build outputs follow repository tracking policy; machine paths stay out of content.

### HARD acceptance

- Core/SQLite/WPF tests plus Unity EditMode transport/binding tests pass.
- WPF export→Unity reconstruction is semantically equal for every action/definition family under test.
- Controlled WPF/Unity fake-host semantic traces match after excluding host timing/cosmetic fields.
- Stale/corrupt/partial/wrong-version artifact and missing bindings fail before session start.
- Development player launches the authored session with WPF closed and surfaces a deliberate preflight/runtime error in UI.

### Commit and handoff

Commit transport/exporter/loader/bootstrap/tests/generated policy/docs and `.meta` files. Handoff includes exact export/build commands, revisions/hashes and legacy bridge isolation.

---

## Ticket 06 — Stage Movement and Layered Actor Renderer

### Goal

Turn semantic performance requests into real movement, pose settlement and coherent layered acting on the approved rig.

### Context

Ticket 01 fixed the rig contract; Ticket 02 fixed Core decisions; Ticket 05 supplies current content and binding validation. The initial topology is nine legal states and sixteen directed transitions. Unity owns spatial routes and world root.

### Contract

1. Graduate/refactor the proof into one reusable actor renderer with a fixed PlayableGraph topology, reusable inputs/crossfades and one facial writer.
2. Author/bind all six station anchors, standing paths/facing, Chair and Side-of-bed contact frames, nine legal states and sixteen directed transitions.
3. Implement one stage mover owning world root, in-place walk/turn matching, station pose transitions, request IDs, progress, per-edge watchdog and arrival acknowledgement at 2 cm/3 degrees plus pose readiness.
4. Implement region claim/suppression priority exactly: transition > finite motion > line gesture > ambient; gaze/head, blink/eyelid and breath/torso rules included.
5. Implement profile ambience/line gesture/face selection requests, dwell/cadence and no gratuitous base restart. Keep actual animation evaluation cosmetic; Core remains selection authority.
6. Validate actual clip curves/masks/signatures against semantic declared regions and approved rig. Reject conflicting writers and stale manifests.
7. Implement Unity pause/cancel/teardown/replay for mover/graph/claims and stale acknowledgement rejection.
8. Add EditMode/PlayMode tests for topology, routes, arrival/failure, suppression/release and lifecycle.

### Guardrails

- No NavMesh/general pathfinding, teleport success, inferred reverse route or walk-and-talk choreography.
- No per-combination Animator states/Timelines and no graph rebuild every frame.
- No two components writing world root or facial channels.
- Metadata does not excuse visible foot slide/contact penetration/mask failure.
- Transition suppresses incompatible body cues; do not queue missed ambient gestures for a burst.
- Do not proceed past a missing real clip by substituting a debug transform in acceptance.

### HARD acceptance

- Pinned Unity compile, EditMode and PlayMode suites pass.
- Automated route tests cover every legal ordered state pair, impossible state and timeout/cancel/stale completion.
- Renderer leaves no duplicate graph/writer/claim after replay or scene unload.
- Core trace and Unity acknowledgements agree on route edges and committed state.

### HUMAN acceptance

User watches real character traverse all locations, chair sit/stand, bed sit/lie/recover, gesture in standing/sitting, retain foundation after overlay, suppress head aim during nod, and continue blink/breath/face appropriately. Review foot slide, furniture contact, penetration, pops, cadence and camera framing. Fix material failures before acceptance.

### Commit and handoff

Commit renderer/mover/bindings/scene-assets/tests/docs/`.meta` after visual gate. Handoff records endpoint tolerances, approved transitions, remaining asset-specific limitations and scene to open.

---

## Ticket 07 — Dialogue, Facial Motion, and Player Presentation

### Goal

Deliver actual dialogue and line acting while supporting four player postures with independent body visibility.

### Context

Structured dialogue and ordering already exist in Core. The initial proof uses approximate audio-amplitude mouth motion, not phoneme visemes. `WaitForContinue` remains the player acknowledgement boundary; speech completion is presentation timing.

### Contract

1. Build subtitle presenter and single FIFO speech channel. Blocking/nonblocking dialog respects Core ordering; subtitles remain until replacement/session end.
2. For voice resources, complete on actual audio end and feed a precomputed amplitude envelope to mouth/jaw. For text-only lines use Core's explicit/estimated duration.
3. Compose face preset, speech mouth/jaw and blink in the sole face writer; enforce channel limits and line-local mood/cue start/end/restoration.
4. Revalidate Auto gesture claims at line onset; suppress with trace reason. Explicit incompatible cue fails visibly. Unfinished line gesture blends out at line end.
5. Implement four player posture/camera/body bindings: Standing, Sitting, Kneeling, LyingDown; toggle body visibility independently; change under short fade; update PlayerFace gaze target.
6. Validate player-state compatibility before commit and surface the blocking cue/recipe. Player posture has no implicit gameplay/consent effect.
7. Integrate choice/Continue UI with ambience, disable duplicate advances, and handle application pause, cancel, error, menu exit and replay.
8. Include at least three short real voice clips with provenance for canary content. Test queued nonblocking lines and context changes.

### Guardrails

- Do not call amplitude animation phoneme-accurate lipsync.
- No cloud TTS/lipsync package or overlapping speech.
- No per-posture duplicated visible/hidden state enum; visibility remains independent.
- No line sentiment inference or implicit Continue at speech end.
- Do not allow camera inside the player head/body.
- Do not let audio failure vanish into console-only logging.

### HARD acceptance

- Unity EditMode/PlayMode and relevant Core tests pass.
- Dialogue after movement starts only after pose readiness; queued lines preserve order and selected cue identity.
- Voice/text timing, pause/resume, line override restoration, claim conflicts and teardown/replay are covered.
- Repeated Continue clicks cannot advance twice.
- Every player posture works with body shown/hidden and updates gaze target.

### HUMAN acceptance

User reviews voiced and text-only lines with all three moods, expression+mouth+blink combinations, gesture timing, gaze, subtitles and every player posture/visibility. Fix facial clipping, robotic mouth, camera/body clipping and pacing severe enough to harm the slice.

### Commit and handoff

Commit presenters/player bindings/audio/tests/content/docs/`.meta`. Handoff labels lipsync limitations, voice provenance and exact acceptance sequence.

---

## Ticket 08 — Procedural Motion and Funscript Playback

### Goal

Run finite oscillator- and recorded-script-driven animation through the same calibrated sampler on arm/prop and pelvis/body examples.

### Context

Motion definitions/import persistence exist; Ticket 01 proved useful reversible intervals; renderer supports region claims. The feature is visual motion only and must remain distinct from smart-toy hardware actions.

### Contract

1. Implement portable normalized sampler functions for oscillator and immutable motion curve, independent of Unity frame rate.
2. Oscillator: source-frames/second editor semantics, one seeded rate per half-cycle, retained segment rate, smoothstep endpoints, absolute due times and guarded catch-up.
3. Script: linear interpolation, applied import inversion, hold-before/after, default duration at final point, explicit truncate/hold-end behavior, no hidden easing/looping.
4. In Unity, pause clip auto-time and set sample time before graph evaluation. Apply one scalar coherently to all recipe-owned body/prop channels with no world-root extraction.
5. Implement entry blend, driver-duration clock, exit blend, completion/release, app pause and cancellation. Blocking waits; nonblocking is finite and `WaitForAll` observes it.
6. Enforce region/stage/mood/player/prop compatibility and relocation conflict ordering. Release claims before Set Performance travel can start.
7. Bind and tune the real arm/prop and seated pelvis/body examples plus one valid imported script and oscillator recipe.
8. Add exact controlled-time tests across different update partitions, boundaries/rate extremes, pause/catch-up/cancel and Unity sampled times.

### Guardrails

- No animation speed reversal of the whole actor, animation-event gameplay, root extraction, multi-axis/video/hardware sync or physics simulation.
- Do not reroll rate every frame or reinterpret 10–30 frames/second as cycles/minute.
- No smoothing of recorded points that changes authored timing.
- A clip is usable only if reverse scrubbing was visually approved.
- One recipe can coordinate multiple regions; do not create unsynchronized clocks for its arm and prop.
- Fatal finite-motion errors must surface to the session, not only background logs.

### HARD acceptance

- Pure sampler tests match exact expected values/times and are partition-independent.
- Unity tests verify actual playable local time, entry/exit duration semantics, pause/resume and claim release.
- Nonblocking motion overlaps compatible speech/blink/gaze; conflicting explicit requests fail; WaitForAll finishes.
- Motion completion/cancel permits later relocation and replay starts clean.

### HUMAN acceptance

User watches both motion examples at slow/fast limits, reversals, script points, entry/release and concurrent speech. Fix visible discontinuities, prop desync, range/contact problems or layer fights.

### Commit and handoff

Commit sampler/runtime/bindings/recipes/script fixture/tests/docs/`.meta`. Handoff includes formulas, rate display examples, script provenance/hash and approved visual ranges.

---

## Ticket 09 — Computed Performance Matrix and Fast Unity Audition

### Goal

Make monthly content creation fast, understandable and enjoyable through reusable profile/cue editing, calculated coverage diagnostics and a short visual audition loop.

### Context

Ticket 04 exposed the basic writer path and captured friction. Ticket 03 owns semantic commands. Ticket 02 owns compatibility. Ticket 05 owns snapshot/manifest versions. Unity renderer now provides a real visual target.

### Contract

1. Build a focused Performances library with profile, cue, stage and motion recipe editors using dedicated controls/view models/services.
2. Profile editor: allowed states, three mood tabs, body/face queries or explicit IDs, gaze choices, None/rest weight, cadence/dwell ranges, eligible counts and example candidates.
3. Computed matrix: legal state rows; mood/channel columns; eligible count, required gap, optional None and binding status; click-through included/excluded candidates with Core rejection reasons.
4. Add route reachability and region-conflict views. They use Core results and are not editable graph/cell stores.
5. Add usage counts/list/navigation, duplicate and contextual Make Unique, transactional bulk metadata assignment and semantic undo/redo. Query-shared profiles update consumers; explicit membership stays fixed.
6. Motion editor shows normalized/seconds/zero-based-frame interval, source sample rate, derived traversal time/cycles per minute, scalar-time plot, imported points, scrub/time controls and validation after clip reimport.
7. Implement WPF→Unity filesystem audition: versioned atomic request/response, monotonic ID, content and binding hashes, source/target/profile/line/motion/seed, explicit Run/Replay, trace/status response and stale-result rejection.
8. Preserve dirty Card buffers, selection, focus and open nested editors through hot refresh. Improve dialogue keyboard flow based on Ticket 04 observations.
9. Write tests proving matrix/UI reuse the Core predicate, shared/fixed semantics, bulk undo, Make Unique, stale manifest/result handling and no second rule implementation.
10. Complete the measurable authoring acceptance tasks from build-plan section 12 with the real GUI.

### Guardrails

- No persisted Cartesian matrix, duplicate WPF eligibility logic, profile inheritance, arbitrary expression builder or embedded Unity/network service.
- Manifest technical fields remain read-only in WPF.
- Audition runs only on explicit user action; no live hot replacement of an active game session.
- Atomic preview files are generated artifacts and must not dirty canonical content/history.
- Catalog refresh cannot erase unsaved Card edits.
- Do not declare the authoring workflow pleasant solely from automated tests.

### HARD acceptance

- Full .NET/WPF suites pass; Unity audition tests pass.
- Every matrix count/rejection matches direct Core eligibility calls.
- Stale/corrupt/mismatched request/response/manifest cannot show a current success.
- Reopen retains editor state/content; all bulk/shared/unique operations undo and redo with exact IDs.
- Adding one matching cue updates two profile consumers without editing their cards; removing sitting support identifies all affected required cells/actions.

### HUMAN acceptance

User performs all six authoring tasks in build-plan section 12: writes conversation, adds reusable gesture, removes compatibility and finds effects, duplicates/makes unique/undoes, imports/previews a script, exports and replays in Unity without editing IDs/JSON/Timeline. Record time, clicks/friction and requested corrections; repair material issues.

### Commit and handoff

Commit WPF UI/services/tests/docs and Unity audition receiver/`.meta` after user gate. Handoff includes measured workflow, remaining friction and exact steps for Ticket 10 content production.

---

## Ticket 10 — Acceptance Content, Canonical Checkpoints, Built Player, and Final Handoff

### Goal

Ship and prove the first playable 3–5 minute performance session, then leave both implementation and authoring workflow ready for continued content creation.

### Context

This ticket integrates and polishes content accumulated during earlier tickets. It must not begin the animation asset workload. The canonical DB may still be at its pre-performance schema/content state; follow strict separate checkpoints.

### Contract

1. Re-read the entire packet and audit each architecture invariant/exclusion against code and content. Resolve drift before canary.
2. Commit/verify migration code is already green. Close writers; verify WAL/SHM absence; recheck/hash Ticket 00 backup; create a fresh backup; run prechecks. Migrate canonical DB through the real migrator once, prove idempotence, run integrity/FK/ledger/semantic-preservation checks, and commit that binary migration as its own checkpoint.
3. Hand-author the acceptance session through WPF into the canonical DB using the approved workflow. Include six locations/tour, at least twelve lines, three voiced lines, Happy/Neutral/Mad, choice branch, meaningful DifferentLocation beat, Continue ambience, arm/prop oscillator motion, pelvis recorded-script motion, chair/bed pose changes, all player postures and both visibility states through an optional test path. Validate and commit this authored batch as a content checkpoint.
4. Meet the minimum asset/content budget in build-plan section 15: required foundations/transitions, at least six body gestures including nod/point/shimmy/Mad hands-on-hips, two faces per mood, actual room/rig/player/audio/motion assets and bindings.
5. Export a fresh validated runtime snapshot/binding manifest and build a Windows development player. Prove it runs with WPF closed from a clean generated state.
6. Execute every acceptance play-path step in build-plan section 15, first seed and alternate seed. Record semantic traces, visible variation, errors and replay cleanup.
7. Run the complete automated matrix: solution tests, WPF build, Unity EditMode/PlayMode, batch compile, built-player smoke, transport/binding freshness, canonical integrity/FK/schema/content semantic checks and `git diff --check`.
8. Run visual review for foot slide/contact/penetration, facial speech composition, gaze fights, pops, cadence/overgesture, camera/body clipping and motion extremes. Fix material defects and rerun affected gates.
9. Run final authoring canary: user creates a small new conversation, auditions/exports and plays it without Unity choreography. Do not seed the canonical DB through SQL behind the user's back.
10. Update README/current architecture docs, Unity run/build/export instructions and final execution report. Clearly label remaining limitations and future work.

### Guardrails

- Do not mix migration-code, canonical binary migration and authored-content checkpoints into one opaque commit.
- Never copy an open DB, commit WAL/SHM, or line-merge SQLite.
- No demo-only shortcuts, fake animation, manual JSON edits, Timeline creation per conversation or hidden fallback/teleport.
- Do not add deferred features to make the final demo look broader.
- A green backend with an unreviewed/broken visual scene is not done; a good animation sandbox disconnected from WPF content is not done.
- Do not call user acceptance passed until the user says so.

### FINAL HARD acceptance

- Every automated command/result is recorded with totals/skips.
- Canonical DB has before/after backup hashes, clean integrity/FK, correct one-row migration ledger entries and meaningful content checkpoint history.
- Standalone player completes the session with WPF closed, surfaces deliberate errors visibly, returns to menu and replays cleanly.
- Alternate seed changes incidental acting where pools permit without changing authored narrative correctness.
- README and focused docs match the implemented architecture; no stale direct-SQLite/Timeline plan remains presented as current.

### FINAL HUMAN acceptance

User plays the complete acceptance session and authors/exports/plays a small new conversation. User judges both game feel and authoring flow ready as the first production foundation. Keep WPF and the relevant Unity scene/player available for inspection until the user responds.

### Commit and final report

Use separate canonical migration and authored-content commits as required, followed by final integration/docs fixes. Do not push unless explicitly authorized. Deliver the report required by `ORCHESTRATION-PROMPT.md`, including exact build path and running process/editor state.
