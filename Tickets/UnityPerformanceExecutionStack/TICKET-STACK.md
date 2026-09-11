# Procedural performance ticket stack

This revision replaces all prior tickets in this file. Read ARCHITECTURE-CONTRACT.md before implementation. Tickets describe required behavior, not completed work. Execute in order; independent work may proceed around an explicitly recorded asset dependency.

## Shared instructions for every ticket

Use existing graph/action/content architecture. Physical portable sources live under Assets/Scripts/Portable; DotNet projects link them. Inspect current code and list actual files before editing. Preserve Card eligibility, consent/capability checks, tagged-dialogue selection, Continue, choices and graph transfers.

Unity owns ingredients and compatibility metadata. WPF owns event rules/cards/dialogue/curves. Core performs selection. No new authoring store for sampled combinations or duplicated ingredient metadata.

Every new action must reach models, clone/equality, Card buffers, action catalogs, scope validation, executor, persistence, export and both hosts. Do not assume adding an enum makes an action supported. Preserve Action Block insertion.

Tests must verify behavior and failure boundaries. Use real Unity evidence for animation acceptance. Do not invent assets, claim simulations as visual verification, add dependencies or change the pinned Unity version without authorization. Follow database checkpoints, .meta/LFS rules and app-launch requirements in agents.md.

Commit coherent completed milestones; keep README accurate. The packet's execution authorization can cover larger coherent file batches and removes repeated routine “go” gates, as described in the orchestrator prompt.

## 00 — Establish source baseline and concrete proof fixtures

**Depends on:** none.

**Context:** The existing project already supplies Card selection, graph execution, WPF editing and SQLite content. This is an extension. Source-only archives and older plans may describe obsolete paths.

**Work:**

- Inspect current README, GraphWorkbench docs, portable ActionExecutor, host interfaces, BackgroundActionTracker, GameSessionEngine, snapshot loader/validators, SQLite mappings, WPF Card buffers and Unity GameManager/bootstrap.
- Record exact current test/build commands, schema version and action discriminators. Establish passing baseline or name existing failures.
- Inventory actual rig, clips, facial channels, props, audio and Unity tools. Distinguish installed packages from usable assets.
- Define a small test Session, representative Cards, dialogue tag pools and initial presentation context. Use existing graph semantics; do not generate a graph per Card.
- Identify source extension points and propose only needed files. Record asset gaps and acceptance evidence locations.

**Guardrails:** No broad refactor, dependency update, asset purchase or automatic sample-content overwrite. Do not alter canonical DB just to get a clean baseline. Checkpoint before later migrations.

**Acceptance:** Reproducible baseline and inventory; missing assets assigned concrete requirements; fixtures cover ordinary card execution, expression refresh, later relocation and a finite driven event. Planning evidence distinguishes authored fixtures from assets still required.

**Handoff:** Source map and baseline results for 01–03.

## 01 — Prove the rig and author ingredients in Unity

**Depends on:** 00.

**Context:** Compatibility belongs beside animation assets. A WPF animation registration editor would create duplicate work and unreliable facts.

**Work:**

- On the actual rig, prove a foundation plus masked body expression, face, gaze, blink/breath and a reversible path involving pelvis/body or a prop. Check both sampling directions and local/world-root separation.
- Add focused Unity ingredient assets/registry with stable semantic IDs, display names, enabled state, tags/moods, legal states, requirements, occupied regions, timings and Unity bindings.
- Add stage descriptors for six named locations and nine legal states. Build enough real bindings for the initial rig proof; finish routes in 06.
- Define the small portable PresentationCatalog DTO boundary and atomic generation outside Assets. Include required-binding signatures and schema version, no Unity objects/coordinates in Core.
- Provide simple Unity inspection/play controls for asset verification. Auto-regenerate descriptors after valid asset changes.
- Validate duplicate IDs, broken enabled bindings, bad windows and inconsistent metadata. Duplication gets a fresh semantic ID; reimport retains identity.

**Guardrails:** No SQLite ingredient tables, second registration form, free-text ID copying, generic clip import framework or card choreography UI. Mark unfinished ingredients disabled. No blanket assertion that retargeting makes every pose compatible.

**Acceptance:** Real-rig visual proof, generated portable catalog, identity/validation checks and a documented asset shortfall if proof cannot pass. An invalid enabled ingredient reports its name and reason. A disabled draft does not join selection.

**Handoff:** Verified graph/rig constraints, catalog fixtures and remaining content requirements. Independent Core work may proceed if assets block visual proof; 10 cannot pass without it.

## 02 — Implement the portable procedural director and actions

**Depends on:** 00 and catalog contract from 01.

**Context:** ActionExecutor currently risks treating always-blocking activity as graph transfer. Persistent ambience must not become an endless background action.

**Work:**

- Add PerformanceEventDefinition for Conversation/Driven motion, typed queries/policies, semantic state, presentation request IDs and the host contract.
- Add Perform, Refresh expressions and Set Player State. Separate control-transfer classification from action blocking. Define readiness separately from finite completion.
- Implement session-scoped director using host ticks/events: candidate filtering, joint compatibility/region constraints, directed least-cost routes, stable tie ordering and independent RNG domains.
- Support Keep/Set mood; Stay/Choose compatible/Different location/Named destination; allowed pose/location filters; event overrides; explicit optional body rest.
- Implement entry/dialogue/idle refresh policy, dwell and immediate anti-repeat when alternatives exist. Sample only valid dependent choices. No per-frame RNG.
- Track desired versus committed state, claims and stale acknowledgements. Preserve state across Cards/GOTO/RETURN.
- Specify and implement finite motion/speech ordering, pause, cancellation, teardown and fatal fault reporting. Persistent ambience stays outside WaitForAll.
- Supply a deterministic fake presentation host for meaningful Core tests.

**Guardrails:** Core contains no Unity/WPF/SQL dependency. No stored sampled performance, separate narrative VM, new selector policy or silent fallback. Context changes wait for accepted speech and finite motion. A second overlapping driven event fails explicitly.

**Acceptance:** Tests prove reachability, Different location, mood/region/prop constraints, empty coverage errors, legitimate rest, refresh and seed independence. Delayed/failed/stale host acknowledgements never commit false arrival. WaitForAll terminates with ambience active; cancellation/error cannot strand a run. Existing control-flow tests still pass.

**Handoff:** Working simulated director/action path and stable DTO/domain shapes for 03–07.

## 03 — Persist event rules and export scoped game content

**Depends on:** 02.

**Context:** SQLite remains canonical game content, while Unity's catalog is generated read-only input. Existing global snapshot validation must not prevent testing a valid Card because an unrelated draft is incomplete.

**Work:**

- Migrate typed event/query/curve storage and new action fields using existing provider conventions. Inspect actual schema and checkpoint canonical content before/after migration.
- Persist event references/overrides, session initial presentation context and optional dialogue voice/timing. Round-trip nested choices and Action Block inserted actions.
- Validate cross-store catalog references explicitly; do not duplicate Unity ingredients to manufacture database foreign keys.
- Implement conservative scoped reconstruction/export for a selected Card/Session or published Session set. Include reachable graph branches, possible tag candidates and enabled presentation candidates regardless of chosen seed.
- Reuse typed reconstruction and Core validation; separate authoring diagnostics from execution errors before the global fail-fast loader.
- Add explicit versioned field DTOs/arrays for all current actions and content. Reconstruct and validate on import. Use existing framework/built-in serializers without adding a Core serializer dependency.
- Publish atomic committed development snapshots outside Assets. Track snapshot freshness/export errors separately so a consumer cannot mistake an older valid snapshot for current content.
- Add automatic regeneration after committed changes with batching. Ordinary export uses a read transaction while WPF stays open; backups/Git checkpoints follow closed-writer rules.
- A valid Unity catalog refresh also regenerates the selected snapshot from committed content, incorporating new candidates without requiring a dummy Card Save or exporting dirty buffers.

**Guardrails:** No runtime SQLite dependency in Unity merely to load this slice; no silent action omission or empty replacement content. No export limited to candidates observed in one run. No manual copy/export procedure per edit.

**Acceptance:** Persistence/transport round-trips new and existing actions, choices and IDs. Included broken references fail; unrelated incomplete drafts do not block scoped tests. Corrupt storage fails visibly. Interrupted export never exposes partial JSON; failed current export marks the previous snapshot stale. DB integrity/FK checks pass.

**Handoff:** Valid fixtures and generated game snapshot usable by WPF simulation and Unity. Curve point import is completed in 08.

## 04 — Author event rules and inspect coverage in WPF

**Depends on:** 01–03.

**Context:** The writer chooses reusable rules and writes cards. The matrix is derived coverage, not a list of authored animation combinations.

**Work:**

- Add an Events library and Perform event picker within existing authoring surfaces. Provide simple Conversation/Driven forms with searchable mood/location/pose/tag choices and effective override display.
- Add new action rows and integrate Card clone/equality, Save/Revert, semantic undo, Action Block insertion and scope rules.
- Show computed legal-state × mood coverage: required base/face availability, optional body candidates/rest and motion candidates. Provide candidate names and exclusion reasons, including reachability and region requirements.
- Reuse the production resolver for coverage queries so UI and runtime cannot disagree. Show a single eligible choice as limited variety, not a failure.
- Reload the read-only Unity catalog automatically while retaining dirty Card buffers, selection and focus. Missing catalog produces a local diagnostic without breaking unrelated editing.
- Support ordinary transactional event editing, where-used and duplication for an intentional independent variant. Keep text/number changes grouped sensibly in undo.
- Add new action simulation to the WPF Reference Player using the same director. Clearly identify simulated presentation.
- Keep routine dialogue insertion quick; only add Ctrl+Enter or paragraph paste if it fits existing editor commands simply.

**Guardrails:** No WPF clip/mask/window editor, per-line face selector, saved results, shared experimental buffers or generated conversation graphs. Matrix cells alter event filters only. Do not require a writer to edit all moods/locations individually when a shared query suffices.

**Acceptance:** Create a reusable event, use it in multiple Cards, change one policy and see all users' effective behavior. Save/Revert/undo and block insertion retain all fields correctly. Catalog refresh preserves unsaved text. Simulated execution explains valid choices and genuine failures without inventing presentation.

**Handoff:** Authorable Cards/events and automatic saved snapshots for early Unity playback.

## 05 — Play real authored Cards in a loaded Unity scene

**Depends on:** 01–04.

**Context:** Unity must consume actual exported content and the production Core pipeline. A legacy ScriptableObject sample deck or custom demonstration coroutine is insufficient.

**Work:**

- Add validated snapshot reconstruction/bootstrap and Unity binding checks. Load current game content and the required catalog descriptors, with useful mismatch diagnostics.
- Add a small development panel to select Card/Session and starting test context/profile, then Play, Stop or Repeat.
- Test Card uses disposable game state, normal eligibility/consent/capability checks and production action/director execution. Unsupported enclosing graph transfers report “Test in session.” Session tests use the graph VM.
- Repeat resets run state, reloads the latest committed snapshot and starts with fresh presentation randomness. Optional fixed seed is diagnostic only.
- Keep scene/rig loaded between runs. Distinguish a current snapshot, a failed export and stale data in the panel. No silent use of outdated rules.
- Connect actual verified foundation/expression bindings from 01 and basic dialogue display. Explicitly mark movement/voice/driver work still awaiting 06–08.

**Guardrails:** No WPF-to-Unity cue queue, remote actor API, selected-line seek, unsaved overlay snapshot or take storage. Development transport stays outside Assets. Do not bypass unsupported actions to make a test appear successful.

**Acceptance:** A WPF-authored Card runs through real Core in Unity. Saving text then pressing Repeat displays the new text without manual import/reload. Stop/repeat leaves no prior work active. A context-dependent Card directs the author to a Session test. This milestone does not claim full visual completion.

**Handoff:** Working early integration loop; subsequent tickets improve the same runtime, not a parallel demo.

## 06 — Complete movement and skeletal layer composition

**Depends on:** 05.

**Context:** Core names legal states/routes. Unity owns anchors, calibration and evaluation. Pose alternatives share a foundation channel.

**Work:**

- Bind all six locations and nine legal states. Implement sixteen directed edges using shared assets where appropriate, with authored costs.
- Add one world-root mover using in-place travel and calibrated pose transitions. Core requests routes; Unity reports arrival only after tolerance and settling.
- Implement one fixed-topology actor PlayableGraph: foundation, expressive overlay, driven channel reservation and verified additive breath. Map logical claims to actual masks.
- Apply transition > driven > expressive > ambient priority. Suppress/yield conflicting gaze/breath and permit disjoint channels.
- Support body/face queries after arrival and legal refresh; no separate Animator/Timeline per combination.
- Add watchdogs, cancellation and correlated completion for travel/pose changes.

**Guardrails:** No teleport fallback, automatic reverse routes, generalized NavMesh or all-upper-body restriction. Pelvis/local pose motion must not move the world root independently. Numeric arrival does not prove furniture contacts.

**Acceptance:** Actual character walks between locations, sits/lies/stands and settles within 2 cm/3 degrees. Inspect foot/furniture contacts. Repeated event runs generate only legal combinations; movement faults fail visibly without false committed state. Graph resources do not accumulate across repeats.

**Handoff:** Real movement and layer evidence plus any asset-specific limits requiring correction before 10.

## 07 — Finish dialogue, face, gaze and player presentation

**Depends on:** 06.

**Context:** Lines select content through existing actions. Acting comes from active procedural rules; no line-directed animation fields.

**Work:**

- Deliver structured line requests with text, identity, optional voice and explicit timing. Provide subtitles and deterministic text-only duration/Continue behavior consistent with action settings.
- Use one facial composer for expression, speech mouth and blink with explicit channel ownership. Basic amplitude mouth movement is enough; no phoneme/TTS project.
- Evaluate head/eye gaze after body animation; respect head-owning gestures and named Unity targets.
- Ensure dialogue start triggers the active event's expression policy with minimum automatic dwell and explicit-refresh behavior.
- Implement four player postures and independent body visibility without attaching gameplay mutations.
- Finish FIFO speech behavior for nonblocking lines and context changes. Pause freezes clocks/audio; Continue/choice waits retain ambience.
- Audit end/cancel/fatal error/unload cleanup across all services, including background faults while waiting for user input.

**Guardrails:** No exact acting metadata on dialogue, lip-sync service dependency or accidental wait on endless ambience. Hidden player body is visibility state, not a duplicated posture catalog.

**Acceptance:** Two selected lines run with procedural expression change, gaze/blink/breath and optional voice. Mouth/face channels do not fight. Nonblocking speech order is stable. All player posture/visibility combinations work. Pause/resume/cancel/Continue and fatal failure leave no hung action or orphan audio.

**Handoff:** Complete conversational playback and lifecycle tests, ready to coexist with finite motion.

## 08 — Implement oscillator and imported scalar motion

**Depends on:** 07; persisted curve shape from 03.

**Context:** The reusable Unity ingredient is a reversible path. WPF specifies how to drive it, not the skeletal animation.

**Work:**

- Add Unity usable-window inspection and manual sampling, with body/prop binding as one ingredient where needed.
- Finish WPF Driven event controls: semantic motion query, scalar min/max, finite duration, entry/exit blend, oscillator half-cycle duration range or recorded curve.
- Implement the contract's scalar-to-window mapping and endpoint-seeded smooth oscillator. Use absolute clock progress and catch-up guards.
- Import the defined single-axis funscript subset with strict timestamp/position validation and useful source errors. Persist immutable points/hash; replacement preserves ID and validates users.
- Use linear curve interpolation and defined endpoint holds; apply inverted once. Do not silently sort, clamp or smooth malformed source.
- Keep conversation rules by default; driver claims take priority and compatible expression/gaze/dialogue continue.
- Start driver time after readiness/entry blend; finish after exit/release. Nonblocking registers only finite completion.

**Guardrails:** Primary rate is seconds per one-way traversal. Equivalent source frames/second is explanatory, not another setting. No animation-event gameplay, world-root extraction, device control, multi-axis expansion or infinite driver task.

**Acceptance:** Test timing at unequal frame intervals, scalar bounds, invalid imports and replacement. Visually demonstrate both prop movement and pelvis/body involvement, oscillator and curve. Dialogue and legal disjoint layers coexist. WaitForAll completes; overlap conflict, cancellation and exit restore valid ambient ownership.

**Handoff:** Both procedural event flavors work through authored Card actions.

## 09 — Prove and simplify the recurring content workflow

**Depends on:** 08.

**Context:** This is a workflow acceptance/fix ticket, not permission to add a second preview product. Monthly content must reuse rules and ingredients without repetitive registration or choreography.

**Work:**

- Perform the writer loop: use an existing event in a new Card, add/select dialogue, Save, then Play/Repeat in the loaded Unity scene. Fix any manual transport, repeated setup or lost-edit friction.
- Change a shared event once and verify its Cards inherit it. Duplicate only when a distinct reusable rule is intended; confirm where-used and undo remain clear.
- Perform the artist loop: add a compatible ingredient in Unity, tag/validate/enable it, regenerate automatically, then repeat an existing Card. Make zero edits to its event/Card.
- Verify incomplete unrelated drafts remain editable without blocking scoped testing. Verify broken included bindings/export failures give actionable names and cannot silently play old content.
- Run repeated seeded Cards to measure valid candidate coverage and immediate anti-repeat. Inspect visual coherence across states/moods. Do not assert “infinite” mathematical variety.
- Simplify redundant controls and setup revealed by these tests. Write short writer and artist instructions based on the actual UI.

**Guardrails:** No audition/take/pin controls, saved random results, exact per-line overrides, shared-draft overlays or remote cue protocol as a remedy for workflow friction. Fix ownership, defaults and automatic committed transport.

**Acceptance:** Demonstrate both loops with evidence. Existing Cards gain a new ingredient with zero edits. Typical additional dialogue requires no Unity asset work. Unity Repeat needs no scene reload or export/import dialog. WPF catalog refresh preserves dirty Cards. The workflow instructions match implemented behavior.

**Handoff:** Proven authoring loop and remaining actionable defects for final acceptance.

## 10 — Deliver and play the full Unity slice

**Depends on:** all previous acceptance requirements.

**Context:** Completion means a playable card game and repeatable content workflow, not just a hand-triggered animation showcase.

**Work:**

- Assemble a small real Session using existing Card selection and graph flow, with multiple eligible Cards/dialogue alternatives and reusable events.
- Demonstrate movement, settle, mood-compatible expression, two dialogue deliveries, refreshed expression and later relocation. Include Continue/choice, inherited state and player presentation.
- Include oscillator and imported-curve examples with compatible dialogue and a pelvis-owning path.
- Package a fresh validated game snapshot and Unity registry into a standalone Windows build. Validate required bindings at build/startup; no external development paths or file receiver in production.
- Run meaningful Core/persistence/host tests and actual editor/build checks. Verify fresh startup, replay, shutdown, cancellation and errors.
- Complete missing real assets and visual proof; retain truthful limitations in delivery notes.
- Provide scene/build paths, short controls/authoring guide, content location, commands/results and visual evidence. Leave the relevant app running.

**Guardrails:** Do not replace the selector with a fixed demonstration script. No “done” while real-rig, standalone or workflow acceptance is unverified. Do not expand into distribution, arbitrary rigs or generalized narrative tooling.

**Acceptance checklist:**

- A player can start, draw/play cards, Continue/choose and finish/replay.
- Actual character moves to valid locations, adopts poses, combines expression layers and speaks selected dialogue.
- Repeated execution of the same Card produces observed valid alternatives.
- Both scalar drivers work, including pelvis/body ownership.
- A normal WPF Save reaches the next Unity run automatically.
- A compatible Unity ingredient expands existing events with no Card/event edits.
- Standalone build runs with packaged content.
- Tests, visual evidence and authoring instructions describe the shipped result accurately.
