using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Content.Samples;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// Ticket 07 gate: the Phase graph VM — node execution, one-card-per-Advance
    /// budget, variable checks, loop guard, and loud errors. Uses the sample
    /// standard phase graph (Entry→CardExecutor→happiness-check→progress-check).
    /// </summary>
    [TestFixture]
    public class PhaseGraphVmTests
    {
        private GameContentDefinition _content;
        private ContentCatalog _catalog;
        private BackgroundActionTracker _tracker;
        private CoreServices _services;
        private Player _player;
        private TemperatureState _temperatures;

        [SetUp]
        public void SetUp()
        {
            _content = SampleContent.Create();
            _catalog = new ContentCatalog(_content);
            _tracker = new BackgroundActionTracker(null);
            _services = new CoreServices(new FakeDelayService());
            _player = new Player("Tester");
            _temperatures = new TemperatureState(_catalog);
        }

        private PhaseGraphVm Vm(string phaseId)
        {
            var phase = _catalog.PhaseById(phaseId);
            return new PhaseGraphVm(_content, phase, _services, _tracker);
        }

        private PhaseRun Run(string phaseId)
        {
            return new PhaseRun($"placement-{phaseId}", phaseId, new PhaseRunRngFactory(1000).Create());
        }

        private ActionExecutionContext Context()
        {
            return new ActionExecutionContext(_player, _services, _catalog, _temperatures, null, ActionOwnerScope.PhaseActionSequence);
        }

        private async Task<PhaseAdvanceResult> Advance(PhaseGraphVm vm, PhaseRun run)
        {
            return await vm.AdvanceAsync(run, Context(), CancellationToken.None);
        }

        // ---------- standard progress loop ----------

        [Test]
        public async Task StandardLoop_YieldsOneCardPerAdvance_ThenTransfersOnComplete()
        {
            var vm = Vm(SampleContent.PhaseWarmUp);
            var run = Run(SampleContent.PhaseWarmUp);

            var results = new List<PhaseAdvanceResult>();
            for (var i = 0; i < 12; i++)
            {
                var result = await Advance(vm, run);
                results.Add(result);
                if (result.Outcome != PhaseAdvanceOutcome.CardExecuted) break;
            }

            // Cards carry +10 progress; 10 cards hit >=100 and the graph transfers.
            var cardAdvances = 0;
            PhaseAdvanceResult final = null;
            foreach (var result in results)
            {
                if (result.Outcome == PhaseAdvanceOutcome.CardExecuted) cardAdvances++;
                final = result;
            }

            // 10 cards of +10 reach 100; the 10th advance runs its card then
            // transfers, so 9 advances report CardExecuted before the transfer.
            Assert.GreaterOrEqual(cardAdvances, 9, "progress accumulates to the completion threshold");
            Assert.AreEqual(PhaseAdvanceOutcome.Transferred, final.Outcome, "graph GOTOs Complete at 100 progress");
            Assert.AreEqual(ActionTransfer.PhaseGoto, final.Transfer.Transfer);
            Assert.AreEqual($"px-{SampleContent.PhaseWarmUp}-complete", final.Transfer.PhaseExitId);
        }

        [Test]
        public async Task OneCardPerAdvance_EvenAcrossCardExecutors()
        {
            // Phase where CardExecutor -> normal -> second CardExecutor (two in a row):
            // one Advance must execute exactly one card and yield at the second.
            var phase = BuildPhase("two-exec", withFail: false);
            var vm = new PhaseGraphVm(_content, phase, _services, _tracker);
            var run = Run(phase.Id);

            var result = await Advance(vm, run);
            Assert.AreEqual(PhaseAdvanceOutcome.CardExecuted, result.Outcome);

            var second = await Advance(vm, run);
            Assert.AreEqual(PhaseAdvanceOutcome.CardExecuted, second.Outcome, "next Advance draws the second executor's card");
        }

        // ---------- action-only / check-before-card ----------

        [Test]
        public async Task ActionOnlyPhase_ExecutesActionsThenTransfers()
        {
            var phase = new PhaseDefinition { Id = "action-only" };
            var entry = Out(new PhaseEntryNodeDefinition { Id = "n-entry" });
            var action = new ActionNodeDefinition
            {
                Id = "n-action",
                Sequence = new ActionSequenceDefinition
                {
                    Id = "seq-action-only",
                    Instances =
                    {
                        new StatIncreaseInstanceDefinition { Id = "i1", StatKey = "courage", Amount = 5f },
                        new DebugInstanceDefinition { Id = "i2", Message = "log line" },
                    },
                },
            };
            action.Outputs.Add(OutId("n-action-out"));
            var returnNode = new ReturnNodeDefinition { Id = "n-return" };

            phase.Graph.Nodes.Add(entry);
            phase.Graph.Nodes.Add(action);
            phase.Graph.Nodes.Add(returnNode);
            phase.Graph.Edges.Add(Edge("n-entry-out", "n-action"));
            phase.Graph.Edges.Add(Edge("n-action-out", "n-return"));

            var vm = new PhaseGraphVm(_content, phase, _services, _tracker);
            var run = Run(phase.Id);
            var result = await Advance(vm, run);

            Assert.AreEqual(5, _player.Stats.Get("courage"), "action node ran its sequence");
            Assert.AreEqual(PhaseAdvanceOutcome.Transferred, result.Outcome);
            Assert.AreEqual(ActionTransfer.Return, result.Transfer.Transfer,
                "ReturnNode is the phase's control transfer placeholder (Ticket 08 wires the stack)");
        }

        [Test]
        public async Task CheckBeforeCard_EvaluatesVariableFirst()
        {
            var phase = new PhaseDefinition { Id = "check-first" };
            var entry = Out(new PhaseEntryNodeDefinition { Id = "n-entry" });
            var check = new VariableCheckNodeDefinition
            {
                Id = "n-check",
                SourceKind = VariableSourceKind.PhaseProgress,
                Operator = VariableCompareOperator.GreaterThanOrEqual,
                CompareValue = 5f,
            };
            check.Outputs.Add(OutId("n-check-true", GraphPortKind.True));
            check.Outputs.Add(OutId("n-check-false", GraphPortKind.False));
            var exec = Out(new CardExecutorNodeDefinition { Id = "n-exec" });
            var returnNode = new ReturnNodeDefinition { Id = "n-return" };

            phase.Graph.Nodes.Add(entry);
            phase.Graph.Nodes.Add(check);
            phase.Graph.Nodes.Add(exec);
            phase.Graph.Nodes.Add(returnNode);
            phase.Graph.Edges.Add(Edge("n-entry-out", "n-check"));
            phase.Graph.Edges.Add(Edge("n-check-false", "n-exec"));
            phase.Graph.Edges.Add(Edge("n-exec-out", "n-check")); // after the card, re-check
            phase.Graph.Edges.Add(Edge("n-check-true", "n-return")); // reached once the card pushes progress >= 5

            var vm = new PhaseGraphVm(_content, phase, _services, _tracker);
            var run = Run(phase.Id);
            run.Progress.Value = 2f;

            var result = await Advance(vm, run);

            // First advance: check (2 < 5) -> CardExecutor runs one card (+10) ->
            // re-check (12 >= 5) -> ReturnNode -> Transferred. The check ran BEFORE the card.
            Assert.AreEqual(PhaseAdvanceOutcome.Transferred, result.Outcome);
            Assert.AreEqual(ActionTransfer.Return, result.Transfer.Transfer);
            Assert.AreEqual(12f, run.Progress.Value, "exactly one card executed before the transfer");
        }

        // ---------- variable operators ----------

        [Test]
        public async Task VariableCheck_AllOperators_EvaluateAgainstTemperature()
        {
            var operators = new[]
            {
                (VariableCompareOperator.LessThan, 51f, true),
                (VariableCompareOperator.LessThanOrEqual, 50f, true),
                (VariableCompareOperator.Equal, 50f, true),
                (VariableCompareOperator.NotEqual, 50f, false),
                (VariableCompareOperator.GreaterThanOrEqual, 50f, true),
                (VariableCompareOperator.GreaterThan, 50f, false),
            };

            foreach (var (op, compare, expectedTrue) in operators)
            {
                var phase = new PhaseDefinition { Id = "op-" + (int)op };
                var entry = Out(new PhaseEntryNodeDefinition { Id = "n-entry" });
                var check = new VariableCheckNodeDefinition
                {
                    Id = "n-check",
                    SourceKind = VariableSourceKind.Temperature,
                    VariableKey = SampleContent.TemperatureHappiness,
                    Operator = op,
                    CompareValue = compare,
                };
                check.Outputs.Add(OutId("n-check-true", GraphPortKind.True));
                check.Outputs.Add(OutId("n-check-false", GraphPortKind.False));
                var exec = Out(new CardExecutorNodeDefinition { Id = "n-exec" });

                phase.Graph.Nodes.Add(entry);
                phase.Graph.Nodes.Add(check);
                phase.Graph.Nodes.Add(exec);
                phase.Graph.Edges.Add(Edge("n-entry-out", "n-check"));
                phase.Graph.Edges.Add(Edge(expectedTrue ? "n-check-true" : "n-check-false", "n-exec"));
                phase.Graph.Edges.Add(Edge("n-exec-out", "n-check"));

                var vm = new PhaseGraphVm(_content, phase, _services, _tracker);
                var run = Run(phase.Id);
                var result = await Advance(vm, run);
                Assert.AreEqual(PhaseAdvanceOutcome.CardExecuted, result.Outcome,
                    $"op {op} against happiness 50 / {compare} should reach the executor");
            }
        }

        // ---------- errors ----------

        [Test]
        public async Task NoEligibleCard_IsARuntimeError()
        {
            var phase = new PhaseDefinition
            {
                Id = "no-card",
                MustIncludeTags = { "does-not-exist-tag" },
            };
            var entry = Out(new PhaseEntryNodeDefinition { Id = "n-entry" });
            var exec = new CardExecutorNodeDefinition { Id = "n-exec" };
            phase.Graph.Nodes.Add(entry);
            phase.Graph.Nodes.Add(exec);
            phase.Graph.Edges.Add(Edge("n-entry-out", "n-exec"));

            var vm = new PhaseGraphVm(_content, phase, _services, _tracker);
            var run = Run(phase.Id);
            var result = await Advance(vm, run);

            Assert.AreEqual(PhaseAdvanceOutcome.Error, result.Outcome);
            StringAssert.Contains("no eligible card", result.ErrorMessage);
        }

        [Test]
        public void MissingSingularEntry_IsRejectedAtConstruction()
        {
            var phase = new PhaseDefinition { Id = "no-entry" };
            phase.Graph.Nodes.Add(new CardExecutorNodeDefinition { Id = "n-exec" });
            Assert.Throws<System.InvalidOperationException>(() => new PhaseGraphVm(_content, phase, _services, _tracker));
        }

        [Test]
        public async Task DeadEndNormalOutput_IsARuntimeError()
        {
            var phase = new PhaseDefinition { Id = "dead-end" };
            var entry = Out(new PhaseEntryNodeDefinition { Id = "n-entry" });
            var action = new ActionNodeDefinition
            {
                Id = "n-action",
                Sequence = new ActionSequenceDefinition
                {
                    Id = "seq-dead",
                    Instances = { new DebugInstanceDefinition { Id = "i1", Message = "x" } },
                },
            };
            action.Outputs.Add(OutId("n-action-out"));
            phase.Graph.Nodes.Add(entry);
            phase.Graph.Nodes.Add(action);
            phase.Graph.Edges.Add(Edge("n-entry-out", "n-action"));
            // n-action has a Normal output but no outgoing edge: dead end after its sequence.

            var vm = new PhaseGraphVm(_content, phase, _services, _tracker);
            var run = Run(phase.Id);
            var result = await Advance(vm, run);

            Assert.AreEqual(PhaseAdvanceOutcome.Error, result.Outcome);
            StringAssert.Contains("dead end", result.ErrorMessage);
        }

        [Test]
        public async Task LoopGuard_TripsOnNonYieldCycle()
        {
            var phase = new PhaseDefinition { Id = "loop" };
            var entry = Out(new PhaseEntryNodeDefinition { Id = "n-entry" });
            var actionA = new ActionNodeDefinition
            {
                Id = "n-a",
                Sequence = new ActionSequenceDefinition
                {
                    Id = "seq-a",
                    Instances = { new DebugInstanceDefinition { Id = "i1", Message = "a" } },
                },
            };
            actionA.Outputs.Add(OutId("n-a-out"));
            var actionB = new ActionNodeDefinition
            {
                Id = "n-b",
                Sequence = new ActionSequenceDefinition
                {
                    Id = "seq-b",
                    Instances = { new DebugInstanceDefinition { Id = "i2", Message = "b" } },
                },
            };
            actionB.Outputs.Add(OutId("n-b-out"));

            phase.Graph.Nodes.Add(entry);
            phase.Graph.Nodes.Add(actionA);
            phase.Graph.Nodes.Add(actionB);
            phase.Graph.Edges.Add(Edge("n-entry-out", "n-a"));
            phase.Graph.Edges.Add(Edge("n-a-out", "n-b"));
            phase.Graph.Edges.Add(Edge("n-b-out", "n-a")); // infinite non-yield cycle

            var vm = new PhaseGraphVm(_content, phase, _services, _tracker);
            var run = Run(phase.Id);
            var result = await Advance(vm, run);

            Assert.AreEqual(PhaseAdvanceOutcome.Error, result.Outcome);
            StringAssert.Contains("loop guard", result.ErrorMessage);
        }

        // ---------- events ----------

        [Test]
        public async Task VariableCheckEvent_FiresWithValueAndResult()
        {
            var vm = Vm(SampleContent.PhaseWarmUp);
            var temperatureSeen = false;
            float seenValue = 0;
            var seenResult = false;
            vm.VariableCheckEvaluated += (check, value, result) =>
            {
                if (check.SourceKind == VariableSourceKind.Temperature)
                {
                    temperatureSeen = true;
                    seenValue = value;
                    seenResult = result;
                }
            };

            var run = Run(SampleContent.PhaseWarmUp);
            await Advance(vm, run);

            Assert.IsTrue(temperatureSeen, "the happiness check evaluated");
            Assert.AreEqual(50f, seenValue);
            Assert.IsFalse(seenResult, "happiness 50 is not < 10");
        }

        // ---------- graph builders ----------

        private static PhaseDefinition BuildPhase(string id, bool withFail)
        {
            var phase = new PhaseDefinition { Id = id };
            var entry = Out(new PhaseEntryNodeDefinition { Id = "n-entry" });
            var exec1 = Out(new CardExecutorNodeDefinition { Id = "n-exec1" });
            var exec2 = Out(new CardExecutorNodeDefinition { Id = "n-exec2" });
            phase.Graph.Nodes.Add(entry);
            phase.Graph.Nodes.Add(exec1);
            phase.Graph.Nodes.Add(exec2);
            phase.Graph.Edges.Add(Edge("n-entry-out", "n-exec1"));
            phase.Graph.Edges.Add(Edge("n-exec1-out", "n-exec2"));
            phase.Graph.Edges.Add(Edge("n-exec2-out", "n-exec1")); // loop back so the second executor parks after its card
            return phase;
        }

        private static T Out<T>(T node) where T : GraphNodeDefinition
        {
            node.Outputs.Add(OutId(node.Id + "-out"));
            return node;
        }

        private static GraphOutputDefinition OutId(string id, GraphPortKind kind = GraphPortKind.Normal)
        {
            return new GraphOutputDefinition { Id = id, Kind = kind };
        }

        private static GraphEdgeDefinition Edge(string sourceOutputId, string targetNodeId)
        {
            return new GraphEdgeDefinition
            {
                Id = "e-" + sourceOutputId + "-" + targetNodeId,
                SourceOutputId = sourceOutputId,
                TargetNodeId = targetNodeId,
            };
        }
    }
}
