using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Content.Samples;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// Ticket 09 gate: the high-level Session graph VM — Start/PhaseReference/
    /// SessionDecision/End execution, SessionGoto through unique instance sockets,
    /// RETURN resuming a decision option, SessionGoto nested under PhaseGoto,
    /// and loud authoring-contract failures.
    /// </summary>
    [TestFixture]
    public class SessionDecisionTests
    {
        private ContentCatalog _catalog;
        private BackgroundActionTracker _tracker;
        private CoreServices _services;
        private RecordingLog _log;
        private Player _player;
        private TemperatureState _temperatures;
        private GameContentDefinition _content;
        private FakePromptService _prompts;

        [SetUp]
        public void SetUp()
        {
            _content = new GameContentDefinition
            {
                Deck = new CardDeckDefinition { Id = "deck", Title = "D" },
                SessionTypes = { new SessionTypeDefinition { Id = "type", Title = "Standard" } },
                Temperatures =
                {
                    new TemperatureDefinition { Id = SampleContent.TemperatureHappiness, Title = "Happiness", MinValue = 0f, MaxValue = 100f, DefaultValue = 50f },
                },
            };
            _catalog = new ContentCatalog(_content);
            _tracker = new BackgroundActionTracker(null);
            _log = new RecordingLog();
            _prompts = new FakePromptService();
            _services = new CoreServices(new FakeDelayService(), _log, _prompts);
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
            return new ActionExecutionContext(_player, _services, _catalog, _temperatures, null, ActionOwnerScope.All);
        }

        private async Task<SessionAdvanceResult> RunToCompletion(SessionGraphVm vm)
        {
            var context = Context();
            SessionAdvanceResult last = null;
            for (var i = 0; i < 50 && !vm.IsComplete; i++)
            {
                last = await vm.AdvanceAsync(context, CancellationToken.None);
            }
            Assert.IsTrue(vm.IsComplete, "session completed; last outcome " + (last?.Outcome)
                + (last?.ErrorMessage != null ? "; error: " + last.ErrorMessage : ""));
            return last;
        }

        /// <summary>Sets the prompt answers AND rebuilds services so the VM sees them.</summary>
        private void WithPrompts(params int?[] answers)
        {
            _prompts = new FakePromptService(answers);
            _services = new CoreServices(new FakeDelayService(), _log, _prompts);
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

        private static SessionGotoInstanceDefinition SessionGoto(string id, string label)
        {
            return new SessionGotoInstanceDefinition { Id = id, Label = label };
        }

        private static StatIncreaseInstanceDefinition Stat(string id, int amount)
        {
            return new StatIncreaseInstanceDefinition { Id = id, StatKey = "courage", Amount = amount };
        }

        private static GraphOutputDefinition Out(string id, GraphPortKind kind = GraphPortKind.Normal)
        {
            return new GraphOutputDefinition { Id = id, Kind = kind };
        }

        private static PhaseExitDefinition Exit(string phaseId, string name)
        {
            return new PhaseExitDefinition { Id = $"px-{phaseId}-complete", Name = name };
        }

        private static GraphEdgeDefinition SessionEdge(string source, string target)
        {
            return new GraphEdgeDefinition { Id = $"se-{source}-{target}", SourceOutputId = source, TargetNodeId = target };
        }

        private static SessionStartNodeDefinition Start(string id = "n-start")
        {
            var node = new SessionStartNodeDefinition { Id = id };
            node.Outputs.Add(Out(id + "-out"));
            return node;
        }

        private static SessionEndNodeDefinition End(string id = "n-end")
        {
            return new SessionEndNodeDefinition { Id = id };
        }

        private static PhaseReferenceNodeDefinition Ref(string id, string phaseId, string exitSocketId, string exitId)
        {
            var node = new PhaseReferenceNodeDefinition { Id = id, PhaseId = phaseId };
            node.Outputs.Add(new GraphOutputDefinition
            {
                Id = exitSocketId,
                Kind = GraphPortKind.PhaseExit,
                PhaseExitId = exitId,
            });
            return node;
        }

        /// <summary>Decision with one option carrying a session-goto socket; returns the node and socket id.</summary>
        private static SessionDecisionNodeDefinition Decision(string id, string prompt, string optionId, string optionLabel,
            ActionInstanceDefinition[] optionActions, string sessionGotoInstanceId, string sessionGotoSocketId)
        {
            var node = new SessionDecisionNodeDefinition { Id = id, Prompt = prompt };
            node.Outputs.Add(Out(id + "-normal"));
            if (sessionGotoInstanceId != null)
            {
                node.Outputs.Add(new GraphOutputDefinition
                {
                    Id = sessionGotoSocketId,
                    Kind = GraphPortKind.SessionGoto,
                    SessionGotoActionInstanceId = sessionGotoInstanceId,
                    Label = optionLabel,
                });
            }
            var option = new SessionDecisionOptionDefinition { Id = optionId, Label = optionLabel };
            option.Sequence.Id = "seq-" + optionId;
            foreach (var action in optionActions) option.Sequence.Instances.Add(action);
            node.Options.Add(option);
            return node;
        }

        // ---------- 1. Start -> Phase -> End ----------

        [Test]
        public async Task StartToPhaseToEnd_RunsThePhase()
        {
            var a = Phase("A",
                Entry("n-a-entry"),
                Action("n-a-goto", Stat("i-a-first", 9), Goto("px-A-complete")),
                EndSession("n-a-end"));
            a.Exits.Add(Exit("A", "Complete"));
            Edge(a, "n-a-entry-out", "n-a-goto");
            Edge(a, "n-a-goto-out", "n-a-end");

            var session = new SessionDefinition { Id = "s1", Title = "s1", SessionTypeId = "type" };
            session.Graph.Nodes.Add(Start());
            session.Graph.Nodes.Add(Ref("n-refA", "A", "n-refA-a", "px-A-complete"));
            session.Graph.Nodes.Add(End());
            session.Graph.Edges.Add(SessionEdge("n-start-out", "n-refA"));
            session.Graph.Edges.Add(SessionEdge("n-refA-a", "n-end"));

            var vm = Session(session, a);
            var result = await RunToCompletion(vm);

            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, result.Outcome);
            Assert.AreEqual(9, _player.Stats.Get("courage"),
                "the phase ran (9) — the GOTO frame is discarded at session completion");
        }

        // ---------- 2. Decision mutation-only -> normal ----------

        [Test]
        public async Task Decision_MutationOnly_FollowsCommonNormalEdge()
        {
            _prompts = new FakePromptService(0);
            _services = new CoreServices(new FakeDelayService(), _log, _prompts);

            var session = new SessionDefinition { Id = "s2", Title = "s2", SessionTypeId = "type" };
            var decision = Decision("n-decision", "Choose", "o0", "First",
                new[] { Stat("i-d1", 3) }, null, null);
            session.Graph.Nodes.Add(Start());
            session.Graph.Nodes.Add(decision);
            session.Graph.Nodes.Add(End());
            session.Graph.Edges.Add(SessionEdge("n-start-out", "n-decision"));
            session.Graph.Edges.Add(SessionEdge("n-decision-normal", "n-end"));

            var vm = Session(session);
            var result = await RunToCompletion(vm);

            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, result.Outcome);
            Assert.AreEqual(3, _player.Stats.Get("courage"));
            Assert.AreEqual(1, _prompts.Requests.Count);
        }

        // ---------- 3. SessionGoto -> Phase -> Return -> resume decision actions -> normal ----------

        [Test]
        public async Task SessionGotoToPhase_ReturnResumesDecisionActions_ThenNormalEdge()
        {
            WithPrompts(0);

            // B: Entry -> Return immediately.
            var b = Phase("B",
                Entry("n-b-entry"),
                new ReturnNodeDefinition { Id = "n-b-return" });
            b.Exits.Add(Exit("B", "Complete"));
            Edge(b, "n-b-entry-out", "n-b-return");

            var session = new SessionDefinition { Id = "s3", Title = "s3", SessionTypeId = "type" };
            var decision = Decision("n-decision", "Choose", "o0", "Go",
                new ActionInstanceDefinition[]
                {
                    Stat("i-d-before", 1),
                    SessionGoto("sg-1", "to B"),
                    Stat("i-d-after", 5), // resumes after B returns
                },
                "sg-1", "n-decision-sg");
            session.Graph.Nodes.Add(Start());
            session.Graph.Nodes.Add(decision);
            session.Graph.Nodes.Add(Ref("n-refB", "B", "n-refB-b", "px-B-complete"));
            session.Graph.Nodes.Add(End());
            session.Graph.Edges.Add(SessionEdge("n-start-out", "n-decision"));
            session.Graph.Edges.Add(SessionEdge("n-decision-sg", "n-refB"));
            session.Graph.Edges.Add(SessionEdge("n-refB-b", "n-end"));
            session.Graph.Edges.Add(SessionEdge("n-decision-normal", "n-end"));

            var vm = Session(session, b);
            var result = await RunToCompletion(vm);

            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, result.Outcome);
            Assert.AreEqual(6, _player.Stats.Get("courage"),
                "decision before-goto (1) + resumed after-goto (5) both ran");
            Assert.AreEqual(0, vm.ContinuationDepth, "stack drained after the return");
        }

        // ---------- 4. SessionGoto nested under PhaseGoto ----------

        [Test]
        public async Task SessionGotoNestedUnderPhaseGoto_UnwindsLifo()
        {
            WithPrompts(0);

            // A GOTOs to the decision, then resumes +10 after the outer return.
            var a = Phase("A",
                Entry("n-a-entry"),
                Action("n-a-goto", Goto("px-A-complete"), Stat("i-a-later", 10)),
                EndSession("n-a-end"));
            a.Exits.Add(Exit("A", "Complete"));
            Edge(a, "n-a-entry-out", "n-a-goto");
            Edge(a, "n-a-goto-out", "n-a-end");

            // B returns immediately, popping the decision frame.
            var b = Phase("B",
                Entry("n-b-entry"),
                Action("n-b-action", Stat("i-b", 1)),
                new ReturnNodeDefinition { Id = "n-b-return" });
            b.Exits.Add(Exit("B", "Complete"));
            Edge(b, "n-b-entry-out", "n-b-action");
            Edge(b, "n-b-action-out", "n-b-return");

            // C returns, popping A's frame.
            var c = Phase("C",
                Entry("n-c-entry"),
                new ReturnNodeDefinition { Id = "n-c-return" });
            c.Exits.Add(Exit("C", "Complete"));
            Edge(c, "n-c-entry-out", "n-c-return");

            var session = new SessionDefinition { Id = "s4", Title = "s4", SessionTypeId = "type" };
            var decision = Decision("n-decision", "Choose", "o0", "Go",
                new ActionInstanceDefinition[]
                {
                    Stat("i-d", 3),
                    SessionGoto("sg-2", "to B"),
                },
                "sg-2", "n-decision-sg");
            session.Graph.Nodes.Add(Start());
            session.Graph.Nodes.Add(Ref("n-refA", "A", "n-refA-a", "px-A-complete"));
            session.Graph.Nodes.Add(decision);
            session.Graph.Nodes.Add(Ref("n-refB", "B", "n-refB-b", "px-B-complete"));
            session.Graph.Nodes.Add(Ref("n-refC", "C", "n-refC-c", "px-C-complete"));
            session.Graph.Nodes.Add(End());
            session.Graph.Edges.Add(SessionEdge("n-start-out", "n-refA"));
            session.Graph.Edges.Add(SessionEdge("n-refA-a", "n-decision"));
            session.Graph.Edges.Add(SessionEdge("n-decision-sg", "n-refB"));
            session.Graph.Edges.Add(SessionEdge("n-decision-normal", "n-refC"));
            session.Graph.Edges.Add(SessionEdge("n-refB-b", "n-end"));
            session.Graph.Edges.Add(SessionEdge("n-refC-c", "n-end"));

            var vm = Session(session, a, b, c);
            var result = await RunToCompletion(vm);

            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, result.Outcome);
            Assert.AreEqual(14, _player.Stats.Get("courage"),
                "B (1) + decision (3) + A resumed (10)");
            Assert.AreEqual(0, vm.ContinuationDepth, "both frames popped (LIFO) before session end");
        }

        // ---------- 5. SessionGoto targets: SessionDecision and End ----------

        [Test]
        public async Task SessionGoto_CanTargetAnotherDecision()
        {
            WithPrompts(0, 0);

            var session = new SessionDefinition { Id = "s5", Title = "s5", SessionTypeId = "type" };
            var d1 = Decision("n-d1", "First", "o1", "One",
                new[] { SessionGoto("sg-3", "to d2") },
                "sg-3", "n-d1-sg");
            var d2 = Decision("n-d2", "Second", "o2", "Two",
                new[] { Stat("i-d2", 7) }, null, null);
            session.Graph.Nodes.Add(Start());
            session.Graph.Nodes.Add(d1);
            session.Graph.Nodes.Add(d2);
            session.Graph.Nodes.Add(End());
            session.Graph.Edges.Add(SessionEdge("n-start-out", "n-d1"));
            session.Graph.Edges.Add(SessionEdge("n-d1-sg", "n-d2"));
            session.Graph.Edges.Add(SessionEdge("n-d1-normal", "n-end"));
            session.Graph.Edges.Add(SessionEdge("n-d2-normal", "n-end"));

            var vm = Session(session);
            var result = await RunToCompletion(vm);

            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, result.Outcome);
            Assert.AreEqual(7, _player.Stats.Get("courage"), "second decision's option ran");
            Assert.AreEqual(2, _prompts.Requests.Count);
        }

        [Test]
        public async Task SessionGoto_CanTargetEnd()
        {
            WithPrompts(0);

            var session = new SessionDefinition { Id = "s6", Title = "s6", SessionTypeId = "type" };
            var decision = Decision("n-decision", "Choose", "o0", "Done",
                new[] { SessionGoto("sg-4", "to end") },
                "sg-4", "n-decision-sg");
            session.Graph.Nodes.Add(Start());
            session.Graph.Nodes.Add(decision);
            session.Graph.Nodes.Add(End());
            session.Graph.Edges.Add(SessionEdge("n-start-out", "n-decision"));
            session.Graph.Edges.Add(SessionEdge("n-decision-sg", "n-end"));

            var vm = Session(session);
            var result = await RunToCompletion(vm);

            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, result.Outcome);
        }

        // ---------- 6. unique output per SessionGoto Action Instance ----------

        [Test]
        public async Task EachSessionGotoInstance_UsesItsOwnSocket()
        {
            // Two options, two distinct SessionGoto instances, two sockets.
            WithPrompts(1); // pick the second option

            var b = Phase("B",
                Entry("n-b-entry"),
                Action("n-b-action", Stat("i-b", 2)),
                new ReturnNodeDefinition { Id = "n-b-return" });
            b.Exits.Add(Exit("B", "Complete"));
            Edge(b, "n-b-entry-out", "n-b-action");
            Edge(b, "n-b-action-out", "n-b-return");

            var session = new SessionDefinition { Id = "s7", Title = "s7", SessionTypeId = "type" };
            var decision = Decision("n-decision", "Choose", "o0", "First",
                new[] { SessionGoto("sg-a", "to A target") },
                "sg-a", "n-decision-sg-a");
            var option1 = new SessionDecisionOptionDefinition
            {
                Id = "o1",
                Label = "Second",
                Sequence = new ActionSequenceDefinition
                {
                    Id = "seq-o1",
                    Instances = { SessionGoto("sg-b", "to B target") },
                },
            };
            decision.Options.Add(option1);
            decision.Outputs.Add(new GraphOutputDefinition
            {
                Id = "n-decision-sg-b",
                Kind = GraphPortKind.SessionGoto,
                SessionGotoActionInstanceId = "sg-b",
                Label = "to B target",
            });
            session.Graph.Nodes.Add(Start());
            session.Graph.Nodes.Add(decision);
            session.Graph.Nodes.Add(Ref("n-refB", "B", "n-refB-b", "px-B-complete"));
            session.Graph.Nodes.Add(End());
            session.Graph.Edges.Add(SessionEdge("n-start-out", "n-decision"));
            session.Graph.Edges.Add(SessionEdge("n-decision-sg-a", "n-end"));
            session.Graph.Edges.Add(SessionEdge("n-decision-sg-b", "n-refB"));
            session.Graph.Edges.Add(SessionEdge("n-refB-b", "n-end"));
            session.Graph.Edges.Add(SessionEdge("n-decision-normal", "n-end"));

            var vm = Session(session, b);
            var result = await RunToCompletion(vm);

            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, result.Outcome);
            Assert.AreEqual(2, _player.Stats.Get("courage"),
                "second option's socket (sg-b) routed to B, which ran +2");
        }

        [Test]
        public async Task SessionGotoWithoutSocket_FailsClearly()
        {
            WithPrompts(0);

            var session = new SessionDefinition { Id = "s8", Title = "s8", SessionTypeId = "type" };
            var decision = Decision("n-decision", "Choose", "o0", "Go",
                new[] { SessionGoto("sg-missing", "no socket") },
                null, null); // NOTE: no socket declared
            session.Graph.Nodes.Add(Start());
            session.Graph.Nodes.Add(decision);
            session.Graph.Nodes.Add(End());
            session.Graph.Edges.Add(SessionEdge("n-start-out", "n-decision"));
            session.Graph.Edges.Add(SessionEdge("n-decision-normal", "n-end"));

            var vm = Session(session);
            var context = Context();
            var result = await vm.AdvanceAsync(context, CancellationToken.None);

            Assert.AreEqual(SessionAdvanceOutcome.Error, result.Outcome);
            StringAssert.Contains("no unique output socket", result.ErrorMessage);
        }

        // ---------- 7. authoring contract: max 3 options ----------

        [Test]
        public async Task SessionDecision_MoreThanThreeOptions_IsRejected()
        {
            WithPrompts(0);

            var session = new SessionDefinition { Id = "s9", Title = "s9", SessionTypeId = "type" };
            var decision = new SessionDecisionNodeDefinition { Id = "n-decision", Prompt = "Choose" };
            decision.Outputs.Add(Out("n-decision-normal"));
            for (var i = 0; i < 4; i++)
            {
                var option = new SessionDecisionOptionDefinition
                {
                    Id = "o" + i,
                    Label = "Opt " + i,
                    Sequence = new ActionSequenceDefinition { Id = "seq-o" + i },
                };
                option.Sequence.Instances.Add(Stat("i-o" + i, i));
                decision.Options.Add(option);
            }
            session.Graph.Nodes.Add(Start());
            session.Graph.Nodes.Add(decision);
            session.Graph.Nodes.Add(End());
            session.Graph.Edges.Add(SessionEdge("n-start-out", "n-decision"));
            session.Graph.Edges.Add(SessionEdge("n-decision-normal", "n-end"));

            var vm = Session(session);
            var context = Context();
            var result = await vm.AdvanceAsync(context, CancellationToken.None);

            Assert.AreEqual(SessionAdvanceOutcome.Error, result.Outcome);
            StringAssert.Contains("at most 3", result.ErrorMessage);
        }

        // ---------- 8. engine facade: WPF playback restored ----------

        [Test]
        public async Task Engine_AdvancesThroughSessionVm_InsteadOfThrowing()
        {
            WithPrompts(0);

            var a = Phase("A",
                Entry("n-a-entry"),
                Action("n-a-goto", Goto("px-A-complete"), Stat("i-a-later", 4)),
                EndSession("n-a-end"));
            a.Exits.Add(Exit("A", "Complete"));
            Edge(a, "n-a-entry-out", "n-a-goto");
            Edge(a, "n-a-goto-out", "n-a-end");

            var session = new SessionDefinition { Id = "s10", Title = "s10", SessionTypeId = "type" };
            session.Graph.Nodes.Add(Start());
            session.Graph.Nodes.Add(Ref("n-refA", "A", "n-refA-a", "px-A-complete"));
            session.Graph.Nodes.Add(End());
            session.Graph.Edges.Add(SessionEdge("n-start-out", "n-refA"));
            session.Graph.Edges.Add(SessionEdge("n-refA-a", "n-end"));
            _content.Sessions.Add(session);
            _content.Phases.Add(a);

            var engine = new GameSessionEngine(_content, "s10", _services);
            var result = await engine.RunUntilYieldAsync(CancellationToken.None);

            Assert.AreEqual(AdvanceResultKind.SessionCompleted, result.Kind);
            Assert.IsTrue(engine.IsComplete);
        }
    }
}
