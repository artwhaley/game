# agents.md

Operating rules for AI agents and collaborators working in this repository.
This is a Unity 6 game project (editor version 6000.5.9f1), built incrementally from a skeleton.

**Current state:** project skeleton only — no game code yet. Unity 6000.5.9f1 being installed by the developer (via Unity Hub). Git repo initialized; remote is `https://github.com/artwhaley/game.git`. Unity CLI notes in [`unity-cli.md`](unity-cli.md).

## Overriding principle: incremental and intentional construction

Never fill in negative space with "app-shaped bullshit" just because it feels like something should be there. If a feature hasn't been asked for, don't build it. If a folder, class, or abstraction isn't needed yet, it doesn't exist yet.

## Rules

1. **Flag harmful changes.** If a change is going to make the app slower, less reliable, or more confusing — stop and flag it, even if the change was asked for.
2. **Plan before touching files.** List every file you plan to modify before touching anything. If the list exceeds 5 files, stop and propose splitting the task.
3. **Three strikes.** If three consecutive fix attempts fail, STOP. Propose: (a) revert, (b) what we know vs. what we don't know, (c) a different approach.
4. **No new dependencies without asking.** Do not introduce new libraries, frameworks, or services without asking first — but do make suggestions when they are the right direction.
5. **Explain before coding.** Before writing code for any non-trivial change, explain in plain language what you understand the goal to be and your planned approach. Wait for the user's "go."
6. **Keep README current.** After each feature, update README.md for someone who will read it in three months having forgotten everything.
7. **Fix root causes.** NEVER implement workarounds or band-aid solutions — ALWAYS fix the root cause.
8. **Development environment, not production.** Because we build incrementally, tests and schema may become outdated. Data isn't sacred. Don't complicate new code to protect old tests — keep the testing harness fitted to the current app state. Same for schema: no backwards-compatibility concerns, old files don't matter.
9. **Flag missing dependencies.** If something required is missing (an install, a package, a tool), flag it — but assume it may be on its way and continue where possible.

## Unity-specific essentials

- **Editor version is pinned** in `ProjectSettings/ProjectVersion.txt` (6000.5.9f1). Don't silently change it.
- **`.meta` files are sacred.** Unity generates one per asset; they carry GUID references. Commit them, never delete them — the `.gitignore` explicitly does not ignore them.
- **Folder conventions.** `Assets/Scripts` for C#, `Assets/Scenes` for scenes. New top-level folders (Materials, Prefabs, Textures, Audio, …) are created only when a feature needs them — per the overriding principle.
- **Verify in the editor.** Unity code and scenes behave differently than they read. Confirm behavior in the actual editor, not just by inspection.
- **Scenes are YAML.** Edit scenes deliberately, in the editor, with small intentional changes — avoid sprawling manual scene edits.
- **Binary assets go through Git LFS** (patterns in `.gitattributes`). Don't commit heavy files straight into history.
- **First open generates files.** Opening this folder in Unity (Hub or CLI) for the first time makes Unity create `ProjectSettings` defaults and `Packages/manifest.json`. That's expected, not a problem.

## Git conventions

- Commit with clear, concise messages focused on *why*; keep commits small.
- Don't push to the remote unless asked.
- Don't stage or commit files you didn't create or change.
- `.meta` files are always committed; binary assets via LFS.

## Unity CLI

- Quick reference and install instructions: [`unity-cli.md`](unity-cli.md).
- The CLI is **experimental** (as of Aug 2026) — verify commands with `unity --help` before relying on them.
- Opening this project: `unity open .` from the project root (uses the pinned 6000.5.9f1).
- Driving a *running* Editor requires the separate Unity Pipeline package — not installed; ask before adding (rule 4).

## When in doubt

- Ask instead of guessing.
- Prefer the smallest change that satisfies the request.
- If a request conflicts with these rules, follow the rules and flag the conflict.
