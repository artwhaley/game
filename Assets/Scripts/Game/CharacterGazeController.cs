using System;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// Applies a calibrated, persistent head aim after authored animation.
    /// It owns no target selection policy; callers provide the target transform.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterGazeController : MonoBehaviour
    {
        [SerializeField] private CharacterRig rig;
        [SerializeField] private Transform target;
        [SerializeField] private bool gazeEnabled = true;
        [SerializeField] private float maximumYawDegrees = 70f;
        [SerializeField] private float maximumPitchUpDegrees = 30f;
        [SerializeField] private float maximumPitchDownDegrees = 40f;
        [SerializeField] private float responsiveness = 10f;

        private Transform head;
        private Transform leftEye;
        private Transform rightEye;
        private Vector3 neutralFaceDirectionLocal;
        private Vector3 neutralUpDirectionLocal;
        private Vector3 smoothedDirection;
        private bool calibrated;
        private bool hasSmoothedDirection;

        public Transform Target => target;
        public bool Enabled => gazeEnabled;

        public void Configure(CharacterRig sourceRig, Transform sourceTarget)
        {
            rig = sourceRig;
            target = sourceTarget;
            calibrated = false;
            hasSmoothedDirection = false;
        }

        public void SetTarget(Transform sourceTarget)
        {
            target = sourceTarget;
            hasSmoothedDirection = false;
        }

        public void SetEnabled(bool enabled)
        {
            gazeEnabled = enabled;
            if (!enabled) hasSmoothedDirection = false;
        }

        public void ResetSmoothing()
        {
            hasSmoothedDirection = false;
        }

        private void Awake()
        {
            if (rig == null) rig = GetComponent<CharacterRig>();
            CalibrateIfNeeded();
        }

        internal void ApplyFinalPose()
        {
            CalibrateIfNeeded();
            if (!gazeEnabled || target == null || head == null)
            {
                hasSmoothedDirection = false;
                return;
            }

            var gazeOrigin = head.position;
            if (leftEye != null && rightEye != null)
                gazeOrigin = (leftEye.position + rightEye.position) * 0.5f;
            var desiredDirection = target.position - gazeOrigin;
            if (desiredDirection.sqrMagnitude < 0.000001f)
            {
                hasSmoothedDirection = false;
                return;
            }

            var animatedFaceDirection = head.TransformDirection(neutralFaceDirectionLocal).normalized;
            var animatedUpDirection = head.TransformDirection(neutralUpDirectionLocal).normalized;
            var clampedTarget = ClampDirection(animatedFaceDirection, animatedUpDirection, desiredDirection.normalized);
            if (!hasSmoothedDirection)
            {
                smoothedDirection = animatedFaceDirection;
                hasSmoothedDirection = true;
            }
            var blend = 1f - Mathf.Exp(-Mathf.Max(0.01f, responsiveness) * Mathf.Max(Time.deltaTime, 0.001f));
            smoothedDirection = Vector3.Slerp(smoothedDirection, clampedTarget, blend).normalized;
            smoothedDirection = ClampDirection(animatedFaceDirection, animatedUpDirection, smoothedDirection);
            head.rotation = Quaternion.FromToRotation(animatedFaceDirection, smoothedDirection) * head.rotation;
        }

        private void CalibrateIfNeeded()
        {
            if (calibrated) return;
            if (rig == null) rig = GetComponent<CharacterRig>();
            if (rig == null) return;
            head = rig.Animator == null ? null : rig.Animator.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;
            leftEye = rig.Animator.GetBoneTransform(HumanBodyBones.LeftEye);
            rightEye = rig.Animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (leftEye == null || rightEye == null)
                throw new InvalidOperationException(rig.name + " requires both eye bones for calibrated gaze.");
            // The vector from the head pivot to the eye positions is not a
            // facing direction: on Genesis 8 it points up into the eyes. Use
            // the eye bones' forward axes so a level target stays level.
            var faceDirection = leftEye.forward + rightEye.forward;
            if (faceDirection.sqrMagnitude < 0.000001f)
                throw new InvalidOperationException(rig.name + " has a zero-length eye-forward direction.");

            faceDirection.Normalize();
            var upDirection = Vector3.ProjectOnPlane(rig.transform.up, faceDirection);
            if (upDirection.sqrMagnitude < 0.000001f)
                upDirection = Vector3.ProjectOnPlane(head.up, faceDirection);
            if (upDirection.sqrMagnitude < 0.000001f)
                throw new InvalidOperationException(rig.name + " has no usable up direction for gaze calibration.");

            neutralFaceDirectionLocal = head.InverseTransformDirection(faceDirection);
            neutralUpDirectionLocal = head.InverseTransformDirection(upDirection.normalized);
            calibrated = true;
        }

        private Vector3 ClampDirection(Vector3 faceDirection, Vector3 upDirection, Vector3 desiredDirection)
        {
            faceDirection.Normalize();
            upDirection = Vector3.ProjectOnPlane(upDirection, faceDirection);
            if (upDirection.sqrMagnitude < 0.000001f)
                return faceDirection;
            upDirection.Normalize();

            var horizontalFace = Vector3.ProjectOnPlane(faceDirection, upDirection).normalized;
            var horizontalTarget = Vector3.ProjectOnPlane(desiredDirection, upDirection).normalized;
            if (horizontalFace.sqrMagnitude < 0.000001f) horizontalFace = faceDirection;
            if (horizontalTarget.sqrMagnitude < 0.000001f) horizontalTarget = horizontalFace;

            var yaw = Mathf.Clamp(Vector3.SignedAngle(horizontalFace, horizontalTarget, upDirection),
                                  -maximumYawDegrees, maximumYawDegrees);
            var yawed = Quaternion.AngleAxis(yaw, upDirection) * faceDirection;
            var right = Vector3.Cross(upDirection, yawed).normalized;
            if (right.sqrMagnitude < 0.000001f) return yawed.normalized;
            var pitch = Vector3.SignedAngle(yawed, desiredDirection, right);
            pitch = Mathf.Clamp(pitch, -maximumPitchDownDegrees, maximumPitchUpDegrees);
            return (Quaternion.AngleAxis(pitch, right) * yawed).normalized;
        }
    }
}
