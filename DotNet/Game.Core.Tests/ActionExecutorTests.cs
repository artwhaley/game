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
        private static string Id(GameActionDefinition action)
        {
            if (string.IsNullOrEmpty(action.Id))
            {
                action.Id = "action-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            return action.Id;
        }

        private static CardDefinition MakeCard(params GameActionDefinition[] actions)
        {
            var card = new CardDefinition { Title = "C" };
            foreach (var action in actions)
            {
                card.ActionIds.Add(action == null ? null : Id(action));
            }
            return card;
        }

        private static ActionExecutor MakeExecutor(BackgroundActionTracker tracker, params GameActionDefinition[] actions)
        {
            foreach (var action in actions) if (action != null) Id(action);
            var content = new GameContentDefinition
            {
                Deck = new CardDeckDefinition { Id = "deck" },
                Actions = new List<GameActionDefinition>(actions)
            };
            return new ActionExecutor(new ContentCatalog(content), tracker);
        }

        private static ActionExecutor MakeExecutorWithResource(BackgroundActionTracker tracker, string resourceId, params GameActionDefinition[] actions)
        {
            foreach (var action in actions) if (action != null) Id(action);
            var content = new GameContentDefinition
            {
                Deck = new CardDeckDefinition { Id = "deck" },
                Actions = new List<GameActionDefinition>(actions)
            };
            content.Resources.Add(new ResourceDefinition { Id = resourceId, Kind = "cutscene" });
            return new ActionExecutor(new ContentCatalog(content), tracker);
        }

        private static DebugActionDefinition DebugAction(string message, float delay = 0f, bool blocking = true)
            => new DebugActionDefinition { Message = message, DelaySeconds = delay, IsBlocking = blocking };

        // ---------- sequencing ----------

        [Test]
        public async Task BlockingActions_RunInOrder()
        {
            var log = new RecordingLog();
            var context = TestServices.Create(log, new FakeDelayService { Order = new List<string>() });
            var a = DebugAction("a");
            var b = DebugAction("b");
            var executor = MakeExecutor(new BackgroundActionTracker(log), a, b);

            await executor.ExecuteCardAsync(MakeCard(a, b), context, CancellationToken.None);

            CollectionAssert.AreEqual(
                new[] { "info:[TruthCardGame] Test: a", "info:[TruthCardGame] Test: b" },
                log.Entries);
        }

        [Test]
        public async Task NullActions_AreSkipped()
        {
            var log = new RecordingLog();
            var context = TestServices.Create(log, new FakeDelayService());
            var ran = DebugAction("ran");
            var executor = MakeExecutor(new BackgroundActionTracker(log), ran);

            await executor.ExecuteCardAsync(MakeCard(null, ran, null), context, CancellationToken.None);

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

            var background = DebugAction("bg", 5f, blocking: false);
            var front = DebugAction("front");
            var executor = MakeExecutor(tracker, background, front);

            await executor.ExecuteCardAsync(MakeCard(background, front), context, CancellationToken.None);

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
            var tracker = new BackgroundActionTracker(log);

            tracker.Start(Task.FromException(new InvalidOperationException("boom")));
            await tracker.DrainAsync();

            Assert.That(log.Entries.Single(e => e.StartsWith("error:")), Does.Contain("boom"));
        }

        [Test]
        public async Task Drain_Quiesces_WhenBackgroundFaultsAfterSnapshot()
        {
            var log = new RecordingLog();
            var tracker = new BackgroundActionTracker(log);

            // Fault lands well after DrainAsync snapshots the active task:
            // draining is pure quiescing — the observer already logged it.
            tracker.Start(Task.Run(async () =>
            {
                await Task.Delay(50);
                throw new InvalidOperationException("late boom");
            }));

            await tracker.DrainAsync();

            Assert.AreEqual(0, tracker.ActiveCount);
            Assert.That(log.Entries.Single(e => e.StartsWith("error:")), Does.Contain("late boom"));
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
            var tracker = new BackgroundActionTracker(services.Log);
            var hello = DebugAction("hello", 3f);
            var executor = MakeExecutor(tracker, hello);

            await executor.ExecuteCardAsync(MakeCard(hello), context, CancellationToken.None);

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
            var instant = DebugAction("instant", 0f);
            var executor = MakeExecutor(new BackgroundActionTracker(log), instant);

            await executor.ExecuteCardAsync(MakeCard(instant), context, CancellationToken.None);

            Assert.AreEqual(0, delay.LastDelayCount);
            Assert.IsTrue(log.Entries.Any(e => e.Contains(": instant")));
        }

        [Test]
        public async Task StatIncrease_MutatesPlayer_AndLogsResult()
        {
            var log = new RecordingLog();
            var context = TestServices.Create(log, new FakeDelayService());
            var action = new StatIncreaseActionDefinition { StatKey = "courage", Amount = 3 };
            var executor = MakeExecutor(new BackgroundActionTracker(log), action);

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
            var childA = DebugAction("a");
            var childB = DebugAction("b");
            var choice = new ChoiceActionDefinition
            {
                Prompt = "Pick",
                Options =
                {
                    new ChoiceOptionDefinition { Label = "A", ChildActionId = Id(childA) },
                    new ChoiceOptionDefinition { Label = "B", ChildActionId = Id(childB) }
                }
            };
            var executor = MakeExecutor(new BackgroundActionTracker(log), choice, childA, childB);

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
            var child = DebugAction("child", 4f, blocking: false);
            var choice = new ChoiceActionDefinition
            {
                Prompt = "Go",
                Options = { new ChoiceOptionDefinition { Label = "Only", ChildActionId = Id(child) } }
            };
            var executor = MakeExecutor(tracker, choice, child);

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
            var child = DebugAction("never");
            var choice = new ChoiceActionDefinition
            {
                Prompt = "Pick",
                Options = { new ChoiceOptionDefinition { Label = "Only", ChildActionId = Id(child) } }
            };
            var executor = MakeExecutor(new BackgroundActionTracker(log), choice, child);

            await executor.ExecuteCardAsync(MakeCard(choice), context, CancellationToken.None);

            Assert.IsFalse(log.Entries.Any(e => e.Contains(": never")));
        }

        [Test]
        public async Task Choice_NullChild_IsNoOp()
        {
            var log = new RecordingLog();
            var prompts = new FakePromptService(0);
            var context = TestServices.Create(log, new FakeDelayService(), prompts);
            var choice = new ChoiceActionDefinition
            {
                Prompt = "Pick",
                Options = { new ChoiceOptionDefinition { Label = "Empty" } }
            };
            var executor = MakeExecutor(new BackgroundActionTracker(log), choice);

            await executor.ExecuteCardAsync(MakeCard(choice), context, CancellationToken.None);

            Assert.IsEmpty(log.Entries.Where(e => e.StartsWith("error:")));
        }

        [Test]
        public async Task Choice_WithoutPromptService_LogsError_NoOp()
        {
            var log = new RecordingLog();
            var context = TestServices.Create(log, new FakeDelayService()); // no prompt service
            var child = DebugAction("never");
            var choice = new ChoiceActionDefinition
            {
                Prompt = "Pick",
                Options = { new ChoiceOptionDefinition { Label = "Only", ChildActionId = Id(child) } }
            };
            var executor = MakeExecutor(new BackgroundActionTracker(log), choice, child);

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
            var choice = new ChoiceActionDefinition { Prompt = "Pick" };
            var executor = MakeExecutor(new BackgroundActionTracker(log), choice);

            await executor.ExecuteCardAsync(MakeCard(choice), context, CancellationToken.None);

            Assert.AreEqual(1, log.Entries.Count(e => e.StartsWith("error:") && e.Contains("no options")));
            Assert.IsEmpty(prompts.Requests);
        }

        [Test]
        public async Task ActionCycle_ChoiceChildReferencingItself_StopsWithLoggedError()
        {
            var log = new RecordingLog();
            var prompts = new FakePromptService(0);
            var context = TestServices.Create(log, new FakeDelayService(), prompts);
            var choice = new ChoiceActionDefinition { Prompt = "Pick" };
            choice.Options.Add(new ChoiceOptionDefinition { Label = "Loop", ChildActionId = Id(choice) });
            var executor = MakeExecutor(new BackgroundActionTracker(log), choice);

            await executor.ExecuteCardAsync(MakeCard(choice), context, CancellationToken.None);

            Assert.That(log.Entries.Single(e => e.StartsWith("error:")), Does.Contain("cycle"));
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
            var action = new CutsceneActionDefinition { ResourceId = "cs:intro" };
            var executor = MakeExecutorWithResource(new BackgroundActionTracker(log), "cs:intro", action);
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
            var action = new CutsceneActionDefinition { ResourceId = resourceId };
            var executor = MakeExecutor(new BackgroundActionTracker(log), action);

            await executor.ExecuteCardAsync(MakeCard(action), context, CancellationToken.None);

            Assert.AreEqual(1, log.Entries.Count(e => e.StartsWith("error:") && e.Contains("no resource assigned")));
            Assert.IsEmpty(cutscenes.Started);
        }

        [Test]
        public async Task Cutscene_MissingService_LogsError_NoOp()
        {
            var log = new RecordingLog();
            var context = TestServices.Create(log, new FakeDelayService()); // no cutscene service
            var action = new CutsceneActionDefinition { ResourceId = "cs:x" };
            var executor = MakeExecutorWithResource(new BackgroundActionTracker(log), "cs:x", action);

            await executor.ExecuteCardAsync(MakeCard(action), context, CancellationToken.None);

            Assert.AreEqual(1, log.Entries.Count(e => e.StartsWith("error:") && e.Contains("no cutscene service")));
        }

        [Test]
        public void Cutscene_UnknownResourceId_FailsLoudly()
        {
            var log = new RecordingLog();
            var context = TestServices.Create(log, new FakeDelayService(), cutscenes: new FakeCutsceneService());
            var action = new CutsceneActionDefinition { ResourceId = "cs:missing" };
            var executor = MakeExecutor(new BackgroundActionTracker(log), action);

            Assert.ThrowsAsync<InvalidOperationException>(
                () => executor.ExecuteCardAsync(MakeCard(action), context, CancellationToken.None));
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
            var action = new CutsceneActionDefinition { ResourceId = "cs:x" };
            var executor = MakeExecutorWithResource(new BackgroundActionTracker(log), "cs:x", action);

            var task = executor.ExecuteCardAsync(MakeCard(action), context, cts.Token);
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
            var bg = DebugAction("bg", 9f, blocking: false);
            var executor = MakeExecutor(tracker, bg);

            // Baseline parity: dispatching a continuous action means the card
            // itself is not held by it, so cancelling the token faults only
            // the background work, which the tracker observes quietly.
            var task = executor.ExecuteCardAsync(MakeCard(bg), context, cts.Token);
            await Task.Yield();
            Assert.DoesNotThrowAsync(async () => await task);

            cts.Cancel();
            await tracker.DrainAsync();
            Assert.AreEqual(0, tracker.ActiveCount);
            Assert.IsEmpty(log.Entries.Where(e => e.StartsWith("error:")));
        }
    }
}
