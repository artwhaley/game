using System;
using System.Collections.Generic;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// A standalone Daz hair renderer. The bridge exports hair with a duplicate
    /// Genesis skeleton; this component instead maps its skinning bones onto
    /// the already-instantiated character rig.
    /// </summary>
    [Obsolete("Use RiggedAttachment for prepared hair and clothing assets.")]
    [DisallowMultipleComponent]
    public sealed class DazHairAttachment : MonoBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer hairRenderer;
        [SerializeField] private string[] boneNames = Array.Empty<string>();
        [SerializeField] private string rootBoneName;
        [SerializeField] private Transform[] fallbackHairBones = Array.Empty<Transform>();
        [SerializeField] private Vector3[] fallbackLocalPositions = Array.Empty<Vector3>();
        [SerializeField] private Quaternion[] fallbackLocalRotations = Array.Empty<Quaternion>();
        [SerializeField] private Vector3[] fallbackLocalScales = Array.Empty<Vector3>();
        [SerializeField] private bool rigidHeadFollow;

        private static readonly HashSet<string> GenesisBodyBoneNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "hip", "pelvis", "abdomenLower", "abdomenUpper", "chestLower", "chestUpper",
            "neckLower", "neckUpper", "head", "lCollar", "rCollar", "lShldrBend",
            "rShldrBend", "lShldrTwist", "rShldrTwist", "lForearmBend", "rForearmBend",
            "lForearmTwist", "rForearmTwist", "lHand", "rHand", "lThighBend", "rThighBend",
            "lThighTwist", "rThighTwist", "lShin", "rShin", "lFoot", "rFoot",
            "lMetatarsals", "rMetatarsals", "lToe", "rToe", "lEye", "rEye"
        };

        public static bool IsGenesisBodyBone(string boneName) => GenesisBodyBoneNames.Contains(boneName);

        public void Configure(
            SkinnedMeshRenderer renderer,
            string[] sourceBoneNames,
            string sourceRootBoneName,
            Transform[] sourceFallbackHairBones,
            Vector3[] sourceFallbackLocalPositions,
            Quaternion[] sourceFallbackLocalRotations,
            Vector3[] sourceFallbackLocalScales,
            bool sourceRigidHeadFollow)
        {
            hairRenderer = renderer;
            boneNames = sourceBoneNames ?? Array.Empty<string>();
            rootBoneName = sourceRootBoneName;
            fallbackHairBones = sourceFallbackHairBones ?? Array.Empty<Transform>();
            fallbackLocalPositions = sourceFallbackLocalPositions ?? Array.Empty<Vector3>();
            fallbackLocalRotations = sourceFallbackLocalRotations ?? Array.Empty<Quaternion>();
            fallbackLocalScales = sourceFallbackLocalScales ?? Array.Empty<Vector3>();
            rigidHeadFollow = sourceRigidHeadFollow;
        }

        public void BindTo(Transform actorRoot)
        {
            if (actorRoot == null) throw new ArgumentNullException(nameof(actorRoot));
            if (hairRenderer == null) hairRenderer = GetComponent<SkinnedMeshRenderer>();
            if (hairRenderer == null) throw new InvalidOperationException("The hair attachment has no SkinnedMeshRenderer.");
            if (boneNames.Length != hairRenderer.sharedMesh.bindposeCount)
                throw new InvalidOperationException($"Hair bind-pose count ({hairRenderer.sharedMesh.bindposeCount}) does not match its stored bone list ({boneNames.Length}).");

            var byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var transform in actorRoot.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(transform.name)) byName.Add(transform.name, transform);

            if (fallbackHairBones.Length != boneNames.Length ||
                fallbackLocalPositions.Length != boneNames.Length ||
                fallbackLocalRotations.Length != boneNames.Length ||
                fallbackLocalScales.Length != boneNames.Length)
                throw new InvalidOperationException("Hair fallback-bone list does not match its stored bone list.");

            var head = byName.TryGetValue("head", out var targetHead) ? targetHead : actorRoot;
            var bones = new Transform[boneNames.Length];
            for (var index = 0; index < boneNames.Length; index++)
            {
                if (!rigidHeadFollow && byName.TryGetValue(boneNames[index], out bones[index])) continue;

                // A Daz hair mesh's bind poses were authored against its own
                // exported skeleton. Keep every such rest transform and parent
                // it to Lara's head for stable rigid-follow behavior.
                var fallback = fallbackHairBones[index];
                if (fallback == null)
                    throw new InvalidOperationException($"The attachment has no fallback for hair bone '{boneNames[index]}'.");
                // The source export is positioned at the world origin while
                // Lara is placed at her showcase spawn. Copying world space
                // made the bangs orbit against her head. Use the source
                // bone's rest pose relative to its head instead.
                fallback.SetParent(head, worldPositionStays: false);
                fallback.localPosition = fallbackLocalPositions[index];
                fallback.localRotation = fallbackLocalRotations[index];
                fallback.localScale = fallbackLocalScales[index];
                bones[index] = fallback;
            }

            hairRenderer.bones = bones;
            if (rigidHeadFollow)
                hairRenderer.rootBone = head;
            else if (!string.IsNullOrWhiteSpace(rootBoneName) && byName.TryGetValue(rootBoneName, out var rootBone))
                hairRenderer.rootBone = rootBone;
            hairRenderer.updateWhenOffscreen = true;
        }
    }
}
