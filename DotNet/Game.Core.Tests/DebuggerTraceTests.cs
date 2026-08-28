using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Content.Samples;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// Ticket 19: the Workbench debugger consumes the real Core VM's fine-grained
    /// trace (SessionNodeChanged / PhaseEntered / PhaseNodeChanged /
    /// VariableCheckEvaluated / CardStarted). These tests drive GameSessionEngine
    /// end-to-end and assert the exact trace a host would highlight against its
    /// canvases, including the continuation-stack depth visible during a nested
    /// (recovery-return) phase.
    /// </summary>
    [TestFixture]
    public class DebuggerTraceTests
    {
        private const string Happiness = SampleContent.TemperatureHappiness;

        private GameContentDefinition _content;
        private ContentCatalog _catalog;
        private RecordingLog _log;
        private FakeDelayService _delay;
        private CoreServices _services;
        private Player _player;
        private TemperatureState _temperatures;

        [SetUp]
        public void SetUp()
        {
            _content = new GameContentDefinition
            {
                Deck = new CardDeckDefinition { Id = "deck", Title = "Deck" },
                SessionTypes = { new SessionTypeDefinition { Id = SampleContent.TypeStandard, Title = "Standard" } },
                Temperatures =
                {
                    new TemperatureDefinition { Id = Happiness, Title = "Happiness", MinValue = 0f, MaxValue = 100f, DefaultValue = 50f },
                },
            };
            _catalog = new ContentCatalog(_content);
            _log = new RecordingLog();
            _delay = new FakeDelayService();
            _services = new CoreServices(_delay, _log, new FakePromptService());
            _player = new Player("Tester");
            _temperatures = new TemperatureState(_catalog);
        }

        // ---------- graph builders (compact, phase-local ids) ----------

        private static PhaseEntryNodeDefinition Entry(string id)
        {
            var node = new PhaseEntryNodeDefinition { Id = id };
            node.Outputs.Add(Out(id + "-out"));
            return node;
        }

        private static CardExecutorNodeDefinition Exec(string id)
        {
            var node = new CardExecutorNodeDefinition { Id = id };
            node.Outputs.Add(Out(id + "-out"));
            return node;
        }

        private static ActionNodeDefinition Action(string id, params ActionInstanceDefinition[] instances)
        {
            var node = new ActionNodeDefinition { Id = id, Sequence = new ActionSequenceDefinition { Id = "seq-" + id } };
            foreach (var instance in instances) node.Sequence.Instances.Add(instance);
            node.Outputs.Add(Out(id + "-out"));
            return node;
        }

        private static VariableCheckNodeDefinition Check(string id, VariableSourceKind source, string key, VariableCompareOperator op, float value)
        {
            var node = new VariableCheckNodeDefinition
            {
                Id = id,
                SourceKind = source,
                VariableKey = key,
                Operator = op,
                CompareValue = value,
            };
            node.Outputs.Add(Out(id + "-true", GraphPortKind.True));
            node.Outputs.Add(Out(id + "-false", GraphPortKind.False));
            return node;
        }

        private static GraphOutputDefinition Out(string id, GraphPortKind kind = GraphPortKind.Normal)
        {
            return new GraphOutputDefinition { Id = id, Kind = kind };
        }

        private static void Edge(PhaseDefinition phase, string sourceOutput, string targetNode)
        {
            phase.Graph.Edges.Add(new GraphEdgeDefinition
            {
                Id = $"e-{sourceOutput}->{targetNode}",
                SourceOutputId = sourceOutput,
                TargetNodeId = targetNode,
            });
        }

        private static PhaseDefinition Phase(string id, params GraphNodeDefinition[] nodes)
        {
            var phase = new PhaseDefinition { Id = id };
            foreach (var node in nodes) phase.Graph.Nodes.Add(node);
            return phase;
        }

        private static PhaseExitDefinition Exit(string name)
        {
            return new PhaseExitDefinition { Id = "px-" + name.ToLowerInvariant(), Name = name };
        }

        private static PhaseGotoInstanceDefinition Goto(string exitId)
        {
            return new PhaseGotoInstanceDefinition { Id = "g-" + exitId, PhaseExitId = exitId };
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

        private static PhaseReferenceNodeDefinition Ref(string id, string phaseId, params (string SocketId, string ExitId)[] sockets)
        {
            var node = new PhaseReferenceNodeDefinition { Id = id, PhaseId = phaseId };
            foreach (var (socketId, exitId) in sockets)
            {
                node.Outputs.Add(new GraphOutputDefinition
                {
                    Id = socketId,
                    Kind = GraphPortKind.PhaseExit,
                    PhaseExitId = exitId,
                });
            }
            return node;
        }

        private static SessionDefinition SessionDef(string id, params (SessionGraphNodeDefinition Node, List<GraphEdgeDefinition> Edges)[] parts)
        {
            var session = new SessionDefinition { Id = id, Title = id, SessionTypeId = SampleContent.TypeStandard };
            foreach (var (node, edges) in parts)
            {
                session.Graph.Nodes.Add(node);
                foreach (var edge in edges) session.Graph.Edges.Add(edge);
            }
            return session;
        }

        private static GraphEdgeDefinition SessionEdge(string source, string target)
        {
            return new GraphEdgeDefinition { Id = $"se-{source}-{target}", SourceOutputId = source, TargetNodeId = target };
        }

        private void AddCard(string id, string title, string[] tags, params ActionInstanceDefinition[] actions)
        {
            var card = new CardDefinition { Id = id, Title = title };
            if (tags != null) card.Tags.AddRange(tags);
            card.Sequence = new ActionSequenceDefinition { Id = "seq-" + id };
            foreach (var action in actions) card.Sequence.Instances.Add(action);
            _content.Cards.Add(card);
            _content.Deck.CardIds.Add(id);
        }

        private static IncrementProgressInstanceDefinition Progress(string id, float amount)
        {
            return new IncrementProgressInstanceDefinition { Id = id, Amount = amount };
        }

        private static ModifyTemperatureInstanceDefinition Temperature(string id, float amount)
        {
            return new ModifyTemperatureInstanceDefinition { Id = id, TemperatureId = Happiness, Amount = amount };
        }

        /// <summary>Advances the engine until the session completes; records results.</summary>
        private async Task<List<AdvanceResult>> RunEngineToCompletion(GameSessionEngine engine)
        {
            var results = new List<AdvanceResult>();
            for (var i = 0; i < 200 && !engine.IsComplete; i++)
            {
                var result = await engine.AdvanceOneCardAsync(CancellationToken.None);
                results.Add(result);
            }
            Assert.IsTrue(engine.IsComplete, "session completed");
            return results;
        }

        // =====================================================================
        // Scenario A — Normal session: the host trace drives canvas highlights.
        // =====================================================================

        [Test]
        public async Task EngineTrace_NormalSession_NodePhaseAndCheckEventsFireInOrder()
        {
            AddCard("calm", "Calm", new[] { "main" }, Progress("i-calm", 10f));

            var main = Phase("main",
                Entry("n-m-entry"), Exec("n-m-exec"),
                Check("n-m-done", VariableSourceKind.PhaseProgress, null, VariableCompareOperator.GreaterThanOrEqual, 100f),
                Action("n-m-goto", Goto("px-complete")));
            main.Exits.Add(Exit("Complete"));
            Edge(main, "n-m-entry-out", "n-m-exec");
            Edge(main, "n-m-exec-out", "n-m-done");
            Edge(main, "n-m-done-false", "n-m-exec");
            Edge(main, "n-m-done-true", "n-m-goto");

            var session = SessionDef("s-main",
                (Start(), new List<GraphEdgeDefinition> { SessionEdge("n-start-out", "n-ref") }),
                (Ref("n-ref", "main", ("n-ref-complete", "px-complete")), new List<GraphEdgeDefinition>
                {
                    SessionEdge("n-ref-complete", "n-end"),
                }),
                (End(), new List<GraphEdgeDefinition>()));

            _content.Sessions.Add(session);
            _content.Phases.Add(main);
            var engine = new GameSessionEngine(_content, "s-main", _services, null,
                new PhaseRunRngFactory(() => new FixedRandomSource(0, 0, 0, 0, 0, 0, 0, 0, 0, 0)));

            var vm = engine.SessionVm; // eager — the debugger subscribes BEFORE the first advance
            Assert.IsNotNull(vm, "SessionVm is available before the first advance (Ticket 19)");

            var sessionTrace = new List<string>();
            var phaseEntered = new List<string>();
            var phaseNodes = new List<string>();
            var checks = new List<(string NodeId, bool Passed)>();
            var cardStarted = 0;
            vm.SessionNodeChanged += id => sessionTrace.Add(id);
            vm.PhaseEntered += id => phaseEntered.Add(id);
            vm.PhaseNodeChanged += id => phaseNodes.Add(id);
            vm.VariableCheckEvaluated += (node, value, passed) => checks.Add((node.Id, passed));
            vm.CardStarted += _ => cardStarted++;

            await RunEngineToCompletion(engine);

            Assert.AreEqual(10, cardStarted, "10 cards: +10 progress each reaches exactly 100");
            CollectionAssert.AreEqual(new[] { "n-start", "n-ref", "n-end" }, sessionTrace,
                "session canvas trace: Start -> ref -> End");
            CollectionAssert.AreEqual(new[] { "main" }, phaseEntered);
            CollectionAssert.Contains(phaseNodes, "n-m-entry");
            CollectionAssert.Contains(phaseNodes, "n-m-exec");
            CollectionAssert.Contains(phaseNodes, "n-m-done");
            CollectionAssert.Contains(phaseNodes, "n-m-goto");

            Assert.AreEqual(10, checks.Count, "the done-check evaluates once per card");
            Assert.AreEqual(true, checks[^1].Passed, "the final check passes");
            Assert.AreEqual(0, vm.ContinuationDepth, "stack empty at completion");
            Assert.IsEmpty(vm.ContinuationSummary());
        }

        // =====================================================================
        // Scenario B — Recovery Return: the host sees depth 1 while nested and
        // depth 0 after RETURN restores the caller.
        // =====================================================================

        [Test]
        public async Task EngineTrace_RecoveryReturn_StackDepthVisibleWhileNested()
        {
            AddCard("drain", "Drain", new[] { "main" }, Temperature("i-drain", -45f), Progress("i-drain-p", 10f));
            AddCard("calm", "Calm", new[] { "main" }, Progress("i-calm", 10f));

            var main = Phase("main",
                Entry("n-m-entry"), Exec("n-m-exec"),
                Check("n-m-fail", VariableSourceKind.Temperature, Happiness, VariableCompareOperator.LessThan, 10f),
                Action("n-m-goto-fail", Goto("px-fail")),
                Check("n-m-done", VariableSourceKind.PhaseProgress, null, VariableCompareOperator.GreaterThanOrEqual, 100f),
                Action("n-m-goto-done", Goto("px-complete")));
            main.Exits.Add(Exit("Fail"));
            main.Exits.Add(Exit("Complete"));
            Edge(main, "n-m-entry-out", "n-m-exec");
            Edge(main, "n-m-exec-out", "n-m-fail");
            Edge(main, "n-m-fail-true", "n-m-goto-fail");
            Edge(main, "n-m-fail-false", "n-m-done");
            Edge(main, "n-m-done-false", "n-m-exec");
            Edge(main, "n-m-done-true", "n-m-goto-done");
            Edge(main, "n-m-goto-fail-out", "n-m-exec"); // resume path after RETURN

            var recovery = Phase("recovery",
                Entry("n-r-entry"),
                Action("n-r-raise", Temperature("i-r-raise", 40f)),
                new ReturnNodeDefinition { Id = "n-r-return" });
            recovery.Exits.Add(Exit("Complete"));
            Edge(recovery, "n-r-entry-out", "n-r-raise");
            Edge(recovery, "n-r-raise-out", "n-r-return");

            var session = SessionDef("s-recovery",
                (Start(), new List<GraphEdgeDefinition> { SessionEdge("n-start-out", "n-ref-main") }),
                (Ref("n-ref-main", "main",
                    ("n-ref-main-fail", "px-fail"),
                    ("n-ref-main-complete", "px-complete")), new List<GraphEdgeDefinition>
                {
                    SessionEdge("n-ref-main-fail", "n-ref-recovery"),
                    SessionEdge("n-ref-main-complete", "n-end"),
                }),
                (Ref("n-ref-recovery", "recovery",
                    ("n-ref-recovery-complete", "px-complete")), new List<GraphEdgeDefinition>
                {
                    SessionEdge("n-ref-recovery-complete", "n-ref-main"),
                }),
                (End(), new List<GraphEdgeDefinition>()));

            _content.Sessions.Add(session);
            _content.Phases.Add(main);
            _content.Phases.Add(recovery);
            // First draw takes the drain card (offset 0); the resumed main run
            // then draws calms (offset 1) until progress reaches 100.
            var engine = new GameSessionEngine(_content, "s-recovery", _services, null,
                new PhaseRunRngFactory(() => new FixedRandomSource(0, 1, 1, 1, 1, 1, 1, 1, 1, 1)));

            var vm = engine.SessionVm;
            var phaseEntered = new List<string>();
            var depthAtPhaseNodes = new List<(string NodeId, int Depth)>();
            var phaseRunSeen = new List<string>();
            vm.PhaseEntered += id => phaseEntered.Add(id);
            vm.PhaseNodeChanged += id =>
            {
                depthAtPhaseNodes.Add((id, vm.ContinuationDepth));
                if (vm.CurrentPlacementNodeId != null && !phaseRunSeen.Contains(vm.CurrentPlacementNodeId))
                {
                    phaseRunSeen.Add(vm.CurrentPlacementNodeId);
                }
            };

            await RunEngineToCompletion(engine);

            // PhaseEntered fires when a run STARTS: main, then the nested
            // recovery. RETURN resumes the main run in place — no re-enter event.
            CollectionAssert.AreEqual(new[] { "main", "recovery" }, phaseEntered,
                "recovery phase runs nested; RETURN resumes main in place");

            var recoveryNodes = depthAtPhaseNodes.FindAll(p => p.NodeId.StartsWith("n-r-"));
            Assert.IsNotEmpty(recoveryNodes, "recovery's nodes appear in the phase trace");
            foreach (var entry in recoveryNodes)
            {
                Assert.AreEqual(1, entry.Depth, "while nested in recovery the continuation stack is depth 1");
            }

            var mainNodesAfterReturn = depthAtPhaseNodes.FindAll(p => p.NodeId.StartsWith("n-m-") && p.Depth == 0);
            Assert.IsNotEmpty(mainNodesAfterReturn, "main's nodes run at depth 0 after RETURN restores the caller");

            // PhaseRun identity: the debugger sees the placement that owns each run.
            CollectionAssert.Contains(phaseRunSeen, "n-ref-main");
            CollectionAssert.Contains(phaseRunSeen, "n-ref-recovery");

            Assert.AreEqual(0, vm.ContinuationDepth, "stack empty at completion");
        }
    }
}
