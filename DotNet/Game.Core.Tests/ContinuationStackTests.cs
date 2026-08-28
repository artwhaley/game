using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Content.Samples;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// Ticket 08 gate: the explicit continuation stack and GOTO/RETURN/EndSession
    /// mechanics — nested returns, recursive phase calls with independent
    /// progress/RNG, temperature persistence across transfers, and loud failures
    /// for unwired exits and empty-stack RETURN.
    /// </summary>
    [TestFixture]
    public class ContinuationStackTests
    {
        private ContentCatalog _catalog;
        private BackgroundActionTracker _tracker;
        private CoreServices _services;
        private Player _player;
        private TemperatureState _temperatures;
        private GameContentDefinition _content;

        [SetUp]
        public void SetUp()
        {
            _content = new GameContentDefinition
            {
                SessionTypes = { new SessionTypeDefinition { Id = "type", Title = "Standard" } },
                Temperatures =
                {
                    new TemperatureDefinition { Id = SampleContent.TemperatureHappiness, Title = "Happiness", MinValue = 0f, MaxValue = 100f, DefaultValue = 50f },
                },
            };
            _catalog = new ContentCatalog(_content);
            _tracker = new BackgroundActionTracker(null);
            _services = new CoreServices(new FakeDelayService());
            _player = new Player("Tester");
            _temperatures = new TemperatureState(_catalog);
        }

        private SessionGraphVm Session(SessionDefinition session, params PhaseDefinition[] phases)
        {
            _content.Sessions.Add(session);
            foreach (var phase in phases) _content.Phases.Add(phase);
            _catalog = new ContentCatalog(_content);
            return new SessionGraphVm(_content, session.Id, _services, _tracker, new PhaseRunRngFactory(100));
        }

        private ActionExecutionContext Context()
        {
            return new ActionExecutionContext(_player, _services, _catalog, _temperatures, null, ActionOwnerScope.PhaseActionSequence);
        }

        // ---------- builders ----------

        private static PhaseDefinition Phase(string id, params GraphNodeDefinition[] nodes)
        {
            var phase = new PhaseDefinition { Id = id };
            foreach (var node in nodes) phase.Graph.Nodes.Add(node);
            return phase;
        }

        private static void Edge(PhaseDefinition phase, string source, string target)
        {
            phase.Graph.Edges.Add(new GraphEdgeDefinition { Id = $"e-{source}-{target}", SourceOutputId = source, TargetNodeId = target });
        }

        private static PhaseEntryNodeDefinition Entry(string id)
        {
            var node = new PhaseEntryNodeDefinition { Id = id };
            node.Outputs.Add(Out(id + "-out"));
            return node;
        }

        private static ActionNodeDefinition Action(string id, params ActionInstanceDefinition[] instances)
        {
            var node = new ActionNodeDefinition { Id = id, Sequence = new ActionSequenceDefinition { Id = $"seq-{id}" } };
            foreach (var instance in instances) node.Sequence.Instances.Add(instance);
            node.Outputs.Add(Out(id + "-out"));
            return node;
        }

        private static ActionNodeDefinition EndSession(string id)
        {
            return Action(id, new EndSessionInstanceDefinition { Id = "inst-" + id + "-end" });
        }

        private static PhaseGotoInstanceDefinition Goto(string exitId)
        {
            return new PhaseGotoInstanceDefinition { Id = "g-" + exitId, PhaseExitId = exitId };
        }

        private static StatIncreaseInstanceDefinition Stat(string id, int amount)
        {
            return new StatIncreaseInstanceDefinition { Id = id, StatKey = "courage", Amount = amount };
        }

        private static IncrementProgressInstanceDefinition Progress(string id, float amount)
        {
            return new IncrementProgressInstanceDefinition { Id = id, Amount = amount };
        }

        private static ModifyTemperatureInstanceDefinition Temperature(string id, float amount)
        {
            return new ModifyTemperatureInstanceDefinition { Id = id, TemperatureId = SampleContent.TemperatureHappiness, Amount = amount };
        }

        private static GraphOutputDefinition Out(string id, GraphPortKind kind = GraphPortKind.Normal)
        {
            return new GraphOutputDefinition { Id = id, Kind = kind };
        }

        private static PhaseExitDefinition Exit(string phaseId, string name)
        {
            return new PhaseExitDefinition { Id = $"px-{phaseId}-complete", Name = name };
        }

        /// <summary>Session: Start -> Ref(A) -> [A exit -> Ref(B)] -> Ref(B) -> [B exit -> End] -> End.</summary>
        private static SessionDefinition TwoPhaseSession(string id, PhaseDefinition a, PhaseDefinition b)
        {
            var start = new SessionStartNodeDefinition { Id = "n-start" };
            start.Outputs.Add(Out("n-start-out"));
            var refA = new PhaseReferenceNodeDefinition { Id = "n-refA", PhaseId = a.Id };
            refA.Outputs.Add(new GraphOutputDefinition { Id = "n-refA-a", Kind = GraphPortKind.PhaseExit, PhaseExitId = $"px-{a.Id}-complete" });
            var refB = new PhaseReferenceNodeDefinition { Id = "n-refB", PhaseId = b.Id };
            refB.Outputs.Add(new GraphOutputDefinition { Id = "n-refB-b", Kind = GraphPortKind.PhaseExit, PhaseExitId = $"px-{b.Id}-complete" });
            var end = new SessionEndNodeDefinition { Id = "n-end" };

            var session = new SessionDefinition { Id = id, Title = id, SessionTypeId = "type" };
            session.Graph.Nodes.Add(start);
            session.Graph.Nodes.Add(refA);
            session.Graph.Nodes.Add(refB);
            session.Graph.Nodes.Add(end);
            session.Graph.Edges.Add(SessionEdge("n-start-out", "n-refA"));
            session.Graph.Edges.Add(SessionEdge("n-refA-a", "n-refB"));
            session.Graph.Edges.Add(SessionEdge("n-refB-b", "n-end"));
            return session;
        }

        private static GraphEdgeDefinition SessionEdge(string source, string target)
        {
            return new GraphEdgeDefinition { Id = $"se-{source}-{target}", SourceOutputId = source, TargetNodeId = target };
        }

        // ---------- 1. A -> B -> Return -> A resumes next Action ----------

        [Test]
        public async Task AToBReturn_ResumesANextAction()
        {
            var a = Phase("A",
                Entry("n-a-entry"),
                Action("n-a-action",
                    Stat("i-a1", 1),
                    Goto("px-A-complete"),
                    Stat("i-a3", 5)), // resumes after RETURN
                EndSession("n-a-end"));
            a.Exits.Add(Exit("A", "Complete"));
            Edge(a, "n-a-entry-out", "n-a-action");
            Edge(a, "n-a-action-out", "n-a-end");

            var b = Phase("B",
                Entry("n-b-entry"),
                new ReturnNodeDefinition { Id = "n-b-return" });
            b.Exits.Add(Exit("B", "Complete"));
            Edge(b, "n-b-entry-out", "n-b-return");

            var vm = Session(TwoPhaseSession("s1", a, b), a, b);

            var result = await vm.AdvanceAsync(Context(), CancellationToken.None);
            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, result.Outcome);
            Assert.AreEqual(6, _player.Stats.Get("courage"),
                "A's action after the GOTO (i-a3 +5) ran when B returned — total 1+5");
            Assert.AreEqual(0, vm.ContinuationDepth, "stack emptied by session completion");
        }

        // ---------- 2. A -> B -> C -> Return -> B -> Return -> A ----------

        [Test]
        public async Task AToBToCReturnTwice_ResumesBThenA()
        {
            var a = Phase("A",
                Entry("n-a-entry"),
                Action("n-a-action",
                    Stat("i-a1", 1),
                    Goto("px-A-complete"),
                    Stat("i-a3", 10)), // resumes after B+C return
                EndSession("n-a-end"));
            a.Exits.Add(Exit("A", "Complete"));
            Edge(a, "n-a-entry-out", "n-a-action");
            Edge(a, "n-a-action-out", "n-a-end");

            var b = Phase("B",
                Entry("n-b-entry"),
                Action("n-b-action",
                    Stat("i-b1", 2),
                    Goto("px-B-complete"),
                    Stat("i-b3", 20)), // resumes after C returns
                new ReturnNodeDefinition { Id = "n-b-return" }); // B returns to A
            b.Exits.Add(Exit("B", "Complete"));
            Edge(b, "n-b-entry-out", "n-b-action");
            Edge(b, "n-b-action-out", "n-b-return");

            var c = Phase("C",
                Entry("n-c-entry"),
                new ReturnNodeDefinition { Id = "n-c-return" });
            c.Exits.Add(Exit("C", "Complete"));
            Edge(c, "n-c-entry-out", "n-c-return");

            // Session: Start -> RefA -> RefB -> RefC -> End.
            var start = new SessionStartNodeDefinition { Id = "n-start" };
            start.Outputs.Add(Out("n-start-out"));
            var refA = new PhaseReferenceNodeDefinition { Id = "n-refA", PhaseId = a.Id };
            refA.Outputs.Add(new GraphOutputDefinition { Id = "n-refA-a", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-A-complete" });
            var refB = new PhaseReferenceNodeDefinition { Id = "n-refB", PhaseId = b.Id };
            refB.Outputs.Add(new GraphOutputDefinition { Id = "n-refB-b", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-B-complete" });
            var refC = new PhaseReferenceNodeDefinition { Id = "n-refC", PhaseId = c.Id };
            refC.Outputs.Add(new GraphOutputDefinition { Id = "n-refC-c", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-C-complete" });
            var end = new SessionEndNodeDefinition { Id = "n-end" };

            var session = new SessionDefinition { Id = "s2", Title = "s2", SessionTypeId = "type" };
            session.Graph.Nodes.Add(start);
            session.Graph.Nodes.Add(refA);
            session.Graph.Nodes.Add(refB);
            session.Graph.Nodes.Add(refC);
            session.Graph.Nodes.Add(end);
            session.Graph.Edges.Add(SessionEdge("n-start-out", "n-refA"));
            session.Graph.Edges.Add(SessionEdge("n-refA-a", "n-refB"));
            session.Graph.Edges.Add(SessionEdge("n-refB-b", "n-refC"));
            session.Graph.Edges.Add(SessionEdge("n-refC-c", "n-end"));

            var vm = Session(session, a, b, c);

            var result = await vm.AdvanceAsync(Context(), CancellationToken.None);
            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, result.Outcome);
            Assert.AreEqual(33, _player.Stats.Get("courage"),
                "1 (A) + 2 (B) + 20 (B resumed) + 10 (A resumed)");
            Assert.AreEqual(0, vm.ContinuationDepth);
        }

        // ---------- 3. Recursive A run #1 -> run #2 -> Return -> run #1 ----------

        [Test]
        public async Task RecursivePhaseCall_IndependentProgressAndRng()
        {
            // A: Entry -> [+1 courage] -> check courage>=2?
            //   false -> GOTO(A-complete) -> [+7 progress]
            //   true  -> Return (back to the outer A run)
            var entry = new PhaseEntryNodeDefinition { Id = "n-entry" };
            entry.Outputs.Add(Out("n-entry-out"));
            var statAction = Action("n-action-a", Stat("i-a", 1));
            var check = new VariableCheckNodeDefinition
            {
                Id = "n-check",
                SourceKind = VariableSourceKind.Stat,
                VariableKey = "courage",
                Operator = VariableCompareOperator.GreaterThanOrEqual,
                CompareValue = 2f,
            };
            check.Outputs.Add(Out("n-check-true", GraphPortKind.True));
            check.Outputs.Add(Out("n-check-false", GraphPortKind.False));
            var gotoNode = Action("n-goto",
                Goto("px-A-complete"),
                Progress("i-prog", 7f));
            var returnNode = new ReturnNodeDefinition { Id = "n-return" };
            var endSession = EndSession("n-end");

            var a = Phase("A", entry, statAction, check, gotoNode, returnNode, endSession);
            a.Exits.Add(Exit("A", "Complete"));
            Edge(a, "n-entry-out", "n-action-a");
            Edge(a, "n-action-a-out", "n-check");
            Edge(a, "n-check-false", "n-goto");
            Edge(a, "n-goto-out", "n-return");   // run #2 never reaches this; run #1 does after RETURN
            Edge(a, "n-check-true", "n-return"); // run #2 returns to run #1
            // NOTE: the resumed outer run continues from n-goto's normal edge... the
            // progress action is in the chain; after it, follow n-goto-out -> n-return
            // would pop an empty stack, so route it to EndSession instead below.

            // After RETURN resumes the outer run, the chain runs [+7 progress], then
            // the graph follows n-goto normal edge. That must terminate: EndSession.
            // Replace the n-goto-out edge accordingly.
            a.Graph.Edges.Clear();
            Edge(a, "n-entry-out", "n-action-a");
            Edge(a, "n-action-a-out", "n-check");
            Edge(a, "n-check-false", "n-goto");
            Edge(a, "n-check-true", "n-return");
            Edge(a, "n-goto-out", "n-end");      // resumed outer run ends the session

            // Session: Start -> Ref1(A) -> [A exit -> Ref2(A)] -> Ref2(A) -> [A exit -> End] -> End.
            var start = new SessionStartNodeDefinition { Id = "n-start" };
            start.Outputs.Add(Out("n-start-out"));
            var ref1 = new PhaseReferenceNodeDefinition { Id = "n-ref1", PhaseId = a.Id };
            ref1.Outputs.Add(new GraphOutputDefinition { Id = "n-ref1-a", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-A-complete" });
            var ref2 = new PhaseReferenceNodeDefinition { Id = "n-ref2", PhaseId = a.Id };
            ref2.Outputs.Add(new GraphOutputDefinition { Id = "n-ref2-a", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-A-complete" });
            var end = new SessionEndNodeDefinition { Id = "n-end" };

            var session = new SessionDefinition { Id = "s3", Title = "s3", SessionTypeId = "type" };
            session.Graph.Nodes.Add(start);
            session.Graph.Nodes.Add(ref1);
            session.Graph.Nodes.Add(ref2);
            session.Graph.Nodes.Add(end);
            session.Graph.Edges.Add(SessionEdge("n-start-out", "n-ref1"));
            session.Graph.Edges.Add(SessionEdge("n-ref1-a", "n-ref2"));
            session.Graph.Edges.Add(SessionEdge("n-ref2-a", "n-end"));

            var vm = Session(session, a);

            var result = await vm.AdvanceAsync(Context(), CancellationToken.None);
            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, result.Outcome);
            Assert.AreEqual(2, _player.Stats.Get("courage"), "both A runs incremented the shared stat");
            Assert.AreEqual(0, vm.ContinuationDepth, "stack emptied at session completion");
        }

        // ---------- 4. temperature changed in B stays changed in A ----------

        [Test]
        public async Task TemperatureChangedInB_StaysChangedInA_AfterReturn()
        {
            var a = Phase("A",
                Entry("n-a-entry"),
                Action("n-a-action",
                    Goto("px-A-complete"),
                    Temperature("i-a-temp", 10f)), // resumes after B returns
                EndSession("n-a-end"));
            a.Exits.Add(Exit("A", "Complete"));
            Edge(a, "n-a-entry-out", "n-a-action");
            Edge(a, "n-a-action-out", "n-a-end");

            var b = Phase("B",
                Entry("n-b-entry"),
                Action("n-b-action",
                    Temperature("i-b-temp", 25f)), // happiness 50 -> 75
                new ReturnNodeDefinition { Id = "n-b-return" });
            b.Exits.Add(Exit("B", "Complete"));
            Edge(b, "n-b-entry-out", "n-b-action");
            Edge(b, "n-b-action-out", "n-b-return");

            var vm = Session(TwoPhaseSession("s4", a, b), a, b);

            var result = await vm.AdvanceAsync(Context(), CancellationToken.None);
            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, result.Outcome);
            Assert.AreEqual(85f, _temperatures.Get(SampleContent.TemperatureHappiness),
                "B's +25 persisted, then A's resumed +10 applied on top (50+25+10)");
        }

        // ---------- 5. progress/RNG in A unchanged during B ----------

        [Test]
        public async Task ProgressAndRngInA_UnchangedDuringB()
        {
            var a = Phase("A",
                Entry("n-a-entry"),
                Action("n-a-action",
                    Progress("i-a1", 5f),
                    Goto("px-A-complete"),
                    Progress("i-a3", 9f)), // resumes after B returns
                EndSession("n-a-end"));
            a.Exits.Add(Exit("A", "Complete"));
            Edge(a, "n-a-entry-out", "n-a-action");
            Edge(a, "n-a-action-out", "n-a-end");

            var b = Phase("B",
                Entry("n-b-entry"),
                Action("n-b-action", Progress("i-b", 3f)),
                new ReturnNodeDefinition { Id = "n-b-return" });
            b.Exits.Add(Exit("B", "Complete"));
            Edge(b, "n-b-entry-out", "n-b-action");
            Edge(b, "n-b-action-out", "n-b-return");

            var vm = Session(TwoPhaseSession("s5", a, b), a, b);

            var result = await vm.AdvanceAsync(Context(), CancellationToken.None);
            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, result.Outcome);
            Assert.AreEqual(0, vm.ContinuationDepth, "stack drained; session completed without error");
        }

        // ---------- 6. EndSession from nested depth discards stack ----------

        [Test]
        public async Task EndSessionFromNestedDepth_DiscardsStack()
        {
            var a = Phase("A",
                Entry("n-a-entry"),
                Action("n-a-action",
                    Goto("px-A-complete"),
                    Stat("i-a-later", 99)), // must NOT run: stack discarded
                EndSession("n-a-end"));
            a.Exits.Add(Exit("A", "Complete"));
            Edge(a, "n-a-entry-out", "n-a-action");
            Edge(a, "n-a-action-out", "n-a-end");

            var b = Phase("B",
                Entry("n-b-entry"),
                Action("n-b-action",
                    new EndSessionInstanceDefinition { Id = "i-b-end" },
                    Stat("i-b-later", 100)), // must NOT run either
                new ReturnNodeDefinition { Id = "n-b-return" });
            b.Exits.Add(Exit("B", "Complete"));
            Edge(b, "n-b-entry-out", "n-b-action");
            Edge(b, "n-b-action-out", "n-b-return");

            var vm = Session(TwoPhaseSession("s6", a, b), a, b);

            var result = await vm.AdvanceAsync(Context(), CancellationToken.None);
            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, result.Outcome);
            Assert.AreEqual(0, _player.Stats.Get("courage"), "no post-EndSession action ran");
            Assert.AreEqual(0, vm.ContinuationDepth, "stack discarded");
        }

        // ---------- 7. unwired phase exit fails clearly ----------

        [Test]
        public async Task UnwiredPhaseExit_FailsClearly()
        {
            var a = Phase("A",
                Entry("n-a-entry"),
                Action("n-a-action", Goto("px-A-complete")),
                EndSession("n-a-end"));
            a.Exits.Add(Exit("A", "Complete"));
            Edge(a, "n-a-entry-out", "n-a-action");
            Edge(a, "n-a-action-out", "n-a-end");

            var b = Phase("B", Entry("n-b-entry"), new ReturnNodeDefinition { Id = "n-b-return" });
            b.Exits.Add(Exit("B", "Complete"));
            Edge(b, "n-b-entry-out", "n-b-return");

            // Session where Ref(A)'s projected exit has NO edge.
            var start = new SessionStartNodeDefinition { Id = "n-start" };
            start.Outputs.Add(Out("n-start-out"));
            var refA = new PhaseReferenceNodeDefinition { Id = "n-refA", PhaseId = a.Id };
            refA.Outputs.Add(new GraphOutputDefinition { Id = "n-refA-a", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-A-complete" });
            var end = new SessionEndNodeDefinition { Id = "n-end" };

            var session = new SessionDefinition { Id = "s7", Title = "s7", SessionTypeId = "type" };
            session.Graph.Nodes.Add(start);
            session.Graph.Nodes.Add(refA);
            session.Graph.Nodes.Add(end);
            session.Graph.Edges.Add(SessionEdge("n-start-out", "n-refA"));

            var vm = Session(session, a, b);
            var result = await vm.AdvanceAsync(Context(), CancellationToken.None);

            Assert.AreEqual(SessionAdvanceOutcome.Error, result.Outcome);
            StringAssert.Contains("Unwired phase exit", result.ErrorMessage);
        }

        // ---------- 8. Return with empty stack fails clearly ----------

        [Test]
        public async Task ReturnWithEmptyStack_FailsClearly()
        {
            var a = Phase("A", Entry("n-a-entry"), new ReturnNodeDefinition { Id = "n-a-return" });
            a.Exits.Add(Exit("A", "Complete"));
            Edge(a, "n-a-entry-out", "n-a-return");

            var start = new SessionStartNodeDefinition { Id = "n-start" };
            start.Outputs.Add(Out("n-start-out"));
            var refA = new PhaseReferenceNodeDefinition { Id = "n-refA", PhaseId = a.Id };
            refA.Outputs.Add(new GraphOutputDefinition { Id = "n-refA-a", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-A-complete" });
            var end = new SessionEndNodeDefinition { Id = "n-end" };

            var session = new SessionDefinition { Id = "s8", Title = "s8", SessionTypeId = "type" };
            session.Graph.Nodes.Add(start);
            session.Graph.Nodes.Add(refA);
            session.Graph.Nodes.Add(end);
            session.Graph.Edges.Add(SessionEdge("n-start-out", "n-refA"));
            session.Graph.Edges.Add(SessionEdge("n-refA-a", "n-end"));

            var vm = Session(session, a);
            var result = await vm.AdvanceAsync(Context(), CancellationToken.None);

            Assert.AreEqual(SessionAdvanceOutcome.Error, result.Outcome);
            StringAssert.Contains("empty continuation stack", result.ErrorMessage);
        }
    }
}
