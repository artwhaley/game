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
