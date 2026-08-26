# Core Extraction — Final Report (Milestone 0.1)

## 1. Baseline

| Item | Value |
|---|---|
| Starting SHA | `a8d0d43c26f331a10093f0f2c56c1434da1a633e` |
| Unity version | 6000.5.9f1 (`ProjectVersion.txt`), API profile .NET Standard 2.1 |
| Desktop .NET TFM | `net10.0` (+ `net10.0-windows` WPF) — SDK 10.0.300/10.0.400 installed; AD-22 preference order |
| Baseline tests | 22/22 EditMode passed headless before any change |
| Baseline manual status | Human Play-mode pass was already an open repo item at baseline (README); cutscene timeline assignment open (repo Ticket 2 "In progress") |

## 2. Final architecture

```text
Game.Content   (netstandard2.1, inert data)          Assets/Scripts/Portable/Game.Content/**
      ^                                              (same physical files compiled by
      |                                               DotNet projects via linked Compile includes)
Game.Core      (netstandard2.1, all rules)
      ^                     ^                ^
      |                     |                |
Unity host            Game.Core.Tests   Game.Content.Json ──> (Content only)
(GameManager thin     (net10.0 NUnit)   (net10.0, System.Text.Json spike)
 adapter, scaled-time
 delay, TCS prompt,
 registry cutscenes,
 UnityEngine.Random draws)

Game.ReferenceHost.Wpf (net10.0-windows) ──> Core + Content + Content.Json
```

Important paths: `Assets/Scripts/Portable/**`, `DotNet/*`, `Game.Workbench.sln`,
`Docs/CoreExtraction/*`, `Assets/Tests/{EditMode,PlayMode}/`.

## 3. What moved to Game.Content

`SessionDefinition`, `PhaseDefinition`, `CardDefinition`, `CardDeckDefinition`,
`GameActionDefinition` (+`DebugActionDefinition`, `StatIncreaseActionDefinition`,
`ChoiceActionDefinition`, `ChoiceOptionDefinition`, `CutsceneActionDefinition`).
Plain classes, settable properties with initialized lists, null entries allowed
where baseline allowed them (deck cards, card actions, choice children).

## 4. What moved to Game.Core

- `Player`, `PlayerStats`, `GameContext`, `CoreServices`
- Contracts: `IRandomSource` + `SystemRandomSource`, `IPromptService`
  (`Task<int?>`, null = dismissed), `ICutsceneService`, `IGameDelay`
  (required at construction), `IGameLog` + `NullGameLog`
- Rules: `SessionDriver` (per-phase base target via phase RNG, live length
  modifier, explicit midpoint-to-even rounding via `MathF.Round(ToEven)`,
  clamp-to-≥1, no-match early advance with warning), `CardSelector`
  (ALL-include / ANY-exclude, ordinal case-sensitive, uniform draw with
  replacement over injected card RNG), `ActionExecutor` (ordered blocking /
  tracker-dispatched nonblocking; logged-no-op misconfiguration parity),
  `BackgroundActionTracker` (no Task.Run, faults observed/logged,
  `DrainAsync`)
- Orchestration: `GameSessionEngine.AdvanceOneCardAsync(ct)` +
  `AdvanceResult(Kind)` + synchronous events `CardStarted`, `CardFinished`
  (= baseline ShowDone point), `PhaseChanged(prev,current)`, `SessionCompleted`.
  User-paced: one ordinary card per call; hosts fire the first advance.

## 5. What remains Unity-specific

- Wrapper assets (unchanged serialized identity/GUIDs): `Session`, `Phase`,
  `Card`, `CardDeck`, `SessionLibrary`, `CardAction` + four concrete actions —
  now data shells with `ToDefinition(...)` converters.
- Host adapters: `GameManager` (thin controller: convert once → construct
  engine → auto-first-draw in `Start()` → forward Draw Next → completion beat:
  `ShowSessionComplete` + 1.6 s scaled wait → menu return; lifetime CTS),
  `UnityHostAdapters.cs` (`UnityRandomSource`, `UnityGameDelay` scaled time on
  main thread, `UnityGameLog`, `UnityPromptService` TCS bridge),
  `DirectorPlayer` as `ICutsceneService` resolving keys via
  `CutsceneBindingRegistry` (opaque runtime counter keys; extraction bridge only).
- Presentation: `GamePanel` (string-titled card display, prompt overlay,
  `HidePrompt`), menu/setup/settings screens, editor builders.

## 6. What WPF implements

Reference player only: loads `DotNet/TestData/parity-content-v1.json`
(schemaVersion 1), session picker, Start/Restart (auto first advance),
live 0.5×–3.0× length slider feeding the engine delegate mid-phase,
Draw Next → `AdvanceOneCardAsync`, state panel (session/phase/progress/card/
status Idle→Executing→Waiting for Choice/Cutscene→Complete), choice buttons
resolving the prompt task exactly once, cutscene placeholder blocking until
"Complete Cutscene", capped execution log, cancellation-safe restart/close.
Fixed seeds (7/11) for reproducible debugging. No gameplay rules reimplemented.

## 7. Verification table

Full mechanic-by-mechanic table: [`02-parity-report.md`](02-parity-report.md).
Summary of final runs:

| Suite | Command | Result |
|---|---|---|
| Portable automated (engine+serializer) | `dotnet test Game.Workbench.sln` | **87/87 PASS** (incl. cancellation commit-boundary + drain quiesce invariants added by the remediation pass) |
| Unity EditMode | batch `-runTests -testPlatform EditMode` | **8/8 PASS** |
| Unity PlayMode smoke | batch `-runTests -testPlatform PlayMode` | **2/2 PASS** |
| Unity manual sample flow | interactive editor, MANUAL-TEST-GUIDE §B | **PASS (human)** — full loop verified post-remediation |
| WPF manual play | interactive, MANUAL-TEST-GUIDE §C | **PASS (human)** — deterministic fixture end-to-end incl. prompt/cutscene/slider/restart |
| Authored Timeline playback | — | **PRE-EXISTING UNVERIFIED** (baseline open item; adapter semantics unit-proven) |

Post-review remediation pass landed: cancellation commit boundary +
DirectorPlayer cancellation correctness, abstract `ToDefinition` (fail-noisy
conversion), optional DirectorPlayer wiring, drain quiesce semantics,
WPF registration/crash hardening, and root documentation truth-up
(README/OVERVIEW historical markers; `agents.md` rules 2/5 now overridable
by an approved execution packet). Deferred follow-ups are ledgered in
[`DEFERRED.md`](DEFERRED.md). With the human passes complete, every column of
the parity report is green except authored Timeline playback, which was open
at baseline and remains a content task.

## 8. Preserved-but-questionable behavior (intentional, do not fix here)

- Draw **with replacement** — immediate repeats possible; no history/shuffle bag.
- Live length modifier recomputed every completion check; shrink can end a
  phase immediately; grow extends it.
- Logged-no-op misconfigurations (missing prompt/cutscene service, empty
  options, dismissed/out-of-range choice, null child, missing resource id).
- Obsolete "missing coroutine runner" error path intentionally NOT reproduced
  (AD-16): the background tracker is Core-owned and cannot be missing.
- String tags, ordinal case-sensitive matching ("Truth" ≠ "truth").
- ScriptableObject granularity & static `SessionConfig` scene handoff.
- Temporary opaque cutscene key registry (extraction bridge, not final IDs).
- Session tags are metadata only and never filter draws.
- One engine rule retained host-side by design: Unity's 1.6 s end-of-session
  presentation delay remains presentation, not a game rule.

## 9. Known limitations

1. ~~Unity/WPF manual verification~~ **Resolved**: both human passes completed
   against [`MANUAL-TEST-GUIDE.md`](MANUAL-TEST-GUIDE.md) after the remediation pass.
2. Authored Timeline still unassigned/unplayed — carried over unchanged from
   baseline (repo Ticket 2); drawing "A Familiar Face" logs the configured
   missing-resource error until a timeline is authored.
3. First Play-mode save normalized previously-empty PlayerSettings defaults
   (target OS version strings, build numbers, pixel density) — written by the
   pinned editor itself; no behavioral settings changed
   (`apiCompatibilityLevel` untouched). Committed in `5fb560c`.
4. `unity` CLI not installed; Unity driven directly via `Unity.exe -batchmode`
   flags (same Test Runner machinery the docs route through).
5. Engine creates its player named "Player"; GameManager's old serialized
   `playerName` field was dropped (dev-environment data, agents.md rule 8).
6. JSON is a spike: schemaVersion 1, embedded sessions/deck, cutscene resource
   ids are registry keys in Unity but arbitrary strings elsewhere; stable
   content IDs deliberately deferred to the authoring milestone.
7. Choice-conversion recursion has no cycle guard yet — an authoring cycle
   (A→B→A) would overflow during conversion; validator + identity model
   (DEFERRED.md items 1–3) own that fix deliberately.

## 10. Next milestone recommendation

> Deliberately specify/evolve the final engine mechanics and expand the WPF
> reference host into the live content authoring workstation.

## 11. Developer run instructions

.NET workbench:

```bash
cd <repo>
dotnet build Game.Workbench.sln        # portable + tests + json + wpf
dotnet test  Game.Workbench.sln        # 85 portable tests
```

WPF reference player:

```bash
DotNet/Game.ReferenceHost.Wpf/bin/Debug/net10.0-windows/Game.ReferenceHost.Wpf.exe
# or open Game.Workbench.sln in Visual Studio and F5 the WPF project
```

Unity:

1. Hub → Add project from disk → `<repo>` (opens pinned 6000.5.9f1), or
   `unity open .` if the CLI is installed.
2. Tests: Window → General → Test Runner (EditMode + PlayMode), or headless:
   `"C:/Program Files/Unity/Hub/Editor/6000.5.9f1/Editor/Unity.exe" -batchmode
   -projectPath <repo> -runTests -testPlatform <EditMode|PlayMode> -testResults out.xml`
3. Play: open `Assets/Scenes/MainMenu.unity` → Play → pick a session → draw.

Final commit of this milestone: see §1 of the agent handoff message (recorded
after this commit lands).
