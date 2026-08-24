# game

A Unity 6 game, built incrementally. *(Project name is a placeholder — the game doesn't have a name yet.)*

## Status

- Project skeleton only. No game code yet.
- Unity 6000.5.9f1 being installed by the developer (via Unity Hub).
- Git repo initialized; remote: `https://github.com/artwhaley/game.git`.
- First scene not created yet — that happens in the editor once Unity is installed.
- Development rules live in [`agents.md`](agents.md) — read it before working here.

## Requirements

- Unity 6000.5.9f1 (via Unity Hub)

## How to open

1. Install Unity 6000.5.9f1 in Unity Hub.
2. Open the project either way:
   - **Unity Hub:** Add → Add project from disk → select this folder.
   - **Unity CLI:** `unity open .` from the project root (see [`unity-cli.md`](unity-cli.md)).
3. On first open, Unity generates `ProjectSettings` defaults and `Packages/manifest.json`. This is expected.
4. Create the first scene under `Assets/Scenes`.

## Folder structure

| Path | Purpose |
|---|---|
| `Assets/Scripts` | C# scripts |
| `Assets/Scenes` | Scenes |
| `unity-cli.md` | Unity CLI quick reference (new tool, experimental) |
| `agents.md` | Development rules for this repo |

More folders (Materials, Prefabs, Textures, …) are added only when a feature needs them.

## Conventions

- Editor version pinned to 6000.5.9f1 — don't change it silently.
- `.meta` files are committed and never deleted.
- Binary assets are tracked via Git LFS (`.gitattributes`).
- After each feature, this README is updated.
