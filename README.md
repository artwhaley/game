# game

A Unity 6 single-player "truth or dare" card game, built incrementally.

## Status

- Playable shell: main menu → tag-filter setup → draw & execute cards.
- Two action types: console debug (with optional delay) and stat increase.
- Editor builders generate all scenes + a starter deck — no manual wiring.
- EditMode tests cover the executor's filtering and sequencing.
- Verified working: compiles clean, all EditMode tests pass, scenes + starter content generated headlessly on 6000.5.9f1 (2026-08-24).
- Unity 6000.5.9f1 installed via the Unity CLI (elevated) — see Requirements.
- Development rules: [`agents.md`](agents.md) · Unity CLI notes: [`unity-cli.md`](unity-cli.md)

## How it works

### Cards, actions, deck — all ScriptableObject assets

- **Card** = title + filter tags + an ordered list of actions.
- **CardAction** (abstract) = a blocking/continuous flag + `Execute(GameContext)` coroutine. Two implementations so far:
  - `DebugAction` — logs a message, waits a configurable delay, completes. Stand-in for cutscenes/voiceover.
  - `StatIncreaseAction` — instantly adds to a named stat on the current player.
- **CardDeck** = the pool a session draws from; exposes the deck's distinct tags for the setup screen.
- Content lives under `Assets/Content/` (deck, cards, actions). Create, duplicate, mutate in the Project window — the seed of the future authoring tools.

### Execution flow

1. **MainMenu** → Start Game loads **GameSetup**: one toggle per distinct deck tag (ON = allowed, OFF = excluded).
2. Start Game writes the filter to `SessionConfig` (a static handoff across scene loads — deliberately not persisted) and loads **Game**.
3. `GameManager` owns the session player + `CardExecutor`. It draws a random card matching the filter and runs its actions in order: **blocking** actions are awaited, **continuous** actions are dispatched and may overlap. Draw Next Card repeats.
4. `GamePanel` is the dumb view (card title, status, buttons).

### Scenes are generated, not hand-wired

Menu bar **TruthCardGame → Build Scenes** builds `MainMenu` / `GameSetup` / `Game`, registers them in Build Settings, and creates the starter content (3 actions, 5 cards, 1 deck). Re-run any time — the code in `Assets/Editor/` is the single source of truth for scene structure.

## Extending

### New action type (mini-game, cutscene, bluetooth toy, …)

1. Subclass `CardAction` in `Assets/Scripts/Actions` (implement `Execute`, add `[CreateAssetMenu]`).
2. Create an action asset (Assets → Create → TruthCardGame).
3. Reference it from a card.

No executor changes needed — that's the point of the abstraction. The `GameContext` is the seam for future needs (toy reference, RNG, multiplayer state).

### New card

Assets → Create → TruthCardGame → Card; give it tags + actions; add it to the deck asset.

## Tests

- EditMode tests in `Assets/Tests/EditMode` — executor filtering, blocking/continuous sequencing, seed actions. Run: Window → General → Test Runner → EditMode, or `unity test . --mode EditMode` from the project root.
- **com.unity.test-framework** and **com.unity.ugui** are pinned in `Packages/manifest.json` (the fresh-import default manifest lacks uGUI, which broke all UI scripts until added — don't remove it).

## Requirements & how to open

1. Install Unity 6000.5.9f1 via Unity Hub.
2. Hub → Add → Add project from disk → this folder, or `unity open .` (see [`unity-cli.md`](unity-cli.md)).
3. First open already happened — `ProjectSettings` defaults and `Packages/manifest.json` are committed.
4. Scenes and starter content are committed too; regenerate any time via menu bar **TruthCardGame → Build Scenes**, then press Play in `Assets/Scenes/MainMenu.unity`.
5. Headless equivalents (no editor GUI): compile check is just `-batchmode -quit`; content builders run via `-executeMethod TruthCardGame.EditorTools.SceneBuilder.BuildAllScenes` / `...SampleContentBuilder.EnsureSampleContent`.
5. If UI clicks don't respond: Project Settings → Player → Active Input Handling should include **Input Manager (Old)** (or Both).

## Development log

- **Phase A** — Card action core: abstract `CardAction` (blocking/continuous + `Execute(GameContext)`), `GameContext`, `Player`, `PlayerStats`, `TruthCardGame` assembly.
- **Phase B** — Seed actions: `DebugAction` (log + delay), `StatIncreaseAction` (instant stat boost).
- **Phase C** — Cards: `Card` (title, tags, actions) and `CardDeck` (pool + distinct-tag helper).
- **Phase D** — Executor: tag-filtered draw + blocking/continuous execution; EditMode tests.
- **Phase E** — Game scene runtime: `SessionConfig` handoff, `GameManager`, `GamePanel`.
- **Phase F** — Menu & setup: `MenuController`, stub `SettingsDialog`, `GameSetupController` with tag toggles.
- **Phase G** — Editor builders: `SceneBuilder` + `SampleContentBuilder` generate scenes and starter content.
- **First Unity open** — fresh-import manifest lacked uGUI/test-framework (added both); ran builders headless; 8/8 tests pass; generated scenes, content, and ProjectSettings committed.

## Tickets

The six-phase execution plan lives in [`Tickets/`](Tickets/README.md) — one
ticket per phase, each with its own acceptance criteria and commit.
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
