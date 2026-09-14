using System;
using System.Collections.Generic;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Core;
using TruthCardGame.Performance;
using UnityEngine;

namespace TruthCardGame.Tests
{
    /// <summary>
    /// Ticket 04: the Unity host's decision half. These tests pin what the host
    /// will and will not execute, with no scene and no Animator: a legal request
    /// resolves to concrete clips and a facial preset, and every way it can be
    /// unrealisable is refused by name instead of approximated.
    ///
    /// The applying half (actual Animator/Playables work) needs a running scene
    /// and is covered by the PlayMode suite and visual inspection.
    /// </summary>
    [TestFixture]
    public class PerformanceStagingResolverTests
    {
        private static readonly string[] Standing = { PresentationPostures.Standing };
        private static readonly string[] BothPostures =
            { PresentationPostures.Standing, PresentationPostures.Sitting };

        private PerformanceRegistry _registry;
        private PresentationCatalogDefinition _catalog;

        [SetUp]
        public void SetUp()
        {
            _registry = ScriptableObject.CreateInstance<PerformanceRegistry>();
            _registry.ReplaceContents(BuildView());
            _catalog = _registry.BuildView().BuildCatalog(DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_registry);
        }

        [Test]
        public void LegalStaging_ResolvesOperationsFoundationOverlayAndPreset()
        {
            var request = Staging("anchor-chair", PresentationPostures.Sitting,
                operations: new[] { PresentationOperationKinds.Sit },
                body: "ing-talking", face: "ing-smile");

            Assert.IsTrue(Resolve(request, out var plan, out var failure), failure);
            Assert.AreEqual(1, plan.OperationClips.Count);
            Assert.AreEqual("Sit", plan.OperationClips[0].name, "the sit operation's bound clip");
            Assert.AreEqual("Sitting_Idle_Loop", plan.FoundationClip.name, "the destination posture's foundation");
            Assert.AreEqual("Talking", plan.OverlayClip.name);
            Assert.AreEqual("ST Mika 8 Natural Smile", plan.FaceControl);
            Assert.AreEqual(100f, plan.FaceWeight);
            Assert.IsFalse(plan.SuspendGaze, "ordinary acting leaves gaze on the player");
        }

        [Test]
        public void BodyRest_PresentsNoOverlayButKeepsFoundationAndFace()
        {
            var request = Staging("anchor-chair", PresentationPostures.Sitting,
                operations: Array.Empty<string>(), body: null, face: "ing-smile");

            Assert.IsTrue(Resolve(request, out var plan, out var failure), failure);
            Assert.IsTrue(request.UseBodyRest);
            Assert.IsNull(plan.OverlayClip, "rest is the explicit absence of a gesture, not a substitution");
            Assert.AreEqual("Sitting_Idle_Loop", plan.FoundationClip.name);
            Assert.AreEqual("ST Mika 8 Natural Smile", plan.FaceControl);
        }

        [Test]
        public void ActingOnlyRefresh_LeavesTheFoundationAlone()
        {
            var request = ActingOnly("anchor-chair", PresentationPostures.Sitting,
                body: "ing-talking", face: "ing-smile");

            Assert.IsTrue(Resolve(request, out var plan, out var failure), failure);
            Assert.IsNull(plan.FoundationClip,
                "a refresh may change acting but must not re-pose, so it composes no foundation");
            Assert.IsEmpty(plan.OperationClips, "a refresh relocates nothing");
            Assert.AreEqual("Talking", plan.OverlayClip.name);
        }

        [Test]
        public void HeadOwningActing_SuspendsGaze()
        {
            var request = Staging("anchor-chair", PresentationPostures.Sitting,
                operations: Array.Empty<string>(), body: "ing-headturn", face: "ing-smile");

            Assert.IsTrue(Resolve(request, out var plan, out var failure), failure);
            Assert.IsTrue(plan.SuspendGaze);
        }

        [Test]
        public void OperationWithoutAUnityClip_IsRefusedByOperationId()
        {
            // The registry declares the operation but the artist never bound a clip.
            var view = BuildView(unboundOperationId: "op-stand");
            _registry.ReplaceContents(view);

            var request = Staging("anchor-room", PresentationPostures.Standing,
                operations: new[] { PresentationOperationKinds.Stand },
                body: "ing-talking", face: "ing-smile");

            Assert.IsFalse(Resolve(request, out _, out var failure));
            StringAssert.Contains("op-stand", failure);
            StringAssert.Contains("no Unity clip binding", failure);
        }

        [Test]
        public void IngredientAbsentFromTheCatalog_IsRefused()
        {
            var request = Staging("anchor-chair", PresentationPostures.Sitting,
                operations: Array.Empty<string>(), body: "ing-talking", face: "ing-missing");

            Assert.IsFalse(Resolve(request, out _, out var failure));
            StringAssert.Contains("ing-missing", failure);
            StringAssert.Contains("not declared", failure);
        }

        [Test]
        public void DisabledCatalogIngredient_IsRefused()
        {
            var request = Staging("anchor-chair", PresentationPostures.Sitting,
                operations: Array.Empty<string>(), body: "ing-dance", face: "ing-smile");

            Assert.IsFalse(Resolve(request, out _, out var failure));
            StringAssert.Contains("ing-dance", failure);
            StringAssert.Contains("disabled", failure);
        }

        [Test]
        public void IngredientWithNoExportedControl_IsRefused()
        {
            var request = Staging("anchor-chair", PresentationPostures.Sitting,
                operations: Array.Empty<string>(), body: "ing-talking", face: "ing-unbound-face");

            Assert.IsFalse(Resolve(request, out _, out var failure));
            StringAssert.Contains("ing-unbound-face", failure);
            StringAssert.Contains("no exported facial control", failure);
        }

        [Test]
        public void HeadOwnershipDisagreementWithCore_IsRefusedRatherThanResolved()
        {
            // The body owns the head, so Core should have asked for OwnsHead=true.
            var request = Staging("anchor-chair", PresentationPostures.Sitting,
                operations: Array.Empty<string>(), body: "ing-headturn", face: "ing-smile");
            request.OwnsHead = false;

            Assert.IsFalse(Resolve(request, out _, out var failure));
            StringAssert.Contains("head ownership", failure);
        }

        [Test]
        public void PostureTheAnchorDoesNotSupport_IsRefused()
        {
            var request = Staging("anchor-room", PresentationPostures.Sitting,
                operations: Array.Empty<string>(), body: "ing-talking", face: "ing-smile");

            Assert.IsFalse(Resolve(request, out _, out var failure));
            StringAssert.Contains("does not support posture", failure);
        }

        [Test]
        public void Stop_ResolvesWithoutTouchingActing()
        {
            var request = new PerformanceExecutionRequest { CorrelationId = "c", Kind = PerformanceRequestKind.Stop };

            Assert.IsTrue(Resolve(request, out var plan, out var failure), failure);
            Assert.IsNull(plan.OverlayClip);
            Assert.IsNull(plan.FoundationClip);
            Assert.IsEmpty(plan.FaceControl);
        }

        // ---- fixtures ----

        private bool Resolve(PerformanceExecutionRequest request, out PerformanceStagingPlan plan, out string failure)
        {
            return PerformanceStagingResolver.TryResolve(request, _registry, _catalog, out plan, out failure);
        }

        private static PerformanceExecutionRequest Staging(
            string anchorId, string postureId, string[] operations, string body, string face)
        {
            var request = new PerformanceExecutionRequest
            {
                CorrelationId = "test",
                Kind = PerformanceRequestKind.Stage,
                AnchorId = anchorId,
                PostureId = postureId,
                FoundationIngredientId = postureId == PresentationPostures.Sitting
                    ? "ing-sitting"
                    : "ing-standing",
                FaceIngredientId = face,
                UseBodyRest = body == null,
                BodyIngredientId = body ?? "",
            };
            foreach (var kind in operations)
            {
                request.Operations.Add(new PlannedPerformanceOperation
                {
                    OperationId = "op-" + kind,
                    Kind = kind,
                    AnchorId = anchorId,
                    ResultPostureId = postureId,
                });
            }
            // The resolver checks this against the selected acting, exactly as it
            // would for a real request Core derived from the catalog.
            request.OwnsHead = string.Equals(body, "ing-headturn", StringComparison.Ordinal);
            return request;
        }

        private static PerformanceExecutionRequest ActingOnly(
            string anchorId, string postureId, string body, string face)
        {
            var request = Staging(anchorId, postureId, Array.Empty<string>(), body, face);
            request.Kind = PerformanceRequestKind.RefreshActing;
            request.FoundationIngredientId = "";
            return request;
        }

        private static PerformanceRegistryView BuildView(string unboundOperationId = null)
        {
            var mask = null as AvatarMask;
            var ingredients = new List<PerformanceIngredientEntry>
            {
                Ingredient("ing-standing", PresentationIngredientKinds.Foundation, true, Standing, "Idle_Loop"),
                Ingredient("ing-sitting", PresentationIngredientKinds.Foundation, true, BothPostures, "Sitting_Idle_Loop"),
                Ingredient("ing-talking", PresentationIngredientKinds.Body, true, BothPostures, "Talking"),
                Ingredient("ing-headturn", PresentationIngredientKinds.Body, true, BothPostures, "HeadTurn", ownsHead: true),
                // Declared but still being authored: the catalog carries it disabled.
                Ingredient("ing-dance", PresentationIngredientKinds.Body, false, Standing, "Dance_Loop"),
                Face("ing-smile", "ST Mika 8 Natural Smile"),
                Face("ing-unbound-face", ""),
            };

            var anchors = new List<PerformanceAnchorEntry>
            {
                Anchor("anchor-room", Standing),
                Anchor("anchor-chair", BothPostures),
            };

            var operations = new List<PerformanceOperationEntry>
            {
                Operation("op-stand", PresentationOperationKinds.Stand,
                    unboundOperationId == "op-stand" ? null : "Sitting_Exit"),
                Operation("op-sit", PresentationOperationKinds.Sit,
                    unboundOperationId == "op-sit" ? null : "Sitting_Enter"),
                Operation("op-move_while_standing", PresentationOperationKinds.MoveWhileStanding,
                    unboundOperationId == "op-move_while_standing" ? null : "Walk_Loop"),
            };

            return new PerformanceRegistryView(mask, ingredients, anchors, operations);
        }

        private static PerformanceIngredientEntry Ingredient(
            string id, string kind, bool enabled, string[] postures, string clipName, bool ownsHead = false)
        {
            var entry = new PerformanceIngredientEntry();
            entry.Configure(id, id, kind, enabled, Array.Empty<string>(), postures, ownsHead, Clip(clipName));
            return entry;
        }

        private static PerformanceIngredientEntry Face(string id, string controlName)
        {
            var entry = new PerformanceIngredientEntry();
            entry.Configure(id, id, PresentationIngredientKinds.Face, true,
                Array.Empty<string>(), BothPostures, false, null, controlName, 100f);
            return entry;
        }

        private static PerformanceAnchorEntry Anchor(string id, string[] postures)
        {
            var entry = new PerformanceAnchorEntry();
            entry.Configure(id, id, id, postures, Array.Empty<string>());
            return entry;
        }

        private static PerformanceOperationEntry Operation(string id, string kind, string clipName)
        {
            var entry = new PerformanceOperationEntry();
            entry.Configure(id, kind, kind, 10, null, clipName == null ? null : Clip(clipName));
            return entry;
        }

        private static AnimationClip Clip(string name)
        {
            var clip = new AnimationClip { name = name };
            return clip;
        }
    }
}
