using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Core.Tests;

namespace TruthCardGame.Core
{
    public class GameSessionEngineTests
    {
        private readonly List<string> _eventOrder = new List<string>();

        private static SessionDefinition MakeSession(params PhaseDefinition[] phases)
        {
            var session = new SessionDefinition { Title = "S" };
            session.Phases.AddRange(phases);
            return session;
        }

        private static PhaseDefinition Phase(int min, int max, params string[] include)
        {
            var phase = new PhaseDefinition { Title = "P" + min + max + string.Concat(include), MinCards = min, MaxCards = max };
            phase.MustIncludeTags.AddRange(include);
            return phase;
        }

        private static CardDefinition Card(string title, params string[] tags)
        {
            var card = new CardDefinition { Title = title };
            card.Tags.AddRange(tags);
            return card;
        }

        private static CardDeckDefinition Deck(params CardDefinition[] cards)
        {
            var deck = new CardDeckDefinition();
            deck.Cards.AddRange(cards);
            return deck;
        }

        private GameSessionEngine MakeEngine(
            SessionDefinition session,
            CardDeckDefinition deck,
            RecordingLog log,
            FakeDelayService delay,
            IPromptService prompts = null,
            ICutsceneService cutscenes = null,
            IRandomSource phaseRng = null,
            IRandomSource cardRng = null)
        {
            _eventOrder.Clear();
            var services = new CoreServices(delay, log, prompts, cutscenes);
            var engine = new GameSessionEngine(
                session,
                deck,
                () => 1f,
                phaseRng ?? new FixedRandomSource(Enumerable.Repeat(0, 64).ToArray()),
                cardRng ?? new FixedRandomSource(Enumerable.Repeat(0, 64).ToArray()),
                services);

            engine.CardStarted += c => _eventOrder.Add("started:" + c.Title);
            engine.CardFinished += c => _eventOrder.Add("finished:" + c.Title);
            engine.PhaseChanged += (p, n) => _eventOrder.Add("phase:" + p + "->" + n);
            engine.SessionCompleted += () => _eventOrder.Add("completed");
            return engine;
        }

        [Test]
        public async Task OneCall_DrawsExactlyOneCard_ThenRequiresAnother()
        {
            var log = new RecordingLog();
            var engine = MakeEngine(
                MakeSession(Phase(3, 3)),
                Deck(Card("A"), Card("B")),
                log, new FakeDelayService());

            var first = await engine.AdvanceOneCardAsync(CancellationToken.None);
            Assert.AreEqual(AdvanceResultKind.CardCompleted, first.Kind);
            Assert.AreEqual("A", first.Card.Title);
            Assert.IsFalse(engine.IsComplete);
            Assert.AreEqual(1, _eventOrder.Count(e => e.StartsWith("started:")));

            var second = await engine.AdvanceOneCardAsync(CancellationToken.None);
            Assert.AreEqual(AdvanceResultKind.CardCompleted, second.Kind);
            Assert.AreEqual("A", second.Card.Title); // fixed card RNG keeps picking index 0
        }

        [Test]
        public async Task NoMatchPhase_Skipped_InSameCall_ToNextPhaseCard()
        {
            var log = new RecordingLog();
            // P1 wants "ending" (deck has none) then advances; P2 draws normally.
            var engine = MakeEngine(
                MakeSession(Phase(1, 1, "ending"), Phase(1, 1)),
                Deck(Card("Normal", "plain")),
                log, new FakeDelayService());

            var result = await engine.AdvanceOneCardAsync(CancellationToken.None);

            // The drawn card is also the final phase's only card, so the session completes with it.
            Assert.AreEqual(AdvanceResultKind.SessionCompleted, result.Kind);
            Assert.AreEqual("Normal", result.Card.Title);
            CollectionAssert.Contains(_eventOrder, "phase:0->1");
        }

        [Test]
        public async Task SeveralConsecutiveNoMatchPhases_AreSkippedSafely()
        {
            var log = new RecordingLog();
            var engine = MakeEngine(
                MakeSession(Phase(1, 1, "x"), Phase(1, 1, "y"), Phase(1, 1)),
                Deck(Card("Only", "z")),
                log, new FakeDelayService());

            var result = await engine.AdvanceOneCardAsync(CancellationToken.None);

            Assert.AreEqual(AdvanceResultKind.SessionCompleted, result.Kind);
            Assert.AreEqual("Only", result.Card.Title);
            // Two no-match skips plus the final card's completion transition.
            Assert.AreEqual(3, _eventOrder.Count(e => e.StartsWith("phase:")));
        }

        [Test]
        public async Task NoMatchThroughFinalPhase_CompletesSession_WithNullCard()
        {
            var log = new RecordingLog();
            var engine = MakeEngine(
                MakeSession(Phase(1, 1, "unreachable")),
                Deck(Card("Anything")),
                log, new FakeDelayService());

            var result = await engine.AdvanceOneCardAsync(CancellationToken.None);

            Assert.AreEqual(AdvanceResultKind.SessionCompleted, result.Kind);
            Assert.IsNull(result.Card);
            Assert.IsTrue(engine.IsComplete);
            CollectionAssert.Contains(_eventOrder, "completed");
        }

        [Test]
        public async Task CardCompletion_AtTarget_AdvancesPhase_AndFinalCardCompletes()
        {
            var log = new RecordingLog();
            var engine = MakeEngine(
                MakeSession(Phase(2, 2), Phase(1, 1)),
                Deck(Card("C1"), Card("C2"), Card("C3")),
                log, new FakeDelayService());

            await engine.AdvanceOneCardAsync(CancellationToken.None);
            Assert.AreEqual(0, engine.PhaseIndex);
            await engine.AdvanceOneCardAsync(CancellationToken.None);
            Assert.AreEqual(1, engine.PhaseIndex);
            CollectionAssert.Contains(_eventOrder, "phase:0->1");

            var final = await engine.AdvanceOneCardAsync(CancellationToken.None);
            Assert.AreEqual(AdvanceResultKind.SessionCompleted, final.Kind);
            Assert.IsNotNull(final.Card);
            CollectionAssert.Contains(_eventOrder, "completed");
        }

        [Test]
        public async Task AdvanceAfterCompletion_ReturnsSessionCompleted_WithoutDrawing()
        {
            var log = new RecordingLog();
            var engine = MakeEngine(MakeSession(Phase(1, 1)), Deck(Card("Only")), log, new FakeDelayService());
            await engine.AdvanceOneCardAsync(CancellationToken.None);
            _eventOrder.Clear();

            var again = await engine.AdvanceOneCardAsync(CancellationToken.None);

            Assert.AreEqual(AdvanceResultKind.SessionCompleted, again.Kind);
            Assert.IsNull(again.Card);
            Assert.IsEmpty(_eventOrder.Where(e => e.StartsWith("started:")));
        }

        [Test]
        public async Task ReentryWhileBusy_IsIgnored()
        {
            var log = new RecordingLog();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cutscenes = new FakeCutsceneService(gate);
            var cutCard = new CardDefinition { Title = "Cut", Tags = { "cs" } };
            cutCard.Actions.Add(new CutsceneActionDefinition { ResourceId = "cs:x" });
            var engine = MakeEngine(
                MakeSession(Phase(1, 1)),
                Deck(cutCard),
                log, new FakeDelayService(), cutscenes: cutscenes);

            var busyWork = engine.AdvanceOneCardAsync(CancellationToken.None);
            await Task.Yield();

            var second = await engine.AdvanceOneCardAsync(CancellationToken.None);
            Assert.AreEqual(AdvanceResultKind.BusyIgnored, second.Kind);
            Assert.IsTrue(engine.IsBusy);

            gate.TrySetResult(true);
            var finished = await busyWork;
            // The gated card was this single-phase session's only card.
            Assert.AreEqual(AdvanceResultKind.SessionCompleted, finished.Kind);
            Assert.IsFalse(engine.IsBusy);
        }

        [Test]
        public async Task Notifications_OccurInPreservedOrder()
        {
            var log = new RecordingLog();
            var engine = MakeEngine(
                MakeSession(Phase(1, 1)),
                Deck(Card("Solo")),
                log, new FakeDelayService());

            await engine.AdvanceOneCardAsync(CancellationToken.None);

            // CardStarted -> action execution -> CardFinished -> progression
            // (phase change incl. completion index) -> SessionCompleted.
            CollectionAssert.AreEqual(
                new[] { "started:Solo", "finished:Solo", "phase:0->-1", "completed" },
                _eventOrder);
        }

        [Test]
        public async Task NonblockingBackgroundAction_DoesNotPreventNextDraw()
        {
            var log = new RecordingLog();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var order = new List<string>();
            var delay = new FakeDelayService { Gate = gate, Order = order };
            var bgCard = new CardDefinition { Title = "Bg" };
            bgCard.Actions.Add(new DebugActionDefinition { Message = "bg", DelaySeconds = 5f, IsBlocking = false });
            var engine = MakeEngine(
                MakeSession(Phase(3, 3)),
                Deck(bgCard, Card("Next")),
                log, delay);

            var first = await engine.AdvanceOneCardAsync(CancellationToken.None);
            Assert.AreEqual(AdvanceResultKind.CardCompleted, first.Kind);
            Assert.AreEqual(1, engine.PendingBackgroundCount);

            var second = await engine.AdvanceOneCardAsync(CancellationToken.None);
            Assert.AreEqual(AdvanceResultKind.CardCompleted, second.Kind);
            Assert.IsFalse(engine.IsBusy);

            gate.TrySetResult(true);
            await engine.DrainBackgroundAsync();
            Assert.AreEqual(0, engine.PendingBackgroundCount);
        }

        [Test]
        public async Task BlockingPrompt_KeepsEngineBusy_UntilResolved()
        {
            var log = new RecordingLog();
            var prompts = new GatedPromptService();
            var choice = new ChoiceActionDefinition
            {
                Prompt = "Pick",
                Options =
                {
                    new ChoiceOptionDefinition { Label = "A", Child = new DebugActionDefinition { Message = "a" } },
                    new ChoiceOptionDefinition { Label = "B" }
                }
            };
            var deck = Deck(new CardDefinition { Title = "Choice", Actions = { choice } });
            var engine = MakeEngine(MakeSession(Phase(1, 1)), deck, log, new FakeDelayService(), prompts: prompts);

            var pending = engine.AdvanceOneCardAsync(CancellationToken.None);
            await Task.Yield();

            Assert.IsTrue(engine.IsBusy);
            Assert.AreEqual(AdvanceResultKind.BusyIgnored, (await engine.AdvanceOneCardAsync(CancellationToken.None)).Kind);

            prompts.Answer(0);
            var result = await pending;
            // Single-phase session: resolving the prompt finishes its only card.
            Assert.AreEqual(AdvanceResultKind.SessionCompleted, result.Kind);
            Assert.IsFalse(engine.IsBusy);
        }

        [Test]
        public async Task Cancellation_ReleasesBusyState_EngineNotStuck()
        {
            var log = new RecordingLog();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cutscenes = new FakeCutsceneService(gate);
            var cts = new CancellationTokenSource();
            var cutCard = new CardDefinition { Title = "Cut", Tags = { "cs" } };
            cutCard.Actions.Add(new CutsceneActionDefinition { ResourceId = "cs:x" });
            var engine = MakeEngine(
                MakeSession(Phase(1, 1)),
                Deck(cutCard),
                log, new FakeDelayService(), cutscenes: cutscenes);

            var blocked = engine.AdvanceOneCardAsync(cts.Token);
            await Task.Yield();
            Assert.IsTrue(engine.IsBusy);

            cts.Cancel();
            Assert.That(async () => await blocked, Throws.InstanceOf<OperationCanceledException>());
            Assert.IsFalse(engine.IsBusy);

            // Engine remains usable after cancellation; completing the retried
            // card also completes this single-phase session.
            var retry = await engine.AdvanceOneCardAsync(CancellationToken.None);
            Assert.AreEqual(AdvanceResultKind.SessionCompleted, retry.Kind);
            Assert.IsFalse(engine.IsBusy);
        }
    }
}
