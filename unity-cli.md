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
- Windows MSIX installs self-update with no rollback.
- Version aliases: `lts` or exact strings like `6000.5.9f1`.
