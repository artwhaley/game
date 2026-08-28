# agents.md

Operating rules for AI agents and collaborators working in this repository.
This is a Unity 6 game project (editor version 6000.5.9f1), built incrementally from a skeleton.

Git repo initialized; remote is `https://github.com/artwhaley/game.git`. Unity CLI notes in [`unity-cli.md`](unity-cli.md).

**Current architecture truth:** read [`README.md`](README.md) first (status),
then [`Docs/GraphWorkbench/`](Docs/GraphWorkbench/) — the authoritative current
design: two-level Session/Phase graph VM, Action Instances, SQLite schema v3
(v2 graph tables + v3 `wpf_*` authoring layout), and the Nodify WPF Workbench.
Read it before writing code.
Supporting context: [`Docs/SqliteContentGraph/`](Docs/SqliteContentGraph/)
(SQLite is the canonical content store at `Content/GameContent.db`; its
NEXT-WPF-HIGH-LEVEL-AUTHORING.md is **superseded** by GraphWorkbench docs), and
[`Docs/CoreExtraction/`](Docs/CoreExtraction/) (extraction milestone history).

Current project status lives in [`README.md`](README.md) — read it first, keep it current (rule 6). This file holds only rules intended to stand for the life of the project; status snapshots belong in the README, never here.

## Overriding principle: incremental and intentional construction

Never fill in negative space with "app-shaped bullshit" just because it feels like something should be there. If a feature hasn't been asked for, don't build it. If a folder, class, or abstraction isn't needed yet, it doesn't exist yet.

## How these rules bind agents

Rules 2 and 5 below are **defaults for interactive collaboration**, not absolutes.
A written execution packet explicitly approved by the user (for example, a ticket
stack handed to an agent as one authorized run) may override them for the duration
of that run — batching larger coherent changesets and proceeding without a fresh
"go" between pre-approved steps — provided the packet names the override and keeps
its own gates. Architecture principles such as fail-noisy/root-cause (rules 7, 10)
are never overridden by a packet.

## Rules

1. **Flag harmful changes.** If a change is going to make the app slower, less reliable, or more confusing — stop and flag it, even if the change was asked for.
2. **Plan before touching files.** List every file you plan to modify before touching anything. If the list exceeds 5 files, stop and propose splitting the task — unless a user-approved execution packet governs the work and explicitly authorizes larger coherent changesets (see "How these rules bind agents" above).
3. **Three strikes.** If three consecutive fix attempts fail, STOP. Propose: (a) revert, (b) what we know vs. what we don't know, (c) a different approach.
4. **No new dependencies without asking.** Do not introduce new libraries, frameworks, or services without asking first — but do make suggestions when they are the right direction.
5. **Explain before coding.** Before writing code for any non-trivial change, explain in plain language what you understand the goal to be and your planned approach. Wait for the user's "go" — unless a user-approved execution packet already contains the plan and constitutes standing authorization (see "How these rules bind agents" above).
6. **Keep README current.** After each feature, update README.md for someone who will read it in three months having forgotten everything.
7. **Fix root causes.** NEVER implement workarounds or band-aid solutions — ALWAYS fix the root cause.
8. **Development environment, not production.** Because we build incrementally, tests and schema may become outdated. Data isn't sacred. Don't complicate new code to protect old tests — keep the testing harness fitted to the current app state. Same for schema: no backwards-compatibility concerns, old files don't matter.
9. **Flag missing dependencies.** If something required is missing (an install, a package, a tool), flag it — but assume it may be on its way and continue where possible.
10. **Fail noisy.** Never invent fallback behavior that masks a problem. If something breaks, let it break loudly and report exactly what failed and why. Silent recovery is worse than a crash.
11. **Report verification honestly.** If a change couldn't be verified (editor closed, headless limits, whatever the reason), say so plainly — never report "done" for an unverified change.
12. **Run the program after every change.** After any code or XAML edit, build and launch the WPF host (`DotNet/Game.ReferenceHost.Wpf`) or Unity editor — don't assume the change is safe because the build succeeded. A clean build does not prove the app still works. Watch the log for exceptions, spot-check the affected feature, and kill the process before committing. This is non-negotiable for GUI work.

## Unity-specific essentials

- **Editor version is pinned** in `ProjectSettings/ProjectVersion.txt` (6000.5.9f1). Don't silently change it.
- **`.meta` files are sacred.** Unity generates one per asset; they carry GUID references. Commit them, never delete them — the `.gitignore` explicitly does not ignore them.
- **Folder conventions.** `Assets/Scripts` for C#, `Assets/Scenes` for scenes. New top-level folders (Materials, Prefabs, Textures, Audio, …) are created only when a feature needs them — per the overriding principle.
- **Verify in the editor.** Unity code and scenes behave differently than they read. Confirm behavior in the actual editor, not just by inspection.
- **Scenes are YAML.** Edit scenes deliberately, in the editor, with small intentional changes — avoid sprawling manual scene edits.
- **Binary assets go through Git LFS** (patterns in `.gitattributes`). Don't commit heavy files straight into history.
- **Generated folders aren't source.** `Library/`, `Temp/`, `Logs/` and friends regenerate locally from what's in git. Missing or stale ones are normal, not corruption.

## Git conventions

- **Save the whole project state on every commit.** Commit everything that should be committed — don't be surgical, don't cherry-pick files, don't stage only "your" changes. If the working tree changed, the next commit saves it. This is a single-branch, single-developer flow; there is no multi-branch or multi-developer cleverness. Just save the state when it changes.
- Commit after each accepted feature/change — clear, concise messages focused on *why*.
- Don't push to the remote unless asked.
- `.meta` files are always committed; binary assets via LFS.
- Check `.gitattributes` before committing a new binary type — adding an LFS pattern after the fact doesn't fix blobs already in history.

## Unity CLI

- Quick reference and install instructions: [`unity-cli.md`](unity-cli.md).
- The CLI is **experimental** (as of Aug 2026) — verify commands with `unity --help` before relying on them.
- Opening this project: `unity open .` from the project root (uses the pinned 6000.5.9f1).
- Driving a *running* Editor requires the separate Unity Pipeline package — not installed; ask before adding (rule 4).
- Scene/content generation runs inside the editor: menu bar **TruthCardGame → Build Scenes** (also creates the sample content). Can't be driven from the CLI without the Unity Pipeline package.

## When in doubt

- Ask instead of guessing.
- Prefer the smallest change that satisfies the request.
- If a request conflicts with these rules, follow the rules and flag the conflict.
