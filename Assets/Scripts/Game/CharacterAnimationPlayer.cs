using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace TruthCardGame
{
    /// <summary>
    /// Small reusable animation owner for the current foundation: one
    /// foundation channel and one masked body overlay. The graph lives for the
    /// character lifetime; changing a clip does not rebuild it or reset the
    /// other channel's time.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class CharacterAnimationPlayer : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private AvatarMask overlayMask;

        private PlayableGraph graph;
        private AnimationLayerMixerPlayable layerMixer;
        private AnimationMixerPlayable foundationMixer;
        private Playable[] foundationSlots = { Playable.Null, Playable.Null };
        private Playable overlayPlayable = Playable.Null;
        private int activeFoundationSlot;
        private bool hasFoundation;
        private float transitionTime;
        private float transitionDuration;
        private bool transitioning;
        private AnimationClip queuedFoundation;
        private double queuedAt;
        private double elapsedTime;

        public Animator Animator => animator;

        public void Configure(Animator sourceAnimator, AvatarMask sourceOverlayMask)
        {
            animator = sourceAnimator;
            overlayMask = sourceOverlayMask;
        }

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            EnsureGraph();
        }

        private void Update()
        {
            if (!graph.IsValid()) return;
            elapsedTime += Time.deltaTime;
            if (transitioning)
            {
                transitionTime += Time.deltaTime;
                var amount = transitionDuration <= 0f ? 1f : Mathf.Clamp01(transitionTime / transitionDuration);
                foundationMixer.SetInputWeight(activeFoundationSlot, 1f - amount);
                foundationMixer.SetInputWeight(1 - activeFoundationSlot, amount);
                if (amount >= 1f)
                {
                    DestroyFoundationSlot(activeFoundationSlot);
                    activeFoundationSlot = 1 - activeFoundationSlot;
                    foundationMixer.SetInputWeight(activeFoundationSlot, 1f);
                    foundationMixer.SetInputWeight(1 - activeFoundationSlot, 0f);
                    transitioning = false;
                }
            }

            if (queuedFoundation != null && elapsedTime >= queuedAt)
            {
                var next = queuedFoundation;
                queuedFoundation = null;
                SetFoundation(next);
            }
        }

        public void SetFoundation(AnimationClip clip, float fadeSeconds = 0.15f)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            EnsureGraph();
            if (!graph.IsPlaying()) graph.Play();
            CancelFoundationTransition();
            if (!hasFoundation)
            {
                ConnectFoundation(clip, 0);
                activeFoundationSlot = 0;
                foundationMixer.SetInputWeight(0, 1f);
                foundationMixer.SetInputWeight(1, 0f);
                hasFoundation = true;
                return;
            }

            var nextSlot = 1 - activeFoundationSlot;
            ConnectFoundation(clip, nextSlot);
            foundationMixer.SetInputWeight(nextSlot, 0f);
            transitionTime = 0f;
            transitionDuration = Mathf.Max(0f, fadeSeconds);
            transitioning = transitionDuration > 0f;
            if (!transitioning)
            {
                DestroyFoundationSlot(activeFoundationSlot);
                activeFoundationSlot = nextSlot;
                foundationMixer.SetInputWeight(activeFoundationSlot, 1f);
            }
        }

        public void PlayOneShotThen(AnimationClip oneShot, AnimationClip next, float fadeSeconds = 0.15f)
        {
            if (oneShot == null) throw new ArgumentNullException(nameof(oneShot));
            EnsureGraph();
            queuedFoundation = next;
            queuedAt = elapsedTime + Math.Max(0.1f, oneShot.length);
            SetFoundation(oneShot, fadeSeconds);
        }

        public void SetOverlay(AnimationClip clip)
        {
            EnsureGraph();
            if (!graph.IsPlaying()) graph.Play();
            if (overlayPlayable.IsValid())
            {
                layerMixer.DisconnectInput(1);
                overlayPlayable.Destroy();
                overlayPlayable = Playable.Null;
            }
            if (clip == null)
            {
                layerMixer.SetInputWeight(1, 0f);
                return;
            }
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            graph.Connect(playable, 0, layerMixer, 1);
            overlayPlayable = playable;
            layerMixer.SetInputWeight(1, 1f);
        }

        public void Stop()
        {
            queuedFoundation = null;
            transitioning = false;
            if (graph.IsValid()) graph.Stop();
        }

        private void EnsureGraph()
        {
            if (graph.IsValid()) return;
            if (animator == null) throw new InvalidOperationException(name + " has no Animator.");
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            graph = PlayableGraph.Create(name + " Character Animation");
            elapsedTime = 0d;
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
            foundationMixer = AnimationMixerPlayable.Create(graph, 2);
            layerMixer = AnimationLayerMixerPlayable.Create(graph, 2);
            graph.Connect(foundationMixer, 0, layerMixer, 0);
            layerMixer.SetInputWeight(0, 1f);
            layerMixer.SetInputWeight(1, 0f);
            if (overlayMask != null) layerMixer.SetLayerMaskFromAvatarMask(1, overlayMask);
            output.SetSourcePlayable(layerMixer);
            graph.Play();
        }

        private void ConnectFoundation(AnimationClip clip, int slot)
        {
            DestroyFoundationSlot(slot);
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(true);
            graph.Connect(playable, 0, foundationMixer, slot);
            foundationSlots[slot] = playable;
        }

        private void CancelFoundationTransition()
        {
            if (!transitioning) return;
            var nextSlot = 1 - activeFoundationSlot;
            var nextWeight = foundationMixer.GetInputWeight(nextSlot);
            if (nextWeight >= 0.5f)
            {
                DestroyFoundationSlot(activeFoundationSlot);
                activeFoundationSlot = nextSlot;
            }
            else
            {
                DestroyFoundationSlot(nextSlot);
            }
            foundationMixer.SetInputWeight(activeFoundationSlot, 1f);
            foundationMixer.SetInputWeight(1 - activeFoundationSlot, 0f);
            transitioning = false;
        }

        private void DestroyFoundationSlot(int slot)
        {
            if (foundationSlots[slot].IsValid())
            {
                foundationMixer.DisconnectInput(slot);
                foundationSlots[slot].Destroy();
            }
            foundationSlots[slot] = Playable.Null;
        }

        private void OnDisable()
        {
            if (graph.IsValid()) graph.Destroy();
            graph = default;
            foundationSlots[0] = Playable.Null;
            foundationSlots[1] = Playable.Null;
            overlayPlayable = Playable.Null;
            hasFoundation = false;
            transitioning = false;
            queuedFoundation = null;
            queuedAt = 0d;
            elapsedTime = 0d;
        }
    }
}
