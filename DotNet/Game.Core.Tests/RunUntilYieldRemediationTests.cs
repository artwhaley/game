using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Core;

namespace TruthCardGame.Core.Tests
{
    [TestFixture]
    public sealed class RunUntilYieldRemediationTests
    {
        private static CoreServices Services() => new CoreServices(new FakeDelayService());

        [Test]
        public async Task RunUntilYield_WaitsThenContinueResumesCardAndFinishesOnce()
        {
            var content = NewContent();
            var phase = Phase("phase", "session");
            var terminal = Action("terminal", new EndSessionInstanceDefinition { Id = "terminal-end" });
            phase.Graph.Nodes.Add(terminal);
            phase.Graph.Edges.Add(Edge("phase-exec-out", terminal.Id));
            var card = Card("card",
                new StatIncreaseInstanceDefinition { Id = "before", StatKey = "courage", Amount = 1f },
                new WaitForContinueInstanceDefinition { Id = "wait", IsBlocking = true },
                new StatIncreaseInstanceDefinition { Id = "after", StatKey = "courage", Amount = 2f });
            content.Phases.Add(phase);
            content.Cards.Add(card);
            content.Sessions.Add(Session("session", phase.Id));

            var engine = new GameSessionEngine(content, "session", Services());
            var started = 0;
            var finished = 0;
            engine.CardStarted += _ => started++;
            engine.CardFinished += _ => finished++;

            var first = await engine.RunUntilYieldAsync(CancellationToken.None);

            Assert.AreEqual(AdvanceResultKind.WaitForContinue, first.Kind);
            Assert.AreEqual(1f, engine.Player.Stats.Get("courage"));
            Assert.AreEqual(1, started);
            Assert.AreEqual(0, finished, "WaitForContinue does not finish the Card");

            var second = await engine.ContinueAsync(CancellationToken.None);

            Assert.AreEqual(AdvanceResultKind.SessionCompleted, second.Kind);
            Assert.AreEqual(3f, engine.Player.Stats.Get("courage"));
            Assert.AreEqual(1, started, "the suspended Card is not drawn again");
            Assert.AreEqual(1, finished, "CardFinished fires after the resumed sequence completes");
        }

        [Test]
        public async Task CardGotoReturn_IsRejectedByTheCardScopeContract()
        {
            Assert.Throws<System.InvalidOperationException>(() => ActionTypeRegistry.ValidateScope(
                new PhaseGotoInstanceDefinition { Id = "goto-rejected" }, ActionOwnerScope.CardSequence));
            return;

#pragma warning disable CS0162
            var content = NewContent();
            var recovery = PhaseWithReturn("recovery", "recovery-stat", 2f);
            var main = Phase("main", "session");
            main.Exits.Add(new PhaseExitDefinition { Id = "px-main-recover", Name = "Recover" });
            main.Exits.Add(new PhaseExitDefinition { Id = "px-main-complete", Name = "Complete" });
            main.Graph.Nodes.Clear();
            main.Graph.Edges.Clear();
            var entry = Entry("main-entry");
            var executor = Executor("main-exec");
            var finish = Action("main-finish", new PhaseGotoInstanceDefinition
            {
                Id = "main-complete-goto",
                PhaseExitId = "px-main-complete",
            });
            main.Graph.Nodes.Add(entry);
            main.Graph.Nodes.Add(executor);
            main.Graph.Nodes.Add(finish);
            main.Graph.Edges.Add(Edge("main-entry-out", executor.Id));
            main.Graph.Edges.Add(Edge("main-exec-out", finish.Id));

            var card = Card("card",
                new StatIncreaseInstanceDefinition { Id = "before-goto", StatKey = "courage", Amount = 1f },
                new PhaseGotoInstanceDefinition { Id = "goto-recovery", PhaseExitId = "px-main-recover" },
                new StatIncreaseInstanceDefinition { Id = "after-return", StatKey = "courage", Amount = 5f });
            content.Phases.Add(main);
            content.Phases.Add(recovery);
            content.Cards.Add(card);
            content.Sessions.Add(SessionWithRecovery("session", main.Id, recovery.Id));

            var vm = new SessionGraphVm(content, "session", Services(),
                new BackgroundActionTracker(null), new PhaseRunRngFactory(1000));
            var lifecycle = new List<string>();
            vm.CardStarted += c => lifecycle.Add("started:" + c.Id);
            vm.CardFinished += c => lifecycle.Add("finished:" + c.Id);

            var context = new ActionExecutionContext(
                new Player("Tester"), Services(), new ContentCatalog(content),
                new TemperatureState(new ContentCatalog(content)), null, ActionOwnerScope.All);
            var result = await vm.RunUntilYieldAsync(context, CancellationToken.None);

            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, result.Outcome, result.ErrorMessage);
            Assert.AreEqual(8f, context.Player.Stats.Get("courage"),
                "the recovery action (+2) and the resumed Card remainder (+5) both ran");
            CollectionAssert.AreEqual(new[] { "started:card", "finished:card" }, lifecycle);
            Assert.AreEqual(0, vm.ContinuationDepth);
#pragma warning restore CS0162
        }

        [Test]
        public async Task EndSessionFromCard_DoesNotFireCardFinished()
        {
            var content = NewContent();
            var phase = Phase("phase", "session");
            phase.Graph.Nodes.Clear();
            phase.Graph.Edges.Clear();
            var entry = Entry("entry");
            var executor = Executor("exec");
            phase.Graph.Nodes.Add(entry);
            phase.Graph.Nodes.Add(executor);
            phase.Graph.Edges.Add(Edge("entry-out", executor.Id));
            var card = Card("terminal", new EndSessionInstanceDefinition { Id = "end" });
            content.Phases.Add(phase);
            content.Cards.Add(card);
            content.Sessions.Add(Session("session", phase.Id));

            var engine = new GameSessionEngine(content, "session", Services());
            var finished = 0;
            engine.CardFinished += _ => finished++;

            var result = await engine.RunUntilYieldAsync(CancellationToken.None);

            Assert.AreEqual(AdvanceResultKind.SessionCompleted, result.Kind);
            Assert.AreEqual(0, finished, "the Card never completed its true sequence");
        }

        private static GameContentDefinition NewContent()
        {
            return new GameContentDefinition
            {
                SessionTypes = { new SessionTypeDefinition { Id = "standard", Title = "Standard" } },
            };
        }

        private static CardDefinition Card(string id, params ActionInstanceDefinition[] actions)
        {
            var card = new CardDefinition { Id = id, Title = id, Sequence = new ActionSequenceDefinition { Id = "seq-" + id } };
            foreach (var action in actions) card.Sequence.Instances.Add(action);
            return card;
        }

        private static PhaseDefinition Phase(string id, string sessionId)
        {
            var phase = new PhaseDefinition { Id = id, Title = id };
            phase.Graph.Nodes.Add(Entry(id + "-entry"));
            phase.Graph.Nodes.Add(Executor(id + "-exec"));
            phase.Graph.Edges.Add(Edge(id + "-entry-out", id + "-exec"));
            return phase;
        }

        private static PhaseDefinition PhaseWithReturn(string id, string statId, float amount)
        {
            var phase = new PhaseDefinition { Id = id, Title = id };
            var entry = Entry(id + "-entry");
            var action = Action(id + "-action",
                new StatIncreaseInstanceDefinition { Id = statId, StatKey = "courage", Amount = amount });
            var ret = new ReturnNodeDefinition { Id = id + "-return" };
            phase.Graph.Nodes.Add(entry);
            phase.Graph.Nodes.Add(action);
            phase.Graph.Nodes.Add(ret);
            phase.Graph.Edges.Add(Edge(id + "-entry-out", action.Id));
            phase.Graph.Edges.Add(Edge(action.Id + "-out", ret.Id));
            return phase;
        }

        private static SessionDefinition Session(string id, string phaseId)
        {
            return SessionWithRecovery(id, phaseId, null);
        }

        private static SessionDefinition SessionWithRecovery(string id, string mainPhaseId, string recoveryPhaseId)
        {
            var start = new SessionStartNodeDefinition { Id = "start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "start-out", Kind = GraphPortKind.Normal });
            var main = new PhaseReferenceNodeDefinition { Id = "main-ref", PhaseId = mainPhaseId };
            main.Outputs.Add(new GraphOutputDefinition
            {
                Id = "main-complete-socket", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-main-complete",
            });
            var end = new SessionEndNodeDefinition { Id = "end" };
            var session = new SessionDefinition { Id = id, Title = id, SessionTypeId = "standard" };
            session.Graph.Nodes.Add(start);
            session.Graph.Nodes.Add(main);
            session.Graph.Nodes.Add(end);
            session.Graph.Edges.Add(Edge("start-out", main.Id));

            if (recoveryPhaseId == null)
            {
                session.Graph.Edges.Add(Edge("main-complete-socket", end.Id));
                return session;
            }

            main.Outputs.Add(new GraphOutputDefinition
            {
                Id = "main-recover-socket", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-main-recover",
            });
            var recovery = new PhaseReferenceNodeDefinition { Id = "recovery-ref", PhaseId = recoveryPhaseId };
            session.Graph.Nodes.Add(recovery);
            session.Graph.Edges.Add(Edge("main-complete-socket", end.Id));
            session.Graph.Edges.Add(Edge("main-recover-socket", recovery.Id));
            return session;
        }

        private static PhaseEntryNodeDefinition Entry(string id)
        {
            var node = new PhaseEntryNodeDefinition { Id = id };
            node.Outputs.Add(new GraphOutputDefinition { Id = id + "-out", Kind = GraphPortKind.Normal });
            return node;
        }

        private static CardExecutorNodeDefinition Executor(string id)
        {
            var node = new CardExecutorNodeDefinition { Id = id };
            node.Outputs.Add(new GraphOutputDefinition { Id = id + "-out", Kind = GraphPortKind.Normal });
            return node;
        }

        private static ActionNodeDefinition Action(string id, params ActionInstanceDefinition[] actions)
        {
            var node = new ActionNodeDefinition { Id = id, Sequence = new ActionSequenceDefinition { Id = "seq-" + id } };
            foreach (var action in actions) node.Sequence.Instances.Add(action);
            node.Outputs.Add(new GraphOutputDefinition { Id = id + "-out", Kind = GraphPortKind.Normal });
            return node;
        }

        private static GraphEdgeDefinition Edge(string source, string target)
        {
            return new GraphEdgeDefinition { Id = "edge-" + source + "-" + target, SourceOutputId = source, TargetNodeId = target };
        }
    }
}
