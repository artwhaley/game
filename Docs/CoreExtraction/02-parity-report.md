# 02 — Parity Report

Mapping of baseline Unity behaviors to their portable tests. Status values:
`PORTABLE` (proven by pure .NET tests), `UNITY-ADAPTER` (Unity EditMode test),
`PENDING` (column not yet executed). Completed during Tickets 08/10/13.

## Baseline test → portable test mapping

| Baseline Unity EditMode test | Portable equivalent(s) | Portable status | Notes |
|---|---|---|---|
| Constructor_FirstPhase_IsCurrent_WithItsFilter | SessionDriverTests.Constructor_FirstPhase_IsCurrent_WithItsFilter | PORTABLE | Rewritten against SessionDefinition + FixedRandomSource |
| Phase_Advances_AfterScaledTargetDraws | SessionDriverTests.Phase_Advances_AfterScaledTargetDraws | PORTABLE | |
| LiveModifier_GrowsPhase_WhenIncreasedMidPhase | SessionDriverTests.LiveModifier_GrowsPhase_WhenIncreasedMidPhase | PORTABLE | |
| LiveModifier_ShrinksPhase_AndAdvancesImmediately | SessionDriverTests.LiveModifier_ShrinksPhase_AndAdvancesImmediately | PORTABLE | |
| NoMatchingCard_AdvancesEarly_WithWarning | SessionDriverTests.NoMatchingCard_Warns_AndAdvancesEarly | PORTABLE | Warning text keeps "advancing early" |
| LastPhase_Completion_EndsSession | SessionDriverTests.LastPhase_Completion_EndsSession | PORTABLE | |
| MinMax_Clamp_Roundtrip | SessionDriverTests.MinCards_BelowOne_ClampsToOne + MaxCards_BelowMin_NormalizesToMin | PORTABLE | Split into two precise cases |

## Behavior → portable coverage (beyond old test names)

| Baseline behavior (verified in 00-baseline-inventory §10) | Portable test(s) | Portable status | Notes |
|---|---|---|---|
| Include = ALL tags, exclude = ANY tag rejects; ordinal case-sensitive | CardSelectorTests.Include_RequiresAllTags, Exclude_RejectsAnyMatch, Matching_IsCaseSensitive_LikeBaseline | PORTABLE | Case-sensitivity explicitly proven both directions |
| Null deck/card entries skipped | CardSelectorTests.DeckOfOnlyNullEntries_DrawsNothing, NullEntries_AreSkipped_ButRealCardsStillMatch | PORTABLE | |
| Uniform draw over matches, [0,count), with replacement | CardSelectorTests.Selection_IndexZero/LastValidIndex/Draw_IsWithReplacement_SameIndexCanRepeat | PORTABLE | |
| Base target per phase start, inclusive min/max range | SessionDriverTests.BaseTarget_UsesFullInclusiveRange, MinMax normalization | PORTABLE | |
| CurrentTarget = max(1, Round(base × live modifier)), midpoint-to-even | SessionDriverTests.CurrentTarget_MidpointRounding_IsToEven_ThenClamped (0.5/1.5/2.5/3.5 cases) | PORTABLE | MathF.Round(ToEven); float-exact halves |
| Remaining clamped ≥ 0; 0 when complete | SessionDriverTests.Remaining_NeverNegative_AfterOvershoot | PORTABLE | |
| Blocking actions awaited in order | ActionExecutorTests.BlockingActions_RunInOrder (+ legacy Unity ExecutorTests.BlockingActions_RunInOrder_AndComplete) | PORTABLE | |
| Nonblocking dispatched without waiting | ActionExecutorTests.NonblockingAction_DoesNotHoldLaterActions | PORTABLE | Tracker-owned; no coroutine runner |
| Background faults observed/logged, never unobserved | ActionExecutorTests.BackgroundFault_IsObservedAndLogged_NotLost | PORTABLE | AD-16: obsolete missing-runner error path intentionally not reproduced (tracker is Core-owned, cannot be missing) |
| DebugAction: log first, wait iff delay > 0 | ActionExecutorTests.DebugAction_LogsBeforeDelay_SharedOrderList, DebugAction_ZeroDelay_CompletesWithoutDelayCall | PORTABLE | Delay via required IGameDelay (host supplies scaled time in Unity) |
| StatIncrease: Add semantics + resulting-value log | PlayerStatsTests.*, ActionExecutorTests.StatIncrease_MutatesPlayer_AndLogsResult | PORTABLE | Unknown keys read 0 |
| Choice: missing prompt service → logged no-op | ActionExecutorTests.Choice_WithoutPromptService_LogsError_NoOp | PORTABLE | |
| Choice: zero options → logged no-op | ActionExecutorTests.Choice_ZeroOptions_LogsError_NoOp | PORTABLE | |
| Choice: dismissed/null/out-of-range → no-op | ActionExecutorTests.Choice_DismissedOrOutOfRange_IsNoOp (null/-1/5) | PORTABLE | |
| Choice: null child → no-op | ActionExecutorTests.Choice_NullChild_IsNoOp | PORTABLE | |
| Choice: blocking child awaited / nonblocking started via same tracker | ActionExecutorTests.Choice_RunsChosenBlockingChild_ToCompletion, Choice_NonblockingChild_StartsThroughSameTracker | PORTABLE | |
| Cutscene: null/empty resource or missing service → logged no-op | ActionExecutorTests.Cutscene_MissingResourceId_LogsError_NoOp, Cutscene_MissingService_LogsError_NoOp | PORTABLE | |
| Cutscene: blocks until logical completion | ActionExecutorTests.Cutscene_WaitsUntilServiceCompletes | PORTABLE | |
| Automatic first draw at session start; user-paced afterwards | GameSessionEngineTests.OneCall_DrawsExactlyOneCard_ThenRequiresAnother | PORTABLE | Host invokes first advance (Unity Start()/WPF Start button); Core construction inert |
| Busy re-entry ignored (no concurrent cards) | GameSessionEngineTests.ReentryWhileBusy_IsIgnored, AdvanceAfterCompletion_ReturnsSessionCompleted_WithoutDrawing | PORTABLE | |
| No-match advances phase and retries within same request | GameSessionEngineTests.NoMatchPhase_Skipped_InSameCall_ToNextPhaseCard, SeveralConsecutiveNoMatchPhases_AreSkippedSafely, NoMatchThroughFinalPhase_CompletesSession_WithNullCard | PORTABLE | |
| Notification order Started → execution → Finished → progression → Completed | GameSessionEngineTests.Notifications_OccurInPreservedOrder + GoldenScenarioTests.Golden_Scenario_ProducesExactTrace | PORTABLE | Golden trace asserts full lifecycle |
| Cancellation releases busy state; engine reusable | GameSessionEngineTests.Cancellation_ReleasesBusyState_EngineNotStuck | PORTABLE | |
| End-of-session presentation (1.6 s delay, scene return) | none — host responsibility | n/a | Stays in GameManager/WPF host; not a Core rule |

## Golden deterministic scenario

`GoldenScenarioTests.Golden_Scenario_ProducesExactTrace` — SFW fixture
(`ParityFixture`): fixed phase RNG, fixed card RNG (Warm Two → Warm One →
Crossroads → The End), prompt answer "Brave", fake cutscene completion.
Asserts exact card order, exact lifecycle event sequence, prompt labels/order,
cutscene resource id, final stats (courage 2, brave 5), drained background work.

## Verification columns (Ticket 13 final pass)

| Mechanic area | Portable automated | Unity automated | Unity manual | WPF manual |
|---|---|---|---|---|
| Session progression / selection / actions / engine | **PASS** (77 engine tests + 8 serializer tests) | **PASS** — EditMode 8/8, PlayMode smoke 2/2 (re-run after JSON/WPF work) | **BLOCKED/UNVERIFIED** — batch/headless environment, no interactive GUI session; human Play-mode pass still owed. Same open item existed at baseline per repo README. | **PARTIAL** — WPF app builds; process launches, survives startup with fixture loaded, closes cleanly (exit 0); interactive end-to-end play NOT exercised (headless). |
| ScriptableObject conversion fidelity | n/a | **PASS** (ContentAdapterTests, 8 tests) | BLOCKED/UNVERIFIED (same reason) | n/a |
| Host adapters at runtime (scaled-time delay, UnityRandomSource draws, wrapper→definition→engine chain) | n/a | **PASS** (HostSmokeTests, PlayMode, headless batch) | BLOCKED/UNVERIFIED | n/a |
| JSON schemaVersion 1 round-trip incl. recursive choice/cutscene id/null entries | **PASS** (8 ContentJsonTests) | n/a (Unity does not consume serializer by design) | n/a | PASS-by-fixture-load at startup (full interactive play pending human pass) |
| Authored Timeline playback | n/a | PRE-EXISTING UNVERIFIED at baseline (AD-25) | PRE-EXISTING UNVERIFIED | n/a |

## Cross-host duplication search result

Searched both hosts for rule constructs (`Random.Range`, `Mathf/RoundToInt`,
`MustInclude`, `IsBlocking` branching, `TryDrawCard`, `CurrentTarget`,
action-type dispatch, choice-child branching):

- Unity host outside `Assets/Scripts/Portable/`: no matching/filtering/rounding/
  sequencing code remains. Only hits are the sanctioned `UnityRandomSource`
  adapter (Core RNG contract backed by UnityEngine.Random.Range), serialized
  field accessors on wrapper assets, and GameManager forwarding.
- WPF host: only engine construction (injected RNGs), `AdvanceOneCardAsync`
  forwarding from Draw Next, and read-only state mirroring for display.

No duplicated gameplay-rule path exists in either host.

### Ticket 13 regression record

- `dotnet test Game.Workbench.sln` → **85/85 passed** (after JSON+WPF additions).
- Unity EditMode re-run → **8/8 passed**; Unity PlayMode smoke re-run → **2/2 passed**
  (proving the JSON/WPF additions did not disturb the Unity host path).
- WPF: startup smoke only (see table); interactive end-to-end remains a human step.


### Ticket 10 verification record

- Command (EditMode): `Unity.exe -batchmode -projectPath <repo> -runTests -testPlatform EditMode -testResults … -logFile …` → **8/8 passed**.
- Command (PlayMode): same with `-testPlatform PlayMode` → **2/2 passed**
  (`PortableCore_RunsThroughUnityHostAdapters_AndCompletes`,
  `ConvertedScriptableObjects_FeedCoreEngine_InPlayMode`). The smoke test
  verifies: engine constructed from converted wrapper assets executes a card
  through ActionExecutor on the main thread; UnityGameDelay consumes real
  scaled game time (≥ configured delay measured against `Time.time`);
  lifecycle returns to idle; zero background faults; no error logs.
- Manual sample flow (items 1–12 of Ticket 10): **NOT RUN** — environment is
  batch/headless; no interactive GUI session exists to click through scenes.
  This is recorded as `BLOCKED/UNVERIFIED`, and per the packet the milestone
  must not be declared fully complete on this basis.

Randomness note (per packet): bitstream parity between System.Random and
UnityEngine.Random is NOT claimed or required. Parity claims cover range
semantics ([0,count) card draws; inclusive [min,max] targets via
NextInt(min,max+1)), matching rules, separate random domains, and deterministic
injection for tests. The Unity host will use UnityEngine.Random.Range for card
draws exactly as baseline does.
