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
    /// Ticket 10 HARD gate: the integrated Core regression suite. Five
    /// deterministic scenarios (normal session, recovery-return, recursive
    /// phase, session decision, one-shot transfer) plus cancellation
    /// boundaries and background-action fault observation. All graphs are
    /// hand-built with scripted card RNGs so every trace is exact.
    /// </summary>
    [TestFixture]
    public class CoreIntegrationRegressionTests
    {
        private const string Happiness = SampleContent.TemperatureHappiness;

        private GameContentDefinition _content;
        private ContentCatalog _catalog;
        private BackgroundActionTracker _tracker;
        private RecordingLog _log;
        private CoreServices _services;
        private Player _player;
        private TemperatureState _temperatures;
        private FakePromptService _prompts;

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
            _tracker = new BackgroundActionTracker(null);
            _log = new RecordingLog();
            _prompts = new FakePromptService();
            _services = new CoreServices(new FakeDelayService(), _log, _prompts);
            _player = new Player("Tester");
            _temperatures = new TemperatureState(_catalog);
        }

        private void WithPrompts(params int?[] answers)
        {
            _prompts = new FakePromptService(answers);
            _services = new CoreServices(new FakeDelayService(), _log, _prompts);
        }

        private SessionGraphVm Session(SessionDefinition session, params PhaseDefinition[] phases)
        {
            _content.Sessions.Add(session);
            foreach (var phase in phases) _content.Phases.Add(phase);
            _catalog = new ContentCatalog(_content);
            return new SessionGraphVm(_content, session.Id, _services, _tracker, new PhaseRunRngFactory(() => new ScriptedRandom(0, 1, 1, 1, 1, 1, 1, 1, 1, 1)));
        }

        private ActionExecutionContext Context()
        {
            return new ActionExecutionContext(_player, _services, _catalog, _temperatures, null, ActionOwnerScope.All);
        }

        /// <summary>Advances until the session completes; returns every result in order.</summary>
        private async Task<List<SessionAdvanceResult>> RunToCompletion(SessionGraphVm vm)
        {
            var context = Context();
            var results = new List<SessionAdvanceResult>();
            for (var i = 0; i < 100 && !vm.IsComplete; i++)
            {
                var result = await vm.AdvanceAsync(context, CancellationToken.None);
                results.Add(result);
                Assert.That(result.Outcome, Is.Not.EqualTo(SessionAdvanceOutcome.Error), "error: " + result.ErrorMessage);
            }
            Assert.IsTrue(vm.IsComplete, "session completed");
            return results;
        }

        // ---------- graph builders ----------

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
            var node = new ActionNodeDefinition { Id = id, Sequence = new ActionSequenceDefinition { Id = $"seq-{id}" } };
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

        private static IncrementProgressInstanceDefinition Progress(string id, float amount)
        {
            return new IncrementProgressInstanceDefinition { Id = id, Amount = amount };
        }

        private static ModifyTemperatureInstanceDefinition Temperature(string id, float amount)
        {
            return new ModifyTemperatureInstanceDefinition { Id = id, TemperatureId = Happiness, Amount = amount };
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

        private static PhaseExitDefinition Exit(string phaseId, string name)
        {
            return new PhaseExitDefinition { Id = $"px-{phaseId}-{name.ToLowerInvariant()}", Name = name };
        }

        private static PhaseDefinition Phase(string id, params GraphNodeDefinition[] nodes)
        {
            var phase = new PhaseDefinition { Id = id };
            foreach (var node in nodes) phase.Graph.Nodes.Add(node);
            return phase;
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

        private static SessionDecisionNodeDefinition Decision(string id, string prompt, string optionId, string optionLabel,
            ActionInstanceDefinition[] actions, string sessionGotoInstanceId, string sessionGotoSocketId)
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
            foreach (var action in actions) option.Sequence.Instances.Add(action);
            node.Options.Add(option);
            return node;
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

        /// <summary>Scripted RNG: pops offsets in order; returns 0 when exhausted.</summary>
        private sealed class ScriptedRandom : IRandomSource
        {
            private readonly Queue<int> _offsets;
            public ScriptedRandom(params int[] offsets) => _offsets = new Queue<int>(offsets);
            public int NextInt(int minInclusive, int maxExclusive)
            {
                if (_offsets.Count == 0) return minInclusive;
                var offset = _offsets.Dequeue();
                return Math.Min(minInclusive + offset, maxExclusive - 1);
            }
        }

        // =====================================================================
        // Scenario 1 — Normal Session: Start -> Warmup -> Main -> End
        // =====================================================================

        [Test]
        public async Task NormalSession_ExactTraceBudgetProgressStack()
        {
            // Cards: +10 progress each, no happiness change.
            AddCard("c1", "Calm", null, Progress("i-c1", 10f), Stat("i-c1s", 1));

            // Warmup: Entry -> Exec -> done(>=100): true -> gotoDone; false -> Exec.
            var warmup = Phase("warmup",
                Entry("n-w-entry"), Exec("n-w-exec"),
                Check("n-w-done", VariableSourceKind.PhaseProgress, null, VariableCompareOperator.GreaterThanOrEqual, 100f),
                Action("n-w-goto", Goto("px-warmup-complete")));
            warmup.Exits.Add(Exit("warmup", "Complete"));
            Edge(warmup, "n-w-entry-out", "n-w-exec");
            Edge(warmup, "n-w-exec-out", "n-w-done");
            Edge(warmup, "n-w-done-false", "n-w-exec");
            Edge(warmup, "n-w-done-true", "n-w-goto");

            var main = Phase("main",
                Entry("n-m-entry"), Exec("n-m-exec"),
                Check("n-m-done", VariableSourceKind.PhaseProgress, null, VariableCompareOperator.GreaterThanOrEqual, 100f),
                Action("n-m-goto", Goto("px-main-complete")));
            main.Exits.Add(Exit("main", "Complete"));
            Edge(main, "n-m-entry-out", "n-m-exec");
            Edge(main, "n-m-exec-out", "n-m-done");
            Edge(main, "n-m-done-false", "n-m-exec");
            Edge(main, "n-m-done-true", "n-m-goto");

            var session = SessionDef("s-normal",
                (Start(), new List<GraphEdgeDefinition> { SessionEdge("n-start-out", "n-ref-warmup") }),
                (Ref("n-ref-warmup", "warmup", ("n-ref-warmup-complete", "px-warmup-complete")), new List<GraphEdgeDefinition>
                {
                    SessionEdge("n-ref-warmup-complete", "n-ref-main"),
                }),
                (Ref("n-ref-main", "main", ("n-ref-main-complete", "px-main-complete")), new List<GraphEdgeDefinition>
                {
                    SessionEdge("n-ref-main-complete", "n-end"),
                }),
                (End(), new List<GraphEdgeDefinition>()));

            var vm = Session(session, warmup, main);

            var sessionTrace = new List<string>();
            vm.SessionNodeChanged += id => sessionTrace.Add(id);
            var cardStarted = 0;
            vm.CardStarted += _ => cardStarted++;

            var results = await RunToCompletion(vm);

            // 20 cards total (10 per phase, +10 progress each => exactly 100).
            Assert.AreEqual(20, cardStarted, "exact card count");
            Assert.AreEqual(20, _player.Stats.Get("courage"), "every card's +1 ran");
            Assert.AreEqual(50f, _temperatures.Get(Happiness), "happiness untouched");
            Assert.AreEqual(0, vm.ContinuationDepth, "stack empty at completion");

            // Session node trace: Start -> warmup ref -> main ref -> End.
            CollectionAssert.AreEqual(
                new[] { "n-start", "n-ref-warmup", "n-ref-main", "n-end" }, sessionTrace,
                "exact session node trace");

            // One card per advance, plus the final transfer advance completing the session.
            Assert.AreEqual(19, results.Count, "19 advances: 18 card-executes + 1 session-complete");
            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, results[^1].Outcome);
        }

        // =====================================================================
        // Scenario 2 — Recovery Return: happiness failure -> GOTO recovery ->
        // RETURN -> Main resumes exact progress/card RNG -> Complete -> End.
        // =====================================================================

        [Test]
        public async Task RecoveryReturn_ResumesExactProgressAndCardRng()
        {
            // drain: happiness 50 -> 5 (below 10), progress +10. calm: progress +10 only.
            AddCard("drain", "Drain", null, Temperature("i-drain", -45f), Progress("i-drain-p", 10f));
            AddCard("calm", "Calm", null, Progress("i-calm", 10f));

            // Main: Entry -> Exec -> fail(<=? happiness<10): true -> gotoFail; false -> done(>=100).
            var main = Phase("main",
                Entry("n-m-entry"), Exec("n-m-exec"),
                Check("n-m-fail", VariableSourceKind.Temperature, Happiness, VariableCompareOperator.LessThan, 10f),
                Action("n-m-goto-fail", Goto("px-main-fail")),
                Check("n-m-done", VariableSourceKind.PhaseProgress, null, VariableCompareOperator.GreaterThanOrEqual, 100f),
                Action("n-m-goto-done", Goto("px-main-complete")));
            main.Exits.Add(Exit("main", "Fail"));
            main.Exits.Add(Exit("main", "Complete"));
            Edge(main, "n-m-entry-out", "n-m-exec");
            Edge(main, "n-m-exec-out", "n-m-fail");
            Edge(main, "n-m-fail-true", "n-m-goto-fail");
            Edge(main, "n-m-fail-false", "n-m-done");
            Edge(main, "n-m-done-false", "n-m-exec");
            Edge(main, "n-m-done-true", "n-m-goto-done");
            // Resume path after RETURN: gotoFail's normal edge continues the loop.
            Edge(main, "n-m-goto-fail-out", "n-m-exec");

            // Recovery: Entry -> raise happiness +40 -> ReturnNode (pops Main's frame).
            var recovery = Phase("recovery",
                Entry("n-r-entry"),
                Action("n-r-raise", Temperature("i-r-raise", 40f)),
                new ReturnNodeDefinition { Id = "n-r-return" });
            recovery.Exits.Add(Exit("recovery", "Complete"));
            Edge(recovery, "n-r-entry-out", "n-r-raise");
            Edge(recovery, "n-r-raise-out", "n-r-return");

            var session = SessionDef("s-recovery",
                (Start(), new List<GraphEdgeDefinition> { SessionEdge("n-start-out", "n-ref-main") }),
                (Ref("n-ref-main", "main",
                    ("n-ref-main-fail", "px-main-fail"),
                    ("n-ref-main-complete", "px-main-complete")), new List<GraphEdgeDefinition>
                {
                    SessionEdge("n-ref-main-fail", "n-ref-recovery"),
                    SessionEdge("n-ref-main-complete", "n-end"),
                }),
                (Ref("n-ref-recovery", "recovery", ("n-ref-recovery-complete", "px-recovery-complete")), new List<GraphEdgeDefinition>
                {
                    SessionEdge("n-ref-recovery-complete", "n-end"),
                }),
                (End(), new List<GraphEdgeDefinition>()));

            var vm = Session(session, main, recovery);

            var cardTrace = new List<string>();
            vm.CardStarted += card => cardTrace.Add(card.Id);
            var progressSeen = new List<float>();
            vm.VariableCheckEvaluated += (check, value, _) =>
            {
                if (check.SourceKind == VariableSourceKind.PhaseProgress) progressSeen.Add(value);
            };
            var mainEntered = 0;
            vm.PhaseEntered += id => { if (id == "main") mainEntered++; };

            var results = await RunToCompletion(vm);

            // First card drains happiness to 5 -> fail -> recovery (+40 -> 45) ->
            // RETURN -> Main resumes and draws calm (progress 10+10=20) — all in advance 1.
            Assert.AreEqual("drain", cardTrace[0], "scripted first draw is the drain card");
            Assert.AreEqual("calm", cardTrace[1], "resumed Main draws calm next (RNG preserved)");
            Assert.AreEqual(10, cardTrace.Count, "exactly 10 cards: 1 drain + 9 calm");

            Assert.AreEqual(45f, _temperatures.Get(Happiness), "5 (drain) + 40 (recovery) = 45");
            Assert.AreEqual(100f, progressSeen[^1], "progress accumulated to exactly 100");
            Assert.AreEqual(1, mainEntered, "Main's PhaseRun was resumed, not re-entered fresh");
            Assert.AreEqual(0, vm.ContinuationDepth, "stack drained after the return and completion");
            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, results[^1].Outcome);
        }

        // =====================================================================
        // Scenario 3 — Recursive Phase: A GOTO another placement of A, then return.
        // =====================================================================

        [Test]
        public async Task RecursivePhase_SecondPlacementReturnsToFirst()
        {
            // A: Entry -> +1 courage -> check(courage >= 2): true -> ReturnNode;
            //    false -> gotoA (GOTO px-A-complete); gotoA normal -> +5 -> EndSession.
            var a = Phase("A",
                Entry("n-a-entry"),
                Action("n-a-plus", Stat("i-a-plus", 1)),
                Check("n-a-check", VariableSourceKind.Stat, "courage", VariableCompareOperator.GreaterThanOrEqual, 2f),
                Action("n-a-goto", Goto("px-A-complete")),
                new ReturnNodeDefinition { Id = "n-a-return" },
                Action("n-a-resume", Stat("i-a-resume", 5), new EndSessionInstanceDefinition { Id = "i-a-end" }));
            a.Exits.Add(Exit("A", "Complete"));
            Edge(a, "n-a-entry-out", "n-a-plus");
            Edge(a, "n-a-plus-out", "n-a-check");
            Edge(a, "n-a-check-true", "n-a-return");
            Edge(a, "n-a-check-false", "n-a-goto");
            Edge(a, "n-a-goto-out", "n-a-resume"); // resumed outer run ends the session

            var session = SessionDef("s-recursive",
                (Start(), new List<GraphEdgeDefinition> { SessionEdge("n-start-out", "n-ref1") }),
                (Ref("n-ref1", "A", ("n-ref1-complete", "px-A-complete")), new List<GraphEdgeDefinition>
                {
                    SessionEdge("n-ref1-complete", "n-ref2"),
                }),
                (Ref("n-ref2", "A", ("n-ref2-complete", "px-A-complete")), new List<GraphEdgeDefinition>
                {
                    SessionEdge("n-ref2-complete", "n-end"),
                }),
                (End(), new List<GraphEdgeDefinition>()));

            var vm = Session(session, a);

            var phaseEntered = 0;
            vm.PhaseEntered += _ => phaseEntered++;
            var results = await RunToCompletion(vm);

            Assert.AreEqual(2, phaseEntered, "A entered twice: two independent PhaseRuns");
            Assert.AreEqual(7, _player.Stats.Get("courage"), "1 (run1) + 1 (run2) + 5 (run1 resumed)");
            Assert.AreEqual(0, vm.ContinuationDepth, "stack drained");
            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, results[^1].Outcome);
        }

        // =====================================================================
        // Scenario 4 — Decision: option mutates, SessionGoto Bonus, Bonus Return,
        // option finishes, normal edge.
        // =====================================================================

        [Test]
        public async Task SessionDecision_SessionGotoThenReturn_OptionFinishesNormalEdge()
        {
            WithPrompts(0);

            var bonus = Phase("bonus",
                Entry("n-b-entry"),
                Action("n-b-plus", Stat("i-b-plus", 2)),
                new ReturnNodeDefinition { Id = "n-b-return" });
            bonus.Exits.Add(Exit("bonus", "Complete"));
            Edge(bonus, "n-b-entry-out", "n-b-plus");
            Edge(bonus, "n-b-plus-out", "n-b-return");

            var session = SessionDef("s-decision",
                (Start(), new List<GraphEdgeDefinition> { SessionEdge("n-start-out", "n-decision") }),
                (Decision("n-decision", "Choose", "o0", "Go",
                    new ActionInstanceDefinition[]
                    {
                        Stat("i-d-before", 1),
                        SessionGoto("sg-1", "to bonus"),
                        Stat("i-d-after", 5),
                    },
                    "sg-1", "n-decision-sg"), new List<GraphEdgeDefinition>
                {
                    SessionEdge("n-decision-sg", "n-ref-bonus"),
                    SessionEdge("n-decision-normal", "n-end"),
                }),
                (Ref("n-ref-bonus", "bonus", ("n-ref-bonus-complete", "px-bonus-complete")), new List<GraphEdgeDefinition>
                {
                    SessionEdge("n-ref-bonus-complete", "n-end"),
                }),
                (End(), new List<GraphEdgeDefinition>()));

            var vm = Session(session, bonus);

            var results = await RunToCompletion(vm);

            Assert.AreEqual(8, _player.Stats.Get("courage"), "1 (option) + 2 (bonus) + 5 (option resumed)");
            Assert.AreEqual(0, vm.ContinuationDepth, "session-level frame popped by Bonus's Return");
            Assert.AreEqual(1, _prompts.Requests.Count, "exactly one decision prompt");
            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, results[^1].Outcome);
        }

        // =====================================================================
        // Scenario 5 — One-shot: action-only phase GOTOs next WITHOUT consuming a
        // card; the next phase's first card executes in the same Advance.
        // =====================================================================

        [Test]
        public async Task OneShotTransfer_NextPhasesFirstCardExecutesInSameAdvance()
        {
            AddCard("c1", "Calm", null, Progress("i-c1", 10f));

            // P1: action-only — Entry -> gotoNext; no CardExecutor at all.
            var p1 = Phase("p1",
                Entry("n-p1-entry"),
                Action("n-p1-goto", Goto("px-p1-complete")));
            p1.Exits.Add(Exit("p1", "Complete"));
            Edge(p1, "n-p1-entry-out", "n-p1-goto");

            var p2 = Phase("p2",
                Entry("n-p2-entry"), Exec("n-p2-exec"),
                Check("n-p2-done", VariableSourceKind.PhaseProgress, null, VariableCompareOperator.GreaterThanOrEqual, 100f),
                Action("n-p2-goto", Goto("px-p2-complete")));
            p2.Exits.Add(Exit("p2", "Complete"));
            Edge(p2, "n-p2-entry-out", "n-p2-exec");
            Edge(p2, "n-p2-exec-out", "n-p2-done");
            Edge(p2, "n-p2-done-false", "n-p2-exec");
            Edge(p2, "n-p2-done-true", "n-p2-goto");

            var session = SessionDef("s-oneshot",
                (Start(), new List<GraphEdgeDefinition> { SessionEdge("n-start-out", "n-ref-p1") }),
                (Ref("n-ref-p1", "p1", ("n-ref-p1-complete", "px-p1-complete")), new List<GraphEdgeDefinition>
                {
                    SessionEdge("n-ref-p1-complete", "n-ref-p2"),
                }),
                (Ref("n-ref-p2", "p2", ("n-ref-p2-complete", "px-p2-complete")), new List<GraphEdgeDefinition>
                {
                    SessionEdge("n-ref-p2-complete", "n-end"),
                }),
                (End(), new List<GraphEdgeDefinition>()));

            var vm = Session(session, p1, p2);

            var context = Context();
            var first = await vm.AdvanceAsync(context, CancellationToken.None);

            // P1 ran its GOTO (no card) and P2's first card executed in the SAME advance.
            Assert.AreEqual(SessionAdvanceOutcome.CardExecuted, first.Outcome);
            Assert.IsNotNull(first.Card, "a card executed in the same advance as the transfer");
            Assert.AreEqual(1, vm.ContinuationDepth, "P1's frame is suspended while P2 runs");

            var results = await RunToCompletion(vm);
            Assert.AreEqual(SessionAdvanceOutcome.SessionCompleted, results[^1].Outcome);
            Assert.AreEqual(0, vm.ContinuationDepth);
        }

        // =====================================================================
        // Cancellation boundaries
        // =====================================================================

        [Test]
        public async Task PreCancelledToken_AbortsTheAdvance()
        {
            AddCard("c1", "Calm", null, Progress("i-c1", 10f));
            var p = Phase("p",
                Entry("n-entry"), Exec("n-exec"),
                Check("n-done", VariableSourceKind.PhaseProgress, null, VariableCompareOperator.GreaterThanOrEqual, 100f),
                Action("n-goto", Goto("px-p-complete")));
            p.Exits.Add(Exit("p", "Complete"));
            Edge(p, "n-entry-out", "n-exec");
            Edge(p, "n-exec-out", "n-done");
            Edge(p, "n-done-false", "n-exec");
            Edge(p, "n-done-true", "n-goto");

            var session = SessionDef("s-cancel",
                (Start(), new List<GraphEdgeDefinition> { SessionEdge("n-start-out", "n-ref-p") }),
                (Ref("n-ref-p", "p", ("n-ref-p-complete", "px-p-complete")), new List<GraphEdgeDefinition>
                {
                    SessionEdge("n-ref-p-complete", "n-end"),
                }),
                (End(), new List<GraphEdgeDefinition>()));

            var vm = Session(session, p);
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.ThrowsAsync<OperationCanceledException>(
                    async () => await vm.AdvanceAsync(Context(), cts.Token));
            }
        }

        // =====================================================================
        // Background action faults observed
        // =====================================================================

        [Test]
        public async Task BackgroundActionFault_IsObservedAndLogged()
        {
            // A nonblocking debug action whose delay faults: the tracker must log it.
            var faultingDelay = new FaultingDelay();
            var log = new RecordingLog();
            var tracker = new BackgroundActionTracker(log);
            var services = new CoreServices(faultingDelay, log);
            _catalog = new ContentCatalog(_content);
            var context = new ActionExecutionContext(_player, services, _catalog, _temperatures, null, ActionOwnerScope.All);
            var executor = new ActionExecutor(tracker);

            var sequence = new ActionSequenceDefinition { Id = "seq-bg" };
            sequence.Instances.Add(new DebugInstanceDefinition
            {
                Id = "bg1",
                Message = "boom",
                DelaySeconds = 1f,
                IsBlocking = false,
            });

            await executor.ExecuteSequenceAsync(sequence, context, CancellationToken.None);
            await tracker.DrainAsync();

            Assert.AreEqual(0, tracker.ActiveCount, "faulted task removed from the tracker");
            Assert.That(log.Entries, Has.Some.Matches<string>(e => e.Contains("Background action faulted")),
                "fault observed and logged, never an unobserved exception");
        }

        private sealed class FaultingDelay : IGameDelay
        {
            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("synthetic background failure");
            }
        }
    }
}
