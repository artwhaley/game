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
    public class ActionExecutorTests
    {
        private static CardDefinition MakeCard(params GameActionDefinition[] actions)
        {
            var card = new CardDefinition { Title = "C" };
            card.Actions.AddRange(actions);
            return card;
        }

        private static DebugActionDefinition DebugAction(string message, float delay = 0f, bool blocking = true)
            => new DebugActionDefinition { Message = message, DelaySeconds = delay, IsBlocking = blocking };

        // ---------- sequencing ----------

        [Test]
        public async Task BlockingActions_RunInOrder()
        {
            var log = new RecordingLog();
            var context = TestServices.Create(log, new FakeDelayService { Order = new List<string>() });
            var executor = new ActionExecutor(new BackgroundActionTracker(log));

            await executor.ExecuteCardAsync(MakeCard(DebugAction("a"), DebugAction("b")), context, CancellationToken.None);

            CollectionAssert.AreEqual(
                new[] { "info:[TruthCardGame] Test: a", "info:[TruthCardGame] Test: b" },
                log.Entries);
        }

        [Test]
        public async Task NullActions_AreSkipped()
        {
            var log = new RecordingLog();
            var context = TestServices.Create(log, new FakeDelayService());
            var executor = new ActionExecutor(new BackgroundActionTracker(log));

            await executor.ExecuteCardAsync(MakeCard(null, DebugAction("ran"), null), context, CancellationToken.None);

            Assert.AreEqual(1, log.Entries.Count(e => e.Contains(": ran")));
        }

        [Test]
        public async Task NonblockingAction_DoesNotHoldLaterActions()
        {
            var order = new List<string>();
            var gate = new TaskCompletionSource<bool>();
            var delay = new FakeDelayService { Gate = gate, Order = order };
            var log = new RecordingLog();
            var context = TestServices.Create(log, delay);
            var tracker = new BackgroundActionTracker(log);
            var executor = new ActionExecutor(tracker);

            var background = DebugAction("bg", 5f, blocking: false);

            await executor.ExecuteCardAsync(MakeCard(background, DebugAction("front")), context, CancellationToken.None);

            // Background started but not finished; the later blocking action completed.
            Assert.That(order, Does.Contain("delay:start"));
            Assert.IsTrue(log.Entries.Any(e => e.Contains(": front")));
            Assert.IsFalse(order.Contains("delay:end"));
            Assert.AreEqual(1, tracker.ActiveCount);

            gate.TrySetResult(true);
            await tracker.DrainAsync();
            Assert.That(order, Does.Contain("delay:end"));
            Assert.AreEqual(0, tracker.ActiveCount);
        }

        [Test]
        public async Task BackgroundFault_IsObservedAndLogged_NotLost()
        {
            var log = new RecordingLog();
            var context = TestServices.Create(log, new FakeDelayService());
            var tracker = new BackgroundActionTracker(log);

            tracker.Start(Task.FromException(new InvalidOperationException("boom")));
            await tracker.DrainAsync();

            Assert.That(log.Entries.Single(e => e.StartsWith("error:")), Does.Contain("boom"));
        }

        // ---------- debug / stat ----------

        [Test]
        public async Task DebugAction_LogsBeforeDelay_SharedOrderList()
        {
            var shared = new List<string>();
            var log = new RecordingLog();
            var delay = new FakeDelayService { Order = shared };
            var player = new Player("Piper");
            var services = new CoreServices(delay, new ProxyLog(log, shared));
            var context = new GameContext(player, services);
            var executor = new ActionExecutor(new BackgroundActionTracker(services.Log));

            await executor.ExecuteCardAsync(MakeCard(DebugAction("hello", 3f)), context, CancellationToken.None);

            Assert.That(shared.IndexOf("log:info:[TruthCardGame] Piper: hello"),
                Is.LessThan(shared.IndexOf("delay:start")));
        }

        private sealed class ProxyLog : IGameLog
        {
            private readonly IGameLog _inner;
            private readonly List<string> _shared;
            public ProxyLog(IGameLog inner, List<string> shared) { _inner = inner; _shared = shared; }
            public void Info(string m) { _shared.Add("log:info:" + m); _inner.Info(m); }
            public void Warning(string m) { _shared.Add("log:warn:" + m); _inner.Warning(m); }
            public void Error(string m) { _shared.Add("log:error:" + m); _inner.Error(m); }
        }

        [Test]
        public async Task DebugAction_ZeroDelay_CompletesWithoutDelayCall()
        {
            var log = new RecordingLog();
            var delay = new FakeDelayService();
            var context = TestServices.Create(log, delay);
            var executor = new ActionExecutor(new BackgroundActionTracker(log));

            await executor.ExecuteCardAsync(MakeCard(DebugAction("instant", 0f)), context, CancellationToken.None);

            Assert.AreEqual(0, delay.LastDelayCount);
            Assert.IsTrue(log.Entries.Any(e => e.Contains(": instant")));
        }

        [Test]
        public async Task StatIncrease_MutatesPlayer_AndLogsResult()
        {
            var log = new RecordingLog();
            var context = TestServices.Create(log, new FakeDelayService());
            var executor = new ActionExecutor(new BackgroundActionTracker(log));
            var action = new StatIncreaseActionDefinition { StatKey = "courage", Amount = 3 };

            await executor.ExecuteCardAsync(MakeCard(action), context, CancellationToken.None);

            Assert.AreEqual(3, context.Player.Stats.Get("courage"));
            Assert.That(log.Entries.Single(), Does.Contain("courage +3 (now 3)"));
        }

        // ---------- choice ----------

        [Test]
        public async Task Choice_RunsChosenBlockingChild_ToCompletion()
        {
            var log = new RecordingLog();
            var prompts = new FakePromptService(1);
            var context = TestServices.Create(log, new FakeDelayService(), prompts);
            var executor = new ActionExecutor(new BackgroundActionTracker(log));
            var choice = new ChoiceActionDefinition
            {
                Prompt = "Pick",
                Options =
                {
                    new ChoiceOptionDefinition { Label = "A", Child = DebugAction("a") },
                    new ChoiceOptionDefinition { Label = "B", Child = DebugAction("b") }
                }
            };

            await executor.ExecuteCardAsync(MakeCard(choice), context, CancellationToken.None);

            Assert.AreEqual(("Pick", new[] { "A", "B" }), (prompts.Requests[0].Prompt, prompts.Requests[0].Options.ToArray()));
            Assert.That(log.Entries, Does.Contain("info:[TruthCardGame] Test: b"));
            Assert.That(log.Entries, Does.Not.Contain("info:[TruthCardGame] Test: a"));
        }

        [Test]
        public async Task Choice_NonblockingChild_StartsThroughSameTracker()
        {
            var gate = new TaskCompletionSource<bool>();
            var order = new List<string>();
            var delay = new FakeDelayService { Gate = gate, Order = order };
            var log = new RecordingLog();
            var prompts = new FakePromptService(0);
            var context = TestServices.Create(log, delay, prompts);
            var tracker = new BackgroundActionTracker(log);
            var executor = new ActionExecutor(tracker);
            var choice = new ChoiceActionDefinition
            {
                Prompt = "Go",
                Options = { new ChoiceOptionDefinition { Label = "Only", Child = DebugAction("child", 4f, blocking: false) } }
            };

            await executor.ExecuteCardAsync(MakeCard(choice), context, CancellationToken.None);

            Assert.AreEqual(1, tracker.ActiveCount);
            Assert.That(order, Does.Contain("delay:start"));

            gate.TrySetResult(true);
            await tracker.DrainAsync();
            Assert.AreEqual(0, tracker.ActiveCount);
        }

        [TestCase(null)]
        [TestCase(-1)]
        [TestCase(5)]
        public async Task Choice_DismissedOrOutOfRange_IsNoOp(int? answer)
        {
            var log = new RecordingLog();
            var prompts = new FakePromptService(answer);
            var context = TestServices.Create(log, new FakeDelayService(), prompts);
            var executor = new ActionExecutor(new BackgroundActionTracker(log));
            var choice = new ChoiceActionDefinition
            {
                Prompt = "Pick",
                Options = { new ChoiceOptionDefinition { Label = "Only", Child = DebugAction("never") } }
            };

            await executor.ExecuteCardAsync(MakeCard(choice), context, CancellationToken.None);

            Assert.IsFalse(log.Entries.Any(e => e.Contains(": never")));
        }

        [Test]
        public async Task Choice_NullChild_IsNoOp()
        {
            var log = new RecordingLog();
            var prompts = new FakePromptService(0);
            var context = TestServices.Create(log, new FakeDelayService(), prompts);
            var executor = new ActionExecutor(new BackgroundActionTracker(log));
            var choice = new ChoiceActionDefinition
            {
                Prompt = "Pick",
                Options = { new ChoiceOptionDefinition { Label = "Empty" } }
            };

            await executor.ExecuteCardAsync(MakeCard(choice), context, CancellationToken.None);

            Assert.IsEmpty(log.Entries.Where(e => e.StartsWith("error:")));
        }

        [Test]
        public async Task Choice_WithoutPromptService_LogsError_NoOp()
        {
            var log = new RecordingLog();
            var context = TestServices.Create(log, new FakeDelayService()); // no prompt service
            var executor = new ActionExecutor(new BackgroundActionTracker(log));
            var choice = new ChoiceActionDefinition
            {
                Prompt = "Pick",
                Options = { new ChoiceOptionDefinition { Label = "Only", Child = DebugAction("never") } }
            };

            await executor.ExecuteCardAsync(MakeCard(choice), context, CancellationToken.None);

            Assert.AreEqual(1, log.Entries.Count(e => e.StartsWith("error:") && e.Contains("prompt service")));
            Assert.IsFalse(log.Entries.Any(e => e.Contains(": never")));
        }

        [Test]
        public async Task Choice_ZeroOptions_LogsError_NoOp()
        {
            var log = new RecordingLog();
            var prompts = new FakePromptService(0);
            var context = TestServices.Create(log, new FakeDelayService(), prompts);
            var executor = new ActionExecutor(new BackgroundActionTracker(log));

            await executor.ExecuteCardAsync(MakeCard(new ChoiceActionDefinition { Prompt = "Pick" }), context, CancellationToken.None);

            Assert.AreEqual(1, log.Entries.Count(e => e.StartsWith("error:") && e.Contains("no options")));
            Assert.IsEmpty(prompts.Requests);
        }

        // ---------- cutscene ----------

        [Test]
        public async Task Cutscene_WaitsUntilServiceCompletes()
        {
            var gate1 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cutscenes = new FakeCutsceneService(gate1, gate2);
            var log = new RecordingLog();
            var context = TestServices.Create(log, new FakeDelayService(), cutscenes: cutscenes);
            var executor = new ActionExecutor(new BackgroundActionTracker(log));
            var action = new CutsceneActionDefinition { ResourceId = "cs:intro" };
            var task = executor.ExecuteCardAsync(MakeCard(action), context, CancellationToken.None);

            await Task.Yield();
            Assert.IsFalse(task.IsCompleted);
            CollectionAssert.AreEqual(new[] { "cs:intro" }, cutscenes.Started);

            gate1.TrySetResult(true);
            await task;
            Assert.IsTrue(task.IsCompletedSuccessfully);
        }

        [Test]
        public async Task Cutscene_MissingResourceId_LogsError_NoOp([Values(null, "")] string resourceId)
        {
            var log = new RecordingLog();
            var cutscenes = new FakeCutsceneService();
            var context = TestServices.Create(log, new FakeDelayService(), cutscenes: cutscenes);
            var executor = new ActionExecutor(new BackgroundActionTracker(log));

            await executor.ExecuteCardAsync(MakeCard(new CutsceneActionDefinition { ResourceId = resourceId }), context, CancellationToken.None);

            Assert.AreEqual(1, log.Entries.Count(e => e.StartsWith("error:") && e.Contains("no resource assigned")));
            Assert.IsEmpty(cutscenes.Started);
        }

        [Test]
        public async Task Cutscene_MissingService_LogsError_NoOp()
        {
            var log = new RecordingLog();
            var context = TestServices.Create(log, new FakeDelayService()); // no cutscene service
            var executor = new ActionExecutor(new BackgroundActionTracker(log));

            await executor.ExecuteCardAsync(MakeCard(new CutsceneActionDefinition { ResourceId = "cs:x" }), context, CancellationToken.None);

            Assert.AreEqual(1, log.Entries.Count(e => e.StartsWith("error:") && e.Contains("no cutscene service")));
        }

        // ---------- cancellation ----------

        [Test]
        public async Task Cancellation_AtPendingCutscene_ThrowsOperationCanceled()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cts = new CancellationTokenSource();
            var cutscenes = new FakeCutsceneService(gate);
            var log = new RecordingLog();
            var context = TestServices.Create(log, new FakeDelayService(), cutscenes: cutscenes);
            var executor = new ActionExecutor(new BackgroundActionTracker(log));

            var task = executor.ExecuteCardAsync(MakeCard(new CutsceneActionDefinition { ResourceId = "cs:x" }), context, cts.Token);
            await Task.Yield();
            cts.Cancel();

            Assert.That(async () => await task, Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task Cancellation_OfNonblockingAction_IsObservedByTracker_WhileCardCompletes()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cts = new CancellationTokenSource();
            var delay = new FakeDelayService { Gate = gate };
            var log = new RecordingLog();
            var context = TestServices.Create(log, delay);
            var tracker = new BackgroundActionTracker(log);
            var executor = new ActionExecutor(tracker);

            // Baseline parity: dispatching a continuous action means the card
            // itself is not held by it, so cancelling the token faults only
            // the background work, which the tracker observes quietly.
            var task = executor.ExecuteCardAsync(MakeCard(DebugAction("bg", 9f, blocking: false)), context, cts.Token);
            await Task.Yield();
            Assert.DoesNotThrowAsync(async () => await task);

            cts.Cancel();
            await tracker.DrainAsync();
            Assert.AreEqual(0, tracker.ActiveCount);
            Assert.IsEmpty(log.Entries.Where(e => e.StartsWith("error:")));
        }
    }
}
