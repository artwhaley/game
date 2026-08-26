# game

A Unity 6 single-player "truth or dare" card game, built incrementally.

## Status

- Playable shell: main menu → session picker → draw & execute cards.
- Game rules now live in a **portable C# engine** (`Game.Content` + `Game.Core`,
  .NET Standard 2.1) that both Unity and a desktop WPF reference player host —
  one engine, two hosts. See [Extraction milestone](#extraction-milestone-01) below.
- Editor builders generate all scenes + starter content — no manual wiring.
- Unity 6000.5.9f1 pinned; verified: compiles clean, EditMode 8/8, PlayMode
  smoke 2/2 headless on this checkout; portable suite 87/87 via `dotnet test`.
  Human Play-mode passes of both hosts completed against
  [`Docs/CoreExtraction/MANUAL-TEST-GUIDE.md`](Docs/CoreExtraction/MANUAL-TEST-GUIDE.md).
- Development rules: [`agents.md`](agents.md) · Unity CLI notes: [`unity-cli.md`](unity-cli.md)

## Extraction milestone (0.1)

The original Unity-side rules were extracted into portable assemblies without
changing observable behavior:

- `Assets/Scripts/Portable/Game.Content/` — inert data model (sessions,
  phases, cards, deck, action definitions).
- `Assets/Scripts/Portable/Game.Core/` — rules & orchestration
  (`SessionDriver`, card selector, async `ActionExecutor`, background tracker,
  user-paced `GameSessionEngine.AdvanceOneCardAsync`).
- Unity is a host: `GameManager` converts assets once and forwards Draw Next;
  the old coroutine engine was removed. ScriptableObject assets keep their
  identity and convert to definitions at session start.
- `DotNet/` contains SDK projects compiling the **same physical source**:
  - `Game.Workbench.sln` opens in Visual Studio;
  - tests: `dotnet test Game.Workbench.sln`;
  - WPF reference player: run `DotNet/Game.ReferenceHost.Wpf` (loads
    `DotNet/TestData/parity-content-v1.json`, one card per Draw Next click);
  - `Game.Content.Json` — schemaVersion-2 JSON spike for future authoring tools; every entity carries a stable GUID `id`.
- Facts/reports: [`Docs/CoreExtraction/`](Docs/CoreExtraction/) — baseline
  inventory, extraction map, parity report.

## How it works

> The sections below describe the **current** architecture (post-extraction).
> The [Development log](#development-log) and [Tickets](#tickets) sections at
> the bottom are historical records of the pre-extraction coroutine era.

### Content — ScriptableObject shells converting to portable definitions

- **Card** = title + filter tags + an ordered list of actions (null entries allowed).
- **CardAction** (abstract) = a serialized data shell: blocking/continuous flag
  plus fields. It has **no execution code** — each wrapper implements
  `ToDefinition(...)` producing its portable `GameActionDefinition`. Four exist:
  - `DebugAction` — logs a message, waits a configurable delay.
  - `StatIncreaseAction` — adds to a named player stat.
  - `ChoiceAction` — prompt + ordered options; each option's child action is converted recursively.
  - `CutsceneAction` — serialized `TimelineAsset` plus an authored stable `resourceId` (e.g. `cs:intro`; minted once if left empty). Portable content references the cutscene by that id; `CutsceneBindingRegistry` resolves it to the asset, failing loudly on duplicates. No counter keys — links survive authoring round trips.
- **CardDeck / Phase / Session / SessionLibrary** = data assets likewise converted once at session start.
- Content lives under `Assets/Content/`. Create, duplicate, mutate in the Project window — the seed of the future authoring tools.
- **Stable content IDs**: every entity (deck, card, action, choice option, phase, session) carries a GUID `id` minted once at authoring time and never regenerated. Names, tags, and list positions may change; ids don't — so cross-host references (e.g. a Unity cutscene binding pointing at a card) survive authoring round trips. Unity SOs mint on creation (`OnValidate`/`EnsureId`); `SampleContentBuilder` ensures and persists ids for existing assets too; JSON requires them from schemaVersion 2 onward.

### Execution flow — one portable engine, Unity is a host

1. **MainMenu** → Start Game loads **GameSetup** (session picker from SessionLibrary); Settings holds the live 0.5×–3.0× length slider writing `SessionConfig.LengthModifier`.
2. Picking a session writes it to `SessionConfig` (static handoff across scene loads — deliberately not persisted) and loads **Game**.
3. `GameManager.Awake` converts the selected session/deck to definitions and constructs the portable **GameSessionEngine** with Unity host services (scaled-time delay, console log, Task-based prompt bridge over GamePanel, DirectorPlayer-backed cutscene service, `UnityEngine.Random` card draws + seeded phase RNG).
4. `GameManager.Start` triggers the **automatic first advance**; every later Draw Next click forwards to `AdvanceOneCardAsync`. Core owns all rules: tag matching, random draw, no-match phase skipping, blocking/continuous action sequencing, phase targets, completion.
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
4. **JSON (if authorable outside Unity)**: add a discriminator token in
   `Game.Content.Json.ActionDefinitionConverter`.
5. **Host service (if it needs host capability)**: extend `CoreServices` with
   an optional contract and implement it per host; keep missing-service
   behavior a tested logged no-op.

### New card

Assets → Create → TruthCardGame → Card; give it tags + actions; add it to the deck asset.

## Tests

- Portable suite: `dotnet test Game.Workbench.sln` — 87 tests covering every engine rule plus the JSON serializer.
- Unity EditMode (`Assets/Tests/EditMode`) — ScriptableObject→definition conversion fidelity (8 tests).
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
