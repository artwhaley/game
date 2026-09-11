# Conversation Performance V1 — execution tickets

Current source-audited packet, September 11, 2026. Six tickets, numbered 00–05. Read ARCHITECTURE-CONTRACT.md for decisions. These are future implementation tasks; this packet revision changes documentation only.

## Instructions applying to every ticket

Read agents.md, root README, current source and the relevant GraphWorkbench/content database documentation. Inspect exact files before edits and announce the concrete file batch. Portable model/Core source lives under Assets/Scripts/Portable and is linked into DotNet. Reuse Game.Content.Sqlite rather than copying its schema or mapping.

Preserve selection, consent/profile/capability checks, Happiness/temperatures, dialogue RNG, action scopes, GOTO/RETURN/EndSession and explicit Continue. Follow database checkpoints, dependency approvals and .meta/LFS requirements. Do not overwrite canonical content with a test initializer.

Only Perform(eventId) is new public performance vocabulary. No public refresh/player action, mood enum, driver/curve schema, game-content JSON, content publication protocol, performance timeline or saved random result.

Run meaningful targeted tests and launch/leave the affected main app running after app changes. Record automated evidence separately from visual checks and unmet dependencies. Current packet approval can authorize coherent batches larger than five files and remove repeated routine “go” requests under agents.md rules 2/5; it does not waive other safeguards.

## 00 — Verify canonical SQLite integration and correct action classification

**Context:** README and UnityContentGraphBuilder explicitly point to the same SQLite schema. Game.Content.Sqlite is netstandard2.1/provider-neutral. Unity currently has no provider/bootstrap. ActionExecutor routes IsAlwaysBlocking to ReduceFlow with exceptions for WaitForAll/PromptChoice.

Game.Content.csproj and Game.Core.csproj compile the same physical Assets/Scripts/Portable sources that Unity already compiles; Game.Content.Sqlite.csproj references those DotNet projects. Importing their compiled Game.Content.dll/Game.Core.dll alongside those Unity sources would create competing CLR type identities.

**Dependencies:** execution authorization; dependency approval if a provider must be added.

**Source starting points:** DotNet/Game.Content.Sqlite/GameContentSnapshotLoader.cs and its project; Assets/Scripts/Game/GameManager.cs; ActionExecutor.cs, ActionTypeRegistry.cs and GameSessionEngine.cs under Assets/Scripts/Portable/Game.Core; Packages/manifest.json; current action/control tests.

**Work:**

- Record actual schema, relevant build/test commands and existing failures; do not repeat historical counts as fresh results.
- Prove a supported SQLite DbConnection provider loads a disposable database using the existing mapping under pinned Unity 6000.5.9f1. Reuse/link the existing Game.Content.Sqlite mapping source or establish a clean build arrangement that retains exactly one CLR identity for each TruthCardGame.Content/Core type. Reuse the schema/mapping implementation without copied SQL, duplicate portable source or a second loader; preserve the existing DotNet test projects.
- Do not import duplicate compiled Game.Content.dll/Game.Core.dll alongside Unity's existing portable sources. Record the selected assembly/source arrangement and verify the SQLite loader and Unity/Core consumers resolve the same types.
- Verify Load(connection, ensureSchema: false), schema checks, consistent read transaction and connection release. Demonstrate WPF committing while Unity reloads at the next run boundary.
- If a new dependency is required, present the concrete choice and why for approval. If integration fails, record exact reproduction and stop that path for review. Continue independent work only; do not substitute JSON.
- Add explicit action execution classification distinguishing Activity from ControlOrYield. Mark WaitForContinue, GOTO, RETURN and EndSession accordingly; blocking PromptChoice/WaitForAll remain activities. Route ReduceFlow by classification, not exceptions.
- Preserve the existing persistent toy action semantics; do not redesign all background work.
- Inventory usable rigs/animations and define a minimal real test Session/Card using the normal selector. Two anchors and standing/sitting suffice.

**Guardrails:** No performance engine yet, no framework upgrade, no content exporter, no fake claim that netstandard compatibility proves the native provider works.

**Acceptance:** Classification tests preserve every existing control/yield behavior and blocking activities execute normally. Actual Unity SQLite smoke evidence exists, or a precise unresolved provider blocker is recorded. Canonical DB remains protected. Baseline records list available assets and missing requirements.

Type-identity acceptance requires a clean pinned-Unity compile plus an editor integration test that loads through GameContentSnapshotLoader and passes its returned GameContentDefinition directly into the Unity-consumed GameSessionEngine. Assert exact Type equality between the loader's declared return type, the relevant engine constructor parameter and Unity's typeof(GameContentDefinition). Inspect loaded assembly type definitions for duplicate full names in TruthCardGame.Content and TruthCardGame.Core and fail on any duplicate or reflection-load failure. This must expose competing definitions even if a compiler warning or a successful isolated loader test would otherwise hide them. Run the existing DotNet test projects to prove the integration arrangement has not broken their source/project references. An unresolved provider or type-identity failure leaves ticket 00's integration acceptance incomplete.

**Handoff:** Proven or explicitly blocked content path; source map; concrete rig requirement. The first visual milestone cannot pass while SQLite loading is blocked.

## 01 — Genuine rig spike and minimal presentation catalog

**Context:** No rig assets were found in the inspected Assets tree. Technology and asset feasibility must precede a fixed mixer architecture.

**Dependencies:** 00 inventory; actual or representative rig. Final SQLite-backed tag picker follows 03; spike fixtures must not become an editable Unity tag vocabulary.

**Source starting points:** Assets/Scripts/Game, existing Unity host/binding conventions, Packages/manifest.json, Assets/Scripts/Portable/Game.Content.

**Work:**

- On the representative rig, demonstrate standing/sitting foundation, travel/pose change, one masked body gesture, simple facial preset and player gaze together.
- Start with Animator/layers. Test targeted Playables or Animation Rigging only when needed, respecting dependency approval. Record why the selected approach works.
- Measure contact/arrival tolerances on that rig; verify an ordinary gesture returns cleanly to foundation and whether head ownership needs an exception.
- Build a small ingredient registry with sparse defaults as defined in the contract. Auto-create IDs/names, infer technical clip data and supply a standard verified gesture mask.
- Define factored anchor capabilities and reusable transition bindings. Share Stand/Sit operations across compatible anchors; no authored Cartesian state graph.
- Generate a versioned read-only PresentationCatalog atomically. Include semantic ingredient IDs, compatibility and capabilities; keep Unity coordinates/bindings local.
- Allow disabled unfinished ingredients. Reject duplicate IDs/broken enabled bindings. Stub vocabulary fixtures are temporary test inputs; the real tag editor remains in WPF.

**Guardrails:** No mandatory PlayableGraph topology, driver channels, general region machine, full metadata questionnaire or Unity-owned semantic tags. No large custom asset browser.

**Acceptance:** Actual rig evidence proves the selected approach. Registry defaults are documented. Adding a second equivalent anchor reuses transition operations. The catalog can be consumed by portable tests. Missing assets remain explicit blockers to visual acceptance.

**Handoff:** Chosen rendering approach, rig constraints, catalog fixture and calibrated reusable operations. Remove spike-only paths when integrated.

## 02 — Minimal Core planner, Perform and async host boundary

**Context:** Core already issues async service calls. Dialogue uses IDialogService, and tagged selection occurs before awaits. V1 needs semantic planning and event boundaries, not continual Core updates.

**Dependencies:** 00 classification and 01 catalog shape.

**Source starting points:** GameContentDefinition, ActionInstances/ActionTypeKeys; ContentCatalog, ContentReferenceValidator, CoreServices, ActionExecutionContext, ActionExecutor, GameSessionEngine; corresponding Core tests.

**Work:**

- Add Conversation Performance Event and Performance Tag model definitions, plus the sole Perform(eventId) action with always-blocking activity classification.
- Implement factored (anchor, posture) state and reusable operation search. Filter viable destination/foundation/face/body combinations before dispatch. Use the contract's explicit body-rest default and mandatory face coverage.
- Add IPerformanceHost and a session-scoped planner. Await readiness, correlate responses and retain desired versus committed state. Keep presentation state across Cards and graph transfers.
- Select semantic ingredients at Perform and policy-enabled dialogue starts. Use a dedicated deterministic RNG, stable ordering and immediate expressive anti-repeat where alternatives exist.
- On blocking Direct Dialog and Dialog From Tags, ask the active director to select and await acceptance of expressive acting immediately before IDialogService.ShowAsync when RefreshAtDialogueStart is enabled. Tagged snippet selection remains synchronous before host awaits. Prove this sequence with the fake performance/dialogue hosts and representative Card.
- Preserve current nonblocking dialogue behavior and RNG ordering; defer exact refresh synchronization to later queued visual presentation. Do not add presentation-start callbacks, stale dialogue callback generations or queue coordination. If preserving existing behavior requires a tiny callback seam, stop and document the concrete case for review before expanding the contract.
- Keep continuous blends, gaze/IK and ambience entirely in Unity. Core has no delta-time API, per-frame timers, cadence loop or speech queue.
- Integrate stop/failure handling with existing lifecycle. Persistent presentation is outside WaitForAll; one failing cleanup service cannot skip the others.
- Implement a fake host and readable decision diagnostics.

**Guardrails:** No per-action overrides, persistent mood, public reroll, player action, future-driver abstractions or saved chosen combinations. Missing required performance service is an error when Perform executes; unrelated existing non-performance behavior remains compatible.

**Acceptance:** Tests cover stay/different/named destination, composed stand–move–sit, missing operations, compatible combinations/rest, no valid face, seed independence and automatic blocking-dialogue refresh. For both dialogue actions, assert refresh acceptance precedes ShowAsync; for tagged dialogue, assert snippet selection precedes host awaits and consumes the existing dialogue RNG unchanged. Disabled refresh/no active event does not request acting. Failed/canceled/stale performance acknowledgements never falsely commit arrival. Existing graph/dialogue tests pass, including nonblocking behavior and selection ordering; exact queued visual-start refresh is not a V1 acceptance requirement. WaitForAll completes while presentation remains active.

**Handoff:** Minimal tested semantic planner and host contract, with no simulation mistaken for visual correctness.

## 03 — Persist and author tags/events through existing WPF/SQLite

**Context:** Existing repositories, relation chips, undo and Card buffers already solve content editing. Performance vocabulary belongs here, while ingredient membership remains in Unity.

**Dependencies:** 02; 00 content path for Unity tag consumption.

**Source starting points:** DotNet/Game.Content.Sqlite migrations, CatalogRepositories/DialogCatalogRepository, GameContentSnapshotLoader, ActionSequenceWriter and clone/reuse helpers; WPF ActionEditorRegistry, GraphViewModels, CardEditBuffer, ActionInstanceCloneUtility, RelationPickerControl and Reference Player.

**Work:**

- Add typed Performance Tag/Event tables/relations and Perform subtype mapping. Extend the existing snapshot, validators and catalog indexing; no parallel transport model.
- Complete the action's full vertical path: scopes, defaults, recursive copy/clone/equality, Save/Revert, undo, Action Block insertion and persistence.
- Add a simple reusable event form: name, ALL/ANY tags, staging policy, optional posture/location constraints, automatic dialogue refresh default on.
- Add separate Performance Tag catalog editing with stable IDs, rename and retirement. Do not merge Card/Dialog namespaces. Protect referenced identities and show known Unity usage.
- Connect Unity tag pickers to the same DB using automatic refresh. Connect WPF to generated presentation descriptors without replacing dirty Card buffers.
- Add simulated performance service to the existing WPF Reference Player and decision logs showing state, destinations, operations, choices/exclusions. Read-only Nodify visualization is optional if cheap.
- Author the representative event, ordinary Card and minimal Session through WPF, following canonical DB checkpoint rules. Use normal dialogue snippets and existing profile rules.

**Guardrails:** No coverage dashboard prerequisite, custom draft framework, animation editor in WPF, tag definition editing in Unity, global-validation suppression or publication system.

**Acceptance:** Typed round-trip and action/editor tests cover all new fields and nested insertion. A WPF-authored event runs through real Core simulation. Tag creation/rename appears in Unity without copied IDs. Catalog refresh preserves dirty edits. Ordinary Card Save updates SQLite; no generated game-content file is required.

**Handoff:** The actual authored DB content and same planner ready for Unity integration.

## 04 — First vertical proof: play a real Card in Unity

**Context:** GameManager still builds a snapshot from legacy ScriptableObjects and does not supply IDialogService. Replace the test entry path with actual SQLite loading and production Core execution.

**Dependencies:** 00–03, including resolved SQLite and rig prerequisites.

**Source starting points:** GameManager, GamePanel and Unity host services; shared snapshot loader; IPerformanceHost; existing GameSessionEngine/SessionGraphVm.

**Work:**

- Implement the selected rig strategy behind IPerformanceHost: execute factored operations, stage foundation, body overlay, face preset and player gaze. Report readiness only after requested staging is coherent.
- Wire the SQLite snapshot into a real Session run, passing normal profile/eligibility inputs. Do not use a demonstration coroutine to choose Cards or ingredients.
- Implement text dialogue via IDialogService and the Core blocking-dialogue refresh sequence from ticket 02. Use ordinary blocking dialogue for both representative lines, visibly testable with host acknowledgement; preserve the separate graph Continue.
- Add a small Play/Stop/Repeat Session panel with existing seed/profile selection and known starting anchor/posture/player target. A one-eligible-Card Session supports convenient repetition.
- Repeat disposes the old run, reads committed SQLite content and current catalog, resets the start and reruns in the loaded scene. Close DB reads before animation begins.
- Test cancellation during travel, late acknowledgements, faults after readiness and end/unload. Persistent acting survives Continue-wait and ends with the run.

**Guardrails:** No new Card interpreter, remote WPF cue controls, JSON export/import, voice/lipsync, player-state variants, elaborate idle cadence or standalone build work.

**Acceptance — first architectural milestone:**

1. WPF-authored Card executes Perform("Playful Tease") through the normal Session selector/Core.
2. Core selects a reachable state and compatible foundation/body/face IDs.
3. Unity moves/poses/layers acting and gazes at the player.
4. Two ordinary tagged dialogue lines trigger automatic compatible refresh.
5. The character remains coherent at Continue; repeating the Card shows legal alternatives.
6. Save a text edit in WPF; next Unity Repeat reads it without export/import or closing WPF.
7. Add a compatible Unity gesture; the same event/Card can select it without edits.
8. WPF simulation explains the semantic choices, while Unity supplies visual evidence.

**Handoff:** Working first slice and concrete visual/workflow defects. This is the milestone to inspect before broadening scope.

## 05 — Correct workflow friction and accept V1

**Context:** The architecture must earn its complexity through actual content iteration. This ticket fixes the narrow slice, not adds a general authoring product.

**Dependencies:** 04 visual proof.

**Work:**

- Run the writer loop: reuse the event, add Card/dialogue content, save, play in Unity. Record repeated setup/transport steps and remove avoidable friction.
- Run gesture intake from New Gesture through enabled playback. Measure edited fields and interactions; target at most clip/tags/posture/Enabled for ordinary assets. Fix defaults where that target fails.
- Ask of real V1 content: does the shared expressive ALL/ANY query force unnatural metadata duplication, such as tagging a facial Smirk with every body behavior verb (for example, tease)? Keep the shared query through the first vertical slice. Record concrete examples; if they demonstrate friction, report it and make the smallest evidence-based correction before V1 acceptance. Different semantic dimensions or separate face/body filtering are possible later responses, not fields to add speculatively now.
- Add another equivalent anchor and verify shared operations suffice without copying state/transition graphs. A generated diagnostic graph may help explain the plan; no authored choreography graph.
- Verify new compatible ingredient selection using candidate diagnostics and bounded seeded runs; adding it must not require editing the event/Card.
- Inspect actual visual quality, mask conflicts, pose contacts, state persistence, error clarity and dirty-edit preservation. Fix demonstrated failures.
- Document the real writer/artist loop, chosen rig technique, SQLite provider setup, tests and remaining limitations.

**Guardrails:** No scope expansion to solve hypothetical cases. Do not introduce takes, saved results, mood systems, comprehensive matrices, drivers or per-line choices. Report existing full-snapshot validation friction honestly instead of promising draft isolation.

**Acceptance:** A playable editor-based Session demonstrates the complete V1 proof repeatedly; ordinary content additions are cheap; sparse gesture intake is measured; the shared-query metadata-friction question has an evidence-backed finding and any demonstrated issue receives the smallest correction; test/visual evidence is recorded; the relevant app remains running. User review of this milestone is required before commissioning Phase 2.

## Performance Phase 2 / Driven Motion — no execution tickets yet

Oscillator, scalar-window playback, funscript, pelvis/body/prop drivers, their storage and editor controls are deferred. Design them in a separate packet after V1 is accepted. Do not create placeholder tables, host methods or mixer channels.

Player posing/visibility, voice/lipsync, broader state repertoire, richer idle/arbitration, comprehensive dashboards and standalone packaging also wait. Semantic behavior graphs remain a possible future response to real authoring needs, not a V1 framework.
