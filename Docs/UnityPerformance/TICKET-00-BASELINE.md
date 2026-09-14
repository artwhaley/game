# Ticket 00 baseline — verified state, assets and missing requirements

Ticket 00 handoff for Conversation Performance V1: the proven or blocked content
path, the source map, and the concrete rig requirement. Everything below was
*run* or *read* on September 11, 2026 against the pinned Unity **6000.5.9f1**;
nothing is an inherited historical count.

Provider decision, vendoring detail and the per-test evidence are in
[SQLITE-PROVIDER-VENDORING.md](SQLITE-PROVIDER-VENDORING.md). The plan itself is
[BUILD-PLAN.md](BUILD-PLAN.md) and
[TICKET-STACK.md](../../Tickets/UnityPerformanceExecutionStack/TICKET-STACK.md).

## Content path: proven

`GameContentSnapshotLoader.Load(DbConnection, bool)` runs in Unity, and the
`GameContentDefinition` it returns is the exact type the Unity-compiled
`GameSessionEngine` takes. Verified by
`Assets/Tests/EditMode/SqliteContentIntegrationTests.cs`:

| Evidence | Result |
|---|---|
| Canonical `Content/GameContent.db`, read-only | loads at core schema **v11**; write rejected; `integrity_check` and `foreign_key_check` clean |
| `Fixtures/GameContent-v1.db` copied and migrated in Unity | v1 → **v11** using the StreamingAssets schema scripts, then loaded through the same loader |
| Loaded snapshot handed straight into `new GameSessionEngine(...)` | run reached `SessionCompleted`; the log records `cardsDrawn=30`, no engine errors |
| Duplicate-type scan over every loaded assembly | exactly one definition per `TruthCardGame.Content`/`.Content.Sqlite`/`Core` type; no assembly loaded from `Plugins/` |
| Loader return type vs engine parameter type vs `typeof(GameContentDefinition)` | same `Type` |
| Connection release | the migrated copy is deletable after its connection is disposed |

Verified the same day: `dotnet test Game.Workbench.sln` → **401 passed, 0
failed** (178 Core, 165 Content.Sqlite, 11 Profile.Sqlite, 47 WPF), and the full
`Assets/Tests/EditMode` suite → **29 passed, 0 failed**. Evidence pairs:
`Logs/Ticket00-FullEditMode-EditMode-20260911233324.{xml,log}`,
`Logs/Ticket00-LoaderIntegration-EditMode-20260911233246.{xml,log}`,
`Logs/Ticket00-EditorSmoke-EditMode-20260911222331.{xml,log}`.

The canonical database was **not** modified by any of this (`Content/GameContent.db`
is absent from `git status`). Every canonical read opens `Mode=ReadOnly` and the
write-path checks run against temporary copies, which is the pattern
`DotNet/Game.Content.Sqlite.Tests` already used.

## Content path: what is blocked

`Content/GameContent.db` is a valid but **content-empty** authoring store at
schema v11:

| Table | Rows |
|---|---|
| `session` | 0 |
| `phase` | 0 |
| `card` | 0 |
| `action_instance` | 0 |
| `dialog_snippet` | 0 |
| `card_tag_definition` | 0 |
| `session_type` | 1 |
| `resource` | 1 |
| `temperature_definition` | 1 |

So "Unity loads the canonical database" is proven, but "Unity plays authored
content" is not available yet: there is nothing to play. Ticket 04's first
visual milestone is therefore blocked on **WPF authoring an ordinary Card and a
Session into the canonical database** (ticket 03), not on any Unity-side work.

The load→engine proof above uses the preserved v1 fixture instead, which is why
it can demonstrate a real run today.

## Available assets

| Kind | Count | Notes |
|---|---|---|
| `Assets/Scripts/**/*.cs` | 130 | portable Core/Content/Profile/Sqlite plus Unity host and UI |
| `.unity` scenes | 3 | `MainMenu`, `GameSetup`, `Game` |
| `.asset` | 25 | ScriptableObject content and scene support |
| `.playable` | 1 | hand-authored Timeline asset for the existing cutscene path |
| `.prefab` | 0 | — |
| `.fbx` / model | 0 | **no rig, no mesh, no avatar** |
| `.anim` / `.controller` / `overrideController` | 0 | **no clips, no controllers, no layers** |
| `.wav` / `.mp3` | 0 | no audio |
| `.mat` / `.shader` | 0 | no custom rendering assets |

Pinned packages (`Packages/manifest.json`): `com.unity.test-framework` 1.4.6,
`com.unity.ugui` 2.0.0, `com.unity.timeline` 1.8.13, `com.unity.cinemachine`
3.1.7, `com.unity.multiplayer.center` 1.0.1, plus the built-in modules.
**Animation Rigging is not declared**, and neither is any Playables support
package beyond the built-in `director`/`animation` modules.

The existing game scene renders an NPC cube through Timeline/Cinemachine — there
is no humanoid character anywhere in the project.

## Missing requirements (ticket 01 inputs)

1. **A representative rig.** Ticket 01's spike needs an actual or representative
   humanoid with a skeleton that can stand and sit, plus a masked body gesture,
   a facial preset, and a definable gaze target. Nothing in the project supplies
   this today, so acquisition is the first blocker — and because the asset and
   any rigging support are third-party, rule 4's approval step applies to both.
   *Acquired since: `Assets/Characters/Luna/luna.fbx` imports as a valid humanoid*
   *and the driving mechanism is proven end to end — but it carries no animation*
   *clips and no blend shapes, and its auto-generated avatar retargets playback*
   *up to a metre off the pose a clip holds. Measurements, the four constraints*
   *that matter and the open decisions are in*
   *[`TICKET-01-RIG-SPIKE.md`](TICKET-01-RIG-SPIKE.md).*
2. **Two anchors and standing/sitting.** Room-side anchors do not exist as
   content or scene objects yet; the contract's factored `(anchor, posture)`
   model needs at least one sit-capable anchor to be non-trivial.
3. **A minimal real Session/Card using the normal selector**, authored through
   WPF into the canonical database (see the blocker above). Ticket 00's
   classification work is done, so the `Perform` action can be added to an
   authored Card as soon as ticket 02 lands.
4. **Non-Windows SQLite natives** (macOS, Linux, plus Android/iOS packages) if
   any target beyond the Windows editor is required. Only the Windows pair is
   vendored; the matrix is in the vendoring doc.
5. **Player-build coverage.** Every ticket-00 test is EditMode
   (`includePlatforms: ["Editor"]`), so nothing yet covers IL2CPP, stripping or
   a standalone player. PlayMode is now run and green (below), but it is
   editor-hosted play mode, not a player build.

## PlayMode: what it actually covers

`Assets/Tests/PlayMode` holds eleven tests and all pass headlessly
(`Logs/Ticket00-FullPlayMode3-PlayMode-20260912193628.{xml,log}`, 11/11).

| Test | Coverage |
|---|---|
| `PortableCore_RunsThroughUnityHostAdapters_AndCompletes` | The engine driven through the real Unity host adapters: `UnityGameDelay`'s scaled-time delay and the host log. Asserts completion, 30 `courage` (3 per card over 10 cards), that at least 0.05 s of scaled game time really elapsed (so the delay awaited rather than no-op'd), engine idle, no pending background work, no error entries |
| `ConvertedScriptableObjects_FeedCoreEngine_InPlayMode` | Real `Phase`/`Card`/`CardDeck`/`Session`/action `ScriptableObject`s created in memory, converted through `UnityContentGraphBuilder`, then run through `GameSessionEngine` to completion with the authored stat applied |
| `GameSceneUiTests.GameScene_PlaysACardThroughGameManagerAndTheContinueButton` | Boots the authored `Game` scene with a real session selected and plays a card through `GameManager` and the real UI |
| `GameSceneUiTests.GameScene_ChoiceCard_ResolvesItsPromptThroughTheOverlay` | Advances the `Intense` session until the authored choice card is drawn, then answers through the overlay's real option button |
| `GameSceneUiTests.GameScene_CutsceneWiring_IsComplete` | The scene's `GameManager`, `DirectorPlayer` and `PlayableDirector` are wired to one another (see defect 3) |
| `GameSceneUiTests.DirectorPlayer_PlaysARegisteredTimeline_ToCompletion` | Registers a cutscene the way conversion does, then plays it: the registry resolves it, the resource row is declared, the director receives the resolved asset, is observed in `PlayState.Playing`, and `PlayAsync` returns only after the timeline's real duration |
| `GameSceneUiTests.DirectorPlayer_CancellingPlayback_StopsTheDirectorAndReportsCancellation` | Cancel mid-playback: the task ends **cancelled** rather than successfully, and the director is stopped instead of left running |
| `GameSceneUiTests.DirectorPlayer_UnregisteredResource_LogsTheMissingCutsceneAndReturnsCleanly` | Pins the authored state (`cs:intro` declared, nothing registered) then proves the failure path: one logged error, no playback, and a task that completes immediately |
| `GameSceneUiTests.DirectorPlayer_WithoutAPlayableDirector_LogsTheRebuildHintInsteadOfThrowing` | The diagnostic that hid defect 3 — with no director wired, playback logs and returns rather than throwing |
| `GameSceneUiTests.GameScene_CutsceneCard_PlaysItsCutscene_AndTheSessionMovesOn` | The whole authored path: relaxes through `Relaxing` via the real Continue button until the cutscene card is drawn by its phase, asserts the scene's director plays the registered asset for its real duration while the card is held, then that the card's full increment ends the phase and Continue draws the next phase's card |
| `GameSceneUiTests.GameScene_CutsceneCard_WithNoRegisteredTimeline_LogsAndTheSessionMovesOn` | The same card with nothing registered under the authored id — the state sample content ships in: one logged error, no playback, and the run still reaches the card's wait and continues into the next phase |

The first two load no scene and touch no MonoBehaviour; the last two exercise
scene glue. `GameSceneUiTests` reads `GamePanel`'s private `[SerializeField]`
view references through `UnityEditor.SerializedObject` and reaches
`GameManager._engine` by reflection (the engine is private by design, and the
test reaches through rather than widening the component's surface), so the
assembly remains editor-hosted by construction.

Both scene tests work *with* the draw order rather than fighting it: `GameManager`
starts the engine on `SessionSpawnOptions.Default` (seed 0), so the sequence is
fixed. Expectations come from the authored assets — the phase's own tag query for
the eligible titles, and the `ChoiceAction`'s prompt, option labels and chosen
branch for the overlay — so relabelling or re-branching sample content cannot
silently pass. With the default seed the choice card arrives on the fourth card,
after three advances; the test allows eight. Its 7.7 s runtime is the two
authored 2.5 s `Debug_Blocking` beats before it, not the overlay.

### Cutscene playback: what is covered, and what still blocks the full path

The cutscene tests drive the authored scene's **own** `DirectorPlayer` (the same
instance `GameManager` binds in `Awake`); none of them construct a parallel rig,
and each asserts the wiring it depends on before asserting anything else. The
timeline they play is built at runtime — a `TimelineAsset` with
`durationMode = FixedLength` — because sample content has no hand-authored
timeline to play. That is deliberate and stated in the fixture: it lets
playback, completion, cancellation and the missing-resource path be proven
today, with the asset's stand-in status explicit rather than implied.

What the tests measure, rather than assert in the abstract:

- playback is real — the playback test's recorded duration is **0.35 s**, equal
  to the stand-in timeline's length, so `PlayAsync` genuinely tracked the
  director rather than returning immediately;
- the registration path is exercised through conversion
  (`CutsceneAction` + `TimelineAsset` → `UnityContentGraphBuilder.CollectCutscene`
  → `CutsceneBindingRegistry`), not by hand-filling the registry, including the
  declared `cutscene` resource row that `ContentReferenceValidator` requires;
- cancellation propagates as cancellation, per `DirectorPlayer`'s own contract;
- the failure path is the authored one: `cs:intro` is declared but unregistered,
  playback logs once and returns, so the session cannot stall on it.

Cutscene behaviour is covered at both levels: the **component** (playback,
completion, cancellation, and the missing-director diagnostic) and the **card**
(authored card → conversion → engine → scene director → UI). The card-level pair
advances `Relaxing` through the real Continue button: with the seed fixed at 0
the cutscene card is the twenty-first draw, reached within the twenty-four
advances the test allows.

What the run let us stop calling blocked:

- **No phase accepted the `cutscene` tag**, so `CutsceneIntro` ("A Familiar
  Face") could never be drawn — phases select with an ALL-tags query and the
  card carries only that tag. `SampleContentBuilder` now authors
  `Phase_Cutscene` (`mustIncludeTags: [cutscene]`) and `Relaxing` runs
  `Warm Up → Teasing → Cutscene → Wind Down`. A phase is also not the right
  place to adjust this: adding the tag to an existing phase's query would stop
  that phase drawing anything, because ALL-tags means the query must be the
  cutscene tag itself.
- **The cutscene card now ends its phase on one draw.** Its progress instance is
  `Increment_Progress100`, not the shared `+10`, so the cutscene plays once as
  the session reaches it instead of the same card ten times.

**Still blocked, and it is content, not code:**

1. `Assets/Content/Actions/Cutscene_Intro.asset` has `timeline: {fileID: 0}`.
   `SampleContentBuilder` says why ("the timeline asset is authored by hand in
   the Timeline window (this builder cannot)"), so `cs:intro` converts to a
   *declared* resource with nothing registered under it — which makes the
   missing-cutscene log the authored state, not a placeholder for one.
   The card-level playback test resolves the authored resource id either way: it
   plays a labelled stand-in timeline while none is assigned, and the authored
   asset — asserting against that asset's real duration — once one is. That
   switch was verified by temporarily assigning the repo's `testtime.playable`
   to the action's timeline field and re-running the test (it passed, playing the
   authored asset through the real registry with no injection), then reverting:
   `testtime.playable` is empty (0 s, no tracks), so it proves the wiring while
   showing nothing, and the stand-in's 0.35 s is the stronger evidence until a
   real cutscene is authored.
2. **The canonical SQLite store still holds no content** (see the blocker
   above), so ticket 04's real content path has nothing to load. Unrelated to
   cutscenes, but it is the remaining gate on the visual milestone.

Also still uncovered: the `MainMenu`/`GameSetup` scenes and player builds.

### Shipped defects the scene fixture found

All five were shipping bugs rather than test artifacts, and all five are fixed.
Defects 1–3 came out of the scene fixture; 4 and 5 came out of extending it to
the cutscene card and to the content that card needs.

1. **The Game scene could not boot at all.** `UnityContentGraphBuilder.CollectCutscene`
   returned early whenever a cutscene action had no `TimelineAsset` assigned, so it
   never declared the `cutscene` resource row — but `CutsceneAction.ToDefinition` still
   emitted an instance referencing the authored id `cs:intro`. `ContentReferenceValidator`
   runs inside the `GameSessionEngine` constructor over *every* card in the deck, so it
   threw `InvalidOperationException` from `GameManager.Awake` for both sample sessions,
   leaving `_engine` null and the panel stuck at "Ready.". Sample content ships the id
   before the hand-authored timeline exists, and the documented behaviour is that drawing
   that card logs a missing-playback error (`Docs/CoreExtraction/00-baseline-inventory.md` §11) —
   not that no session can start. The builder now declares the resource whenever the
   converted instance carries an id, and binds a timeline only when one is assigned.
2. **The waiting panel replaced the card's name with "Continue".** `GameManager.RunAdvance`
   called `panel.ShowWaitingForContinue("Continue")`, and that method assigned its argument
   to the card-title label, so the drawn card's name was overwritten by the button's own
   label. `GameManager` now tracks the drawn card's title and passes it, and the panel only
   overwrites the title when a non-empty one is supplied.

3. **`DirectorPlayer.director` was never wired, so no cutscene could ever
   play.** `SceneBuilder.CreateDirector()` added a `PlayableDirector` and a
   `DirectorPlayer` to the same GameObject (`CutsceneDirector`) but never
   assigned the reference between them, so the built `Game` scene carried
   `director: {fileID: 0}`. In that state `DirectorPlayer.PlayAsync` returns
   immediately, having logged "DirectorPlayer has no PlayableDirector. Rebuild
   with TruthCardGame → Build Scenes." — advice that could not work, because the
   builder that menu item runs was the thing omitting the wiring. Every cutscene
   was therefore a silent no-op, and the log line explaining it appeared only at
   the moment the card played. `SceneBuilder.CreateDirector()` now assigns the
   field, and the checked-in scene was corrected to match.

All five are pinned. The first in EditMode
(`ContentAdapterTests.CutsceneAction_AuthoredIdWithoutTimeline_DeclaresResourceButBindsNothing`);
the second by the PlayMode scene fixture; the third by
`GameScene_CutsceneWiring_IsComplete` — checked by reverting the scene's field to
`{fileID: 0}` and confirming it fails with exactly the wiring message, then
restoring it; the fourth by the card-level cutscene tests, which reach the phase
after the cutscene and drew nothing until the deck carried the ending card (the
first run failed with `The phase after the cutscene must be able to draw`, then
with the graph budget error, each time pointing at one half of the gap); and the
fifth by running `SampleContentBuilder` and `SceneBuilder` headlessly — see
[`unity-cli.md`](../../unity-cli.md).

4. **No sample session could finish.** Two halves of one content gap, both
   fixed in `SampleContentBuilder`:
   1. **The ending card was not in the deck.** `EnsureSampleSessions` authored
      `TheEnd` and gave both sessions an `ending` phase, but the deck was written
      from a card list that omitted it — so the run entered its last phase and
      threw `No eligible Card … (Wind Down). Query: ALL [ending]`. Every play of
      every session died there; `GameManager.RunAdvance` logged the exception and
      the panel simply stopped moving. The deck now carries the ending card
      (eight cards), which is also what made the card-level cutscene test able to
      assert that the run continues past its phase.
   2. **`TheEnd` had no wait and no progress.** `GetOrCreateCard` does not
      re-apply its action list to an existing asset, and the ending card is
      authored in `EnsureSampleSessions`, outside the pacing pass — so the card
      on disk carried only `CouragePlusOne`. With nothing blocking it never
      yielded, and with no increment the phase never completed: it redrew the same
      card until the graph's own budget stopped it (`Graph execution safety
      budget of 10000 was exceeded … node 'pn-…-draw'`). The ending card now goes
      through the same pacing pass as every other card.
5. **`TruthCardGame → Build Scenes` threw and stopped halfway.** `SettingsDialog`
   dropped its session-length modifier (`lengthSlider`, `lengthValue`) when phase
   cadence became authored graph control, but `SceneBuilder.CreateSettingsDialog`
   still wired those fields; `SetField` dereferenced the missing property and
   threw `NullReferenceException` inside `BuildMainMenuScene`, which aborted
   before `BuildGameSetupScene`, `BuildGameScene` and the Build Settings write.
   Fixed twice over: the stale wiring is gone, and `SetField` now fails *loudly*
   and *completely* — it names the component and the field, refuses to wire a
   null asset, and continues the build, because an unexplained NRE here leaves
   half the scenes unrebuilt with no clue which field was wrong. Verified by
   running the builder headlessly: `Scenes built and registered in Build
   Settings` with exit 0.

### One wart the suite exposed: a cancelled action is logged as an error

Destroying the scene cancels `GameManager`'s lifetime token, and sample cards
keep a 5 s nonblocking `Debug_Continuous` action in flight, so nearly every
scene test tears down while one is mid-delay. `ActionExecutor` reports that
cancellation as

    ACTION FAILED Debug Log [3d0a…]: The operation was canceled.

through `UnityGameLog.Error` → `Debug.LogError`, which the test framework counts
as an unhandled error and fails the test with — even though the test body passed.
`BackgroundActionTracker` already states the right rule for the very same
exception ("Cancellation is expected during teardown; nothing to report"), so the
nonblocking diagnostics path contradicts its own tracker, and a player that
changes scene or quits mid-card emits the same errors. Until Core agrees that a
cancelled action is not a failed one, the fixture suppresses *unexpected logs*
for its teardown only (assertion failures are never ignored); the Core fix is a
semantics change and is listed as a follow-up rather than taken here.

### One real failure found, and it was a stale test

The first PlayMode run of this ticket produced **1 passed, 1 failed**:

    TruthCardGame.Core.GraphExecutionException: Session '…' runtime error:
    Graph execution safety budget of 10000 was exceeded while processing action.
    node 'seq-…', used 10001. The graph may contain a non-yielding loop.

It is **not** a regression from ticket 00. `Phase.ToDefinition()` hard-codes the
phase template's progress check at 100 and documents that `minCards`/`maxCards`
are obsolete and *ignored by conversion*; `Card.ToDefinition()` documents that
"pacing and progress are ordinary authored instances; conversion never injects
either." The test still assumed the old behaviour — `maxCards = 1` implying a
target of 10 plus an injected +10 progress instance — so progress never
advanced, the `CardExecutor → done(false)` edge redrew the single card, and the
graph budget stopped it. Throwing was the correct outcome; silently looping was
not on offer.

Fix: the test now authors its increment explicitly (`IncrementProgressAction`,
amount 100), matching `Assets/Editor/SampleContentBuilder.cs`, which gives every
ordinary card an explicit `Increment_Progress10`. The test's original
expectation (one draw completes the phase) is preserved, and the re-run is 2/2.

This had been invisible because earlier headless PlayMode attempts in this
repository produced no result XML at all, and `Docs/GraphWorkbenchFinish/00-baseline.md`
records those runs as "unavailable, not passed". The runner from this ticket is
what made the failure observable.

## Source map (what moved)

| Concern | Location |
|---|---|
| Shared mapping source (loader, repositories, migrations, commands) | `Assets/Scripts/Portable/Game.Content.Sqlite/` with `Game.Content.Sqlite.asmdef` |
| DotNet project that links it | `DotNet/Game.Content.Sqlite/Game.Content.Sqlite.csproj` |
| Canonical core schema scripts (single copy, 11 files) | `Assets/StreamingAssets/GameContentSchema/` |
| Unity schema reader | `Assets/Scripts/Game/UnitySchemaScripts.cs` → `SchemaScripts.Reader` |
| Script resolution shared by both hosts | `Assets/Scripts/Portable/Game.Content.Sqlite/SchemaScripts.cs` |
| Ticket-00 tests | `Assets/Tests/EditMode/SqliteProviderSmokeTests.cs`, `Assets/Tests/EditMode/SqliteContentIntegrationTests.cs`, `DotNet/Game.Content.Sqlite.Tests/SchemaScriptSourceTests.cs` |
| Headless Unity runner | `scripts/run-unity-tests.sh` (docs in `unity-cli.md`) |

Note for anyone following older documents: `TICKET-STACK.md` and
`Docs/MilestoneCReadinessHotfix/00-baseline.md` still name
`DotNet/Game.Content.Sqlite/GameContentSnapshotLoader.cs`. The file is now at
`Assets/Scripts/Portable/Game.Content.Sqlite/GameContentSnapshotLoader.cs`; the
DotNet project path is unchanged.
