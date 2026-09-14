using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// Ticket 02 acceptance: on ordinary blocking dialogue, Core asks the active
    /// performance director to select and await compatible acting immediately
    /// before IDialogService.ShowAsync; tagged snippet selection stays
    /// synchronous before any host await; nonblocking dialogue is unchanged.
    /// </summary>
    [TestFixture]
    public class PerformanceDialogueRefreshTests
    {
        private static ConversationPerformanceEventDefinition EventDefinition(string id)
        {
            var performanceEvent = new ConversationPerformanceEventDefinition
            {
                Id = id,
                Name = id,
                RequireAllTags = true,
                StagingPolicy = PerformanceStagingPolicy.Stay,
            };
            performanceEvent.PerformanceTagIds.Add(PerformanceTestCatalog.TagPlayful);
            performanceEvent.PerformanceTagIds.Add(PerformanceTestCatalog.TagTease);
            return performanceEvent;
        }

        private static GameContentDefinition ContentWithEvent(ConversationPerformanceEventDefinition performanceEvent)
        {
            var content = new GameContentDefinition();
            content.PerformanceEvents.Add(performanceEvent);
            content.DialogTags.Add(new DialogTagDefinition { Id = "tag-tease", Title = "Tease" });
            var snippet = new DialogSnippetDefinition { Id = "snip", Name = "Line", Text = "Such a tease." };
            snippet.DialogTagIds.Add("tag-tease");
            content.DialogSnippets.Add(snippet);
            return content;
        }

        private sealed class CountingRandomSource : IRandomSource
        {
            private readonly IRandomSource _inner;
            public int NextIntCount;
            public CountingRandomSource(IRandomSource inner) => _inner = inner;
            public int NextInt(int minInclusive, int maxExclusive)
            {
                NextIntCount++;
                return _inner.NextInt(minInclusive, maxExclusive);
            }
            public float NextFloat(float minInclusive, float maxExclusive) =>
                _inner.NextFloat(minInclusive, maxExclusive);
        }

        [Test]
        public async Task BlockingDirectDialog_RefreshesActingBeforePresenting()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            var content = ContentWithEvent(EventDefinition("evt"));
            var order = new List<string>();

            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(PerformanceTestCatalog.AnchorRoomCenter, PresentationPostures.Standing))
            { Order = order };
            var director = new PerformanceDirector(catalog, host, new SystemRandomSource(3), new RecordingLog());
            await director.PerformAsync(EventDefinition("evt"), CancellationToken.None);
            order.Clear();

            var dialog = new OrderRecordingDialogService(order);
            var log = new RecordingLog();
            var services = new CoreServices(new FakeDelayService(), log, dialog: dialog, performance: host);
            var contentCatalog = new ContentCatalog(content);
            var context = new ActionExecutionContext(
                new Player("Tester"), services, contentCatalog, new TemperatureState(contentCatalog),
                new PhaseProgressState(), ActionOwnerScope.CardSequence,
                SeededRandomDomains.CreateDialogSelection(5), director);
            var executor = new ActionExecutor(new BackgroundActionTracker(log));

            var sequence = new ActionSequenceDefinition { Id = "seq" };
            sequence.Instances.Add(new DialogInstanceDefinition { Id = "d", Text = "Come here.", IsBlocking = true });
            await executor.ExecuteSequenceAsync(sequence, context, CancellationToken.None);

            CollectionAssert.AreEqual(
                new[] { "perform:RefreshActing", "dialog:Come here." }, order,
                "acting refresh must be awaited immediately before the blocking line is presented");
        }

        [Test]
        public async Task BlockingTaggedDialog_SelectsSnippetBeforeAnyHostAwait()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            var content = ContentWithEvent(EventDefinition("evt"));
            var order = new List<string>();
            var dialogRng = new CountingRandomSource(SeededRandomDomains.CreateDialogSelection(5));

            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(PerformanceTestCatalog.AnchorRoomCenter, PresentationPostures.Standing))
            { Order = order };
            var director = new PerformanceDirector(catalog, host, new SystemRandomSource(3), new RecordingLog());
            await director.PerformAsync(EventDefinition("evt"), CancellationToken.None);
            order.Clear();

            var dialogCountAtRefresh = -1;
            host.Responder = request =>
            {
                if (request.Kind == PerformanceRequestKind.RefreshActing)
                {
                    dialogCountAtRefresh = dialogRng.NextIntCount;
                }
                return PerformanceExecutionResult.Accept(request.CorrelationId);
            };

            var dialog = new OrderRecordingDialogService(order);
            var log = new RecordingLog();
            var services = new CoreServices(new FakeDelayService(), log, dialog: dialog, performance: host);
            var contentCatalog = new ContentCatalog(content);
            var context = new ActionExecutionContext(
                new Player("Tester"), services, contentCatalog, new TemperatureState(contentCatalog),
                new PhaseProgressState(), ActionOwnerScope.CardSequence, dialogRng, director);
            var executor = new ActionExecutor(new BackgroundActionTracker(log));

            var fromTags = new DialogFromTagsInstanceDefinition { Id = "d", IsBlocking = true };
            fromTags.RequiredDialogTagIds.Add("tag-tease");
            var sequence = new ActionSequenceDefinition { Id = "seq" };
            sequence.Instances.Add(fromTags);

            await executor.ExecuteSequenceAsync(sequence, context, CancellationToken.None);

            Assert.AreEqual(1, dialogRng.NextIntCount, "the snippet was selected exactly once");
            Assert.AreEqual(1, dialogCountAtRefresh,
                "snippet selection consumed the dialog RNG before the refresh request was made");
            CollectionAssert.AreEqual(
                new[] { "perform:RefreshActing", "dialog:Such a tease." }, order);
        }

        [Test]
        public async Task NonblockingDialog_DoesNotRefreshActing()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            var content = ContentWithEvent(EventDefinition("evt"));
            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(PerformanceTestCatalog.AnchorRoomCenter, PresentationPostures.Standing));
            var director = new PerformanceDirector(catalog, host, new SystemRandomSource(3), new RecordingLog());
            await director.PerformAsync(EventDefinition("evt"), CancellationToken.None);
            var staged = host.Requests.Count;

            var log = new RecordingLog();
            var dialog = new FakeDialogService();
            var services = new CoreServices(new FakeDelayService(), log, dialog: dialog, performance: host);
            var contentCatalog = new ContentCatalog(content);
            var context = new ActionExecutionContext(
                new Player("Tester"), services, contentCatalog, new TemperatureState(contentCatalog),
                new PhaseProgressState(), ActionOwnerScope.CardSequence,
                SeededRandomDomains.CreateDialogSelection(5), director);
            var tracker = new BackgroundActionTracker(log);
            var executor = new ActionExecutor(tracker);

            var sequence = new ActionSequenceDefinition { Id = "seq" };
            sequence.Instances.Add(new DialogInstanceDefinition { Id = "d", Text = "Aside.", IsBlocking = false });
            await executor.ExecuteSequenceAsync(sequence, context, CancellationToken.None);
            await tracker.DrainAsync();

            CollectionAssert.AreEqual(new[] { "Aside." }, dialog.Shown);
            Assert.AreEqual(staged, host.Requests.Count, "nonblocking dialogue never requests acting");
        }

        [Test]
        public async Task NoActiveEvent_LeavesDialogueUnchanged()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            var content = ContentWithEvent(EventDefinition("evt"));
            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(PerformanceTestCatalog.AnchorRoomCenter, PresentationPostures.Standing));
            var director = new PerformanceDirector(catalog, host, new SystemRandomSource(3), new RecordingLog());

            var log = new RecordingLog();
            var dialog = new FakeDialogService();
            var services = new CoreServices(new FakeDelayService(), log, dialog: dialog, performance: host);
            var contentCatalog = new ContentCatalog(content);
            var context = new ActionExecutionContext(
                new Player("Tester"), services, contentCatalog, new TemperatureState(contentCatalog),
                new PhaseProgressState(), ActionOwnerScope.CardSequence,
                SeededRandomDomains.CreateDialogSelection(5), director);
            var executor = new ActionExecutor(new BackgroundActionTracker(log));

            var sequence = new ActionSequenceDefinition { Id = "seq" };
            sequence.Instances.Add(new DialogInstanceDefinition { Id = "d", Text = "Plain line.", IsBlocking = true });
            await executor.ExecuteSequenceAsync(sequence, context, CancellationToken.None);

            CollectionAssert.AreEqual(new[] { "Plain line." }, dialog.Shown);
            Assert.AreEqual(0, host.Requests.Count, "no active event means no refresh request");
        }

        [Test]
        public void Perform_WithoutAHost_IsALoudError()
        {
            var content = ContentWithEvent(EventDefinition("evt"));
            var log = new RecordingLog();
            var services = new CoreServices(new FakeDelayService(), log);
            var contentCatalog = new ContentCatalog(content);
            var context = new ActionExecutionContext(
                new Player("Tester"), services, contentCatalog, new TemperatureState(contentCatalog),
                new PhaseProgressState(), ActionOwnerScope.CardSequence,
                SeededRandomDomains.CreateDialogSelection(5));
            var executor = new ActionExecutor(new BackgroundActionTracker(log));

            var sequence = new ActionSequenceDefinition { Id = "seq" };
            sequence.Instances.Add(new PerformInstanceDefinition { Id = "p", EventId = "evt", IsBlocking = true });

            var failure = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await executor.ExecuteSequenceAsync(sequence, context, CancellationToken.None));
            StringAssert.Contains("requires a performance host", failure.Message);
        }

        [Test]
        public async Task WaitForAll_CompletesWhilePresentationRemainsActive()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            var content = ContentWithEvent(EventDefinition("evt"));
            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(PerformanceTestCatalog.AnchorRoomCenter, PresentationPostures.Standing));
            var director = new PerformanceDirector(catalog, host, new SystemRandomSource(3), new RecordingLog());
            await director.PerformAsync(EventDefinition("evt"), CancellationToken.None);
            var staged = host.Requests.Count;

            var log = new RecordingLog();
            var services = new CoreServices(new FakeDelayService(), log, performance: host);
            var contentCatalog = new ContentCatalog(content);
            var context = new ActionExecutionContext(
                new Player("Tester"), services, contentCatalog, new TemperatureState(contentCatalog),
                new PhaseProgressState(), ActionOwnerScope.CardSequence, null, director);
            var tracker = new BackgroundActionTracker(log);
            var executor = new ActionExecutor(tracker);

            var sequence = new ActionSequenceDefinition { Id = "seq" };
            sequence.Instances.Add(new WaitForAllInstanceDefinition { Id = "wait" });
            await executor.ExecuteSequenceAsync(sequence, context, CancellationToken.None);

            Assert.AreEqual(0, tracker.ActiveCount,
                "persistent presentation is not background work and never affects Wait For All");
            Assert.AreEqual("evt", director.ActiveEventId, "presentation remains active across the barrier");
            Assert.AreEqual(staged, host.Requests.Count);
        }

        [Test]
        public void Registry_ClassifiesPerformAsABlockingActivity()
        {
            var info = ActionTypeRegistry.ByTypeKey(ActionTypeKeys.Perform);
            Assert.AreEqual(ActionExecutionKind.Activity, info.ExecutionKind);
            Assert.IsTrue(info.IsAlwaysBlocking);
            Assert.IsFalse(info.BlockingConfigurable);
            Assert.IsFalse(ActionTypeKeys.IsAlwaysNonBlocking(ActionTypeKeys.Perform));

            Assert.Throws<InvalidOperationException>(() => ActionTypeRegistry.ValidateScope(
                new PerformInstanceDefinition { Id = "p", EventId = "e", IsBlocking = false },
                ActionOwnerScope.CardSequence));
        }

        [Test]
        public void ContentReferenceValidator_RequiresAKnownEvent()
        {
            var content = new GameContentDefinition();
            var card = new CardDefinition { Id = "card-perform" };
            card.Sequence.Instances.Add(new PerformInstanceDefinition { Id = "p", EventId = "", IsBlocking = true });
            content.Cards.Add(card);
            var missing = Assert.Throws<InvalidOperationException>(() => ContentReferenceValidator.Validate(content));
            StringAssert.Contains("requires a Performance Event", missing.Message);

            card.Sequence.Instances.Clear();
            card.Sequence.Instances.Add(new PerformInstanceDefinition { Id = "p", EventId = "evt-nope", IsBlocking = true });
            var unknown = Assert.Throws<InvalidOperationException>(() => ContentReferenceValidator.Validate(content));
            StringAssert.Contains("unknown Performance Event", unknown.Message);

            content.PerformanceTags.Add(new PerformanceTagDefinition { Id = PerformanceTestCatalog.TagPlayful, Title = "Playful" });
            content.PerformanceTags.Add(new PerformanceTagDefinition { Id = PerformanceTestCatalog.TagTease, Title = "Tease" });
            content.PerformanceEvents.Add(EventDefinition("evt-nope"));
            Assert.DoesNotThrow(() => ContentReferenceValidator.Validate(content));
        }
    }
}
