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
    /// Ticket 02 acceptance: factored planning, destination policies, composed
    /// stand-move-sit, the explicit body-rest default, required face coverage,
    /// loud failures, and committed-state integrity across rejection and
    /// cancellation.
    /// </summary>
    [TestFixture]
    public class PerformanceDirectorTests
    {
        private const string RoomCenter = PerformanceTestCatalog.AnchorRoomCenter;
        private const string Chair = PerformanceTestCatalog.AnchorChair;
        private const string ChairB = "anchor-chair-b";

        private static ConversationPerformanceEventDefinition Event(
            string id,
            PerformanceStagingPolicy policy = PerformanceStagingPolicy.Stay,
            string namedAnchor = "",
            string[] tags = null,
            string[] postures = null,
            bool requireAll = true)
        {
            var performanceEvent = new ConversationPerformanceEventDefinition
            {
                Id = id,
                Name = id,
                RequireAllTags = requireAll,
                StagingPolicy = policy,
                NamedAnchorId = namedAnchor,
            };
            performanceEvent.PerformanceTagIds.AddRange(
                tags ?? new[] { PerformanceTestCatalog.TagPlayful, PerformanceTestCatalog.TagTease });
            if (postures != null) performanceEvent.AllowedPostureIds.AddRange(postures);
            return performanceEvent;
        }

        private static PerformanceDirector Director(
            PresentationCatalogDefinition catalog, FakePerformanceHost host, params int[] randomOffsets)
        {
            var rng = randomOffsets.Length > 0
                ? (IRandomSource)new FixedRandomSource(randomOffsets)
                : new SystemRandomSource(1234);
            return new PerformanceDirector(catalog, host, rng, new RecordingLog());
        }

        private static PresentationCatalogDefinition WithSecondChair()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            PerformanceTestCatalog.AddAnchor(catalog, ChairB, "Chair B", "chair-b",
                new[] { PresentationPostures.Standing, PresentationPostures.Sitting }, RoomCenter);
            // Keep the room center symmetric so chair B is reachable from chair A.
            catalog.Anchors.Find(a => a.Id == RoomCenter).ConnectedAnchorIds.Add(ChairB);
            return catalog;
        }

        [Test]
        public async Task Stay_SelectsCompatibleActing_AndCommitsArrival()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(RoomCenter, PresentationPostures.Standing));
            var director = Director(catalog, host);

            await director.PerformAsync(Event("evt-tease"), CancellationToken.None);

            var request = host.Requests.Single();
            Assert.AreEqual(PerformanceRequestKind.Stage, request.Kind);
            Assert.AreEqual(RoomCenter, request.AnchorId);
            Assert.AreEqual(PresentationPostures.Standing, request.PostureId);
            Assert.AreEqual(PerformanceTestCatalog.FoundationStanding, request.FoundationIngredientId);
            Assert.AreEqual(PerformanceTestCatalog.FaceSmile, request.FaceIngredientId);
            Assert.IsFalse(request.UseBodyRest, "a matching body gesture exists");
            Assert.IsTrue(request.BodyIngredientId == PerformanceTestCatalog.BodyTalking
                          || request.BodyIngredientId == PerformanceTestCatalog.BodyInteract);
            Assert.AreEqual(RoomCenter + "/" + PresentationPostures.Standing, director.CommittedState.ToString());
            Assert.AreEqual("evt-tease", director.ActiveEventId);
        }

        [Test]
        public async Task DifferentLocation_MovesToAnotherAnchor()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(RoomCenter, PresentationPostures.Standing));
            var director = Director(catalog, host);

            await director.PerformAsync(
                Event("evt-move", PerformanceStagingPolicy.DifferentLocation), CancellationToken.None);

            var request = host.Requests.Single();
            Assert.AreEqual(Chair, request.AnchorId);
            CollectionAssert.AreEqual(
                new[] { PresentationOperationKinds.MoveWhileStanding },
                request.Operations.Select(o => o.Kind).ToArray());
        }

        [Test]
        public async Task NamedLocation_SittingFromASittingAnchor_ComposesStandMoveSit()
        {
            var catalog = WithSecondChair();
            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(Chair, PresentationPostures.Sitting));
            var director = Director(catalog, host);

            await director.PerformAsync(
                Event("evt-move-sit", PerformanceStagingPolicy.NamedLocation, ChairB,
                    postures: new[] { PresentationPostures.Sitting }),
                CancellationToken.None);

            var request = host.Requests.Single();
            Assert.AreEqual(ChairB, request.AnchorId);
            Assert.AreEqual(PresentationPostures.Sitting, request.PostureId);
            CollectionAssert.AreEqual(
                new[]
                {
                    PresentationOperationKinds.Stand,
                    PresentationOperationKinds.MoveWhileStanding,
                    PresentationOperationKinds.MoveWhileStanding,
                    PresentationOperationKinds.Sit,
                },
                request.Operations.Select(o => o.Kind).ToArray());
            Assert.AreEqual(PerformanceTestCatalog.FoundationSitting, request.FoundationIngredientId);
        }

        [Test]
        public async Task ChooseCompatible_UsesShortestLegalPath()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(RoomCenter, PresentationPostures.Standing));
            var director = Director(catalog, host);

            await director.PerformAsync(
                Event("evt-compat", PerformanceStagingPolicy.ChooseCompatible), CancellationToken.None);

            // Already standing at a compatible anchor: no operation is needed.
            var request = host.Requests.Single();
            Assert.AreEqual(RoomCenter, request.AnchorId);
            Assert.AreEqual(0, request.Operations.Count);
            Assert.AreEqual(0, host.Requests.Single().Operations.Count);
        }

        [Test]
        public void MissingRequiredOperation_ExcludesThePlan_AndFailsLoudly()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            catalog.Operations.RemoveAll(o => o.Kind == PresentationOperationKinds.Sit);
            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(RoomCenter, PresentationPostures.Standing));
            var director = Director(catalog, host);

            var failure = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await director.PerformAsync(
                    Event("evt-sit", PerformanceStagingPolicy.NamedLocation, Chair,
                        postures: new[] { PresentationPostures.Sitting }),
                    CancellationToken.None));

            StringAssert.Contains("no viable plan", failure.Message);
            StringAssert.Contains("no legal operation path", failure.Message);
            Assert.AreEqual(0, host.Requests.Count, "no request is sent for an unviable plan");
            Assert.AreEqual(RoomCenter + "/" + PresentationPostures.Standing, director.CommittedState.ToString());
        }

        [Test]
        public void NoMatchingFace_FailsLoudly_AndNeverSubstitutes()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(RoomCenter, PresentationPostures.Standing));
            var director = Director(catalog, host);

            var failure = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await director.PerformAsync(
                    Event("evt-unknown", tags: new[] { "perf-tag-does-not-exist" }), CancellationToken.None));

            StringAssert.Contains("no viable plan", failure.Message);
            StringAssert.Contains("no matching face ingredient", failure.Message);
            Assert.AreEqual(0, host.Requests.Count);
        }

        [Test]
        public async Task NoMatchingBody_UsesExplicitRest_ButKeepsTheFace()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(RoomCenter, PresentationPostures.Standing));
            var director = Director(catalog, host);

            await director.PerformAsync(
                Event("evt-stern", tags: new[] { PerformanceTestCatalog.TagStern }), CancellationToken.None);

            var request = host.Requests.Single();
            Assert.IsTrue(request.UseBodyRest, "no body gesture carries the stern tag");
            Assert.AreEqual("", request.BodyIngredientId);
            Assert.AreEqual(PerformanceTestCatalog.FaceFrown, request.FaceIngredientId);
            Assert.IsFalse(string.IsNullOrEmpty(request.FoundationIngredientId), "a foundation is still required");
        }

        [Test]
        public void RejectedRequest_IsLoud_AndDoesNotCommitArrival()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(RoomCenter, PresentationPostures.Standing))
            {
                Responder = request => PerformanceExecutionResult.Reject(request.CorrelationId, "rig busy"),
            };
            var director = Director(catalog, host);

            var failure = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await director.PerformAsync(
                    Event("evt", PerformanceStagingPolicy.DifferentLocation), CancellationToken.None));

            StringAssert.Contains("rig busy", failure.Message);
            StringAssert.Contains("Committed state is unchanged", failure.Message);
            Assert.AreEqual(RoomCenter + "/" + PresentationPostures.Standing, director.CommittedState.ToString());
            Assert.IsNull(director.Acting);
        }

        [Test]
        public void Cancellation_DoesNotCommitArrival()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(RoomCenter, PresentationPostures.Standing))
            {
                Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            };
            var director = Director(catalog, host);

            using (var cts = new CancellationTokenSource())
            {
                var run = director.PerformAsync(Event("evt"), cts.Token);
                cts.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () => await run);
            }

            Assert.AreEqual(RoomCenter + "/" + PresentationPostures.Standing, director.CommittedState.ToString());
            Assert.IsNull(director.Acting);
        }

        [Test]
        public async Task RefreshAtDialogueStart_RetainsLocation_AndChangesActing()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(RoomCenter, PresentationPostures.Standing));
            var director = Director(catalog, host);

            await director.PerformAsync(
                Event("evt", PerformanceStagingPolicy.DifferentLocation, requireAll: false),
                CancellationToken.None);
            var performRequest = host.Requests.Single();
            var stagedBody = performRequest.BodyIngredientId;

            await director.RefreshAtDialogueStartAsync(CancellationToken.None);

            var refresh = host.Requests.Last();
            Assert.AreEqual(PerformanceRequestKind.RefreshActing, refresh.Kind);
            Assert.AreEqual(performRequest.AnchorId, refresh.AnchorId);
            Assert.AreEqual(performRequest.PostureId, refresh.PostureId);
            Assert.AreEqual(0, refresh.Operations.Count, "refresh never moves");
            Assert.AreNotEqual(stagedBody, refresh.BodyIngredientId,
                "immediate repeat exclusion picks the other legal body when one exists");
        }

        [Test]
        public async Task Refresh_DisabledOrInactive_IsANoOp()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(RoomCenter, PresentationPostures.Standing));
            var director = Director(catalog, host);

            // No active event yet.
            await director.RefreshAtDialogueStartAsync(CancellationToken.None);
            Assert.AreEqual(0, host.Requests.Count);

            var performanceEvent = Event("evt");
            performanceEvent.RefreshAtDialogueStart = false;
            await director.PerformAsync(performanceEvent, CancellationToken.None);
            var afterPerform = host.Requests.Count;

            await director.RefreshAtDialogueStartAsync(CancellationToken.None);
            Assert.AreEqual(afterPerform, host.Requests.Count, "disabled refresh asks for nothing");
        }

        [Test]
        public void StartingAnchorOutsideTheCatalog_IsAConstructionError()
        {
            var catalog = PerformanceTestCatalog.CreateV1();
            var host = new FakePerformanceHost(catalog, new PerformanceActorState("anchor-nowhere", "standing"));

            var failure = Assert.Throws<InvalidOperationException>(() => Director(catalog, host));
            StringAssert.Contains("not in the presentation catalog", failure.Message);
        }

        [Test]
        public async Task PerformanceRng_DoesNotConsumeTheDialogStream()
        {
            var catalog = PerformanceTestCatalog.CreateV1();

            // The dialog stream with and without a preceding Perform must pick the same snippet.
            var content = new GameContentDefinition();
            content.DialogTags.Add(new DialogTagDefinition { Id = "tag-x", Title = "X" });
            content.DialogSnippets.Add(new DialogSnippetDefinition
            {
                Id = "snip-a", Name = "A", Text = "first",
            });
            content.DialogSnippets[0].DialogTagIds.Add("tag-x");
            content.DialogSnippets.Add(new DialogSnippetDefinition
            {
                Id = "snip-b", Name = "B", Text = "second",
            });
            content.DialogSnippets[1].DialogTagIds.Add("tag-x");
            content.PerformanceEvents.Add(Event("evt-seed"));

            var withoutPerform = await RunDialogFromTags(content, catalog, withPerform: false);
            var withPerform = await RunDialogFromTags(content, catalog, withPerform: true);

            Assert.AreEqual(withoutPerform, withPerform,
                "performance selection must not perturb Dialog From Tags randomness");
        }

        private static async Task<string> RunDialogFromTags(
            GameContentDefinition content, PresentationCatalogDefinition catalog, bool withPerform)
        {
            var log = new RecordingLog();
            var dialog = new FakeDialogService();
            var host = new FakePerformanceHost(catalog,
                new PerformanceActorState(PerformanceTestCatalog.AnchorRoomCenter, PresentationPostures.Standing));
            var services = new CoreServices(new FakeDelayService(), log, dialog: dialog, performance: host);
            var contentCatalog = new ContentCatalog(content);
            var director = new PerformanceDirector(catalog, host, SeededRandomDomains.CreatePerformance(7), log);

            var context = new ActionExecutionContext(
                new Player("Tester"), services, contentCatalog,
                new TemperatureState(contentCatalog), new PhaseProgressState(),
                ActionOwnerScope.CardSequence, SeededRandomDomains.CreateDialogSelection(99), director);

            var executor = new ActionExecutor(new BackgroundActionTracker(log));
            var sequence = new ActionSequenceDefinition { Id = "seq" };
            if (withPerform)
            {
                sequence.Instances.Add(new PerformInstanceDefinition { Id = "p", EventId = "evt-seed", IsBlocking = true });
            }
            var fromTags = new DialogFromTagsInstanceDefinition { Id = "d", IsBlocking = true };
            fromTags.RequiredDialogTagIds.Add("tag-x");
            sequence.Instances.Add(fromTags);

            await executor.ExecuteSequenceAsync(sequence, context, CancellationToken.None);
            return dialog.Shown.Single();
        }
    }
}
