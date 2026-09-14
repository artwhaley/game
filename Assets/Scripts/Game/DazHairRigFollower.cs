using System;
using System.Collections.Generic;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// Keeps a Daz hair mesh on the exact skeleton it was bound against, then
    /// mirrors Lara's animated Genesis body bones onto that private skeleton.
    /// Product-specific hair bones retain their original hierarchy and rest pose.
    /// </summary>
    [Obsolete("Use RiggedAttachment's preserved-skeleton mode for prepared hair assets.")]
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class DazHairRigFollower : MonoBehaviour
    {
        [SerializeField] private Transform[] proxyBodyBones = Array.Empty<Transform>();
        [SerializeField] private string[] targetBoneNames = Array.Empty<string>();
        [SerializeField] private Transform targetActorRoot;

        private Transform[] targetBodyBones = Array.Empty<Transform>();

        public void Configure(Transform[] sourceProxyBodyBones, string[] sourceTargetBoneNames)
        {
            proxyBodyBones = sourceProxyBodyBones ?? Array.Empty<Transform>();
            targetBoneNames = sourceTargetBoneNames ?? Array.Empty<string>();
        }

        public void BindTo(Transform actorRoot)
        {
            if (actorRoot == null) throw new ArgumentNullException(nameof(actorRoot));
            targetActorRoot = actorRoot;
            ResolveTargetBones();
        }

        private void Awake()
        {
            // BindTo runs while the editor builds the scene, but the resolved
            // runtime array is intentionally not serialized. Resolve it again
            // after the Play-mode domain reload. The attachment is a direct
            // child of Lara, so the parent is also a safe fallback if the
            // cross-prefab scene reference was not recorded as an override.
            if (targetActorRoot == null) targetActorRoot = transform.parent;
            if (targetActorRoot != null) ResolveTargetBones();
        }

        private void ResolveTargetBones()
        {
            if (proxyBodyBones.Length != targetBoneNames.Length)
                throw new InvalidOperationException("Hair proxy bone list does not match its target-name list.");

            var targetsByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var candidate in targetActorRoot.GetComponentsInChildren<Transform>(true))
            {
                // The proxy is parented below actorRoot in the finished scene.
                // Never let duplicate Genesis names inside that proxy resolve as
                // their own targets; that would leave the source skeleton in its
                // bind pose at ground level.
                if (candidate == transform || candidate.IsChildOf(transform)) continue;
                if (!targetsByName.ContainsKey(candidate.name)) targetsByName.Add(candidate.name, candidate);
            }

            targetBodyBones = new Transform[targetBoneNames.Length];
            for (var index = 0; index < targetBoneNames.Length; index++)
            {
                if (proxyBodyBones[index] == null)
                    throw new InvalidOperationException("Hair proxy contains a missing source body bone.");
                if (!targetsByName.TryGetValue(targetBoneNames[index], out targetBodyBones[index]))
                    throw new InvalidOperationException("Lara is missing body bone '" + targetBoneNames[index] + "'.");
            }
            SynchronizeBodyBones();
        }

        private void LateUpdate()
        {
            SynchronizeBodyBones();
        }

        private void SynchronizeBodyBones()
        {
            if (targetBodyBones.Length != proxyBodyBones.Length) return;
            for (var index = 0; index < proxyBodyBones.Length; index++)
            {
                proxyBodyBones[index].position = targetBodyBones[index].position;
                proxyBodyBones[index].rotation = targetBodyBones[index].rotation;
            }
        }
    }
}
