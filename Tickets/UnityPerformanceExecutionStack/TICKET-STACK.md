# Ticket Stack — Unity Performance Playable Slice

Read every ticket before starting Ticket 00. Execute in order. Each ticket repeats its local context so it can be handed to a fresh worker, but the architecture contract and build plan remain binding. Workflow revision: September 11, 2026. Tickets are dependency milestones that may span coherent commits. Only 01, 06 and 10 require user acceptance; other manual checks require executor evidence, not a new permission pause. The first real WPF-to-Unity audition must work in Ticket 05.

## Ticket 00 — Baseline, Protection, and Focused Implementation Map

### Goal

Create a reproducible execution base, protect authored content, inventory the real presentation assets and map each contract to current code before behavior changes.

### Context

The repository has migration files through v11 and reports 384 passing .NET tests; the actual canonical DB version and test results must be measured. Unity still uses the legacy SO/deck bridge. The working directory may contain untracked database backups that must remain untouched. Ticket 01 cannot close without a real rig and animation/audio inputs.

### Contract

1. Confirm the exact user-approved packet commit, create the implementation branch, and record Git/SDK/Unity/package state.
2. Run the full .NET baseline, available Unity EditMode/PlayMode baseline, WPF build/start smoke against an explicit disposable DB, and current Unity batch compile. Avoid an automatic canonical migration during smoke launch. Report observed totals and limitations rather than copying README numbers.
3. Close canonical DB writers normally, prove no WAL/SHM companions, make/hash a timestamped byte copy outside the commit, and record integrity, FK, migration ledger and semantic table row counts.
4. Create disposable DB copies for migration/authoring tests. Never use the canonical DB for exploratory work.
5. Inventory actual character/room/animation/voice/player assets and their licenses/provenance. Record missing items against Ticket 01's manifest.
6. Trace and document exact current extension points for portable definitions/Core, action execution, services/shutdown, SQLite migration/loader/writer/undo/clone, WPF sequence/library/reference player, Unity bootstrap/bindings, and all relevant tests.
7. Create `Docs/UnityPerformance/Execution/IMPLEMENTATION-MAP.md` and `RUNNING-REPORT.md`. Include expected file groups and any justified ticket split needed for reviewable commits.
8. Walk through one current Card edit/Save/preview and record focus, dirty-buffer and snapshot-loading behavior. Map `CardEditBuffer`, the global validator call in `GameContentSnapshotLoader.Load`, and the Reference Player dirty pause. These are explicit integration points for isolated buffered audition; do not bypass them with a logging-only preview.

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

1. Add semantic definitions and stable resource kinds for stages, legal states, directed transitions, cues/profiles, motion recipes/curves, session setup, player presentation and structured dialogue fields. Include cue Enabled state, independent Keep/Set profile/mood intent, dialogue Inherit/Auto/None distinctions, voice-associated text fingerprint, and nullable action duration override (Use recipe by default).
2. Add closed moods/regions/destination policies/line-cue policies with explicit validation. Keep extensible catalog IDs where the architecture contract says names are content, not engine enums.
3. Add `IPerformanceService` or equivalent semantic host boundary and a session-scoped `PerformanceDirector`. Separate requested/committed state and correlate host acknowledgements by request ID.
4. Implement one serialized execution context, route calculation, typed compatibility/exclusion diagnostics, region arbitration, scheduled update API, manual-clock test seam and structured traces.
5. Add independently seeded stable-ID-sorted destination, gesture, face, gaze/cadence and motion-rate RNG domains. Prove card/dialog selections do not change.
6. Implement anti-repeat, explicit optional None, minimum face dwell, line-local mood restoration and queued-cue claim revalidation exactly as the architecture contract states.
7. Add `Set Performance`, `Play Motion`, and `Set Player State` instances, registry metadata/defaults/scopes and execution. Classify transfer actions explicitly; blocking activity must never call `ReduceFlow`.
8. Extend direct/tagged dialog resolution to produce structured requests while preserving existing tag selection/order/RNG. Define text-only duration and voiced completion contracts.
9. Expand session shutdown/fatal error handling. Fatal nonblocking presentation errors latch once, surface to the host even at Continue, reject later work and trigger complete cleanup. Preserve unrelated legacy failure semantics unless a root-cause fix requires a documented change.
10. Build deterministic fake performance/dialog hosts and manual clock tests for the complete example sequence, nested choices, GOTO/RETURN and replay.
11. Add the local presentation harness using the production executor/director and disposable run state: selected presentation-only passage plus explicit starting context, settled initialization or Test arrival, stopped/replaced request cleanup. Reject game-flow/choice/stat selections with a typed Play session diagnostic. Normal session playback still uses the existing graph VM. Implement presentation-seed override and take/trace capture so New take changes only acting; Replay take retains the resolved text/cues/events of the last take.

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
- Mood-only/location-only updates keep all other effective state; invalid combined updates commit nothing. Missing initial session context is an error; Keep/None/Inherit are never conflated.
- Local audition has no DB/profile/stat effects, supports Stop/replace, and cannot consume card/dialog RNG on New take. Normal gameplay movement tests cannot use sandbox settled initialization to pass.
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

1. Use Ticket 00's actual next migration number. Create embedded additive/rebuild SQL and transforms for typed definitions/relations, cue Enabled state, partial-update actions, recipe-duration inheritance, dialogue fields and motion points. Add constraints/indexes/FKs for committed records. Incomplete experiments remain in editor buffers; do not add a generalized draft/versioning subsystem.
2. Implement typed repositories and semantic commands for all definitions, relations, usage queries, bulk cue metadata changes and importer writes.
3. Extend shared reconstruction/mappings and Core validation with a scoped snapshot builder. Audition includes the selected rows/context and every eligible referenced candidate; publish includes selected Sessions and all potential reachable branches/tag-query results/required states/routes/assets, not only a sampled seed or consent-filtered subset. Keep global Check library diagnostics available. Do not invoke global fail-fast Load before selecting roots, catch its errors, or silently drop broken included dependencies. Storage corruption/FKs still fail.
4. Extend action sequence writer, type mapping, default creation, recursive PromptChoice persistence, Action Blocks, Card duplication, Session/Phase clone/Make Unique where relevant, and undo/redo identity restoration.
5. Implement the narrow `.funscript` importer in the provider/authoring boundary. Parse accepted subset, report point context, apply inversion once, retain provenance/hash/importer version, and persist immutable normalized points transactionally. Replace source retains the curve ID, validates affected recipes and updates points/provenance in one undoable transaction; existing running snapshots remain immutable.
6. Implement usage counts/navigation data and RESTRICT/explicit delete behavior for shared profiles, cues, recipes, resources and curves. No orphan repair fallback.
7. Write migration idempotence, load/roundtrip, invalid schema/content, recursive clone, undo, bulk edit and importer tests on disposable DBs.
8. Commit migration/provider code and green disposable-DB tests first. Then separately checkpoint and migrate the canonical DB through the production migrator with backup/hash, no writers/WAL/SHM, before/after integrity/FK/ledger/content checks, and an idempotence check on a disposable copy. Commit the canonical migration separately before launching the new WPF version on it.
9. Provide an explicit, idempotent starter-content command keyed by stable fixture IDs. It installs reviewed state/route/profile definitions and the minimal valid proof palette supplied by Ticket 01; it never marks nonexistent assets valid or overwrites authored records. Install once at Ticket 04 setup, and evolve one real acceptance conversation thereafter. Destructive tests continue to use disposable DBs.

### Guardrails

- Never hand-edit ledger rows or silently choose a migration number after collision.
- No EAV parameter bags, JSON blobs standing in for relational authoring, reflection mapping or direct SQL from WPF/Core.
- Do not treat a motion curve as a toy hardware resource.
- Reject duplicate timestamps/out-of-range values; never sort/clamp/fix invalid scripts silently.
- Preserve stable IDs on undo; duplicates mint full stable IDs.
- Do not write starter/content rows into canonical storage before the separate migration checkpoint. Starter installation is explicit in Ticket 04; routine authoring thereafter uses normal transactions while WPF remains open.

### HARD acceptance

- Full Core and SQLite suites pass.
- Every current and new action subtype round-trips, including nested PromptChoice and Action Block insertion.
- Close/reopen retains profiles, legal states, transitions, dialogue fields, recipe settings, imported points and provenance exactly.
- Migration applies once/idempotently to multiple representative prior-version disposable DBs; integrity/FK checks pass.
- Invalid references/load/imports fail with source IDs and actionable context.
- Deleting/in-use/undo/redo/duplicate/Make Unique semantics are covered and deterministic.
- Scoped audition/export succeeds with unrelated invalid authoring outside its roots, but fails with a broken possible included candidate. Disabled cues stay out of Auto; explicit runtime references to them error. Snapshot overrides preserve IDs and cannot write the DB. Replace source and duration inheritance survive reopen/undo.

### Commit and handoff

Commit code/tests/docs, then the canonical migration checkpoint separately. Handoff records migration name/version, verified backup/hash/checks, typed scoped-load and buffer-overlay APIs, and starter install command for Ticket 04. Do not declare authoring content created yet.

---

## Ticket 04 — Basic WPF Writing and Simulated Performance Play

### Goal

Give a writer the smallest pleasant GUI needed to author and execute the first complete nonvisual performance conversation before advanced matrix work.

### Context

The existing shared ActionSequence editor, explicit editor registry, semantic undo and Reference Player are the foundation. This ticket establishes the daily writing/buffer/preview flow and minimal diagnostic counts. Ticket 05 supplies actual visual audition; Ticket 09 adds bulk intake/matrix tools without postponing essential usability.

### Contract

1. Add explicit editors for Set Performance, Play Motion and Set Player State in every legal root/nested scope, with filtered name-based pickers, Keep current/Use recipe defaults, Reset override, effective-value/source hints and concise summaries. A reusable Card shows From caller if no single effective context is known.
2. Extend Direct Dialog and Dialog Snippet/From Tags editing for voice/duration/mood/cue handling. Keep simple fields visible and advanced acting intent collapsible.
3. Add session presentation setup editing for required stage, initial state, profile/mood and initial player state.
4. Add minimal focused library editors and buffers for cues/profiles/recipes with Apply/Revert, usage links and eligible counts. Before Apply, show the shared impact/coverage diff. Make unique here copies edited values and rebinds only the selected action in its owning buffer. Install the reviewed starter stage/palette using Ticket 03's command; ordinary writers do not fill out state/transition tables. Reserve detailed technical setup for a separate stage inspector.
5. Implement a real WPF simulated performance/dialog host with explicit durations, manual/fast-test clock mode and live inspector: requested/committed stage, mood, selected cues, claims, route, scalar and diagnostics.
6. Preserve active card selection, focus, dirty buffer and nested editors during refresh. Ctrl+Enter adds/focuses Dialogue; Shift+Enter adds an internal newline; Paste dialogue converts paragraphs into rows as one undo unit at the chosen insertion point. Advanced fields stay collapsed. Distinguish spoken text from Card body text.
7. Show fatal performance errors and missing coverage/routes in both Workbench preview and Reference Player. Stop/restart leaves no simulated work alive.
8. Add New conversation using existing Session/Phase/Card commands, one undoable scaffold/tag/filter with normal graph wiring, starter context, Dialogue and explicit WaitForContinue. A fresh ordinary CardTag attached only to its Card and required by its Phase constrains the single CardExecutor, followed by a normal EndSession action. Test that an existing eligible-looking library Card cannot be drawn instead. Author the canonical conversation through the GUI and evolve it in subsequent tickets; never retype it into a second DB. Use labelled simulated preview until the selected features are bound in Unity; destructive tests stay disposable.
9. Implement Audition/New take/Replay take/Stop/Use on this line controls and the visible context strip against the simulated harness. Overlay only explicitly participating buffers, label Unsaved changes, and validate the selected scope. Keep Card Save/Revert and the normal player's dirty-Card pause unchanged. This UI must call the same orchestration service that Ticket 05 connects to Unity.

### Guardrails

- WPF must call Core selectors/validators; do not duplicate compatibility logic.
- No generic reflection property grid or arbitrary rule editor.
- No direct SQLite editing in code-behind, general draft-document store or implicit Save on Audition. Shared-definition buffers use the same focused transactional command pattern as existing Card buffers.
- Do not expand `MainWindow.xaml.cs`/existing giant partials when a focused control/view model/service can own the feature.
- Simulated preview must be labeled and cannot claim visual correctness.
- Do not refresh an open editor by discarding dirty buffers or selection.

### HARD acceptance

- Core/SQLite/WPF suites and WPF build pass.
- GUI-authored canonical content survives close/reopen and produces the expected structured Core trace; checkpoint it after closing writers per DB rules, then reopen for further writing.
- Recursive choice/action-block authoring, duplicate and undo/redo work.
- Fast-test and real-time simulation have explicit distinct labels and deterministic tests.
- Stop/error/replay do not retain queues, claims, clocks or state.
- Dirty line/profile audition plays the edited values without changing DB bytes, unrelated dirty buffers, shared consumers or the normal session. Apply shared/Make unique/Revert/undo have distinct tested results.

### Hands-on verification

With WPF running, the executor creates a conversation, types/pastes six lines, changes mood without selecting a profile again, and auditions a dirty line/profile in the simulated host. Save/reopen it and record focus/undo/context friction. Invite user feedback without adding an approval stop; Ticket 06 is the first integrated user gate.

### Commit and handoff

Commit WPF controls/services/tests/docs and a separate validated authored-content checkpoint as needed. Handoff includes the canonical conversation IDs, preview-service interface, measurements/friction and remaining bulk matrix work.

---

## Ticket 05 — Runtime Export, Unity Bootstrap, and First Visual Audition

### Goal

Load WPF-authored current content through a validated transport and close the first real edit-in-WPF/watch-in-Unity loop, using the approved proof rig at one supported state.

### Context

Unity currently constructs content from legacy ScriptableObjects and lacks the current dialogue host path. Unity should not gain a SQLite provider for this milestone. The transport must cover the whole current action vocabulary, not only demo actions.

### Contract

1. Define a versioned flat field-based runtime DTO envelope with explicit discriminators/arrays for all current definitions/actions, including recursive choices. Keep serializer adapters outside Core.
2. Export a selected scope from one consistent SQLite read using Ticket 03's shared mappings/scoped builder/validators. Include schema/transport version, deterministic content hash and independent required-asset signatures. Publishing reads committed content only; audition overlays participating buffers explicitly. Scope includes all possible candidates, not just the drawn path.
3. Write temp, deserialize/reconstruct/validate, then atomically replace the prior generated artifact. On failure retain old bytes but mark/display them stale.
4. Add complete roundtrip tests comparing semantic snapshots and action trees. Unsupported mappings fail export naming source ID/type.
5. Add Unity loader/reconstructor compatible with built-in JSON serialization constraints and a serialized binding registry keyed by semantic resource ID.
6. Generate the technical manifest on actual Unity asset/binding changes; WPF refreshes it automatically. Include kind, duration/sample rate, rig/region signature, targets/props and asset descriptors used by later intake. Validate required bindings per selected dependency set. Unchanged clips need no new manifest for a text edit; adding an unrelated asset does not stale a take. Avoid mutually dependent content/binding hashes.
7. Add snapshot-based session selection and labeled in-memory acceptance profile using existing eligibility/spawn contracts. Reachable unsupported host capability blocks preflight with an in-game error.
8. Connect the current Core performance boundary to Ticket 01's real proof renderer for one bound standing/sitting context, and add a basic text-only dialogue presenter with the existing timing contract. This is a narrow real visual bridge; full route coverage and voiced speech remain Tickets 06/07. Fake hosts remain for tests only. Restrict the visual proof's published profile/session scope to actually bound states/candidates and report other unfinished features explicitly.
9. Keep legacy SO fixtures isolated for old tests. The new playable path must not call `UnityContentGraphBuilder`.
10. Produce and smoke-test a Windows development player with WPF closed; show loaded content/binding revision and visible runtime errors.
11. Connect Ticket 04's Audition command to an already-running development host with one-time setup and Ready/Busy/Unavailable status. WPF writes an atomic local request; the host consumes it automatically, with request/hash validation, cancellation acknowledgement and obsolete-result rejection. No second Unity Run click, Save requirement or manual export. Load immutable preview JSON outside Assets, keep scene/rig loaded, and reset only disposable audition state between requests.
12. Prove dirty-line/profile audition, replay of the last immutable take, New take that changes only acting, cue pin/reset, and prompt Stop/replacement. Keep text-entry focus and report unavailable/failed connection without losing edits. Measure warm command-to-first-frame latency; target two seconds excluding authored travel/blends/import. Build packaging continues to use its separate committed JSON TextAsset path.

### Guardrails

- No direct SQLite in Unity and no hand-edited JSON content.
- Do not serialize the polymorphic `GameContentDefinition` graph directly.
- No runtime `AssetDatabase`; Editor tooling may use it only to build/validate serialized bindings.
- No sample-only parallel engine or silent omission of older action kinds.
- Do not claim persisted WPF user-profile parity.
- Generated artifacts/build outputs follow repository tracking policy; machine paths stay out of content.
- No per-line Editor import/domain reload/rebuild, manual manifest handoff, autoplay from ordinary edits, separate Unity button, or active-game snapshot mutation. Audition controls an isolated development host; production builds do not enable its receiver.

### HARD acceptance

- Core/SQLite/WPF tests plus Unity EditMode transport/binding tests pass.
- WPF export→Unity reconstruction is semantically equal for every action/definition family under test.
- Controlled WPF/Unity fake-host semantic traces match after excluding host timing/cosmetic fields.
- Stale/corrupt/partial/wrong-version artifacts and missing required bindings fail before start. Unrelated unfinished Cards/disabled unbound cues do not block local audition; an invalid included possible candidate does.
- Development player launches the authored session with WPF closed and surfaces a deliberate preflight/runtime error in UI.
- Edit and audition the same unsaved line ten times from WPF and watch the actual rig/text in Unity with no Save/export/app-switch/second-click loop. Retain context/seed, record latency and repair avoidable churn. Verify committed DB/active-session state is unchanged and the final player never packages preview buffers.

### Commit and handoff

Commit transport/exporter/loader/bootstrap/audition services/tests/docs and `.meta` in coherent changes. Handoff includes one-time connection setup, the working WPF command, proof conversation IDs, latency evidence, build commands and deferred route/audio bindings.

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
9. Extend the existing WPF audition loop to every now-bound stage state and Test arrival. Reuse the canonical conversation and profile; adding supported places must not require creating a replacement writer document. Preserve settled line audition as the fast default and test actual routes separately.

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

User edits a dirty Dialogue row, auditions it from WPF, changes only mood, tries another take and pins a cue. They also watch actual Test arrival travel through all locations, chair sit/stand, bed sit/lie/recover, gesture in standing/sitting, retained foundation, gaze suppression and blink/breath/face. Review both iteration friction and visual quality before proceeding. This is the second of the three explicit user gates.

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
2. For voice resources, complete on actual audio end and feed a precomputed amplitude envelope to mouth/jaw. For text-only lines use Core's explicit/estimated duration. Track text fingerprint when attaching voice; changed wording displays Voice needs update. Provide explicit persistent Text only audition mode for drafting. A published voiced line must have a current association or its author must clear the optional voice; no silent old-audio/new-text mismatch.
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

### Hands-on verification

Executor reviews voiced and text-only lines with all three moods, expression+mouth+blink combinations, gesture timing, gaze, subtitles and every posture/visibility through the existing audition controls. Fix material clipping/pacing failures; collect optional user feedback without an extra approval gate. Final user judgment remains Ticket 10.

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
9. Connect the recipe edit buffer to the existing WPF audition loop. Range/rate slider changes remain local until Apply; Audition uses those values. Actions with Use recipe duration follow later changes; explicit duration overrides are labelled/resettable. Replace source retains curve identity, checks dependent recipes and refreshes its plot without re-adding every action.

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

### Hands-on verification

Executor watches both motion examples at slow/fast limits, reversals, script points, entry/release and concurrent speech, then edits/reruns the recipe from WPF and replaces its script source. Fix discontinuities, desync or layer fights; record optional user feedback without another approval stop.

### Commit and handoff

Commit sampler/runtime/bindings/recipes/script fixture/tests/docs/`.meta`. Handoff includes formulas, rate display examples, script provenance/hash and approved visual ranges.

---

## Ticket 09 — Computed Coverage, Asset Intake, and Authoring Polish

### Goal

Make monthly content creation fast, understandable and enjoyable through reusable profile/cue editing, calculated coverage diagnostics and a short visual audition loop.

### Context

The basic writer/shared-buffer path shipped in Ticket 04, and real one-command audition shipped in Ticket 05. This ticket completes bulk vocabulary intake/coverage inspection and repairs remaining friction; it must not rebuild those paths or introduce a second preview protocol.

### Contract

1. Extend the existing focused Performances library/controls; retain its buffers, usage links and working audition service. Do not rewrite the main shell or create duplicate editing surfaces.
2. Profile editor: allowed states, three mood tabs, body/face queries or explicit IDs, gaze choices, None/rest weight, cadence/dwell ranges, eligible counts and example candidates.
3. Computed matrix: legal state rows; mood/channel columns; eligible count, required gap, optional None and binding status; click-through included/excluded candidates with Core rejection reasons.
4. Add route reachability and region-conflict views. They use Core results and are not editable graph/cell stores.
5. Complete bulk metadata assignment, sibling-rule copying, usage navigation, duplicate/Make unique and semantic undo. Shared Apply shows before/after affected coverage with repair links while preserving the selected cell/test context. Querying profiles include newly enabled matching cues; explicit membership stays fixed.
6. Motion editor shows normalized/seconds/zero-based-frame interval, source sample rate, derived traversal time/cycles per minute, scalar-time plot, imported points, scrub/time controls and validation after clip reimport.
7. Add intake using Ticket 05's Unity descriptors/manifest. Register selected assets once in WPF, mint semantic identities, send idempotent registry-registration requests to Unity's Editor helper and acknowledge bindings. Bulk assign author-reviewed mood/pose/tags; new cues are Not in rotation until explicitly enabled after validation. No GUID copy, duplicate name entry or invisible auto-enabling. Provide Try eligible cues for one-at-a-time visual inspection using the existing audition service.
8. Preserve dirty Card buffers, selection, focus and open nested editors through hot refresh. Improve dialogue keyboard flow based on Ticket 04 observations.
9. Write tests proving matrix/UI reuse the Core predicate, shared/fixed semantics, bulk undo, Make Unique, stale manifest/result handling and no second rule implementation.
10. Rehearse all authoring tasks from build-plan section 12 with the real GUI; record actions/time/latency/focus. Required user final acceptance is Ticket 10. Fix avoidable repeated setup, modal prompts or export/app-switch chores now.

### Guardrails

- No persisted Cartesian matrix, duplicate WPF eligibility logic, profile inheritance, arbitrary expression builder or embedded Unity/network service.
- Manifest technical fields remain read-only in WPF.
- Audition runs only on explicit user action; no live hot replacement of an active game session.
- Atomic preview files are generated artifacts and must not dirty canonical content/history.
- Catalog refresh cannot erase unsaved Card edits.
- Do not declare the authoring workflow pleasant solely from automated tests.
- Do not make Save a prerequisite to audition or force a full library validation. Do not add a generic importer, template editor, draft database or replay framework to implement these conveniences.

### HARD acceptance

- Full .NET/WPF suites pass; Unity audition tests pass.
- Every matrix count/rejection matches direct Core eligibility calls.
- Stale/corrupt/mismatched request/response/manifest cannot show a current success.
- Reopen retains editor state/content; all bulk/shared/unique operations undo and redo with exact IDs.
- Adding one matching cue updates two profile consumers without editing their cards; removing sitting support identifies all affected required cells/actions.
- Register/retry/reimport a sibling clip with no duplicate semantic identities; new disabled clips cannot affect live Auto pools. Enable validates and updates counts. Replacing a required asset/curve invalidates only affected previews/recipes and leads to the relevant repair control.
- Shared-draft audition leaves DB/other Cards unchanged, Apply affects consumers intentionally, and Make unique/Revert/undo restore exact identities. A valid selected audition works while unrelated disabled/unbound vocabulary remains unfinished.

### Hands-on verification

Executor performs the complete revised authoring tasks in build-plan section 12, including dirty-buffer audition, ten edit/watch repetitions, shared experiment/Apply/Make unique, cue intake, scoped errors, script replacement and committed export. Record timings and friction and repair material issues. User may review now, but a fourth pre-final approval gate is not required.

### Commit and handoff

Commit the intake/matrix refinements/tests/docs/Unity Editor binding helper and `.meta` files in coherent commits. Handoff includes measured workflow, remaining friction and the same canonical conversation for Ticket 10 completion.

---

## Ticket 10 — Acceptance Content, Canonical Checkpoints, Built Player, and Final Handoff

### Goal

Ship and prove the first playable 3–5 minute performance session, then leave both implementation and authoring workflow ready for continued content creation.

### Context

This ticket completes the canonical conversation begun in Ticket 04, after Ticket 03's migration checkpoint. It must not begin the animation asset workload or require re-entering already-authored content. Verify the recorded migrations/content checkpoints before proceeding.

### Contract

1. Re-read the entire packet and audit each architecture invariant/exclusion against code and content. Resolve drift before canary.
2. Verify earlier migration code/DB checkpoints and their backups. Close writers only for the final backup/Git content checkpoint, confirm no WAL/SHM, create/hash a fresh backup, and run integrity/FK/ledger/semantic checks. If intervening tickets added another migration, test/commit its code first and checkpoint canonical migration separately through the real migrator. Do not reapply/rebuild canonical content to satisfy a stale ticket instruction.
3. Complete the existing acceptance session in WPF, preserving its IDs/authored lines. Include six locations/tour, at least twelve lines, three voiced lines, Happy/Neutral/Mad, choice branch, meaningful DifferentLocation beat, Continue ambience, arm/prop oscillator motion, pelvis recorded-script motion, chair/bed poses, all player postures and both visibility states through an optional test path. Validate/checkpoint the meaningful authored batch, then reopen WPF for iteration.
4. Meet the minimum asset/content budget in build-plan section 15: required foundations/transitions, at least six body gestures including nod/point/shimmy/Mad hands-on-hips, two faces per mood, actual room/rig/player/audio/motion assets and bindings.
5. Export a fresh validated runtime snapshot/binding manifest and build a Windows development player. Prove it runs with WPF closed from a clean generated state.
6. Execute every acceptance play-path step in build-plan section 15, first seed and alternate seed. Record semantic traces, visible variation, errors and replay cleanup.
7. Run the complete automated matrix: solution tests, WPF build, Unity EditMode/PlayMode, batch compile, built-player smoke, transport/binding freshness, canonical integrity/FK/schema/content semantic checks and `git diff --check`.
8. Run visual review for foot slide/contact/penetration, facial speech composition, gaze fights, pops, cadence/overgesture, camera/body clipping and motion extremes. Fix material defects and rerun affected gates.
9. Run final authoring canary: user creates a conversation from the configured starter, types/pastes lines, changes mood/destination with inherited profile, auditions unsaved edits, replays/varies/pins a cue, tries/reverts a shared edit, registers/enables one gesture and replaces a script source. Measure ten warm edit/Audition cycles (two-second target excluding authored movement/blend/import), verify no manual Save/export/Unity-button choreography, then publish committed data and play the build. An unrelated unfinished item cannot block the valid local selection; a broken included dependency must. Do not retype content into another database.
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
- One-command warm audition, controlled buffer overlays, partial updates, scoped validation and asset registration have measured hands-on evidence. No studio setup/export/version negotiation is required per line. Build export cannot include unsaved buffers accidentally.

### FINAL HUMAN acceptance

User plays the complete acceptance session and authors/exports/plays a small new conversation. User judges both game feel and authoring flow ready as the first production foundation. Keep WPF and the relevant Unity scene/player available for inspection until the user responds.

### Commit and final report

Use separate canonical migration and authored-content commits as required, followed by final integration/docs fixes. Do not push unless explicitly authorized. Deliver the report required by `ORCHESTRATION-PROMPT.md`, including exact build path and running process/editor state.
