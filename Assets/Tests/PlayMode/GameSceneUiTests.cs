using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Timeline;
using UnityEngine.UI;
using TruthCardGame.Core;

namespace TruthCardGame.Tests
{
    /// <summary>
    /// Boots the authored Game scene and drives it through the real
    /// MonoBehaviour stack: GameManager owns the engine, GamePanel renders the
    /// draw, and the Continue button and choice overlay that SceneBuilder wired
    /// (and GamePanel.Awake attached) are pressed the way a player would.
    ///
    /// HostSmokeTests deliberately bypasses all of this — no scene, no
    /// MonoBehaviour, no UI — so until this fixture the scene graph had zero
    /// PlayMode coverage. That is exactly the wiring Ticket 04 replaces when
    /// SQLite content loading takes over from the ScriptableObject path.
    ///
    /// GameManager starts the engine with the default spawn options, so the run
    /// seed is the fixed `SessionSpawnOptions.Default` (seed 0) and the draw
    /// order is reproducible: a test may advance until a specific authored card
    /// comes up rather than hoping it does.
    ///
    /// Editor-hosted by construction: it loads the authored session/deck with
    /// AssetDatabase and reads GamePanel's private [SerializeField] view
    /// references the way the editor does, so it runs in the editor PlayMode
    /// runner rather than a player build.
    /// </summary>
    public class GameSceneUiTests
    {
        private const string GameSceneName = "Game";
        private const string SessionLibraryPath = "Assets/Content/Sessions/SessionLibrary.asset";
        private const string StarterDeckPath = "Assets/Content/Deck/StarterDeck.asset";
        private const string WaitingStatus = "Waiting for Continue.";

        // Generous ceilings for scene activation plus a card run, guarding a
        // hang rather than describing an expected wait. Sample phases hold
        // blocking debug beats (2.5 s of scaled time) between draws.
        private const int FrameBudget = 4000;
        private const float AdvanceDeadlineSeconds = 120f;

        // A sample phase draws ten cards and the choice card is eligible in the
        // Intense session's first two phases. With the fixed default seed the
        // observed run reaches it on the fourth card, after three advances
        // (TheCrowdWatches, TheCrowdWatches, DareAndCelebrate, then the choice
        // card); this bound stays comfortably above that while still failing if
        // tag/weighting changes push the choice card out of the phase entirely.
        private const int MaxDrawsToReachChoice = 8;

        // The authored sample content declares this cutscene resource — so
        // content validation passes and sessions boot — but Cutscene_Intro has
        // no TimelineAsset assigned and no Phase queries the `cutscene` tag, so
        // nothing registers it. That authored state makes it the fixture's
        // vehicle for the unregistered-resource path.
        private const string CutsceneResourceId = "cs:intro";

        // Playback length of the stand-in timeline the cutscene tests build at
        // runtime. Long enough to be many frames, short enough not to pad a run.
        private const double CutsceneSeconds = 0.35;

        // Assets the cutscene tests create at runtime; destroyed after each test.
        private readonly List<UnityEngine.Object> _runtimeObjects = new List<UnityEngine.Object>();

        private sealed class SceneRefs
        {
            public GameManager Manager;
            public GamePanel Panel;
            public Text Title;
            public Text Status;
            public Button Continue;
            public GameObject PromptRoot;
            public Text PromptText;
            public RectTransform PromptContainer;
            public GameSessionEngine Engine;
            public DirectorPlayer Cutscene;
            public PlayableDirector Director;
            public TimelineAsset Timeline;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SessionConfig.SelectedSession = null;

            // Destroying the scene cancels GameManager's lifetime token, and the
            // sample deck keeps a 5 s nonblocking 'Debug_Continuous' action in
            // flight on every card, so nearly every scene test tears down while
            // one is mid-delay. Core reports that cancellation as
            //   ACTION FAILED Debug Log [...]: The operation was canceled.
            // through UnityGameLog.Error, which the test framework counts as an
            // unhandled error and fails the test with — even though the test
            // body passed and BackgroundActionTracker documents cancellation as
            // expected during teardown. Suppress unexpected *logs* for the
            // teardown only, so that noise cannot race the frames the destroys
            // need. Assertion failures are never ignored.
            LogAssert.ignoreFailingMessages = true;

            var scene = SceneManager.GetSceneByName(GameSceneName);
            if (scene.isLoaded)
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    UnityEngine.Object.Destroy(root);
                }
            }

            // Two frames: one for the destroys, one for OnDestroy's cancellation
            // to propagate into the action still awaiting its delay.
            yield return null;
            yield return null;

            // Scene first, then the runtime assets the scene's director was
            // pointed at, so nothing is still referencing them when they go.
            foreach (var runtime in _runtimeObjects)
            {
                if (runtime != null) UnityEngine.Object.Destroy(runtime);
            }
            _runtimeObjects.Clear();

            yield return null;

            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        public IEnumerator GameScene_PlaysACardThroughGameManagerAndTheContinueButton()
        {
            // Arrange — the setup screen's job: pick a real session off the
            // authored library and hand it across the scene load. Relaxing's
            // first phase draws solo cards, so no prompt can interpose.
            var session = LoadSampleSession("Relaxing", out var deck);
            var eligibleTitles = EligibleCardTitles(session, deck, 0);
            CollectionAssert.IsNotEmpty(eligibleTitles,
                $"No deck card satisfies '{session.Title}'s first phase, so the scene cannot draw.");

            var refs = new SceneRefs();
            yield return BootGameScene(session, refs);

            Assert.IsFalse(refs.PromptRoot.activeSelf,
                "Only a ChoiceAction shows the overlay; a solo card must not.");

            // GameManager.Start runs the session automatically; it must stop at
            // the first authored WaitForContinue and leave the panel showing the
            // card that was drawn.
            yield return WaitForStatus(refs, WaitingStatus);

            Assert.AreEqual(WaitingStatus, refs.Status.text,
                "GameManager.Start must run to the first WaitForContinue. A 'Ready.' here means " +
                "Awake's deck/session guard returned early — check the console for its error.");
            Assert.IsTrue(refs.Continue.interactable, "Continue must be enabled while waiting.");
            CollectionAssert.Contains(eligibleTitles, refs.Title.text,
                $"The waiting panel must name the drawn card, but shows '{refs.Title.text}'. " +
                $"Eligible first-phase cards: {string.Join(", ", eligibleTitles)}.");

            // Act — press Continue through the UI, not by calling GameManager.
            var drawn = new List<string>();
            refs.Engine.CardStarted += card => drawn.Add(card.Title);

            refs.Continue.onClick.Invoke();

            yield return WaitForStatus(refs, WaitingStatus);

            // Assert — the button's listener reached GameManager.DrawNextCard,
            // the engine played the next card, and the panel reflects it.
            CollectionAssert.IsNotEmpty(drawn,
                "Clicking the panel's Continue button must draw the next card through GameManager.");
            Assert.IsTrue(refs.Continue.interactable, "Continue must be enabled again for the next card.");
            Assert.AreEqual(drawn[drawn.Count - 1], refs.Title.text,
                "The waiting panel must keep showing the card it just played.");
            CollectionAssert.Contains(eligibleTitles, drawn[drawn.Count - 1],
                "The button must draw from the session's first-phase eligible cards.");
        }

        [UnityTest]
        public IEnumerator GameScene_ChoiceCard_ResolvesItsPromptThroughTheOverlay()
        {
            // Arrange — Intense is the sample session whose phases can draw the
            // authored choice card. Expectations come from the authored assets,
            // so relabelling or re-branching the choice does not silently pass.
            var session = LoadSampleSession("Intense", out var deck);
            var choice = FindChoiceAction(deck, out var choiceCard);

            var expectedLabels = new List<string>();
            var statOptionIndex = -1;
            var statKey = string.Empty;
            var statAmount = 0;
            for (var i = 0; i < choice.Options.Count; i++)
            {
                var option = choice.Options[i];
                Assert.IsNotNull(option, "The authored choice must not contain null options.");
                expectedLabels.Add(option.Label);
                if (statOptionIndex >= 0 || !(option.Action is StatIncreaseAction statAction)) continue;

                statOptionIndex = i;
                var serialized = new SerializedObject(statAction);
                statKey = serialized.FindProperty("statKey").stringValue;
                statAmount = serialized.FindProperty("amount").intValue;
            }
            Assert.GreaterOrEqual(statOptionIndex, 0,
                "One authored option must carry a stat change, so the test can prove the chosen branch ran.");

            var refs = new SceneRefs();
            yield return BootGameScene(session, refs);

            // Act 1 — the automatic first run plays cards until the choice card
            // comes up. UnityPromptService shows the overlay synchronously as
            // the card's ChoiceAction runs, and the engine then awaits the answer.
            var deadline = Time.realtimeSinceStartup + AdvanceDeadlineSeconds;
            var draws = 0;
            while (!refs.PromptRoot.activeSelf && draws < MaxDrawsToReachChoice
                   && !refs.Engine.IsComplete && Time.realtimeSinceStartup < deadline)
            {
                if (refs.Continue.interactable)
                {
                    refs.Continue.onClick.Invoke();
                    draws++;
                }
                yield return null;
            }

            Assert.IsTrue(refs.PromptRoot.activeSelf,
                $"The choice card '{choiceCard.Title}' must be drawn within {MaxDrawsToReachChoice} advances " +
                $"(the scene runs the fixed default seed) and show its prompt overlay. Advances taken: {draws}.");
            Assert.AreEqual(choice.Prompt, refs.PromptText.text,
                "The overlay must show the choice action's authored prompt.");
            Assert.AreEqual(choiceCard.Title, refs.Title.text,
                "The prompt appears over the card that is being played.");

            var buttons = new List<Button>();
            var labels = new List<string>();
            ReadPromptButtons(refs.PromptContainer, buttons, labels);
            CollectionAssert.AreEqual(expectedLabels, labels,
                "The overlay must offer the authored options, in authored order.");

            // Act 2 — answer through the real overlay button.
            var before = refs.Engine.Player.Stats.Get(statKey);
            buttons[statOptionIndex].onClick.Invoke();

            yield return WaitForStatus(refs, WaitingStatus);

            // Assert — the overlay closed and the chosen branch actually ran.
            Assert.IsFalse(refs.PromptRoot.activeSelf, "Answering must hide the overlay.");
            Assert.AreEqual(WaitingStatus, refs.Status.text,
                "After the answer, the card should run on to its authored WaitForContinue.");
            Assert.AreEqual(before + statAmount, refs.Engine.Player.Stats.Get(statKey),
                $"Choosing '{expectedLabels[statOptionIndex]}' must run its nested stat action through the UI.");
            Assert.IsTrue(refs.Continue.interactable, "Continue must be enabled once the choice resolves.");
        }

        // ---------- cutscene: DirectorPlayer + Timeline playback ----------

        [UnityTest]
        public IEnumerator GameScene_CutsceneWiring_IsComplete()
        {
            // Arrange — the scene as SceneBuilder writes it and GameManager boots it.
            var session = LoadSampleSession("Relaxing", out _);
            var refs = new SceneRefs();
            yield return BootGameScene(session, refs);

            refs.Cutscene = UnityEngine.Object.FindFirstObjectByType<DirectorPlayer>();
            Assert.IsNotNull(refs.Cutscene,
                "The Game scene must contain the DirectorPlayer SceneBuilder builds for cutscenes.");
            Assert.AreSame(refs.Cutscene, SerializedReference<DirectorPlayer>(refs.Manager, "directorPlayer"),
                "GameManager must receive the DirectorPlayer the scene actually carries, or its cutscene " +
                "service is a different, unbound object than the one the scene plays on.");

            // Assert — the field SceneBuilder used to leave at None. Unwired, every
            // cutscene logs 'DirectorPlayer has no PlayableDirector' at play time
            // and returns, so playback silently never happens.
            refs.Director = SerializedReference<PlayableDirector>(refs.Cutscene, "director");
            Assert.IsNotNull(refs.Director,
                "DirectorPlayer.director is not wired, so no cutscene can play. SceneBuilder must " +
                "assign the PlayableDirector it creates alongside DirectorPlayer, and the Game scene " +
                "must be rebuilt (TruthCardGame → Build Scenes) so the wiring lands in the scene asset.");
            Assert.AreSame(refs.Cutscene.gameObject, refs.Director.gameObject,
                "SceneBuilder builds DirectorPlayer and its PlayableDirector on one CutsceneDirector object.");
        }

        [UnityTest]
        public IEnumerator DirectorPlayer_PlaysARegisteredTimeline_ToCompletion()
        {
            // Arrange — the scene's own DirectorPlayer, bound through the same
            // conversion path authored content uses (see the helper below).
            var refs = new SceneRefs();
            yield return BootSceneWithRegisteredCutscene(refs);

            var start = Time.realtimeSinceStartupAsDouble;
            var playback = refs.Cutscene.PlayAsync(CutsceneResourceId, CancellationToken.None);

            // Play runs synchronously up to PlayAsync's first yield, so the
            // director already owns the resolved asset.
            Assert.AreSame(refs.Timeline, refs.Director.playableAsset,
                "PlayAsync must hand the PlayableDirector the asset the registry resolved for the resource id.");

            // Act — let real playback run to its end.
            var observedPlaying = false;
            for (var frame = 0; frame < FrameBudget && !playback.IsCompleted; frame++)
            {
                if (refs.Cutscene.IsPlaying) observedPlaying = true;
                yield return null;
            }

            // Assert — the served contract: play, then complete when it stops.
            Assert.IsTrue(playback.IsCompleted,
                "PlayAsync must return once the timeline stops. Still pending here means the director never " +
                "left PlayState.Playing, which would hang the session that awaits the cutscene.");
            Assert.IsFalse(playback.IsFaulted, "Playback must not fault: " + playback.Exception);
            Assert.IsTrue(observedPlaying,
                "The timeline must genuinely run: the director was never observed in PlayState.Playing.");
            Assert.AreNotEqual(PlayState.Playing, refs.Director.state,
                "Playback must be over by the time PlayAsync returns.");
            Assert.GreaterOrEqual(Time.realtimeSinceStartupAsDouble - start, CutsceneSeconds * 0.75,
                $"PlayAsync must await the timeline's real {CutsceneSeconds}s of playback rather than returning " +
                "immediately — an instant return would mean it never tracked the director's state.");
        }

        [UnityTest]
        public IEnumerator DirectorPlayer_CancellingPlayback_StopsTheDirectorAndReportsCancellation()
        {
            var refs = new SceneRefs();
            yield return BootSceneWithRegisteredCutscene(refs);

            var cts = new CancellationTokenSource();
            var playback = refs.Cutscene.PlayAsync(CutsceneResourceId, cts.Token);

            for (var frame = 0; frame < 60 && !refs.Cutscene.IsPlaying; frame++)
            {
                yield return null;
            }
            Assert.IsTrue(refs.Cutscene.IsPlaying,
                "The timeline must actually be playing before the cancel, or this test proves nothing.");

            // Act — cancel mid-playback, the way a scene change or teardown does.
            cts.Cancel();

            for (var frame = 0; frame < FrameBudget && !playback.IsCompleted; frame++)
            {
                yield return null;
            }

            Assert.IsTrue(playback.IsCompleted,
                "Cancelling must end PlayAsync rather than leaving it waiting on a stopped director.");
            Assert.IsTrue(playback.IsCanceled,
                "DirectorPlayer's contract is that cancellation propagates as cancellation — never as success.");
            Assert.AreNotEqual(PlayState.Playing, refs.Director.state,
                "Cancelling must stop the director; orphaned playback would keep running behind the game.");
            cts.Dispose();
        }

        [UnityTest]
        public IEnumerator DirectorPlayer_UnregisteredResource_LogsTheMissingCutsceneAndReturnsCleanly()
        {
            var session = LoadSampleSession("Relaxing", out var deck);
            var refs = new SceneRefs();
            yield return BootGameScene(session, refs);

            refs.Cutscene = UnityEngine.Object.FindFirstObjectByType<DirectorPlayer>();
            Assert.IsNotNull(refs.Cutscene, "The Game scene must contain a DirectorPlayer.");
            refs.Director = SerializedReference<PlayableDirector>(refs.Cutscene, "director");
            Assert.IsNotNull(refs.Director,
                "DirectorPlayer.director is not wired; fix SceneBuilder before trusting a cutscene result.");

            // First, what authored content actually converts to today, the same
            // way GameManager.Awake does it: the 'cs:intro' resource is declared
            // (so ContentReferenceValidator accepts the deck and every session
            // still boots) but Cutscene_Intro carries no TimelineAsset, so
            // nothing is registered under it. Both halves are asserted, so this
            // stops holding only when someone authors the timeline — which is
            // the fix, not a break.
            var authoredRegistry = new CutsceneBindingRegistry();
            var authoredContent = new UnityContentGraphBuilder(authoredRegistry).Build(session, deck);
            Assert.IsTrue(authoredContent.Resources.Exists(r => r.Id == CutsceneResourceId && r.Kind == "cutscene"),
                $"Authored content must declare the '{CutsceneResourceId}' resource row the cutscene instance " +
                "references, or content validation rejects every session that ships the card.");
            var authoredAction = FindAuthoredCutsceneAction(deck, CutsceneResourceId);
            Assert.IsNotNull(authoredAction,
                $"Sample content must still carry a cutscene action with resource id '{CutsceneResourceId}'.");
            Assert.AreEqual(authoredAction.HasTimeline,
                authoredRegistry.TryResolve(CutsceneResourceId, out var registeredForAuthored),
                "Registration must follow the authored TimelineAsset and nothing else: an assigned timeline " +
                "registers, an unassigned one does not. This test's remaining steps assume the unassigned state.");
            Assert.IsTrue(registeredForAuthored == null || authoredAction.HasTimeline,
                "Nothing may be registered for an action with no timeline.");

            // A populated table that simply has no entry for the authored id.
            var registry = new CutsceneBindingRegistry();
            registry.Register("cs:elsewhere", CreateRuntimeTimeline("ElsewhereCutscene", CutsceneSeconds));
            refs.Cutscene.Bind(registry);

            var before = refs.Director.playableAsset;

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                $"[TruthCardGame] No cutscene registered for resource '{CutsceneResourceId}'.")));

            // Act
            var playback = refs.Cutscene.PlayAsync(CutsceneResourceId, CancellationToken.None);

            for (var frame = 0; frame < FrameBudget && !playback.IsCompleted; frame++)
            {
                yield return null;
            }

            // Assert — a missing cutscene must be a logged no-op that the
            // session survives, not a hang or a fault.
            Assert.IsTrue(playback.IsCompleted,
                "An unregistered cutscene resource must complete immediately; the session awaiting it " +
                "would otherwise stop dead on a card the player cannot see past.");
            Assert.IsFalse(playback.IsFaulted, "A missing cutscene must not fault: " + playback.Exception);
            Assert.AreSame(before, refs.Director.playableAsset,
                "Nothing may be played for an unregistered resource.");
            Assert.IsFalse(refs.Cutscene.IsPlaying,
                "An unregistered resource must not start playback.");
        }

        [UnityTest]
        public IEnumerator DirectorPlayer_WithoutAPlayableDirector_LogsTheRebuildHintInsteadOfThrowing()
        {
            // The diagnostic behind the silent no-op above: with no director
            // wired, PlayAsync logs this and returns. That message is also what
            // hid the SceneBuilder defect — the scene obeyed it and still did
            // nothing, because the builder never assigned the field.
            var go = new GameObject("BareDirectorPlayer");
            _runtimeObjects.Add(go);
            var player = go.AddComponent<DirectorPlayer>();

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                "[TruthCardGame] DirectorPlayer has no PlayableDirector. Rebuild with TruthCardGame \u2192 Build Scenes.")));

            // Act
            var playback = player.PlayAsync(CutsceneResourceId, CancellationToken.None);

            for (var frame = 0; frame < FrameBudget && !playback.IsCompleted; frame++)
            {
                yield return null;
            }

            Assert.IsTrue(playback.IsCompleted,
                "A missing PlayableDirector must not strand the session waiting on a cutscene.");
            Assert.IsFalse(playback.IsFaulted, "A missing PlayableDirector must be a logged no-op: " + playback.Exception);
            Assert.IsFalse(player.IsPlaying, "Nothing can be playing without a PlayableDirector.");
        }

        [UnityTest]
        public IEnumerator GameScene_CutsceneCard_PlaysItsCutscene_AndTheSessionMovesOn()
        {
            // Arrange — the authored cutscene card, the authored phase that can
            // draw it, and the phase the run continues into afterwards. Sample
            // content ships the resource id but no hand-authored TimelineAsset
            // (SampleContentBuilder documents that the timeline is authored in
            // the Timeline window), so a stand-in stands in for it here. The
            // card, the phase, the conversion, the engine and the scene's own
            // DirectorPlayer are all the real thing.
            var session = LoadSampleSession("Relaxing", out var deck);
            var cutsceneCard = FindCutsceneCard(deck, out var resourceId);
            Assert.IsNotNull(cutsceneCard, "Sample content must ship a card whose action plays a cutscene.");
            Assert.AreEqual(CutsceneResourceId, resourceId,
                "The authored cutscene resource id is the stable key portable content references; update this test " +
                "only if the sample deliberately renames it.");

            var cutscenePhase = PhaseIndexAccepting(session, cutsceneCard);
            Assert.GreaterOrEqual(cutscenePhase, 0,
                $"No phase in '{session.Title}' accepts the '{cutsceneCard.Title}' tags " +
                $"({string.Join(", ", cutsceneCard.Tags)}), so the card can never be drawn. A phase accepts a card only " +
                "when the card carries every tag the phase asks for.");
            var nextPhaseCards = EligibleCardTitles(session, deck, cutscenePhase + 1);
            CollectionAssert.IsNotEmpty(nextPhaseCards,
                "The phase after the cutscene must be able to draw, so the run has somewhere to go.");

            var refs = new SceneRefs();
            yield return BootGameScene(session, refs);

            // The action either carries a hand-authored timeline or it does not.
            // When it does, conversion already registered it and this plays that
            // real asset; when it does not, a labelled stand-in keeps playback
            // provable end to end until someone authors it.
            var authoredTimeline = FindAuthoredCutsceneAction(deck, resourceId)?.Timeline;
            var expectedTimeline = authoredTimeline;
            var expectedSeconds = authoredTimeline != null ? (float)authoredTimeline.duration : CutsceneSeconds;
            var source = authoredTimeline != null
                ? $"the hand-authored timeline on Cutscene_Intro ({authoredTimeline.duration:0.00}s)"
                : $"the fixture's stand-in timeline ({CutsceneSeconds}s)";
            if (authoredTimeline == null)
            {
                BindSceneCutscene(refs, resourceId);
                expectedTimeline = refs.Timeline;
            }
            else
            {
                ResolveSceneDirector(refs);
            }

            var run = new CutsceneRun();
            refs.Engine.CardStarted += card => run.Drawn.Add(card.Title);

            // Act — play the session through the Continue button until the
            // cutscene card comes up and reaches its authored WaitForContinue.
            yield return AdvanceThroughCutsceneCard(refs, cutsceneCard.Title, DrawsBeforeCutscene(cutscenePhase), run);

            Assert.IsTrue(run.Reached,
                $"'{cutsceneCard.Title}' must be drawn by the cutscene phase (phase index {cutscenePhase}) within " +
                $"{DrawsBeforeCutscene(cutscenePhase)} advances. Cards drawn: {string.Join(", ", run.Drawn)}.");

            // Assert — the engine's cutscene service played the registered
            // asset, for its real duration, before the card moved on.
            Assert.AreSame(expectedTimeline, refs.Director.playableAsset,
                $"The cutscene card's instance must resolve the authored resource id and hand {source} to the " +
                "scene's PlayableDirector.");
            if (expectedSeconds > 0.05f)
            {
                // A zero-length timeline is over before this loop can sample it,
                // so the observation only means something for a real duration.
                Assert.IsTrue(run.SawPlaying,
                    $"The director must genuinely run while the card is on screen ({source}).");
            }
            Assert.GreaterOrEqual(run.SinceCutsceneStart, expectedSeconds * 0.75,
                $"The blocking cutscene must hold the card for the {expectedSeconds:0.00}s of {source}; the card " +
                $"reached its wait after {run.SinceCutsceneStart:0.00}s, so the engine did not await the timeline.");
            Assert.AreEqual(cutsceneCard.Title, refs.Title.text,
                "The panel keeps showing the card that just played its cutscene.");

            // Act/Assert — the card's full increment completes the cutscene phase
            // on that single draw, so the run moves on rather than repeating it.
            var before = run.Drawn.Count;
            Assert.IsTrue(refs.Continue.interactable, "Continue must be enabled at the cutscene card's authored wait.");
            refs.Continue.onClick.Invoke();

            for (var frame = 0; frame < FrameBudget && run.Drawn.Count == before; frame++)
            {
                yield return null;
            }

            Assert.Greater(run.Drawn.Count, before,
                "Playing the cutscene must not stall the session; Continue must draw the next card.");
            var next = run.Drawn[run.Drawn.Count - 1];
            CollectionAssert.Contains(nextPhaseCards, next,
                $"After the cutscene the run must continue into the next phase, whose cards are " +
                $"{string.Join(", ", nextPhaseCards)}, but drew '{next}'.");
        }

        [UnityTest]
        public IEnumerator GameScene_CutsceneCard_WithNoRegisteredTimeline_LogsAndTheSessionMovesOn()
        {
            // The other half of the authored state: the card is drawable and its
            // resource id is declared, but nothing is registered under it, so the
            // play is a logged no-op. A populated registry that simply lacks the
            // authored key keeps this deterministic however content changes.
            var session = LoadSampleSession("Relaxing", out var deck);
            var cutsceneCard = FindCutsceneCard(deck, out var resourceId);
            Assert.IsNotNull(cutsceneCard, "Sample content must ship a card whose action plays a cutscene.");
            var cutscenePhase = PhaseIndexAccepting(session, cutsceneCard);
            Assert.GreaterOrEqual(cutscenePhase, 0,
                $"No phase in '{session.Title}' accepts the '{cutsceneCard.Title}' tags, so the card can never be drawn.");
            var nextPhaseCards = EligibleCardTitles(session, deck, cutscenePhase + 1);

            var refs = new SceneRefs();
            yield return BootGameScene(session, refs);
            BindSceneCutscene(refs, "cs:elsewhere");

            var beforePlayback = refs.Director.playableAsset;

            // The cutscene card is drawn once — its full increment ends the phase
            // on that draw — so exactly one error is expected, and expecting it
            // before the draw is what makes an unexpected second one fail.
            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape(
                $"[TruthCardGame] No cutscene registered for resource '{resourceId}'.")));

            var run = new CutsceneRun();
            refs.Engine.CardStarted += card => run.Drawn.Add(card.Title);

            // Act
            yield return AdvanceThroughCutsceneCard(refs, cutsceneCard.Title, DrawsBeforeCutscene(cutscenePhase), run);

            // Assert
            Assert.IsTrue(run.Reached,
                $"'{cutsceneCard.Title}' must still be drawn — a missing cutscene is a runtime no-op, not an " +
                $"eligibility change. Cards drawn: {string.Join(", ", run.Drawn)}.");
            Assert.AreSame(beforePlayback, refs.Director.playableAsset,
                "Nothing may be played for a resource the registry does not hold.");
            Assert.IsFalse(run.SawPlaying, "Playback must not start for an unregistered cutscene.");

            var drawnBefore = run.Drawn.Count;
            Assert.IsTrue(refs.Continue.interactable,
                "The logged no-op must still reach the card's authored WaitForContinue.");
            refs.Continue.onClick.Invoke();

            for (var frame = 0; frame < FrameBudget && run.Drawn.Count == drawnBefore; frame++)
            {
                yield return null;
            }

            Assert.Greater(run.Drawn.Count, drawnBefore,
                "A missing cutscene must not stall the session; Continue must draw the next card.");
            CollectionAssert.Contains(nextPhaseCards, run.Drawn[run.Drawn.Count - 1],
                $"The run must continue into the next phase ({string.Join(", ", nextPhaseCards)}) after the " +
                "cutscene card's missing-resource no-op.");
        }

        // ---------- scene boot ----------

        private static IEnumerator BootGameScene(Session session, SceneRefs refs)
        {
            SessionConfig.SelectedSession = session;
            SceneManager.LoadScene(GameSceneName, LoadSceneMode.Single);

            for (var frame = 0; frame < FrameBudget && (refs.Manager == null || refs.Panel == null); frame++)
            {
                yield return null;
                refs.Manager = UnityEngine.Object.FindFirstObjectByType<GameManager>();
                refs.Panel = UnityEngine.Object.FindFirstObjectByType<GamePanel>();
            }

            Assert.IsNotNull(refs.Manager, "The Game scene must contain a GameManager.");
            Assert.IsNotNull(refs.Panel, "The Game scene must contain a GamePanel.");

            // The view references SceneBuilder wrote, read the way the editor does.
            refs.Title = SerializedReference<Text>(refs.Panel, "cardTitle");
            refs.Status = SerializedReference<Text>(refs.Panel, "status");
            refs.Continue = SerializedReference<Button>(refs.Panel, "drawNextButton");
            refs.PromptRoot = SerializedReference<GameObject>(refs.Panel, "promptRoot");
            refs.PromptText = SerializedReference<Text>(refs.Panel, "promptText");
            refs.PromptContainer = SerializedReference<RectTransform>(refs.Panel, "promptContainer");

            Assert.IsNotNull(refs.Title, "GamePanel.cardTitle is not wired.");
            Assert.IsNotNull(refs.Status, "GamePanel.status is not wired.");
            Assert.IsNotNull(refs.Continue, "GamePanel.drawNextButton is not wired.");
            Assert.IsNotNull(refs.PromptRoot, "GamePanel.promptRoot is not wired.");
            Assert.IsNotNull(refs.PromptText, "GamePanel.promptText is not wired.");
            Assert.IsNotNull(refs.PromptContainer, "GamePanel.promptContainer is not wired.");
            Assert.AreSame(refs.Manager, SerializedReference<GameManager>(refs.Panel, "gameManager"),
                "GamePanel's buttons are wired to a different GameManager than the scene's.");

            refs.Engine = EngineOf(refs.Manager);
        }

        private static IEnumerator WaitForStatus(SceneRefs refs, string status)
        {
            for (var frame = 0; frame < FrameBudget && refs.Status.text != status; frame++)
            {
                yield return null;
            }
        }

        // ---------- content helpers ----------

        private static Session LoadSampleSession(string title, out CardDeck deck)
        {
            var library = AssetDatabase.LoadAssetAtPath<SessionLibrary>(SessionLibraryPath);
            Assert.IsNotNull(library, $"Missing {SessionLibraryPath}. Run TruthCardGame → Build Scenes.");
            deck = AssetDatabase.LoadAssetAtPath<CardDeck>(StarterDeckPath);
            Assert.IsNotNull(deck, $"Missing {StarterDeckPath}. Run TruthCardGame → Build Scenes.");

            var session = FindSession(library, title);
            Assert.IsNotNull(session, $"The sample library must offer the '{title}' session.");
            return session;
        }

        private static Session FindSession(SessionLibrary library, string title)
        {
            foreach (var session in library.Sessions)
            {
                if (session != null && session.Title == title) return session;
            }
            return null;
        }

        private static ChoiceAction FindChoiceAction(CardDeck deck, out Card card)
        {
            foreach (var candidate in deck.Cards)
            {
                if (candidate == null) continue;
                foreach (var action in candidate.Actions)
                {
                    if (action is ChoiceAction choice)
                    {
                        card = candidate;
                        return choice;
                    }
                }
            }
            card = null;
            return null;
        }

        /// <summary>Deck card titles matching one phase's include-query.</summary>
        private static List<string> EligibleCardTitles(Session session, CardDeck deck, int phaseIndex)
        {
            var titles = new List<string>();
            var required = session.Phases[phaseIndex].MustIncludeTags;
            foreach (var card in deck.Cards)
            {
                if (card == null) continue;
                var matches = true;
                foreach (var tag in required)
                {
                    if (!Contains(card.Tags, tag))
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches && !titles.Contains(card.Title)) titles.Add(card.Title);
            }
            return titles;
        }

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            for (var i = 0; i < values.Count; i++)
            {
                if (values[i] == value) return true;
            }
            return false;
        }

        /// <summary>Prompt overlay buttons in authored option order, with their labels.</summary>
        private static void ReadPromptButtons(RectTransform container, List<Button> buttons, List<string> labels)
        {
            for (var i = 0; i < container.childCount; i++)
            {
                var child = container.GetChild(i);
                var button = child.GetComponent<Button>();
                if (button == null) continue;
                var label = child.GetComponentInChildren<Text>();
                buttons.Add(button);
                labels.Add(label != null ? label.text : string.Empty);
            }
        }

        private static T SerializedReference<T>(UnityEngine.Object target, string field)
            where T : UnityEngine.Object
        {
            var property = new SerializedObject(target).FindProperty(field);
            Assert.IsNotNull(property, $"{target.GetType().Name} has no serialized field '{field}'.");
            return property.objectReferenceValue as T;
        }

        /// <summary>
        /// GameManager keeps the engine private on purpose — it is a thin host and
        /// nothing else should drive it — so the test reaches through reflection
        /// instead of widening the component's public surface.
        /// </summary>
        private static GameSessionEngine EngineOf(GameManager manager)
        {
            var field = typeof(GameManager).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "GameManager._engine was renamed or removed; update GameSceneUiTests.");
            var engine = field.GetValue(manager) as GameSessionEngine;
            Assert.IsNotNull(engine,
                "GameManager.Awake must build its engine; null here means the deck/session guard " +
                "returned early — check the console for its error.");
            return engine;
        }

        // ---------- cutscene helpers ----------

        /// <summary>
        /// Boots the Game scene and binds its DirectorPlayer to a registry whose
        /// entry was produced exactly the way conversion produces one: a
        /// CutsceneAction carrying the authored resource id and a TimelineAsset,
        /// collected by UnityContentGraphBuilder — not a hand-filled registry.
        /// GameManager binds this same component in Awake; rebinding only swaps
        /// the resolution table, which is the host's job anyway.
        /// </summary>
        private IEnumerator BootSceneWithRegisteredCutscene(SceneRefs refs)
        {
            var session = LoadSampleSession("Relaxing", out _);
            yield return BootGameScene(session, refs);

            refs.Cutscene = UnityEngine.Object.FindFirstObjectByType<DirectorPlayer>();
            Assert.IsNotNull(refs.Cutscene, "The Game scene must contain the DirectorPlayer SceneBuilder builds.");
            refs.Director = SerializedReference<PlayableDirector>(refs.Cutscene, "director");
            Assert.IsNotNull(refs.Director,
                "DirectorPlayer.director is not wired, so no timeline can play. See the wiring test.");

            refs.Timeline = CreateRuntimeTimeline("RuntimeCutscene", CutsceneSeconds);
            var action = CreateRuntimeCutsceneAction(CutsceneResourceId, refs.Timeline);

            var registry = new CutsceneBindingRegistry();
            var builder = new UnityContentGraphBuilder(registry);
            builder.CollectCutscene(action);

            Assert.IsTrue(registry.TryResolve(CutsceneResourceId, out var resolved),
                "Conversion must register a CutsceneAction's timeline under its authored resource id.");
            Assert.AreSame(refs.Timeline, resolved,
                "The registry must hand back the converted action's own TimelineAsset.");
            Assert.IsTrue(builder.Resources.Exists(r => r.Id == CutsceneResourceId && r.Kind == "cutscene"),
                "Conversion must declare the 'cutscene' resource row the instance references — without it " +
                "content validation rejects every session that ships the card.");

            refs.Cutscene.Bind(registry);
        }

        /// <summary>
        /// A stand-in for the hand-authored timeline sample content does not have
        /// yet: a fixed-length TimelineAsset, empty of tracks, whose duration is
        /// still real. Nothing about TimelineAsset's runtime API requires the
        /// editor to have authored it, so playback can be covered today and the
        /// assertions stay honest about what is being played.
        /// </summary>
        private TimelineAsset CreateRuntimeTimeline(string name, double seconds)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = name;
            timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
            timeline.fixedDuration = seconds;
            _runtimeObjects.Add(timeline);
            return timeline;
        }

        /// <summary>The authored cutscene action the deck carries under a resource id, or null.</summary>
        private static CutsceneAction FindAuthoredCutsceneAction(CardDeck deck, string resourceId)
        {
            foreach (var card in deck.Cards)
            {
                if (card == null) continue;
                foreach (var action in card.Actions)
                {
                    if (action is CutsceneAction cutscene && cutscene.ResourceId == resourceId) return cutscene;
                }
            }
            return null;
        }

        /// <summary>A runtime CutsceneAction carrying an authored id + timeline, without touching the asset on disk.</summary>
        private static CutsceneAction CreateRuntimeCutsceneAction(string resourceId, TimelineAsset timeline)
        {
            var action = ScriptableObject.CreateInstance<CutsceneAction>();
            action.name = "RuntimeCutsceneAction";
            var serialized = new SerializedObject(action);
            serialized.FindProperty("resourceId").stringValue = resourceId;
            serialized.FindProperty("timeline").objectReferenceValue = timeline;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return action;
        }

        // ---------- card-level cutscene helpers ----------

        /// <summary>What a run through the session did, filled in by AdvanceThroughCutsceneCard.</summary>
        private sealed class CutsceneRun
        {
            public readonly List<string> Drawn = new List<string>();
            public bool Reached;
            public bool SawPlaying;
            public double StartedAt = -1;
            public double SinceCutsceneStart = -1;
            public int Draws;
        }

        /// <summary>
        /// Plays the session through the real Continue button until the named card
        /// has been drawn and has reached its authored WaitForContinue, sampling
        /// DirectorPlayer.IsPlaying every frame. It stops as soon as the cutscene
        /// card resolves, so what happens after it is the caller's to assert.
        /// </summary>
        private IEnumerator AdvanceThroughCutsceneCard(SceneRefs refs, string cardTitle, int maxDraws, CutsceneRun run)
        {
            var deadline = Time.realtimeSinceStartup + AdvanceDeadlineSeconds;
            while (run.Draws < maxDraws && !refs.Engine.IsComplete && Time.realtimeSinceStartup < deadline)
            {
                if (refs.Cutscene != null && refs.Cutscene.IsPlaying) run.SawPlaying = true;

                if (run.StartedAt < 0 && LastDrawn(run) == cardTitle)
                {
                    run.StartedAt = Time.realtimeSinceStartupAsDouble;
                }

                if (refs.Status.text == WaitingStatus)
                {
                    if (run.StartedAt >= 0)
                    {
                        // The card that played the cutscene has reached its wait,
                        // so the cutscene is behind us.
                        run.SinceCutsceneStart = Time.realtimeSinceStartupAsDouble - run.StartedAt;
                        run.Reached = true;
                        break;
                    }
                    if (refs.Continue.interactable)
                    {
                        refs.Continue.onClick.Invoke();
                        run.Draws++;
                    }
                }

                yield return null;
            }
        }

        private static string LastDrawn(CutsceneRun run)
        {
            return run.Drawn.Count == 0 ? null : run.Drawn[run.Drawn.Count - 1];
        }

        /// <summary>
        /// Advances the run needs before the cutscene card can appear. The phase
        /// template's progress target is 100 (Phase.ToDefinition) and every sample
        /// card before the cutscene contributes the shared +10 increment, so each
        /// preceding phase is ten draws; the slack covers the cutscene phase's own
        /// draw and the frame a click lands on.
        /// </summary>
        private static int DrawsBeforeCutscene(int cutscenePhaseIndex)
        {
            return 10 * cutscenePhaseIndex + 4;
        }

        /// <summary>The scene's DirectorPlayer and the PlayableDirector it is wired to, both asserted.</summary>
        private void ResolveSceneDirector(SceneRefs refs)
        {
            refs.Cutscene = UnityEngine.Object.FindFirstObjectByType<DirectorPlayer>();
            Assert.IsNotNull(refs.Cutscene, "The Game scene must contain the DirectorPlayer SceneBuilder builds.");
            refs.Director = SerializedReference<PlayableDirector>(refs.Cutscene, "director");
            Assert.IsNotNull(refs.Director,
                "DirectorPlayer.director is not wired, so no timeline can play. See the wiring test.");
        }

        /// <summary>Resolves the scene's director, then binds a registry holding one key, so the authored id resolves only when asked to.</summary>
        private void BindSceneCutscene(SceneRefs refs, string registeredResourceId)
        {
            ResolveSceneDirector(refs);

            refs.Timeline = CreateRuntimeTimeline("RuntimeCutscene", CutsceneSeconds);
            var registry = new CutsceneBindingRegistry();
            registry.Register(registeredResourceId, refs.Timeline);

            // GameManager bound this same component in Awake; rebinding swaps the
            // resolution table, which is the host's job anyway — nothing else in
            // the run plays a cutscene.
            refs.Cutscene.Bind(registry);
        }

        /// <summary>The deck card whose action plays a cutscene, with its authored resource id.</summary>
        private static Card FindCutsceneCard(CardDeck deck, out string resourceId)
        {
            foreach (var card in deck.Cards)
            {
                if (card == null) continue;
                foreach (var action in card.Actions)
                {
                    if (action is CutsceneAction cutscene && !string.IsNullOrEmpty(cutscene.ResourceId))
                    {
                        resourceId = cutscene.ResourceId;
                        return card;
                    }
                }
            }
            resourceId = null;
            return null;
        }

        /// <summary>
        /// The first phase that can draw the card, applying the rule CardEligibility
        /// applies: the card must carry every tag the phase asks for. -1 when no
        /// phase can, which is exactly what made the cutscene card undrawable.
        /// </summary>
        private static int PhaseIndexAccepting(Session session, Card card)
        {
            for (var i = 0; i < session.Phases.Count; i++)
            {
                var required = session.Phases[i].MustIncludeTags;
                var matches = true;
                for (var t = 0; t < required.Count; t++)
                {
                    if (!Contains(card.Tags, required[t]))
                    {
                        matches = false;
                        break;
                    }
                }
                if (matches) return i;
            }
            return -1;
        }
    }
}
