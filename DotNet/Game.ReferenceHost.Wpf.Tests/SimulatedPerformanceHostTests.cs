using System.Linq;
using System.Threading;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Core;
using TruthCardGame.ReferenceHost.Wpf;

namespace TruthCardGame.ReferenceHost.Wpf.Tests
{
    /// <summary>
    /// Ticket 03: the Workbench simulates presentation so a performance's
    /// semantic choices are explainable without Unity. The simulation must honour
    /// the same accept/reject contract as the real host — an incoherent request
    /// is refused, and the committed state never advances on a rejection.
    /// </summary>
    [TestFixture]
    public class SimulatedPerformanceHostTests
    {
        private static PresentationCatalogDefinition Catalog()
        {
            var catalog = new PresentationCatalogDefinition();

            var standing = new PresentationAnchorDefinition { Id = "anchor-hall", LocationGroup = "hall" };
            standing.SupportedPostureIds.Add(PresentationPostures.Standing);
            standing.ConnectedAnchorIds.Add("anchor-chair");
            catalog.Anchors.Add(standing);

            var chair = new PresentationAnchorDefinition { Id = "anchor-chair", LocationGroup = "study" };
            chair.SupportedPostureIds.Add(PresentationPostures.Sitting);
            chair.ConnectedAnchorIds.Add("anchor-hall");
            catalog.Anchors.Add(chair);

            catalog.Ingredients.Add(Ingredient("fnd-standing", PresentationIngredientKinds.Foundation,
                PresentationPostures.Standing, "tag-playful", enabled: true));
            catalog.Ingredients.Add(Ingredient("fnd-sitting", PresentationIngredientKinds.Foundation,
                PresentationPostures.Sitting, "tag-playful", enabled: true));
            catalog.Ingredients.Add(Ingredient("body-clap", PresentationIngredientKinds.Body,
                PresentationPostures.Standing, "tag-playful", enabled: true));
            catalog.Ingredients.Add(Ingredient("face-smile", PresentationIngredientKinds.Face,
                PresentationPostures.Standing, "tag-playful", enabled: true));
            catalog.Ingredients.Add(Ingredient("body-disabled", PresentationIngredientKinds.Body,
                PresentationPostures.Standing, "tag-playful", enabled: false));
            return catalog;
        }

        private static PresentationIngredientDefinition Ingredient(
            string id, string kind, string posture, string tag, bool enabled)
        {
            var ingredient = new PresentationIngredientDefinition
            {
                Id = id, DisplayName = id, Kind = kind, Enabled = enabled,
            };
            ingredient.SupportedPostureIds.Add(posture);
            ingredient.PerformanceTagIds.Add(tag);
            return ingredient;
        }

        private static PerformanceExecutionRequest Stage(
            string anchor, string posture, string foundation, string body, string face, bool ownsHead = false)
        {
            var request = new PerformanceExecutionRequest
            {
                CorrelationId = "perf-1",
                Kind = PerformanceRequestKind.Stage,
                AnchorId = anchor,
                PostureId = posture,
                FoundationIngredientId = foundation,
                BodyIngredientId = body,
                UseBodyRest = string.IsNullOrEmpty(body),
                FaceIngredientId = face,
                OwnsHead = ownsHead,
            };
            request.Operations.Add(new PlannedPerformanceOperation
            {
                OperationId = "op-1", Kind = PresentationOperationKinds.MoveWhileStanding,
                AnchorId = anchor, ResultPostureId = posture,
            });
            return request;
        }

        private static SimulatedPerformanceHostService Host()
        {
            return new SimulatedPerformanceHostService(
                Catalog(), new PerformanceActorState("anchor-hall", PresentationPostures.Standing));
        }

        [Test]
        public void Stage_IsAcceptedAndRecordsDestinationOperationsAndActing()
        {
            string logged = null;
            var host = new SimulatedPerformanceHostService(
                Catalog(), new PerformanceActorState("anchor-hall", PresentationPostures.Standing),
                line => logged = line);

            var result = host.ExecuteAsync(
                Stage("anchor-chair", PresentationPostures.Sitting, "fnd-sitting", null, "face-smile"),
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsTrue(result.Accepted, result.FailureReason);
            Assert.AreEqual("anchor-chair/sitting", host.State.ToString());
            Assert.AreEqual(1, host.Decisions.Count);
            var decision = host.Decisions[0];
            StringAssert.Contains("anchor-chair/sitting", decision);
            StringAssert.Contains("move_while_standing:anchor-chair->sitting", decision);
            StringAssert.Contains("foundation=fnd-sitting", decision);
            StringAssert.Contains("body=<rest>", decision);
            StringAssert.Contains("face=face-smile", decision);
            StringAssert.Contains("gaze=on player", decision);
            Assert.AreEqual(decision, logged, "each decision is surfaced to the player log");
        }

        [Test]
        public void Stage_WithAnUnknownAnchor_IsRejectedAndLeavesStateUnchanged()
        {
            var host = Host();

            var result = host.ExecuteAsync(
                Stage("anchor-nope", PresentationPostures.Standing, "fnd-standing", null, "face-smile"),
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsFalse(result.Accepted);
            StringAssert.Contains("not in the catalog", result.FailureReason);
            Assert.AreEqual("anchor-hall/standing", host.State.ToString());
            Assert.IsEmpty(host.Decisions, "a rejection is a failure, not a decision to record");
        }

        [Test]
        public void Stage_WithAnIngredientThatCannotHoldThePosture_IsRejected()
        {
            var host = Host();

            var result = host.ExecuteAsync(
                Stage("anchor-chair", PresentationPostures.Sitting, "fnd-sitting", "body-clap", "face-smile"),
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsFalse(result.Accepted);
            StringAssert.Contains("body ingredient 'body-clap' does not support posture 'sitting'",
                result.FailureReason);
        }

        [Test]
        public void Stage_WithADisabledIngredient_IsRejected()
        {
            var host = Host();

            var result = host.ExecuteAsync(
                Stage("anchor-hall", PresentationPostures.Standing, "fnd-standing", "body-disabled", "face-smile"),
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsFalse(result.Accepted);
            StringAssert.Contains("disabled", result.FailureReason);
        }

        [Test]
        public void Stage_WithAGazeMismatch_IsRejected()
        {
            var host = Host();

            var result = host.ExecuteAsync(
                Stage("anchor-hall", PresentationPostures.Standing, "fnd-standing", "body-clap", "face-smile",
                    ownsHead: true),
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsFalse(result.Accepted);
            StringAssert.Contains("OwnsHead", result.FailureReason);
        }

        [Test]
        public void RefreshActing_MayNotRelocateButIsAcceptedInPlace()
        {
            var host = Host();

            var relocated = host.ExecuteAsync(new PerformanceExecutionRequest
            {
                CorrelationId = "perf-refresh-move",
                Kind = PerformanceRequestKind.RefreshActing,
                AnchorId = "anchor-chair",
                PostureId = PresentationPostures.Sitting,
                FoundationIngredientId = "fnd-sitting",
                FaceIngredientId = "face-smile",
                UseBodyRest = true,
            }, CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsFalse(relocated.Accepted);
            StringAssert.Contains("may not relocate", relocated.FailureReason);

            var refresh = host.ExecuteAsync(new PerformanceExecutionRequest
            {
                CorrelationId = "perf-refresh",
                Kind = PerformanceRequestKind.RefreshActing,
                AnchorId = "anchor-hall",
                PostureId = PresentationPostures.Standing,
                FoundationIngredientId = "fnd-standing",
                BodyIngredientId = "body-clap",
                FaceIngredientId = "face-smile",
            }, CancellationToken.None).GetAwaiter().GetResult();
            Assert.IsTrue(refresh.Accepted, refresh.FailureReason);
            Assert.AreEqual("anchor-hall/standing", host.State.ToString());
            StringAssert.Contains("acting refreshed, still at anchor-hall/standing", host.Decisions.Single());
        }

        [Test]
        public void Stop_IsAcceptedAndReleasesTheState()
        {
            var host = Host();

            var result = host.ExecuteAsync(new PerformanceExecutionRequest
            {
                CorrelationId = "perf-stop", Kind = PerformanceRequestKind.Stop,
            }, CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual("/", host.State.ToString());
            StringAssert.Contains("released anchor-hall/standing", host.Decisions.Single());
        }

        [Test]
        public void ExecuteAsync_PropagatesCancellation()
        {
            var host = Host();
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();
                Assert.Throws<System.OperationCanceledException>(() => host.ExecuteAsync(
                    Stage("anchor-hall", PresentationPostures.Standing, "fnd-standing", null, "face-smile"),
                    cts.Token).GetAwaiter().GetResult());
            }
        }

        [Test]
        public void DefaultInitialState_PrefersAStandingAnchorAndFailsWithoutOne()
        {
            var state = PresentationCatalogLoader.TryDefaultInitialState(Catalog());
            Assert.IsNotNull(state);
            Assert.AreEqual("anchor-hall", state.AnchorId);
            Assert.AreEqual(PresentationPostures.Standing, state.PostureId);

            Assert.IsNull(PresentationCatalogLoader.TryDefaultInitialState(new PresentationCatalogDefinition()));
        }
    }
}
