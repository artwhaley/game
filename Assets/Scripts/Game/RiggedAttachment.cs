using System;
using System.Collections.Generic;
using UnityEngine;

namespace TruthCardGame
{
    public enum RiggedAttachmentMode
    {
        SharedSkeleton,
        PreservedSkeleton
    }

    [Serializable]
    public sealed class RiggedRendererBinding
    {
        [SerializeField] private SkinnedMeshRenderer renderer;
        [SerializeField] private string[] boneNames = Array.Empty<string>();
        [SerializeField] private string rootBoneName;

        public SkinnedMeshRenderer Renderer => renderer;
        public string[] BoneNames => boneNames;
        public string RootBoneName => rootBoneName;

        public RiggedRendererBinding(SkinnedMeshRenderer sourceRenderer, string[] sourceBoneNames, string sourceRootBoneName)
        {
            renderer = sourceRenderer;
            boneNames = sourceBoneNames ?? Array.Empty<string>();
            rootBoneName = sourceRootBoneName;
        }

        public RiggedRendererBinding() { }
    }

    /// <summary>
    /// Reusable attachment contract for prepared hair, clothing and props.
    /// Shared-skeleton items bind their renderers to CharacterRig. Items with
    /// product-specific bones retain that private hierarchy and mirror only
    /// their explicitly prepared body-bone mapping.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class RiggedAttachment : MonoBehaviour
    {
        [SerializeField] private RiggedAttachmentMode mode = RiggedAttachmentMode.SharedSkeleton;
        [SerializeField] private string supportedRigFamilyId = "Genesis8Female";
        [SerializeField] private RiggedRendererBinding[] bindings = Array.Empty<RiggedRendererBinding>();
        [SerializeField] private Transform[] preservedBodyBones = Array.Empty<Transform>();
        [SerializeField] private string[] preservedBodyBoneNames = Array.Empty<string>();
        [SerializeField] private Transform preservedRootBone;
        [SerializeField] private bool hideUntilBound = true;

        private CharacterRig owner;
        private Transform[] resolvedBodyBones = Array.Empty<Transform>();
        private bool[] rendererEnabledBeforeBind = Array.Empty<bool>();
        private Transform[][] rendererBonesBeforeBind = Array.Empty<Transform[]>();
        private Transform[] rendererRootsBeforeBind = Array.Empty<Transform>();
        private bool bound;

        public RiggedAttachmentMode Mode => mode;
        public string SupportedRigFamilyId => supportedRigFamilyId;
        public bool IsBound => bound;
        public CharacterRig Owner => owner;

        public void ConfigureShared(
            SkinnedMeshRenderer renderer,
            string[] boneNames,
            string rootBoneName,
            string sourceRigFamilyId = "Genesis8Female")
        {
            mode = RiggedAttachmentMode.SharedSkeleton;
            supportedRigFamilyId = string.IsNullOrWhiteSpace(sourceRigFamilyId) ? "Genesis8Female" : sourceRigFamilyId;
            bindings = new[] { new RiggedRendererBinding(renderer, boneNames, rootBoneName) };
            preservedBodyBones = Array.Empty<Transform>();
            preservedBodyBoneNames = Array.Empty<string>();
            preservedRootBone = null;
        }

        public void ConfigurePreserved(
            SkinnedMeshRenderer renderer,
            Transform[] sourceBodyBones,
            string[] sourceBodyBoneNames,
            Transform sourceRootBone,
            string sourceRigFamilyId = "Genesis8Female")
        {
            mode = RiggedAttachmentMode.PreservedSkeleton;
            supportedRigFamilyId = string.IsNullOrWhiteSpace(sourceRigFamilyId) ? "Genesis8Female" : sourceRigFamilyId;
            bindings = new[]
            {
                new RiggedRendererBinding(
                    renderer,
                    Array.ConvertAll(renderer.bones, bone => bone == null ? string.Empty : bone.name),
                    renderer.rootBone == null ? null : renderer.rootBone.name)
            };
            preservedBodyBones = sourceBodyBones ?? Array.Empty<Transform>();
            preservedBodyBoneNames = sourceBodyBoneNames ?? Array.Empty<string>();
            preservedRootBone = sourceRootBone;
        }

        private void Awake()
        {
            TryBindToNearestRig();
        }

        private void OnEnable()
        {
            TryBindToNearestRig();
        }

        private void OnTransformParentChanged()
        {
            if (!isActiveAndEnabled) return;
            Unbind();
            TryBindToNearestRig();
        }

        private void OnDisable()
        {
            Unbind();
        }

        private void TryBindToNearestRig()
        {
            var nextOwner = GetComponentInParent<CharacterRig>();
            if (nextOwner == null)
            {
                SetRenderersEnabled(!hideUntilBound);
                return;
            }
            if (bound && owner == nextOwner) return;
            Unbind();
            owner = nextOwner;
            try
            {
                owner.EnsureInitialized();
                if (!string.IsNullOrWhiteSpace(supportedRigFamilyId) &&
                    !string.Equals(supportedRigFamilyId, owner.RigFamilyId, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Attachment supports rig family '" + supportedRigFamilyId + "' but found '" + owner.RigFamilyId + "'.");
                Bind();
                owner.Register(this);
                bound = true;
            }
            catch (Exception exception)
            {
                RestoreRendererBindings();
                ClearBindingSnapshots();
                owner = null;
                bound = false;
                SetRenderersEnabled(false);
                Debug.LogError("[RIGGED-ATTACHMENT] " + name + " failed to bind: " + exception.Message, this);
                // A bad optional attachment must remain visible as a named
                // diagnostic without taking down the character or authored
                // scene. The renderer stays hidden until the asset is repaired
                // and the component is rebound.
            }
        }

        private void Bind()
        {
            if (bindings == null || bindings.Length == 0)
                throw new InvalidOperationException("Attachment has no renderer bindings.");
            rendererEnabledBeforeBind = new bool[bindings.Length];
            rendererBonesBeforeBind = new Transform[bindings.Length][];
            rendererRootsBeforeBind = new Transform[bindings.Length];
            for (var bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                var binding = bindings[bindingIndex];
                if (binding == null || binding.Renderer == null)
                    throw new InvalidOperationException("Attachment has a missing renderer binding.");
                if (binding.BoneNames == null)
                    throw new InvalidOperationException(binding.Renderer.name + " has no prepared bone-name list.");
                var renderer = binding.Renderer;
                rendererEnabledBeforeBind[bindingIndex] = renderer.enabled;
                rendererBonesBeforeBind[bindingIndex] = renderer.bones;
                rendererRootsBeforeBind[bindingIndex] = renderer.rootBone;
                if (renderer.sharedMesh == null)
                    throw new InvalidOperationException(renderer.name + " has no mesh.");
                if (binding.BoneNames.Length != renderer.sharedMesh.bindposeCount)
                    throw new InvalidOperationException(renderer.name + " has " + binding.BoneNames.Length +
                                                        " prepared bones for " + renderer.sharedMesh.bindposeCount + " bind poses.");

                if (mode == RiggedAttachmentMode.SharedSkeleton)
                {
                    if (string.IsNullOrWhiteSpace(binding.RootBoneName))
                        throw new InvalidOperationException(renderer.name + " has no prepared root bone name.");
                    var bones = new Transform[binding.BoneNames.Length];
                    for (var boneIndex = 0; boneIndex < bones.Length; boneIndex++)
                        bones[boneIndex] = owner.RequireBone(binding.BoneNames[boneIndex]);
                    renderer.bones = bones;
                    renderer.rootBone = owner.RequireBone(binding.RootBoneName);
                }
                renderer.updateWhenOffscreen = true;
            }

            if (mode == RiggedAttachmentMode.PreservedSkeleton)
            {
                if (preservedBodyBones.Length == 0 || preservedBodyBones.Length != preservedBodyBoneNames.Length)
                    throw new InvalidOperationException("Preserved attachment body mapping is empty or inconsistent.");
                if (preservedRootBone == null) throw new InvalidOperationException("Preserved attachment has no source root bone.");
                resolvedBodyBones = new Transform[preservedBodyBoneNames.Length];
                for (var index = 0; index < resolvedBodyBones.Length; index++)
                    resolvedBodyBones[index] = owner.RequireBone(preservedBodyBoneNames[index]);
            }
            SetRenderersEnabled(true);
            ClearBindingSnapshots();
        }

        internal void ApplyFinalPose()
        {
            if (!bound || mode != RiggedAttachmentMode.PreservedSkeleton) return;
            // The editor preparation orders these ancestors before their
            // descendants. Copy world transforms only after the character's
            // final face/gaze pose has been applied.
            for (var index = 0; index < preservedBodyBones.Length; index++)
            {
                preservedBodyBones[index].position = resolvedBodyBones[index].position;
                preservedBodyBones[index].rotation = resolvedBodyBones[index].rotation;
            }
        }

        private void Unbind()
        {
            if (owner != null) owner.Unregister(this);
            owner = null;
            resolvedBodyBones = Array.Empty<Transform>();
            bound = false;
            if (bindings != null && rendererEnabledBeforeBind.Length == bindings.Length)
                for (var index = 0; index < bindings.Length; index++)
                    if (bindings[index] != null && bindings[index].Renderer != null)
                        bindings[index].Renderer.enabled = rendererEnabledBeforeBind[index];
            ClearBindingSnapshots();
        }

        private void RestoreRendererBindings()
        {
            if (bindings == null || rendererBonesBeforeBind.Length != bindings.Length) return;
            for (var index = 0; index < bindings.Length; index++)
            {
                var renderer = bindings[index] == null ? null : bindings[index].Renderer;
                if (renderer == null) continue;
                if (rendererBonesBeforeBind[index] != null) renderer.bones = rendererBonesBeforeBind[index];
                if (rendererRootsBeforeBind.Length == bindings.Length) renderer.rootBone = rendererRootsBeforeBind[index];
            }
        }

        private void ClearBindingSnapshots()
        {
            rendererBonesBeforeBind = Array.Empty<Transform[]>();
            rendererRootsBeforeBind = Array.Empty<Transform>();
            rendererEnabledBeforeBind = Array.Empty<bool>();
        }

        private void SetRenderersEnabled(bool enabled)
        {
            if (bindings == null) return;
            foreach (var binding in bindings)
                if (binding != null && binding.Renderer != null) binding.Renderer.enabled = enabled;
        }
    }
}
