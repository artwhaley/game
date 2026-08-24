# game

A Unity 6 single-player "truth or dare" card game, built incrementally.

## Status

- In progress — Phase A of the 7-phase plan (card action core) just landed.
- Unity 6000.5.9f1 (install pending at time of writing).
- Development rules: [`agents.md`](agents.md) · Unity CLI notes: [`unity-cli.md`](unity-cli.md)

## Requirements

- Unity 6000.5.9f1 (via Unity Hub)

## How to open

1. Install Unity 6000.5.9f1.
2. Unity Hub → Add → Add project from disk → select this folder (or `unity open .`).
3. First open generates `ProjectSettings` defaults and `Packages/manifest.json` — expected.
4. If the EditMode test assembly reports missing references, add **com.unity.test-framework** via Package Manager (Unity's own framework, used for the EditMode tests).
5. From the menu bar: **TruthCardGame → Build Scenes** — generates the three scenes (MainMenu, GameSetup, Game), starter card/action content, and registers the scenes in Build Settings.
6. Open `Assets/Scenes/MainMenu.unity` and press Play.

## Development log

- **Phase A** — Card action core: abstract `CardAction` ScriptableObject (blocking/continuous + `Execute(GameContext)`), `GameContext`, `Player`, `PlayerStats`, and the `TruthCardGame` assembly.
- **Phase B** — Seed actions: `DebugAction` (log + configurable delay) and `StatIncreaseAction` (instant stat boost), both `[CreateAssetMenu]`-authorable.
- **Phase C** — Cards: `Card` ScriptableObject (title, tags, action list) and `CardDeck` (card pool + distinct-tag helper for the setup screen).
- **Phase D** — Executor: `CardExecutor` draws a random card matching must-include/must-exclude tags and runs its actions (blocking awaited, continuous dispatched). EditMode tests cover filtering, sequencing, and stat application.
- **Phase E** — Game scene runtime: `SessionConfig` (static tag-filter handoff across scene loads), `GameManager` (player, executor, draw loop), `GamePanel` (card title/status + Draw Next / Main Menu).
- **Phase F** — Menu & setup: `MenuController` (Start Game / Settings), stub `SettingsDialog`, `GameSetupController` (one toggle per distinct deck tag, ON = allowed / OFF = excluded).
