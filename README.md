# game

A Unity 6 single-player "truth or dare" card game, built incrementally.

## Status

- **SQLite is the canonical source of content truth** at `Content/GameContent.db`
  (schema v5), owned by the provider-neutral `Game.Content.Sqlite` project.
  Sessions compose **reusable Phases through a Session/Phase graph model**
  (nodes + edges + Action Instances) — the PhaseSlot/slot-candidate era is
  gone except as migration history. The **Nodify WPF Graph Workbench**
  (`DotNet/Game.ReferenceHost.Wpf`) is the authoring host: Library (Sessions /
  Phases / Cards / Catalogs), Session Graph, Phase Graph **or Card Editor**,
  Inspector, drag/drop placement, live port projection, exit/GOTO authoring,
  decision options, Copy/Duplicate/Make-Unique, semantic undo/redo, an
  embedded **live Core preview / graph debugger**, a persistent **user/test
  profile**, **selection diagnostics**, and a consumer-style **Play by Type**
  flow. See [Milestone B](#milestone-b-cards-profile-selection-04) below.
- **Milestone B (content selection) is implemented**: authored catalogs
  (Session Types, Card Tags, Kinks, Equipment, Smart Toy capabilities), Cards
  with body text + relations + owned ActionSequences, include-only phase card
  queries (ALL/ANY), per-Session Happiness/Kink weighting, a deterministic
  eligibility→weighting→weighted-draw pipeline, and a separate
  `UserProfile.db` (per-user app data, never merged with content). The deck
  concept, shared tag table, and legacy v1 action tables were **dropped
  deliberately** at schema v5 (see
  [`Docs/MilestoneB/02-schema-audit.md`](Docs/MilestoneB/02-schema-audit.md)).
- **Milestone B authoring readiness is implemented**: Cards use the shared
  ActionSequence editor, the Inspector has a scoped searchable Action Browser
  with plain alphabetized names, a wrapping multi-column layout, and drag/drop;
  PromptChoice has recursive option sequences, relation fields
  use stable-ID searchable chips, catalogs have editors, and the separate
  runner logs selection/check/action/flow diagnostics plus full card body text.
  Graph node palette inserts now refresh both live canvases from SQLite,
  center new nodes in the current viewport, repair legacy duplicate positions,
  and render nested PromptChoice action sequences without terminating the host.
  Session GOTO is restricted to direct SessionDecision option sequences;
  nested PromptChoice sequences inherit session-safe actions without borrowing
  the containing decision's projected socket ownership.
  The Reference Player and live Preview retain 10,000 diagnostic lines and
  support Copy Log, UTF-8 Save Log, and log-only Clear Log; Card Started entries
  include the full card body. Selection diagnostics resolve catalog names while
  retaining stable IDs, and an existing unreadable UserProfile.db blocks runs
  with an explicit error. Phase deletion transactionally detaches owned
  PhaseGoto exit references before cascading the phase graph, while phases
  placed in Sessions remain protected from deletion. Library reloads preserve
  the active Sessions/Phases/Cards/Catalogs drawer, every deletable Library
  item has a right-click Delete action, double-click opens an undoable rename
  dialog for Sessions, Phases, Cards, and Catalog entries, and the Catalog
  kind/entry lists use the full available width. Action-row drag starts only
  from the action label and is guarded against re-entry, preventing editor
  controls from initiating a fatal nested drag. Stat Increase uses an editable
  dropdown of authored stat keys; Modify Temperature uses the configured
  Temperature definitions dropdown. Dual-field action rows initialize their
  numeric value before synchronizing the selected key, so rebuilding the Card
  editor after Save cannot reset authored amounts to zero. Schema v6 adds
  Dialog (text + blocking), Delay (duration + blocking), and Toy Activity
  (configured Smart Toy Capability + intensity + duration + blocking, default
  blocking). Dialog currently uses the same temporary host presentation as a
  Cutscene. Card duplication recursively assigns fresh IDs to the owned action
  sequence, every PromptChoice option sequence, and every nested action.
  See [`Docs/MilestoneBAuthoringReadiness/FINAL-REPORT.md`](Docs/MilestoneBAuthoringReadiness/FINAL-REPORT.md).
- Game rules live in a **portable C# engine** (`Game.Content` + `Game.Core` +
  `Game.Profile`, .NET Standard 2.1) that both Unity and the WPF hosts run —
  one engine, multiple hosts. `GameContentDefinition` is the in-memory
  snapshot; `Game.Core` owns resolution and runtime. See
  [Extraction milestone](#extraction-milestone-01) below.
- Editor builders generate scenes + starter content — no manual wiring.
- Unity 6000.5.9f1 is pinned. The Ticket 10 batch check reached script
  compilation but currently fails on existing Unity-side drift:
  `CardDeck.cs(51,38)` references the missing
  `TruthCardGame.Content.CardDeckDefinition`. The full Unity Action
  binding/Timeline bridge remains deferred; no Unity bridge changes were made
  in the Pre-Milestone C reliability stack. See
  [`Docs/PreMilestoneC/10-final-regression.md`](Docs/PreMilestoneC/10-final-regression.md)
  and do not infer human acceptance from older milestone reports.
- Latest automated non-canonical .NET gates: **139 Core tests passed**, **122
  SQLite tests passed**, **11 profile tests passed**, and **28 WPF tests
  passed**. The canonical DB still passes integrity and foreign-key checks; its
  authored-content presence assertion is currently inapplicable because the
  active DB contains zero Sessions and zero Cards. The WPF host builds and
  launches.
- Development rules: [`agents.md`](agents.md) · Unity CLI notes: [`unity-cli.md`](unity-cli.md)

## Milestone B — Cards, Profile, Selection (0.4)

The execution packet in
`new tickets/Game_Milestone_B_Cards_Profile_Selection_Execution_Packet/`
implemented the first real content-selection layer on top of the Graph
Workbench. Contracts and per-ticket records:
[`Docs/MilestoneB/`](Docs/MilestoneB/).

- **Schema v5** — authored catalogs (`card_tag_definition`, `kink_definition`,
  `equipment_definition`, `smart_toy_capability_definition`, completed
  `session_type` with sort order + all-required capability join), Card
  relations (`card_kink`, `card_required_equipment`,
  `card_required_smart_toy_capability`), Card body text, include-only phase
  card queries (`phase_card_all_tag` / `phase_card_any_tag`), and per-Session
  card weighting (`session_card_weighting`, nonnegative, default 1.0). The
  shared `tag` table, deck tables, PhaseSlot remnants, and legacy v1 action
  tables were dropped after in-transaction copies (audit decision).
- **Selection pipeline** (`Game.Core`) — ordered: phase tag query →
  consent/kink configuration (DontConsent and Unconfigured both hard-exclude)
  → equipment → capabilities → weighting → weighted RNG draw using the
  PhaseRun card RNG. Typed machine-readable rejection reasons feed the WPF
  diagnostics; no-eligible is a loud typed failure, never a silent skip.
  Happiness (0–100, spawn default 50) drives Love/Like/Torture scores per the
  frozen formula. GOTO suspends/restores the caller PhaseRun RNG; called
  phases draw from fresh RNGs (seed-replay proven).
- **User profile** (`Game.Profile` + `Game.Profile.Sqlite`) — separate
  `UserProfile.db` under `%LocalAppData%/TruthCardGame`, own migration
  ledger, no cross-database FKs; unknown content ids tolerated; replacing
  `GameContent.db` never touches it. Kink preference rows: Love / Like /
  Torture / DontConsent; missing row = Unconfigured.
- **WPF** — Library gains Cards and Catalogs modes (referenced-deletion
  blocking with usage counts) and a Profile window; selecting a Card opens
  the Card editor in the lower center pane (title, body, searchable relation
  pickers, the reusable ActionSequence editor — new Cards default to
  WaitForContinue + IncrementProgress +10); SessionStart gains the six
  weighting fields; PhaseEntry gains ALL/ANY query editors, eligible-card
  preview, and a diagnostics window with a Happiness slider;
  **Play by Type** runs the consumer flow (type eligibility → uniform random
  session → profile-driven selection). All runner surfaces expose a visible
  integer seed; Session selection and PhaseRun/Card selection use separate
  deterministic domains, and the exact seed is logged for replay.
- ActionSequence authoring saves use recursive stable-ID diffs. Surviving
  Action Instances, PromptChoice options, and nested sequences retain their
  rows and IDs; removed objects alone are deleted, with extension-table
  cascade safety covered by SQLite tests.
- Graph Undo/Redo restores captured edge IDs and endpoints across Session and
  Phase disconnects, replacements, node deletion, projected sockets, and
  compound PhaseExit paths.
- **Deferred by design**: anti-repeat/recent-card penalties, decks without
  replacement, rarity/manual multipliers, boolean eligibility expressions,
  kink intensity scales, Unity Timeline/Action binding, Buttplug/DG-Lab/
  TCode, hardware discovery, content packs, cloud/multi profiles, persistent
  Happiness, rich-media card presentation. Exclusion-by-tag is deferred to a
  future status-effect system.

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
  exit assignment; **schema v5** adds the Milestone B catalogs, card relations,
  phase card queries, and session weighting (see above).
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
  schema v1→v5 migrations, snapshot loader, empty-DB initializer, granular
  authoring repositories (Session / Phase / graph nodes and edges / exits /
  decision options / action instances / cards + relations / catalogs /
  weighting), and the seed tool.
- `Content/GameContent.db` — the committed canonical DB, seeded from the Unity
  sample content and migrated to the current schema version. Guarded by tests:
  it must always load and pass `integrity_check` / `foreign_key_check`, and
  contains **no per-user state** (authored Kink/Equipment/Capability
  *catalogs* are content; the user's preferences/ownership live only in the
  separate `UserProfile.db` — see the UserProfileBoundary tests and
  [`Docs/MilestoneB/01-contract.md`](Docs/MilestoneB/01-contract.md)).
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

Workbench → Library → Cards → New (defaults to WaitForContinue +
IncrementProgress +10), then edit title/body, pick tags/kinks/equipment/
capabilities, and author its ActionSequence in the Card editor. In Unity
(pre-Milestone B bridge content), Assets → Create → TruthCardGame → Card
creates the ScriptableObject shell converted at session start.

## Tests

- Portable suite: `dotnet test Game.Workbench.sln` — 279 tests: 135 Core
  (graph VMs, continuation stack, decisions, spawn seam, debugger traces,
  eligibility/weighting/selection pipeline, session-type eligibility),
  123 SQLite (schema constraints, v1→v5 migration, snapshot round-trip,
  authoring repositories + undo snapshots, canonical-DB integrity,
  user-profile boundary, end-to-end DB→snapshot→Core playback,
  host-extension safety, Milestone B integration gates), 11 profile
  (separate UserProfile.db persistence/independence), 10 WPF (layout
  bindings, reason mapping, play-by-type flow, card editor host).
- Unity EditMode (`Assets/Tests/EditMode`) — ScriptableObject→definition conversion fidelity via `UnityContentGraphBuilder` (24 tests). Not run this milestone (informational only).
- Unity PlayMode smoke (`Assets/Tests/PlayMode`) — Core running through real Unity adapters inside live play mode (2 tests). Not run this milestone (informational only).
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
