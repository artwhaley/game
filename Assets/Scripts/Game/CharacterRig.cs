using System;
using System.Collections.Generic;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// Owns the body skeleton used by presentation attachments. The map is
    /// deliberately built from the body root only, so duplicate bone names in
    /// a private hair or clothing skeleton can never steal an attachment bind.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class CharacterRig : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private Transform bodySkeletonRoot;
        [SerializeField] private Transform attachmentsRoot;
        [SerializeField] private string rigFamilyId = "Genesis8Female";

        private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly List<RiggedAttachment> attachments = new List<RiggedAttachment>();
        private CharacterPresentation presentation;
        private bool initialized;

        public Animator Animator => animator;
        public Transform BodySkeletonRoot => bodySkeletonRoot;
        public Transform AttachmentsRoot => attachmentsRoot;
        public string RigFamilyId => rigFamilyId;
        public bool IsInitialized => initialized;

        public void Configure(Animator sourceAnimator, Transform sourceBodySkeletonRoot, Transform sourceAttachmentsRoot, string sourceRigFamilyId)
        {
            animator = sourceAnimator;
            bodySkeletonRoot = sourceBodySkeletonRoot;
            attachmentsRoot = sourceAttachmentsRoot;
            if (!string.IsNullOrWhiteSpace(sourceRigFamilyId)) rigFamilyId = sourceRigFamilyId;
            initialized = false;
            EnsureInitialized();
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void LateUpdate()
        {
            // A plain CharacterRig remains useful without the optional
            // coordinator. When one is present it owns the final-pose order.
            if (presentation == null) ApplyAttachments();
        }

        public void EnsureInitialized()
        {
            if (initialized) return;
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            if (animator == null) throw new InvalidOperationException(name + " has no Animator.");
            if (bodySkeletonRoot == null)
            {
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                bodySkeletonRoot = hips == null ? null : hips.parent;
            }
            if (bodySkeletonRoot == null) throw new InvalidOperationException(name + " has no explicit body skeleton root.");
            if (attachmentsRoot == null)
            {
                var child = transform.Find("Attachments");
                attachmentsRoot = child;
            }
            if (attachmentsRoot == null)
                throw new InvalidOperationException(name + " has no explicit Attachments container.");

            bones.Clear();
            foreach (var candidate in bodySkeletonRoot.GetComponentsInChildren<Transform>(true))
            {
                if (bones.ContainsKey(candidate.name))
                    throw new InvalidOperationException(name + " has duplicate body bone name '" + candidate.name + "'.");
                bones.Add(candidate.name, candidate);
            }
            initialized = true;
        }

        public bool TryGetBone(string boneName, out Transform bone)
        {
            if (string.IsNullOrWhiteSpace(boneName))
            {
                bone = null;
                return false;
            }
            EnsureInitialized();
            return bones.TryGetValue(boneName, out bone);
        }

        public Transform RequireBone(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName))
                throw new InvalidOperationException(name + " requested an empty body bone name.");
            if (!TryGetBone(boneName, out var bone))
                throw new InvalidOperationException(name + " is missing body bone '" + boneName + "'.");
            return bone;
        }

        internal void Register(RiggedAttachment attachment)
        {
            if (attachment == null) return;
            EnsureInitialized();
            if (!attachments.Contains(attachment)) attachments.Add(attachment);
        }

        internal void Unregister(RiggedAttachment attachment)
        {
            attachments.Remove(attachment);
        }

        internal void SetPresentation(CharacterPresentation owner)
        {
            presentation = owner;
        }

        internal void ClearPresentation(CharacterPresentation owner)
        {
            if (presentation == owner) presentation = null;
        }

        public void ApplyAttachments()
        {
            for (var index = attachments.Count - 1; index >= 0; index--)
            {
                var attachment = attachments[index];
                if (attachment == null)
                {
                    attachments.RemoveAt(index);
                    continue;
                }
                attachment.ApplyFinalPose();
            }
        }
    }
}
