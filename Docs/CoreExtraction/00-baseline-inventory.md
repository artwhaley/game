# 00 — Baseline Inventory (Ticket 00)

Factual before-state recorded before any extraction code was written. Every item
below was verified against cloned HEAD unless explicitly marked otherwise.

## 1. Starting SHA / date

- Starting commit: `a8d0d43c26f331a10093f0f2c56c1434da1a633e`
  (`a8d0d43` "Add PROJECT-OVERVIEW.md: full architecture, history, and open questions for external review")
- Cloned from: `https://github.com/artwhaley/game.git`, branch `main`
- Date of baseline run: 2026-08-25 (local)

## 2. Environment / toolchain inventory

| Item | Value |
|---|---|
| OS | Windows (win32), PowerShell 5.1 shell |
| .NET SDKs installed | `10.0.300` (only SDK listed by `dotnet --list-sdks`) |
| git | `2.53.0.windows.2` |
| Unity editor | `6000.5.9f1` at `C:\Program Files\Unity\Hub\Editor\6000.5.9f1` |
| Other editors present | 6000.3.0f1, 6000.4.0f1 (not used) |
| Unity CLI (`unity`) | **Not on PATH**; Unity.exe driven directly with batch flags instead |
| Licensing | Batch activation works via the Hub-installed licensing client (test run completed successfully) |
| Project pin | `ProjectSettings/ProjectVersion.txt`: `m_EditorVersion: 6000.5.9f1 (b57deb96f08d)` |
| API compatibility | `ProjectSettings.asset`: `apiCompatibilityLevel: 6` → **.NET Standard 2.1 profile** |

Package manifest (`Packages/manifest.json`) relevant versions:

| Package | Version |
|---|---|
| com.unity.test-framework | 1.4.6 |
| com.unity.ugui | 2.0.0 |
| com.unity.timeline | 1.8.13 |
| com.unity.cinemachine | 3.1.7 |
| com.unity.multiplayer.center | 1.0.1 |
| builtin modules | defaults (audio, director, UI, uielements, etc.) |

Asmdefs at HEAD:

| Asmdef | References | noEngineReferences |
|---|---|---|
| `Assets/Scripts/TruthCardGame.asmdef` | Unity.Timeline, Unity.Cinemachine | false |
| `Assets/Editor/TruthCardGame.Editor.asmdef` (Editor only) | TruthCardGame, Unity.Timeline, Unity.Cinemachine, Unity.Timeline.Editor | false |
| `Assets/Tests/EditMode/TruthCardGame.Tests.EditMode.asmdef` (Editor only) | TruthCardGame, Unity.Timeline, UnityEngine.TestRunner, UnityEditor.TestRunner (+ nunit precompiled) | false |

## 3. Desktop TFM selected and why

**Selected: `net10.0` for desktop test/serializer projects; `net10.0-windows` for the WPF host.**

Rationale: `.NET SDK 10.0.300` is installed (LTS per AD-22 preference order);
no .NET 8 SDK is present. Portable assemblies remain `netstandard2.1`
regardless (Unity compiles them under its .NET Standard 2.1 API profile).

## 4. Baseline Git status

- Fresh clone was clean.
- First headless import touched `ProjectSettings/ProjectAuditorSettings.asset`
  with a line-ending-only change (CRLF normalization, zero content diff);
  restored via `git restore`. Tree verified clean before extraction work began.

## 5. Baseline Unity build/test/manual-run status

| Check | Command | Result |
|---|---|---|
| Compile + EditMode tests (headless batch) | `"C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode -projectPath <repo> -runTests -testPlatform EditMode -testResults <tmp>\tc-baseline-results.xml -logFile <tmp>\tc-baseline.log` | **22/22 passed**, 0 failed, 0 skipped (~0.06s execution after import) |

- The repo's documented `unity test . --mode EditMode` route could not be used
  verbatim because the `unity` CLI is not installed on this machine; the direct
  `Unity.exe -runTools` equivalent above is the same Test Runner machinery.
- **Manual Play-mode flow: NOT RUN.** This execution environment has no
  interactive GUI session for driving the editor Play button. The repo's own
  README already records that "the full play loop needs a human Play-mode pass
  in the editor" — this is a **pre-existing limitation**, not an extraction
  regression.

## 6. Actual engine flow diagram (as observed in code)

```text
MainMenu (MenuController)
   └─ Start Game → GameSetup scene
        GameSetupController: one button per SessionLibrary.Sessions entry
        selection → SessionConfig.SelectedSession (static handoff)
        SettingsDialog: slider 0.5×–3.0× writes SessionConfig.LengthModifier live
   └─ Start Game → Game scene

Game scene:
  GameManager.Awake:
      new Player(name)
      GameServices(runner: this, prompts: this, cutscene: DirectorPlayer)
      new SessionDriver(SessionConfig.SelectedSession)   // default modifier reads static SessionConfig.LengthModifier
  GameManager.Start: DrawNextCard()                       // automatic first draw
  GameManager.DrawNextCard (user-triggered afterwards):
      guard: _busy || driver.IsComplete → return
      build CardExecutor(deck, runner:this, driver.CurrentMustInclude, CurrentMustExclude)
      TryDrawCard:
        match found → _busy = true; panel.ShowDrawing(card); StartCoroutine(RunCard)
        none       → driver.OnNoMatchingCard()          // warns + advances phase
                     ├─ session complete → CompleteSession()
                     └─ else recurse DrawNextCard()     // same user request
  RunCard coroutine:
      yield return executor.ExecuteCard(card, new GameContext(player, services))
      panel.ShowDone(card)                               // re-enables Draw Next
      _busy = false
      driver.OnCardCompleted()                           // live-target advance check
      └─ if IsComplete → CompleteSession()
  CompleteSession:
      panel.ShowSessionComplete(); wait 1.6s scaled; SceneManager.LoadScene("MainMenu")

CardExecutor.TryDrawCard:
    matches = deck.Cards where card != null
                       && card has ALL mustInclude tags
                       && card has NONE of mustExclude tags
    card = matches[UnityEngine.Random.Range(0, matches.Count)]   // with replacement
CardExecutor.ExecuteCard (coroutine):
    foreach action in card.Actions:
        null → skip
        IsBlocking → yield return action.Execute(context)          // awaited in order
        else       → runner.StartRoutine(action.Execute(context))  // fire-and-forget
```

## 7. Actual content type list (all ScriptableObjects under `Assets/Scripts/Cards`, `Assets/Scripts/Actions`)

- `Session` — title, metadata tags, ordered `List<Phase>`
- `Phase` — title, mustIncludeTags, mustExcludeTags, minCards (default 1), maxCards (default 3)
- `Card` — title, tags, ordered `List<CardAction>` (null entries possible in principle)
- `CardDeck` — `List<Card>` + `DistinctTags()` helper
- `SessionLibrary` — list of Sessions (setup-screen menu source)
- `CardAction` (abstract SO) — serialized `isBlocking`; abstract `IEnumerator Execute(GameContext)`
- `DebugAction` — message, delaySeconds; logs then optionally waits (scaled time)
- `StatIncreaseAction` — statKey (default "courage"), amount (default 1); adds to player stats, logs
- `ChoiceAction` — prompt, `List<ChoiceOption{label, CardAction action}>`
- `CutsceneAction` — serialized `TimelineAsset timeline`

Sample content generated by `SampleContentBuilder`: 6 actions
(Debug_Blocking, Debug_Continuous, CouragePlusOne, Cutscene_Intro,
Choice_FaceTheCrowd + shared courage asset reuse), 8 cards (Courage Boost,
The Crowd Watches, Ambient Whispers, Dare & Celebrate, Twin Whispers,
A Familiar Face [cutscene], Face the Crowd [choice], The End [ending]),
StarterDeck, 2 sessions ("Relaxing", "Intense") each with 3 phases ending in an
authored "ending"-tagged phase.

## 8. Current Unity coupling table

| Type/file | Unity dependency | Current purpose | Extraction treatment |
|---|---|---|---|
| `SessionDriver.cs` | `Mathf.Max/RoundToInt`, `Debug.LogWarning`, reads static `SessionConfig.LengthModifier` by default | Phase progression rules | Move rules into portable Core (`SessionDefinition`); inject phase RNG + modifier delegate + logger; explicit midpoint-to-even rounding |
| `CardExecutor.cs` | `UnityEngine.Random.Range`, coroutines, `ICoroutineRunner`, SO types | Matching/draw + action sequencing | Split: matching+draw → portable selector with injected card RNG; sequencing → Core async executor; coroutine path retired |
| `ICoroutineRunner` (in CardExecutor.cs) | `IEnumerator` host abstraction | Dispatch continuous actions | Obsolete after parity — replaced by Core-owned BackgroundActionTracker |
| `GameManager.cs` | MonoBehaviour, StartCoroutine, SceneManager, implements old IPromptService | Session loop host + prompt bridge + presentation timing | Thin host adapter around Core engine; keeps 1.6s end delay + scene return |
| `GameContext.cs` | none itself (references Player/GameServices) | Per-execution context | Portable Core equivalent |
| `GameServices.cs` | references Unity-typed service contracts | Service bundle | Replaced by portable Core services (delay required; log non-null; prompt/cutscene optional) |
| `IPromptService.cs` | `CustomYieldInstruction` | Choice prompt contract | Replace with `Task<int?> AskAsync(...)` |
| `PromptHandle.cs` | `CustomYieldInstruction` | Prompt suspension | Obsolete after parity |
| `ICutscenePlayer.cs` | `PlayableAsset` | Cutscene contract | Replace with string-keyed `Task ICutsceneService.PlayAsync(resourceId, ct)` |
| `DirectorPlayer.cs` | PlayableDirector, Playables | Cutscene playback impl | Unity host adapter implementing Core ICutsceneService via registry keys |
| `SessionConfig.cs` | static global holding SO reference + float | Scene-handoff state + live modifier | Stays Unity-side; feeds Core via delegate |
| `Card/CardDeck/Phase/Session/SessionLibrary/CardAction+concrete` | ScriptableObject | Serialized content assets | Keep as serialized shells; add conversion to portable definitions |
| `Player.cs`, `PlayerStats.cs` | none | Player state | Move to portable Core as-is conceptually |
| `GamePanel.cs`, `MenuController.cs`, `GameSetupController.cs`, `SettingsDialog.cs` | uGUI | Presentation | Presentation only; narrow input types if needed |
| `SceneBuilder.cs`, `SampleContentBuilder.cs` (Editor) | AssetDatabase, Timeline/Cinemachine editor APIs | Generated scenes/content | Editor tooling; update wiring minimally when host changes require it |

## 9. Existing tests and what each proves

`Assets/Tests/EditMode` — all 22 green at baseline:

SessionDriverTests (7):
- `Constructor_FirstPhase_IsCurrent_WithItsFilter` — first phase active, filter exposed
- `Phase_Advances_AfterScaledTargetDraws` — advance exactly at target; next filter applied
- `LiveModifier_GrowsPhase_WhenIncreasedMidPhase` — raising modifier extends phase live
- `LiveModifier_ShrinksPhase_AndAdvancesImmediately` — lowering modifier can end phase on next completion
- `NoMatchingCard_AdvancesEarly_WithWarning` — early advance logs warning containing "advancing early"
- `LastPhase_Completion_EndsSession` — final phase completes session (IsComplete, PhaseIndex −1, Remaining 0)
- `MinMax_Clamp_Roundtrip` — min>max authoring error clamps without throwing; target ≥ 1

ExecutorTests (9):
- `Draw_WithNoFilters_ReturnsAnyCard`
- `Draw_RequiresAllMustIncludeTags` — ALL-of include semantics
- `Draw_ExcludesCardsWithMustExcludeTags` — ANY-of exclude semantics
- `Draw_ReturnsFalse_WhenNothingMatches`
- `BlockingActions_RunInOrder_AndComplete` — blocking actions awaited sequentially
- `ContinuousAction_IsDispatched_WithoutWaiting` — nonblocking dispatched, later blocking still awaited
- `StatIncreaseAction_AddsToPlayersStat`
- `DebugAction_WithZeroDelay_CompletesImmediately`

ChoiceActionTests (4):
- `Execute_YieldsOnPrompt_AndRunsChosenBlockingChild`
- `Execute_DispatchesContinuousChild_WithoutWaiting`
- `Execute_WithoutPromptService_FailsLoudly` (error log, bounded steps)
- `Execute_WithNoOptions_FailsLoudly` (error log, bounded steps)

CutsceneActionTests (3):
- `Execute_YieldsWhilePlaying_AndReturnsWhenDone` (fake player)
- `Execute_WithoutCutscenePlayer_CompletesWithoutHanging`
- `Execute_WithoutTimeline_CompletesWithoutHanging`

Note: there are no tests covering `Mathf.RoundToInt` `.5` boundaries, case
sensitivity of tag matching, or GameManager-level orchestration — these get new
portable coverage during extraction.

## 10. Exact observable semantics to preserve (verified against source)

Tag matching / drawing:
1. Include tags use ALL-of matching; exclude tags reject on ANY match.
2. Comparison is `List<string>.Contains` — default ordinal, **case-sensitive**.
3. Null deck entries are skipped; empty include list imposes no constraint.
4. Selection is uniform over matches via `[0, count)`; draws are **with replacement**.
5. No eligible card ⇒ logical no-result (caller advances phase).

SessionDriver:
6. Base target picked once per phase start: `rng.Next(max(1,min), max(min,max)+1)` — both ends inclusive.
7. `CurrentTarget = max(1, RoundToInt(base × liveModifier))` — recomputed live on every query/completion check; `Mathf.RoundToInt` is midpoint-to-even.
8. `Remaining = max(0, CurrentTarget − drawn)`; 0 once complete.
9. Advance when `drawn >= CurrentTarget()` evaluated on each card completion.
10. No-match ⇒ warn (message contains "advancing early") and advance immediately.
11. Advancing past last phase ⇒ `IsComplete=true`, `PhaseIndex=-1`, filters empty.
12. Constructor immediately activates phase 0 and picks its base target.
13. Default modifier source = static `SessionConfig.LengthModifier`; default warn = `Debug.LogWarning`; default RNG = unseeded `System.Random`.

Actions (order within a card preserved):
14. Null actions skipped; blocking awaited in order; nonblocking dispatched without awaiting.
15. DebugAction: logs `"[TruthCardGame] {player}: {message}"` first; waits scaled time iff `delaySeconds > 0`.
16. StatIncrease: `Stats.Add(statKey, amount)` (unknown key starts at 0); logs resulting value.
17. Choice: missing prompt service ⇒ error log + no-op; options null/empty ⇒ error + no-op; dismissed/out-of-range index ⇒ no-op; null child ⇒ no-op; blocking child awaited; nonblocking child dispatched via runner; nonblocking child without runner ⇒ error log (obsolete misconfiguration after extraction — see AD-16).
18. Cutscene: missing timeline ⇒ error + no-op; missing player ⇒ error + no-op; otherwise play and poll `IsPlaying` until it clears.

Orchestration pacing (GameManager):
19. First draw happens automatically at session start (`Start()`).
20. Subsequent ordinary cards only on user Draw Next; busy flag ignores re-entry.
21. Order per card: execute → ShowDone (button re-enabled) → busy cleared → OnCardCompleted → possible completion.
22. No-match loops phases within the same user request until a card draws or the session completes.
23. Session completion: ShowSessionComplete → ~1.6 s scaled delay → load MainMenu (host presentation).

Stats: unknown keys read as 0; Add accumulates.

## 11. Known pre-existing incomplete behavior

- **Cutscene timeline assignment**: `Cutscene_Intro.asset` has no `TimelineAsset`
  assigned; repo ticket 2 is marked "In progress" and PROJECT-OVERVIEW §6.1
  lists assigning/verifying the hand-authored timeline as open. Drawing "A
  Familiar Face" currently logs the missing-timeline error by design. Per
  AD-25 this is carried forward as pre-existing, not fixed here.
- **No PlayMode tests exist**; scene-glue wiring is uncovered (repo-documented risk).
- Manual Play-mode verification of the full loop had not been done by any
  machine-readable baseline; human playtesting per README covered menu→draw→
  choice flow but not cutscene playback.
- `SessionDriver`'s default modifier delegate reaches into static
  `SessionConfig` (hidden global coupling; harmless today, replaced by injected
  delegate in Core).
