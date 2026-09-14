using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TruthCardGame.Content;
using TruthCardGame.Core;
using UnityEngine;

namespace TruthCardGame.Performance
{
    /// <summary>
    /// Unity's presentation host: it owns rendering and reports readiness only
    /// once the requested staging is actually coherent. Core plans against
    /// <see cref="Catalog"/> and submits correlated semantic requests; this
    /// component resolves them into concrete clips, pose and facial control
    /// (see <see cref="PerformanceStagingResolver"/>), applies them in order, and
    /// acknowledges only after the operation clips have finished.
    ///
    /// It deliberately has no opinion about which acting is correct — selection
    /// is Core's. It refuses anything it cannot realise, by name, rather than
    /// approximating: a rejected request leaves Core's committed state untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnityPerformanceHost : MonoBehaviour, IPerformanceHost
    {
        [SerializeField] private PerformanceRegistry registry;
        [SerializeField] private PerformanceStageAnchors stageAnchors;
        [SerializeField] private CharacterAnimationPlayer animationPlayer;
        [SerializeField] private CharacterFaceController face;
        [SerializeField] private CharacterGazeController gaze;
        [Tooltip("Seconds allowed for a foundation crossfade before the host reports arrival.")]
        [SerializeField, Min(0f)] private float foundationFadeSeconds = 0.15f;

        private readonly List<string> _decisions = new List<string>();
        private PresentationCatalogDefinition _catalog;
        private bool _catalogFailureLogged;
        private IGameDelay _delay;

        public const string CatalogFileName = "PresentationCatalog.json";

        /// <summary>Every applied or refused request, newest last, for on-screen diagnostics.</summary>
        public IReadOnlyList<string> Decisions => _decisions;

        /// <summary>
        /// The generated read-only catalog, at the repository root beside the
        /// content database. Loading is lazy because the engine reads it once,
        /// during construction.
        /// </summary>
        public PresentationCatalogDefinition Catalog
        {
            get
            {
                if (_catalog == null) _catalog = LoadCatalogOrThrow();
                return _catalog;
            }
        }

        public PerformanceActorState InitialState =>
            stageAnchors == null
                ? new PerformanceActorState("", "")
                : stageAnchors.InitialState();

        /// <summary>
        /// Runtime wiring for hosts assembled by the editor scene setup: binds
        /// the character's presentation controllers and the stage anchors. The
        /// registry stays project-owned (the same asset the performance fixture
        /// maintains) and is passed in rather than loaded by path, so a scene in
        /// a build can be wired with any registry the host chooses to supply.
        /// </summary>
        public void Configure(
            PerformanceRegistry sourceRegistry,
            PerformanceStageAnchors sourceStageAnchors,
            CharacterAnimationPlayer sourceAnimationPlayer,
            CharacterFaceController sourceFace,
            CharacterGazeController sourceGaze)
        {
            registry = sourceRegistry;
            stageAnchors = sourceStageAnchors;
            animationPlayer = sourceAnimationPlayer;
            face = sourceFace;
            gaze = sourceGaze;
        }

        /// <summary>Reloads the catalog from disk; used by Repeat so a regenerate is picked up.</summary>
        public void InvalidateCatalog()
        {
            _catalog = null;
            _catalogFailureLogged = false;
        }

        /// <summary>
        /// Returns the character to the declared starting placement between
        /// independent session runs. This is presentation reset only; Core
        /// still reloads the canonical content snapshot for Repeat.
        /// </summary>
        public void ResetToInitialPlacement()
        {
            ApplyStop();
            if (stageAnchors == null || stageAnchors.CharacterRoot == null) return;
            if (!stageAnchors.TryGetAnchor(stageAnchors.StartingAnchorId, out var startingAnchor) ||
                startingAnchor == null)
            {
                Debug.LogWarning(
                    "[PERFORMANCE] Cannot reset character placement: starting anchor '" +
                    stageAnchors.StartingAnchorId + "' is not placed.");
                return;
            }
            stageAnchors.CharacterRoot.SetPositionAndRotation(
                startingAnchor.position, startingAnchor.rotation);
        }

        /// <summary>Repo-relative catalog location, matching the editor generator.</summary>
        public static string ResolveCatalogPath()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.Combine(projectRoot, "Content", CatalogFileName);
        }

        public async Task<PerformanceExecutionResult> ExecuteAsync(
            PerformanceExecutionRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();

            // Stop is teardown, not a content-resolution request. It must still
            // release the rig when a generated catalog was deleted or is being
            // regenerated during Repeat.
            if (request.Kind == PerformanceRequestKind.Stop)
            {
                ApplyStop();
                Record(request, "ready: stop: release presentation");
                return PerformanceExecutionResult.Accept(request.CorrelationId);
            }

            if (!PerformanceStagingResolver.TryResolve(
                    request, registry, Catalog, out var plan, out var failure))
            {
                return Refuse(request, failure);
            }

            try
            {
                switch (plan.Kind)
                {
                    case PerformanceRequestKind.RefreshActing:
                        if (!ApplyActing(plan, out var actingFailure)) return Refuse(request, actingFailure);
                        break;
                    default:
                        if (!TryResolveDestination(plan, out var destination, out var placementFailure))
                        {
                            return Refuse(request, placementFailure);
                        }
                        if (animationPlayer == null)
                        {
                            return Refuse(request,
                                "the scene has no CharacterAnimationPlayer, so staging cannot be presented");
                        }
                        await ApplyStagingAsync(destination, plan, cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!ApplyActing(plan, out var stagingFailure)) return Refuse(request, stagingFailure);
                        cancellationToken.ThrowIfCancellationRequested();
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation must propagate: a cancelled request never claims
                // arrival, and Core keeps its previously committed state.
                Record(request, "cancelled; staging not acknowledged");
                throw;
            }

            Record(request, "ready: " + plan.Describe());
            return PerformanceExecutionResult.Accept(request.CorrelationId);
        }

        /// <summary>
        /// Travel and re-pose, awaiting every composed operation clip. Awaiting is
        /// what makes the acknowledgement mean "the actor is there", not "the
        /// command was handed over".
        /// </summary>
        private async Task ApplyStagingAsync(
            Transform destination, PerformanceStagingPlan plan, CancellationToken cancellationToken)
        {
            for (var index = 0; index < plan.OperationClips.Count; index++)
            {
                var clip = plan.OperationClips[index];
                var kind = plan.OperationKinds[index];
                var duration = Mathf.Max(0.1f, clip.length);

                animationPlayer.SetFoundation(clip, foundationFadeSeconds);
                if (kind == PresentationOperationKinds.MoveWhileStanding)
                {
                    await TravelAsync(destination, duration, cancellationToken);
                }
                else
                {
                    await DelayAsync(duration, cancellationToken);
                }
            }

            // Contact: snap exactly onto the anchor after travel so repeated
            // staging cannot drift, and so arrival is the declared anchor pose.
            if (stageAnchors != null && stageAnchors.CharacterRoot != null)
            {
                stageAnchors.CharacterRoot.SetPositionAndRotation(
                    destination.position, destination.rotation);
            }

            animationPlayer.SetFoundation(plan.FoundationClip, foundationFadeSeconds);
            await DelayAsync(foundationFadeSeconds, cancellationToken);
        }

        /// <summary>Overlay, facial preset and gaze. Shared by staging and acting-only refresh.</summary>
        private bool ApplyActing(PerformanceStagingPlan plan, out string failure)
        {
            failure = null;

            if (animationPlayer == null)
            {
                failure = "the scene has no CharacterAnimationPlayer, so acting cannot be presented";
                return false;
            }

            if (face == null)
            {
                failure = "the character has no CharacterFaceController, so the required face cannot be presented";
                return false;
            }

            if (face.CountPresetMatches(plan.FaceControl) <= 0)
            {
                failure =
                    $"facial control '{plan.FaceControl}' matched no channel on this character's facial renderers";
                return false;
            }

            if (gaze == null)
            {
                failure = "the character has no CharacterGazeController";
                return false;
            }

            Transform target = null;
            if (!plan.SuspendGaze)
            {
                target = stageAnchors == null ? null : stageAnchors.PlayerGazeTarget;
                if (target == null)
                {
                    failure =
                        "gaze is on for this acting but no player gaze target is placed in PerformanceStageAnchors";
                    return false;
                }
            }

            // All required presentation dependencies are known before any
            // state changes. The remaining match check is defensive only; the
            // non-mutating preflight above is what prevents partial updates.
            animationPlayer.SetOverlay(plan.OverlayClip);
            var matched = face.SetPreset(plan.FaceControl, plan.FaceWeight);
            if (matched <= 0)
            {
                failure =
                    $"facial control '{plan.FaceControl}' matched no channel on this character's facial renderers";
                return false;
            }
            if (target != null) gaze.SetTarget(target);
            gaze.SetEnabled(!plan.SuspendGaze);
            gaze.ResetSmoothing();
            return true;
        }

        /// <summary>Releases presentation. Safe when nothing was staged.</summary>
        private void ApplyStop()
        {
            if (animationPlayer != null)
            {
                animationPlayer.SetOverlay(null);
                animationPlayer.Stop();
            }
            if (face != null) face.SetPreset(null, 0f);
            if (gaze != null) gaze.SetEnabled(false);
        }

        /// <summary>
        /// The anchor must be placed in this scene. An unplaced anchor is a setup
        /// gap the author can act on, so it is refused by name rather than thrown.
        /// </summary>
        private bool TryResolveDestination(
            PerformanceStagingPlan plan, out Transform destination, out string failure)
        {
            destination = null;
            failure = null;

            if (stageAnchors == null)
            {
                failure = "the scene has no PerformanceStageAnchors, so no anchor can be staged";
                return false;
            }
            if (!stageAnchors.TryGetAnchor(plan.AnchorId, out destination) || destination == null)
            {
                failure =
                    $"anchor '{plan.AnchorId}' has no scene placement in PerformanceStageAnchors; " +
                    "the host cannot stage the actor there";
                return false;
            }
            return true;
        }

        /// <summary>Interpolates the character root onto the destination anchor; cancellable mid-travel.</summary>
        private async Task TravelAsync(Transform destination, float duration, CancellationToken cancellationToken)
        {
            var root = stageAnchors == null ? null : stageAnchors.CharacterRoot;
            if (root == null)
            {
                await DelayAsync(duration, cancellationToken);
                return;
            }

            var startPosition = root.position;
            var startRotation = root.rotation;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                elapsed += Time.deltaTime;
                var amount = Mathf.Clamp01(elapsed / duration);
                root.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, destination.position, amount),
                    Quaternion.Slerp(startRotation, destination.rotation, amount));
                await Task.Yield();
            }

            root.SetPositionAndRotation(destination.position, destination.rotation);
        }

        private Task DelayAsync(float seconds, CancellationToken cancellationToken)
        {
            if (seconds <= 0f) return Task.CompletedTask;
            _delay ??= new UnityGameDelay();
            return _delay.DelayAsync(TimeSpan.FromSeconds(seconds), cancellationToken);
        }

        private PresentationCatalogDefinition LoadCatalogOrThrow()
        {
            var path = ResolveCatalogPath();
            try
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        "generated presentation catalog not found at " + path +
                        "; run TruthCardGame/Performance/Generate Presentation Catalog in the editor.", path);
                }
                var catalog = PresentationCatalogJson.FromJson(File.ReadAllText(path));
                PresentationCatalogValidator.Validate(catalog);
                if (!_catalogFailureLogged)
                {
                    _decisions.Add(
                        $"catalog loaded: {catalog.Ingredients.Count} ingredients, " +
                        $"{catalog.Anchors.Count} anchors, {catalog.Operations.Count} operations ({path})");
                }
                return catalog;
            }
            catch (Exception ex)
            {
                if (!_catalogFailureLogged)
                {
                    _catalogFailureLogged = true;
                    Debug.LogError("[PERFORMANCE] Unity performance host cannot load its catalog: " + ex.Message);
                }
                throw;
            }
        }

        private PerformanceExecutionResult Refuse(PerformanceExecutionRequest request, string failure)
        {
            // A refusal is not a silent no-op: it names what could not be
            // realised, and Core keeps its committed state.
            Record(request, "REFUSED: " + failure);
            return PerformanceExecutionResult.Reject(request.CorrelationId, failure);
        }

        private void Record(PerformanceExecutionRequest request, string outcome)
        {
            var line = $"PERFORM {request.CorrelationId} {request.Kind} {outcome}";
            _decisions.Add(line);
            Debug.Log("[PERFORMANCE] " + line);
        }
    }
}
