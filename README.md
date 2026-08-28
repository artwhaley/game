# game

A Unity 6 single-player "truth or dare" card game, built incrementally.

## Status

- **SQLite is the canonical source of content truth** at `Content/GameContent.db`
  (schema v4), owned by the provider-neutral `Game.Content.Sqlite` project.
  Sessions compose **reusable Phases through a Session/Phase graph model**
  (nodes + edges + Action Instances) — the PhaseSlot/slot-candidate era is
  gone except as v1→v2 migration history. The **Nodify WPF Graph Workbench**
  (`DotNet/Game.ReferenceHost.Wpf`) is the authoring host: Library + Session
  Graph + Phase Graph + Inspector, drag/drop placement, live port projection,
  exit/GOTO authoring, decision options, Copy/Duplicate/Make-Unique, semantic
  undo/redo, and an embedded **live Core preview / graph debugger**.
  See [Graph Workbench milestone](#graph-workbench-milestone-03) below and
  [`Docs/GraphWorkbench/`](Docs/GraphWorkbench/) (authoritative current design).
- Game rules live in a **portable C# engine** (`Game.Content` + `Game.Core`,
  .NET Standard 2.1) that both Unity and the WPF hosts run — one engine,
  multiple hosts. `GameContentDefinition` is the in-memory snapshot;
  `Game.Core` owns resolution and runtime. See
  [Extraction milestone](#extraction-milestone-01) below.
- Editor builders generate scenes + starter content — no manual wiring.
- Unity 6000.5.9f1 is pinned. The current finish-pass verification is recorded
  in [`Docs/GraphWorkbenchFinish/FINAL-REPORT.md`](Docs/GraphWorkbenchFinish/FINAL-REPORT.md);
  do not infer Unity or human acceptance from older milestone reports.
- Latest automated .NET gates: **105 Core tests passed**, **112 SQLite tests
  passed, 1 skipped**, and **4 WPF Workbench regression tests passed**.
  The SQLite skip preserves the canonical DB's known unconnected projected
  PhaseExit. The WPF host has a clean build; launch and interactive acceptance
  remain separate gates and are reported explicitly in the finish report.
- Development rules: [`agents.md`](agents.md) · Unity CLI notes: [`unity-cli.md`](unity-cli.md)

## Graph Workbench milestone (0.3)

The remediation ticket stack in `new tickets/Game_GraphWorkbench_Remediation_Patch_Stack/`
established the graph-model content architecture and WPF authoring tool. The
finish stack in `new tickets/Game_GraphWorkbench_Finish_Patch_Stack/` closes
the remaining authoring correctness gaps. Automated, app-smoke, human, and
not-run gates are separated in
[`Docs/GraphWorkbenchFinish/FINAL-REPORT.md`](Docs/GraphWorkbenchFinish/FINAL-REPORT.md).

- **Schema v2** — two-level graph model: `session_graph_node` (Start /
  PhaseReference / SessionDecision / End), `phase_graph_node` (Entry /
  CardExecutor / VariableCheck / Action / PhaseDecision / Return), edges,
  ordered `action_sequence` + `action_instance` (typed, blocking) replacing
  top-level configured actions; session_node_output carries phase-exit
  projections and session-goto ports. **Schema v3** adds the `wpf_*` authoring
  layout tables (node positions, viewport state); **schema v4** adds nullable PhaseGoto
  exit assignment.
- **Portable model** (`Game.Content`) — `SessionGraphDefinition` /
  `PhaseGraphDefinition` with typed node definitions and Action Instances;
  `Game.Core` runs the Session/Phase graph VMs (GOTO/RETURN continuation stack,
  decisions, variable checks, card executors) through the real engine.
- **WPF Workbench** (`DotNet/Game.ReferenceHost.Wpf`) — four logical Nodify
  panes in three outer columns: Library, stacked Session/Phase graphs, and
  Inspector. Authoring persists immediately to SQLite (continuous
  persistence — the DB is always the source of truth): node/edge create,
  drag-drop Phase placement with live exit-socket projection, inline
  two-way Nodify ItemContainer location binding for durable Session and Phase
  node positions,
  VariableCheck and typed ActionSequence editors, owner-scoped searchable
  Action-Type pickers, exits strip, Copy Session / Duplicate Phase / Make
  Unique with the shared-port lock, and semantic undo/redo (Ctrl+Z / Ctrl+Y)
  over every edit, including typed Action Instance add/remove/reorder/update,
  phase tags, and structural node creation. SessionDecision and PhaseDecision
  options use the same ordered ActionSequence editor as Action nodes.
- **Live preview/debugger** — a collapsible strip runs the real Core engine
  against a fresh SQLite snapshot with amber node rings, transfer-edge and
  check-branch highlighting, and readable temperatures/progress/stack/PhaseRun
  readouts.
- Docs: [`Docs/GraphWorkbench/`](Docs/GraphWorkbench/) (baseline, architecture
  decisions, execution semantics, schema v2 design, WPF authoring spec,
  implementation map, action types, manual test guide, final report).

## SQLite content pipeline milestone (0.2)

SQLite replaced JSON as the canonical authored content store. The JSON
spike (`Game.Content.Json`) was removed; no JSON schema v3 will be created.
(The PhaseSlot/PhaseSlotCandidate model described below is the v1-era
architecture; the graph model in milestone 0.3 superseded it — v1 data is
migrated, not taught.)

- `DotNet/Game.Content.Sqlite` — provider-neutral (any `DbConnection`):
  schema v1→v4 migrations, snapshot loader, empty-DB initializer, granular
  authoring repositories (Session / Phase / graph nodes and edges / exits /
  decision options / action instances), and the seed tool.
- `Content/GameContent.db` — the committed canonical DB, seeded from the Unity
  sample content and migrated to the current schema version. Guarded by tests:
  it must always load and pass `integrity_check` / `foreign_key_check`, and
  contains **no per-user data** (see `Docs/GraphWorkbench/USER-PROFILE-DEFERRED.md`).
  Authoring and migration checkpoints follow
  [`Docs/CONTENT-DATABASE-VERSIONING.md`](Docs/CONTENT-DATABASE-VERSIONING.md):
  close writers, verify no WAL/SHM files, validate, and commit each meaningful
  content batch. Divergent SQLite binaries are replayed, never line-merged.
- `GameContentDefinition` (portable `Game.Content`) is the **in-memory
  snapshot**, not the store: sessions reference reusable Phases via
  PhaseReference placements; cards own ordered Action Sequences; tags and
  resources are independent entities.
- `Game.Core` resolves the snapshot through `ContentCatalog` — no DB types
  in the engine.
- **WPF** is the primary core-content authoring host (Graph Workbench).
- **Unity** currently uses a temporary ScriptableObject→snapshot bridge
  (`UnityContentGraphBuilder`); it later reads the same SQLite schema and owns
  `unity_*` extension data in it (host tables are preserved by all core
  operations — see `Docs/SqliteContentGraph/02-integrity-audit.md` and the
  HostExtensionSafety tests).
- Docs: [`Docs/SqliteContentGraph/`](Docs/SqliteContentGraph/) (checkpoint,
  schema v1, integrity audit).

## Extraction milestone (0.1)

The original Unity-side rules were extracted into portable assemblies without
changing observable behavior:

- `Assets/Scripts/Portable/Game.Content/` — inert data model (sessions,
  phases, cards, deck, action definitions).
- `Assets/Scripts/Portable/Game.Core/` — rules & orchestration
  (Session/Phase graph VMs, card selector, async `ActionExecutor`, background
  tracker, `GameSessionEngine.RunUntilYieldAsync` / `ContinueAsync`).
- Unity is a host: `GameManager` converts assets once and forwards Continue;
  the old coroutine engine was removed. ScriptableObject assets keep their
  identity and convert to definitions at session start.
- `DotNet/` contains SDK projects compiling the **same physical source**:
  - `Game.Workbench.sln` opens in Visual Studio;
  - tests: `dotnet test Game.Workbench.sln`;
  - WPF reference player: run `DotNet/Game.ReferenceHost.Wpf` (loads the
    canonical `Content/GameContent.db`; pacing is authored by
    `WaitForContinue` Action Instances).
- Facts/reports: [`Docs/CoreExtraction/`](Docs/CoreExtraction/) — baseline
  inventory, extraction map, parity report (the JSON pipeline those reports
  describe was superseded by SQLite in milestone 0.2).

## How it works

> The sections below describe the **current** architecture (post-extraction).
> The [Development log](#development-log-historical--pre-extraction) and
> [Tickets](#tickets-historical--pre-extraction) sections at the bottom are
> historical records of the pre-extraction coroutine era.

### Content — ScriptableObject shells converting to portable definitions

- **Card** = title + filter tags + an ordered list of actions (dense — no null entries).
- **CardAction** (abstract) = a serialized data shell: blocking/continuous flag
  plus fields. It has **no execution code** — each wrapper implements
  `ToDefinition(...)` producing its portable `GameActionDefinition`. Four exist:
  - `DebugAction` — logs a message, waits a configurable delay.
  - `StatIncreaseAction` — adds to a named player stat.
  - `ChoiceAction` — prompt + ordered options; each option's child action is converted recursively.
  - `CutsceneAction` — serialized `TimelineAsset` plus an authored stable `resourceId` (e.g. `cs:intro`; minted once if left empty). Portable content references the cutscene by that id; `CutsceneBindingRegistry` resolves it to the asset, failing loudly on duplicates. No counter keys — links survive authoring round trips.
  - `WaitForContinueAction` and `IncrementProgressAction` — explicit authored
    pacing/progress wrappers; no implicit post-card progress exists.
- **CardDeck / Phase / Session / SessionLibrary** = data assets likewise converted once at session start.
- Content lives under `Assets/Content/`. Create, duplicate, mutate in the Project window — the seed of the future authoring tools.
- **Stable content IDs**: every entity (deck, card, action, choice option, phase, session) carries an opaque string `id` minted once at authoring time and never regenerated. Names, tags, and list positions may change; ids don't — so cross-host references (e.g. a Unity cutscene binding pointing at a card) survive authoring round trips. Unity SOs mint on creation (`OnValidate`/`EnsureId`); the SQLite canonical DB uses the same IDs as the Unity sample content.

### Execution flow — run-until-yield portable engine, Unity is a host

1. **MainMenu** → Start Game loads **GameSetup** (session picker from SessionLibrary); Settings holds the live 0.5×–3.0× length slider writing `SessionConfig.LengthModifier`.
2. Picking a session writes it to `SessionConfig` (static handoff across scene loads — deliberately not persisted) and loads **Game**.
3. `GameManager.Awake` converts the selected session/deck to definitions and constructs the portable **GameSessionEngine** with Unity host services (scaled-time delay, console log, Task-based prompt bridge over GamePanel, DirectorPlayer-backed cutscene service, `UnityEngine.Random` card draws + seeded phase RNG).
4. `GameManager.Start` starts `RunUntilYieldAsync`; Continue forwards
   `ContinueAsync`. Core runs through ordinary cards and graph nodes until an
   authored `WaitForContinue`, `SessionEnd`, cancellation, or runtime error.
   Card GOTO/RETURN preserves the suspended card/action continuation.
5. `GamePanel` is the dumb view (card title, status, buttons) driven by Core lifecycle events.

### Scenes are generated by editor builders

Menu bar **TruthCardGame → Build Scenes** builds `MainMenu` / `GameSetup` / `Game`, registers them in Build Settings, and creates the starter content. This is bootstrap/editor automation for this dev phase — useful and idempotent, not a permanent requirement that visual Unity work must be reconstructed in C#.

## Extending

### New action type (mini-game, bluetooth toy, …)

Adding an action now touches the portable engine — there is no Unity-side
`Execute` anymore:

1. **Portable definition**: add `FooActionDefinition` in
   `Assets/Scripts/Portable/Game.Content/` (plain data + `IsBlocking`).
2. **Unity wrapper**: subclass `CardAction` in `Assets/Scripts/Actions`
   (`[CreateAssetMenu]`, serialized fields) and implement `ToDefinition(...)`
   — it is abstract, so the compiler forces this step.
3. **Core handler**: add a case in `Game.Core.ActionExecutor.ExecuteActionAsync`
   interpreting the definition (log-and-no-op for misconfiguration, matching
   existing semantics).
4. **SQLite schema (if authorable outside Unity)**: add the per-type table +
   `action_type` discriminator value in `Game.Content.Sqlite` (schema v1 or a
   v2 migration) and teach `GameContentSnapshotLoader` to read it.
5. **Host service (if it needs host capability)**: extend `CoreServices` with
   an optional contract and implement it per host; keep missing-service
   behavior a tested logged no-op.

### New card

Assets → Create → TruthCardGame → Card; give it tags + actions; add it to the deck asset.

## Tests

- Portable suite: `dotnet test Game.Workbench.sln` — 204 tests: 102 covering every engine rule (graph VMs, continuation stack, decisions, spawn seam, debugger traces), 102 covering SQLite schema constraints, v1→v2 migration, snapshot round-trip, authoring repositories + undo snapshots, canonical-DB integrity, user-profile boundary, end-to-end DB→snapshot→Core playback, and host-extension safety.
- Unity EditMode (`Assets/Tests/EditMode`) — ScriptableObject→definition conversion fidelity via `UnityContentGraphBuilder` (24 tests).
- Unity PlayMode smoke (`Assets/Tests/PlayMode`) — Core running through real Unity adapters inside live play mode (2 tests).
- **com.unity.test-framework** and **com.unity.ugui** are pinned in `Packages/manifest.json` (the fresh-import default manifest lacks uGUI, which broke all UI scripts until added — don't remove it).

## Requirements & how to open

1. Install Unity 6000.5.9f1 via Unity Hub.
2. Hub → Add → Add project from disk → this folder, or `unity open .` (see [`unity-cli.md`](unity-cli.md)).
3. First open already happened — `ProjectSettings` defaults and `Packages/manifest.json` are committed.
4. Scenes and starter content are committed too; regenerate any time via menu bar **TruthCardGame → Build Scenes**, then press Play in `Assets/Scenes/MainMenu.unity`.
5. Headless equivalents (no editor GUI): compile check is just `-batchmode -quit`; content builders run via `-executeMethod TruthCardGame.EditorTools.SceneBuilder.BuildAllScenes` / `...SampleContentBuilder.EnsureSampleContent`.
5. If UI clicks don't respond: Project Settings → Player → Active Input Handling should include **Input Manager (Old)** (or Both).

## Development log (historical — pre-extraction)

> Entries below describe the original coroutine-era architecture
> (`CardExecutor`, `Execute(GameContext)` coroutines, `ICoroutineRunner`,
> yield-based prompts). They are kept as history; see
> [Extraction milestone](#extraction-milestone-01) and
> [`Docs/CoreExtraction/FINAL-REPORT.md`](Docs/CoreExtraction/FINAL-REPORT.md)
> for what the project actually is today.

- **Phase A** — Card action core: abstract `CardAction` (blocking/continuous + `Execute(GameContext)`), `GameContext`, `Player`, `PlayerStats`, `TruthCardGame` assembly.
- **Phase B** — Seed actions: `DebugAction` (log + delay), `StatIncreaseAction` (instant stat boost).
- **Phase C** — Cards: `Card` (title, tags, actions) and `CardDeck` (pool + distinct-tag helper).
- **Phase D** — Executor: tag-filtered draw + blocking/continuous execution; EditMode tests.
- **Phase E** — Game scene runtime: `SessionConfig` handoff, `GameManager`, `GamePanel`.
- **Phase F** — Menu & setup: `MenuController`, stub `SettingsDialog`, `GameSetupController` with tag toggles.
- **Phase G** — Editor builders: `SceneBuilder` + `SampleContentBuilder` generate scenes and starter content.
- **First Unity open** — fresh-import manifest lacked uGUI/test-framework (added both); ran builders headless; 8/8 tests pass; generated scenes, content, and ProjectSettings committed.

## Tickets (historical — pre-extraction)

The six-phase execution plan below built the original Unity implementation.
It is superseded by the extraction milestone; the ticket texts reference
removed types (`CardExecutor`, coroutine `Execute`) and are retained only as
history. The extraction stack itself is documented in
[`Docs/CoreExtraction/`](Docs/CoreExtraction/).
- **Ticket 1 — Runtime services plumbing**: `GameContext` now carries optional
  `GameServices` (coroutine runner, `IPromptService`, `ICutscenePlayer`). Pure,
  package-free interfaces (typed against core PlayableAsset). Seam for cutscenes
  and choices. 8/8 tests green.
- **Ticket 2 — Cutscene action**: `CutsceneAction` (plays a `TimelineAsset` via
  `context.Cutscene`, blocking) + `DirectorPlayer` (PlayableDirector wrapper).
  Game scene now has a CinemachineBrain camera, a Cinemachine cutscene camera,
  an NPC cube, and a CutsceneDirector GO wired into GameManager. Added
  Timeline 1.8.13 + Cinemachine 3.1.7 (the 6000.5-compatible versions — 1.8.1/3.1.3
  fail to compile against 6000.5's obsolete-API-as-error). Sample cutscene card
  "A Familiar Face" added; its timeline is authored by hand in the Timeline
  window. 11/11 EditMode tests green.
- **Ticket 3 — Choice action**: `ChoiceAction` (prompt + ordered options, each a
  label + child `CardAction`; blocking by nature). Branches via
  `IPromptService` (`CustomYieldInstruction`-based, implemented by GameManager
  through GamePanel's runtime-built prompt overlay). Sample choice card
  "Face the Crowd" added. Note: Unity's `CustomYieldInstruction` implements
  `IEnumerator` (MoveNext = keepWaiting), which matters for coroutine drivers.
  15/15 EditMode tests green.
- **Ticket 4 — Session/Phase data**: `Session` (title + metadata tags + ordered
  phases), `Phase` (must-include/exclude tags + min/max draw range), and
  `SessionLibrary`. `SessionConfig` now carries `SelectedSession` +
  `LengthModifier` (live-read, never baked). Sample content: "Relaxing" and
  "Intense" sessions with phases, each ending in an authored ending phase
  whose tags match an ending card. 15/15 EditMode tests green.
- **Ticket 5 — SessionDriver core**: plain-C# driver that walks a session's
  phases. Picks an unscaled base target once per phase; advance check
  `drawn >= Round(base × live LengthModifier)`, clamped ≥ 1, evaluated on every
  card completion — a mid-session modifier change takes effect on the next
  draw (grow extends, shrink can end the phase immediately). Early advance
  with a warning when no card matches; completion after the last phase.
  22/22 EditMode tests green.
- **Ticket 6 — Screen rework + settings**: setup screen is now a session picker
  (buttons from SessionLibrary → SessionConfig.SelectedSession). Settings
  dialog gains a live length-modifier slider (0.5x–3x) writing
  SessionConfig.LengthModifier. GameManager draws through SessionDriver
  per-phase: card completes → driver advances → last phase → "Session complete"
  → back to menu; no matching card → early advance with warning. The old tag
  toggles are gone. 22/22 EditMode tests green. The full play loop needs a
  human Play-mode pass in the editor.
