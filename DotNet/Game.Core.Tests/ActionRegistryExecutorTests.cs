using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Content.Samples;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// Ticket 05 gate: the explicit Action Type registry and the instance
    /// executor — general actions run their parameterized behavior, flow
    /// actions reduce to transfer requests, scope/blocking guards reject
    /// illegal authoring, and progress changes ONLY through instances.
    /// </summary>
    [TestFixture]
    public class ActionRegistryExecutorTests
    {
        private ContentCatalog _catalog;
        private BackgroundActionTracker _tracker;
        private RecordingLog _log;
        private CoreServices _services;
        private ActionExecutor _executor;
        private Player _player;

        [SetUp]
        public void SetUp()
        {
            _catalog = new ContentCatalog(SampleContent.Create());
            _tracker = new BackgroundActionTracker(null);
            _log = new RecordingLog();
            _services = new CoreServices(new FakeDelayService(), _log);
            _executor = new ActionExecutor(_tracker);
            _player = new Player("Tester");
        }

        private ActionExecutionContext Context(ActionOwnerScope scope, bool withPhaseRun = true)
        {
            return new ActionExecutionContext(
                _player,
                _services,
                _catalog,
                new TemperatureState(_catalog),
                withPhaseRun ? new PhaseProgressState() : null,
                scope);
        }

        private static async Task<ActionExecutionResult> Run(ActionExecutor executor, ActionExecutionContext context,
            params ActionInstanceDefinition[] instances)
        {
            var sequence = new ActionSequenceDefinition { Id = "seq-test" };
            foreach (var instance in instances) sequence.Instances.Add(instance);
            return await executor.ExecuteSequenceAsync(sequence, context, CancellationToken.None);
        }

        // ---------- registry ----------

        [Test]
        public void Registry_CoversEveryInstanceType()
        {
            var instanceTypes = new ActionInstanceDefinition[]
            {
                new DebugInstanceDefinition(),
                new StatIncreaseInstanceDefinition(),
                new IncrementProgressInstanceDefinition(),
                new ModifyTemperatureInstanceDefinition(),
                new CutsceneInstanceDefinition(),
                new DialogInstanceDefinition(),
                new DialogFromTagsInstanceDefinition(),
                new DelayInstanceDefinition(),
                new ToySetPatternInstanceDefinition(),
                new ToyActivityInstanceDefinition(),
                new PromptChoiceInstanceDefinition(),
                new WaitForContinueInstanceDefinition(),
                new WaitForAllInstanceDefinition(),
                new PhaseGotoInstanceDefinition(),
                new SessionGotoInstanceDefinition(),
                new ReturnInstanceDefinition(),
                new EndSessionInstanceDefinition(),
            };

            foreach (var instance in instanceTypes)
            {
                var info = ActionTypeRegistry.ForInstance(instance);
                Assert.IsNotEmpty(info.TypeKey, "every instance type has a registry key");
                Assert.IsNotEmpty(info.DisplayLabel);
                Assert.IsNotEmpty(info.EditorDiscriminator);
                Assert.AreNotEqual(ActionOwnerScope.None, info.LegalScopes);
            }
        }

        [Test]
        public void FlowActions_AreAlwaysBlocking_AndNotConfigurable()
        {
            foreach (var key in new[]
                     {
                         ActionTypeKeys.PhaseGoto,
                         ActionTypeKeys.SessionGoto,
                         ActionTypeKeys.WaitForContinue,
                         ActionTypeKeys.WaitForAll,
                         ActionTypeKeys.Return,
                         ActionTypeKeys.EndSession,
                     })
            {
                var info = ActionTypeRegistry.ByTypeKey(key);
                Assert.IsTrue(info.IsAlwaysBlocking, key + " always blocking");
                Assert.IsFalse(info.BlockingConfigurable, key + " blocking not configurable");
                Assert.IsTrue(ActionTypeKeys.IsAlwaysBlocking(key), key + " shared rule agrees");
            }
        }

        [Test]
        public void OwnerScopeRules_RejectOnlyIllegalCombinations()
        {
            // A Card is a content leaf; Phase GOTO belongs to the Phase graph.
            var gotoInstance = new PhaseGotoInstanceDefinition { Id = "g1", PhaseExitId = "px-x" };
            Assert.Throws<System.InvalidOperationException>(
                () => ActionTypeRegistry.ValidateScope(gotoInstance, ActionOwnerScope.CardSequence));

            // SessionGoto only in session decision options.
            var sessionGoto = new SessionGotoInstanceDefinition { Id = "g2" };
            Assert.Throws<System.InvalidOperationException>(
                () => ActionTypeRegistry.ValidateScope(sessionGoto, ActionOwnerScope.CardSequence));

            // IncrementProgress needs a PhaseRun; illegal in session decision options.
            var progress = new IncrementProgressInstanceDefinition { Id = "p1" };
            Assert.Throws<System.InvalidOperationException>(
                () => ActionTypeRegistry.ValidateScope(progress, ActionOwnerScope.SessionDecisionOptionSequence));

            Assert.Throws<System.InvalidOperationException>(
                () => ActionTypeRegistry.ValidateScope(sessionGoto, ActionOwnerScope.SessionDecisionPromptChoiceSequence));

            Assert.DoesNotThrow(() => ActionTypeRegistry.ValidateScope(
                new StatIncreaseInstanceDefinition { Id = "nested-stat" },
                ActionOwnerScope.SessionDecisionPromptChoiceSequence));
        }

        [Test]
        public void NestedPromptChoiceUnderSessionDecision_RejectsSessionGoto()
        {
            var prompts = new FakePromptService(0);
            _services = new CoreServices(new FakeDelayService(), _log, prompts);
            var context = Context(ActionOwnerScope.SessionDecisionOptionSequence, withPhaseRun: false);
            var prompt = new PromptChoiceInstanceDefinition
            {
                Id = "prompt",
                Prompt = "Nested?",
                Options =
                {
                    new PromptChoiceOptionDefinition
                    {
                        Id = "option",
                        Label = "Go",
                        Sequence = new ActionSequenceDefinition
                        {
                            Id = "nested-sequence",
                            Instances =
                            {
                                new SessionGotoInstanceDefinition { Id = "nested-goto", Label = "illegal" },
                            },
                        },
                    },
                },
            };

            Assert.ThrowsAsync<System.InvalidOperationException>(
                async () => await Run(_executor, context, prompt));
        }

        [Test]
        public void FlowAction_MarkedNonblocking_IsRejectedAtExecution()
        {
            var end = new EndSessionInstanceDefinition { Id = "e1", IsBlocking = false };
            var context = Context(ActionOwnerScope.CardSequence);
            Assert.ThrowsAsync<System.InvalidOperationException>(
                async () => await Run(_executor, context, end));
        }

        // ---------- general execution ----------

        [Test]
        public async Task DialogDelayAndToyActivity_UseConfiguredHostSemantics()
        {
            var delay = new FakeDelayService();
            var dialog = new FakeDialogService();
            var toy = new FakeToyActivityService();
            _services = new CoreServices(delay, _log, dialog: dialog, toyActivity: toy);
            var context = Context(ActionOwnerScope.CardSequence);

            await Run(_executor, context,
                new DialogInstanceDefinition { Id = "dialog", Text = "Hello", IsBlocking = true },
                new DelayInstanceDefinition { Id = "delay", DurationSeconds = 2f, IsBlocking = true },
                new ToyActivityInstanceDefinition
                    { Id = "toy", CapabilityId = "vibrate", PatternResourceId = "res-pat-50", DurationSeconds = 3f, IsBlocking = true });

            CollectionAssert.AreEqual(new[] { "Hello" }, dialog.Shown);
            Assert.That(delay.LastDelayCount, Is.EqualTo(1));
            Assert.That(toy.TimedStarted.Single(),
                Is.EqualTo(("vibrate", "res-pat-50", TimeSpan.FromSeconds(3))));
        }

        [Test]
        public async Task StatIncrease_And_Debug_RunAgainstPlayerAndLog()
        {
            var context = Context(ActionOwnerScope.CardSequence);
            var result = await Run(_executor, context,
                new StatIncreaseInstanceDefinition { Id = "s1", StatKey = "courage", Amount = 2f },
                new DebugInstanceDefinition { Id = "d1", Message = "hello" });

            Assert.AreEqual(ActionTransfer.None, result.Transfer);
            Assert.AreEqual(2, _player.Stats.Get("courage"));
            Assert.IsTrue(_log.Entries.Exists(e => e.Contains("hello")), "debug message logged");
        }

        [Test]
        public async Task TwoProgressInstances_AreIndependent()
        {
            var context = Context(ActionOwnerScope.CardSequence);
            var result = await Run(_executor, context,
                new IncrementProgressInstanceDefinition { Id = "p10", Amount = 10f },
                new IncrementProgressInstanceDefinition { Id = "p5", Amount = 5f });

            Assert.AreEqual(ActionTransfer.None, result.Transfer);
            Assert.AreEqual(15f, context.PhaseProgress.Value, "10 then 5 accumulates to 15");
        }

        [Test]
        public async Task CardWithNoProgressAction_LeavesProgressUnchanged()
        {
            var context = Context(ActionOwnerScope.CardSequence);
            await Run(_executor, context,
                new DebugInstanceDefinition { Id = "d1", Message = "no progress here" });

            Assert.AreEqual(0f, context.PhaseProgress.Value, "no implicit progress increment");
        }

        [Test]
        public async Task ModifyTemperature_ClampsToDefinitionBounds()
        {
            var context = Context(ActionOwnerScope.CardSequence);
            var happiness = context.Temperatures.Get(SampleContent.TemperatureHappiness);
            Assert.AreEqual(50f, happiness);

            await Run(_executor, context,
                new ModifyTemperatureInstanceDefinition
                {
                    Id = "m1",
                    TemperatureId = SampleContent.TemperatureHappiness,
                    Amount = 1000f,
                });

            Assert.AreEqual(100f, context.Temperatures.Get(SampleContent.TemperatureHappiness),
                "clamped to definition max");
        }

        [Test]
        public async Task Cutscene_RunsThroughService()
        {
            var cutscene = new FakeCutsceneService();
            _services = new CoreServices(new FakeDelayService(), _log, cutscene: cutscene);
            var context = Context(ActionOwnerScope.CardSequence);

            await Run(_executor, context,
                new CutsceneInstanceDefinition { Id = "c1", ResourceId = SampleContent.ResourceCutsceneIntro });

            CollectionAssert.Contains(cutscene.Started, SampleContent.ResourceCutsceneIntro);
        }

        [Test]
        public async Task PromptChoice_ExecutesSelectedOptionsSequence_InOrder()
        {
            var prompts = new FakePromptService(1); // select option index 1
            _services = new CoreServices(new FakeDelayService(), _log, prompts);
            var context = Context(ActionOwnerScope.CardSequence);

            var result = await Run(_executor, context,
                new PromptChoiceInstanceDefinition
                {
                    Id = "ch1",
                    Prompt = "Which?",
                    Options =
                    {
                        new PromptChoiceOptionDefinition
                        {
                            Id = "o0",
                            Label = "First",
                            Sequence = new ActionSequenceDefinition
                            {
                                Id = "seq-o0",
                                Instances =
                                {
                                    new StatIncreaseInstanceDefinition { Id = "s0", StatKey = "courage", Amount = 1f },
                                },
                            },
                        },
                        new PromptChoiceOptionDefinition
                        {
                            Id = "o1",
                            Label = "Second",
                            Sequence = new ActionSequenceDefinition
                            {
                                Id = "seq-o1",
                                Instances =
                                {
                                    new StatIncreaseInstanceDefinition { Id = "s1a", StatKey = "courage", Amount = 2f },
                                    new StatIncreaseInstanceDefinition { Id = "s1b", StatKey = "spark", Amount = 3f },
                                },
                            },
                        },
                    },
                });

            Assert.AreEqual(ActionTransfer.None, result.Transfer);
            Assert.AreEqual(2, _player.Stats.Get("courage"), "option 1 ran both its actions in order");
            Assert.AreEqual(3, _player.Stats.Get("spark"));
            Assert.AreEqual(1, prompts.Requests.Count);
        }

        [Test]
        public async Task NonblockingInstance_ContinuesSequenceWithoutWaiting()
        {
            var delay = new FakeDelayService { Gate = new TaskCompletionSource<bool>() };
            _services = new CoreServices(delay, _log);
            var context = Context(ActionOwnerScope.CardSequence);

            var result = await Run(_executor, context,
                new DebugInstanceDefinition { Id = "nb", Message = "bg", DelaySeconds = 1f, IsBlocking = false },
                new StatIncreaseInstanceDefinition { Id = "s2", StatKey = "courage", Amount = 1f });

            Assert.AreEqual(ActionTransfer.None, result.Transfer);
            Assert.AreEqual(1, _player.Stats.Get("courage"), "blocking stat action completed while debug still gated");
            Assert.AreEqual(1, _tracker.ActiveCount, "nonblocking debug is tracked in background");

            delay.Gate.TrySetResult(true);
            await _tracker.DrainAsync();
            Assert.AreEqual(0, _tracker.ActiveCount);
        }

        [Test]
        public async Task WaitForAll_WaitsForBackgroundActionsAlreadyRunning()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _tracker.Start(gate.Task);
            var context = Context(ActionOwnerScope.CardSequence);

            var run = Run(_executor, context,
                new WaitForAllInstanceDefinition { Id = "wait" },
                new DebugInstanceDefinition { Id = "after", Message = "after barrier" });

            Assert.IsFalse(run.IsCompleted, "the barrier must yield while current background work is active");
            gate.SetResult(true);
            await run;

            Assert.IsTrue(_log.Entries.Exists(entry => entry.Contains("after barrier")));
        }

        [Test]
        public async Task SetToyPattern_IsAcknowledgedInline_AndIsNotTracked()
        {
            var toy = new GatedToyActivityService();
            _services = new CoreServices(new FakeDelayService(), _log, toyActivity: toy);
            var context = Context(ActionOwnerScope.CardSequence);
            var run = Run(_executor, context,
                new ToySetPatternInstanceDefinition
                {
                    Id = "set-pattern", CapabilityId = "vibrate", PatternResourceId = "res-pat-50"
                },
                new StatIncreaseInstanceDefinition { Id = "after-set", StatKey = "courage", Amount = 1f });

            await Task.Yield();
            Assert.That(run.IsCompleted, Is.False, "the next action waits for host acknowledgement");
            Assert.That(_tracker.ActiveCount, Is.EqualTo(0), "persistent toy state is not background work");
            Assert.That(_player.Stats.Get("courage"), Is.EqualTo(0));

            toy.SetAcknowledgement.TrySetResult(true);
            await run;
            Assert.That(toy.SetCount, Is.EqualTo(1));
            Assert.That(_player.Stats.Get("courage"), Is.EqualTo(1));
        }

        [Test]
        public async Task NonblockingTimedToy_IsTracked_AndWaitForAllDrainsIt()
        {
            var toy = new GatedToyActivityService();
            _services = new CoreServices(new FakeDelayService(), _log, toyActivity: toy);
            var context = Context(ActionOwnerScope.CardSequence);
            var run = Run(_executor, context,
                new ToyActivityInstanceDefinition
                {
                    Id = "timed-toy", CapabilityId = "vibrate", PatternResourceId = "res-pat-50",
                    DurationSeconds = 2f, IsBlocking = false
                },
                new WaitForAllInstanceDefinition { Id = "wait-for-toy" });

            await Task.Yield();
            Assert.That(toy.TimedCount, Is.EqualTo(1));
            Assert.That(_tracker.ActiveCount, Is.EqualTo(1));
            Assert.That(run.IsCompleted, Is.False);

            toy.TimedAcknowledgement.TrySetResult(true);
            await run;
            Assert.That(_tracker.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void WaitForAll_IsRejectedInsidePromptChoiceDescendants()
        {
            var nested = new ActionSequenceDefinition
            {
                Id = "nested-wait",
                Instances =
                {
                    new WaitForAllInstanceDefinition { Id = "nested-wait-action" },
                },
            };

            Assert.Throws<InvalidOperationException>(() =>
                ActionSequenceScopeValidator.ValidatePromptChoiceDescendant(
                    nested, ActionOwnerScope.CardSequence));
        }

        // ---------- flow reduction ----------

        [Test]
        public async Task PhaseGoto_ReducesToTransferRequest()
        {
            var context = Context(ActionOwnerScope.PhaseActionSequence);
            var result = await Run(_executor, context,
                new PhaseGotoInstanceDefinition { Id = "pg", PhaseExitId = "px-fail" });

            Assert.AreEqual(ActionTransfer.PhaseGoto, result.Transfer);
            Assert.AreEqual("px-fail", result.PhaseExitId);
        }

        [Test]
        public async Task EndSession_ReducesToTransferRequest()
        {
            var context = Context(ActionOwnerScope.CardSequence);
            var result = await Run(_executor, context,
                new StatIncreaseInstanceDefinition { Id = "s3", StatKey = "courage", Amount = 1f },
                new EndSessionInstanceDefinition { Id = "es" });

            Assert.AreEqual(ActionTransfer.EndSession, result.Transfer);
        }
    }
}
