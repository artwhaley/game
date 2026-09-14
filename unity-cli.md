# Unity CLI quick reference

The new Unity command-line interface (`unity`) — installs/manages Unity Editors and modules from the terminal. Ships with Unity Hub (auto-installed) and also available as a standalone binary. This project uses editor **6000.5.9f1**, which comes with it.

> **Status: experimental** (Unity docs, checked 2026-08-24). Features and syntax may change — run `unity --help` before relying on anything in scripts.
>
> Sources: [docs.unity.com/en-us/unity-cli](https://docs.unity.com/en-us/unity-cli), [docs.unity.com/en-us/unity-cli/use-unity-cli](https://docs.unity.com/en-us/unity-cli/use-unity-cli), [unity.com/blog/meet-the-unity-cli](https://unity.com/blog/meet-the-unity-cli)

## Install / update

| Platform | Command |
|---|---|
| Windows | `winget install Unity.CLI` (Hub installs it automatically too) |
| Windows update | `winget upgrade Unity.CLI` |
| macOS/Linux | `brew install --cask unity-cli` |
| Any (script) | `curl -fsSL https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.sh \| UNITY_CLI_CHANNEL=beta bash` |
| Self-update | `unity upgrade` |

## Core commands

| Command | What it does |
|---|---|
| `unity --help` | top-level help |
| `unity --version` | CLI version (e.g. `1.0.0-beta.5`) |
| `unity install 6000.5.9f1` | install that editor version |
| `unity install lts` | install latest LTS editor |
| `unity install lts -m ios android webgl` | install editor + modules |
| `unity install-modules -e 6000.5.9f1 -m android` | add modules to an installed editor |
| `unity editors -i` | list installed editors |
| `unity open .` (or `unity .`) | open a project (uses the version pinned in `ProjectSettings/ProjectVersion.txt`) |
| `unity shell` | interactive session, run many commands in one go |
| `unity doctor` | diagnostic snapshot |
| `unity auth login` / `unity auth status` | sign in to a Unity account (browser flow) |
| `unity config update-check off` | disable background update checks |

## This project

- Open with: `unity open .` from the project root → launches 6000.5.9f1.
- Editor version pinned in `ProjectSettings/ProjectVersion.txt` — the CLI respects it.

## Running the test suite headlessly

`scripts/run-unity-tests.sh` drives `-batchmode -runTests` against the pinned editor and writes the NUnit results XML plus the full editor log into `Logs/`, named `Logs/<label>-<platform>-<timestamp>.{xml,log}`.

```bash
scripts/run-unity-tests.sh                                     # full EditMode suite
scripts/run-unity-tests.sh --filter SqliteProviderSmokeTests   # one fixture
scripts/run-unity-tests.sh --platform PlayMode
scripts/run-unity-tests.sh --label Ticket00-EditorSmoke
```

Exit codes: `0` all passed, `2` tests failed, `3` could not start (no editor found, or the project is locked). It refuses to run while an editor holds the project open, detected via `Temp/UnityLockfile` — batch mode and an open editor cannot share a project directory. Set `UNITY_EDITOR` to override the editor lookup; otherwise the pinned `m_EditorVersion` decides.

## Regenerating scenes and sample content headlessly

Both builders are public static methods, so `-executeMethod` runs them without
opening the editor. Batch mode needs the project to itself — close the editor
first (`Temp/UnityLockfile` is the giveaway).

```
UNITY="/c/Program Files/Unity/Hub/Editor/6000.5.9f1/Editor/Unity.exe"

# Sample content: cards, actions, deck, sessions, phases
"$UNITY" -batchmode -nographics -quit -projectPath "$(pwd)" \
  -executeMethod TruthCardGame.EditorTools.SampleContentBuilder.EnsureSampleContent \
  -logFile "$(pwd)/Logs/BuildSampleContent.log"

# Scenes + Build Settings (BuildAllScenes runs the sample-content builder first)
"$UNITY" -batchmode -nographics -quit -projectPath "$(pwd)" \
  -executeMethod TruthCardGame.EditorTools.SceneBuilder.BuildAllScenes \
  -logFile "$(pwd)/Logs/BuildScenes.log"
```

Exit `0` means the method returned. `Aborting batchmode due to failure` in the
log means it threw — the stack is there. Assets Unity creates this way get real
`NativeFormatImporter` metas, which is the reason to run these instead of writing
asset or meta files by hand. Both builders are idempotent: existing assets are
reloaded and reconfigured, not recreated.

One caveat worth knowing before you regenerate: **scene output is not
diff-stable.** The builder mints a fresh white `Sprite` and lets Unity allocate
local fileIDs, so `Assets/Scenes/*.unity` churns by thousands of lines even when
nothing meaningful changed — and committed scenes are the hand-edited, stable
ones. Regenerate deliberately, then review the diff; don't leave a scene rebuild
in an unrelated change.## Character rig spike headlessly

`scripts/run-rig-spike.sh` runs the ticket 01 rig steps in batch mode — pin the
model's import settings, report what actually imported, build and verify the spike
fixtures — and writes one log per step to `Logs/<label>-<step>-<timestamp>.log`.
`--diagnose` also re-runs the experiments that explain the rig's behaviour.

```
scripts/run-rig-spike.sh                    # configure, report, build+verify fixtures
scripts/run-rig-spike.sh --diagnose         # ...plus the clip and bake diagnostics
```

Two things are worth knowing before reading any of those logs:

- **The log is the result.** These steps answer measurement questions (is the
  skeleton humanoid, does a composed pose replay, does the avatar retarget
  correctly), and every answer is a `[RIG]` line rather than a screenshot.
- **Any headless pose needs `AnimatorCullingMode.AlwaysAnimate`.** The default,
  `CullUpdateTransforms`, skips writing the pose whenever the Animator is not
  visible — and with no camera rendering anything, nothing ever is. Under the
  default a `PlayableGraph` evaluate writes nothing at all, for any clip on any rig,
  including through Unity's own `AnimationPlayableUtilities.PlayClip`.

The findings and the open decisions are in
[`Docs/UnityPerformance/TICKET-01-RIG-SPIKE.md`](Docs/UnityPerformance/TICKET-01-RIG-SPIKE.md).

## Controlling a running Editor (agent workflows)

- The CLI alone manages installs; it does **not** drive a running Editor by itself.
- Driving an Editor (running commands inside an open project) requires setting up the **Unity Pipeline package** in the project. Not installed here yet — per `agents.md` rule 4, ask before adding it.

## CI / headless

- Service-account auth for unattended machines: set `UNITY_SERVICE_ACCOUNT_ID` and `UNITY_SERVICE_ACCOUNT_SECRET` env vars.
- Structured output (JSON/TSV) and predictable exit codes — good for CI scripts.
- Works on machines without the Hub desktop app.

## Gotchas

- Experimental — expect breaking changes between CLI releases.
- Modules can only be added to editors installed via Hub or the CLI (not manually installed editors).
- Windows MSIX installs self-update with no rollback.- Version aliases: `lts` or exact strings like `6000.5.9f1`.
- Headless animation is culled by default: an `Animator` left at
  `CullUpdateTransforms` writes no pose off screen. Set `AlwaysAnimate` before
  evaluating a `PlayableGraph` or `SampleAnimation`-equivalence will not hold.
