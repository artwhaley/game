using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// Asset-only viewer for the current rig foundation. It owns test buttons
    /// and fixture travel; reusable presentation components own the actual pose.
    /// </summary>
    public sealed class Phase00RigShowcase : MonoBehaviour
    {
        public Animator sourceAnimator;
        public Animator targetAnimator;
        public CharacterAnimationPlayer sourcePlayer;
        public CharacterAnimationPlayer targetPlayer;
        public CharacterFaceController faceController;
        public CharacterGazeController gazeController;
        public Transform sourceActor;
        public Transform targetActor;
        public Transform gazeTarget;
        public AvatarMask upperBodyMask;
        public AnimationClip idle;
        public AnimationClip walk;
        public AnimationClip sitEnter;
        public AnimationClip sitIdle;
        public AnimationClip sitExit;
        public AnimationClip talking;
        public AnimationClip interact;

        private AnimationClip foundation;
        private AnimationClip overlay;
        private AnimationClip queuedFoundation;
        private float switchAt;
        private bool moving;
        private bool gazeEnabled = true;
        private float movementStart;
        private bool owPhonemeEnabled;
        private string faceStatus = "Exported neutral baseline";
        private const float MovementSeconds = 2.4f;

        private void Start()
        {
            sourcePlayer = sourcePlayer == null ? GetOrAddPlayer(sourceActor, sourceAnimator) : sourcePlayer;
            targetPlayer = targetPlayer == null ? GetOrAddPlayer(targetActor, targetAnimator) : targetPlayer;
            sourcePlayer.Configure(sourceAnimator, upperBodyMask);
            targetPlayer.Configure(targetAnimator, upperBodyMask);
            if (faceController == null && targetActor != null)
                faceController = targetActor.GetComponent<CharacterFaceController>();
            if (gazeController == null && targetActor != null)
                gazeController = targetActor.GetComponent<CharacterGazeController>();
            if (gazeController != null) gazeController.SetTarget(gazeTarget);
            ResetActors();
            PlayFoundation(idle);
        }

        private void Update()
        {
            if (queuedFoundation != null && Time.time >= switchAt)
            {
                var next = queuedFoundation;
                queuedFoundation = null;
                PlayFoundation(next);
            }
            if (!moving) return;
            var t = Mathf.Clamp01((Time.time - movementStart) / MovementSeconds);
            MoveActor(sourceActor, -1.35f, t);
            MoveActor(targetActor, 1.35f, t);
            if (t >= 1f)
            {
                moving = false;
                PlayFoundation(idle);
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, 310, 620), GUI.skin.box);
            GUILayout.Label("Phase 00: same source clip on both Humanoid Avatars");
            GUILayout.Label("Debug camera: WASD move, Q/E up/down, RMB drag look");
            if (GUILayout.Button("Reset / Stand Idle")) { ResetActors(); PlayFoundation(idle); }
            if (GUILayout.Button("Walk A to B")) BeginWalk();
            if (GUILayout.Button("Sit Down → Sitting Idle")) PlayOneShotThen(sitEnter, sitIdle);
            if (GUILayout.Button("Sitting Idle")) PlayFoundation(sitIdle);
            if (GUILayout.Button("Stand Up → Standing Idle")) PlayOneShotThen(sitExit, idle);
            GUILayout.Space(8);
            if (GUILayout.Button("Overlay: Talking")) PlayOverlay(talking);
            if (GUILayout.Button("Overlay: Interact")) PlayOverlay(interact);
            if (GUILayout.Button("Clear Overlay")) PlayOverlay(null);
            GUILayout.Space(8);
            if (GUILayout.Button("Face: Exported Neutral")) { owPhonemeEnabled = false; SetFacePreset(null, 0f); }
            if (GUILayout.Button("Face: Natural Smile")) { owPhonemeEnabled = false; SetFacePreset("ST Mika 8 Natural Smile", 100f); }
            if (GUILayout.Button("Face: Frown")) { owPhonemeEnabled = false; SetFacePreset("eCTRLFrown_HD", 100f); }
            if (GUILayout.Button("Face: Toggle OW Phoneme " + (owPhonemeEnabled ? "OFF" : "ON")))
            {
                owPhonemeEnabled = !owPhonemeEnabled;
                SetFacePreset(owPhonemeEnabled ? "eCTRLvOW" : null, owPhonemeEnabled ? 100f : 0f);
            }
            var requestedGaze = GUILayout.Toggle(gazeEnabled, "Target head looks at green marker");
            if (requestedGaze != gazeEnabled)
            {
                gazeEnabled = requestedGaze;
                if (gazeController != null) gazeController.SetEnabled(gazeEnabled);
            }
            GUILayout.Space(8);
            GUILayout.Label("Foundation: " + (foundation == null ? "none" : foundation.name));
            GUILayout.Label("Overlay: " + (overlay == null ? "none" : overlay.name));
            GUILayout.Label("Face: " + faceStatus);
            GUILayout.Label("Left: Quaternius source rig. Right: supplied target.");
            GUILayout.EndArea();
        }

        private void BeginWalk()
        {
            ResetActors();
            moving = true;
            movementStart = Time.time;
            PlayFoundation(walk);
        }

        private void ResetActors()
        {
            MoveActor(sourceActor, -1.35f, 0f);
            MoveActor(targetActor, 1.35f, 0f);
            moving = false;
            if (gazeController != null) gazeController.ResetSmoothing();
        }

        private static void MoveActor(Transform actor, float x, float t)
        {
            if (actor != null) actor.position = new Vector3(x, 0f, Mathf.Lerp(-1.25f, 2.75f, t));
        }

        private void PlayOneShotThen(AnimationClip oneShot, AnimationClip next)
        {
            if (oneShot == null) return;
            queuedFoundation = next;
            switchAt = Time.time + Mathf.Max(0.1f, oneShot.length);
            PlayFoundation(oneShot);
        }

        private void PlayFoundation(AnimationClip clip)
        {
            if (clip == null) return;
            foundation = clip;
            sourcePlayer.SetFoundation(clip);
            targetPlayer.SetFoundation(clip);
        }

        private void PlayOverlay(AnimationClip clip)
        {
            overlay = clip;
            sourcePlayer.SetOverlay(clip);
            targetPlayer.SetOverlay(clip);
        }

        private void SetFacePreset(string exactExportedControl, float weight)
        {
            if (faceController == null)
            {
                faceStatus = "CharacterFaceController missing";
                return;
            }
            var matches = faceController.SetPreset(exactExportedControl, weight);
            faceStatus = string.IsNullOrEmpty(exactExportedControl)
                ? "Exported neutral baseline restored"
                : $"{exactExportedControl} ({weight:0}% on {matches} exact channel{(matches == 1 ? "" : "s")})";
        }

        // Kept as a tiny compatibility seam for the existing EditMode asset
        // readiness test. Face ownership lives in CharacterFaceController;
        // this viewer method only resolves that component for a prefab-only
        // test that has not entered Start yet.
        private void CaptureNeutralFace()
        {
            if (faceController == null && targetActor != null)
                faceController = targetActor.GetComponent<CharacterFaceController>();
        }

        private static CharacterAnimationPlayer GetOrAddPlayer(Transform actor, Animator animator)
        {
            if (actor == null) return null;
            var player = actor.GetComponent<CharacterAnimationPlayer>() ?? actor.gameObject.AddComponent<CharacterAnimationPlayer>();
            player.Configure(animator, null);
            return player;
        }

        private void OnDestroy()
        {
            if (sourcePlayer != null) sourcePlayer.Stop();
            if (targetPlayer != null) targetPlayer.Stop();
        }
    }
}
