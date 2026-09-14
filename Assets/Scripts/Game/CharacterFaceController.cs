using System;
using System.Collections.Generic;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>Owns the prepared facial renderers and explicit exported controls.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterFaceController : MonoBehaviour
    {
        [SerializeField] private CharacterRig rig;
        [SerializeField] private SkinnedMeshRenderer[] facialRenderers = Array.Empty<SkinnedMeshRenderer>();
        [SerializeField] private string jawBoneName = "lowerJaw";

        private readonly Dictionary<SkinnedMeshRenderer, float[]> neutralWeights =
            new Dictionary<SkinnedMeshRenderer, float[]>();
        private Transform jaw;
        private Vector3 neutralJawPosition;
        private Quaternion neutralJawRotation;
        private Vector3 neutralJawScale;
        private bool captured;

        public void Configure(CharacterRig sourceRig, SkinnedMeshRenderer[] sourceFacialRenderers, string sourceJawBoneName)
        {
            rig = sourceRig;
            facialRenderers = sourceFacialRenderers ?? Array.Empty<SkinnedMeshRenderer>();
            if (!string.IsNullOrWhiteSpace(sourceJawBoneName)) jawBoneName = sourceJawBoneName;
            captured = false;
        }

        private void Awake()
        {
            if (rig == null) rig = GetComponent<CharacterRig>();
            CaptureNeutralIfNeeded();
        }

        internal void ApplyFinalPose()
        {
            CaptureNeutralIfNeeded();
            if (jaw == null) return;
            jaw.localPosition = neutralJawPosition;
            jaw.localRotation = neutralJawRotation;
            jaw.localScale = neutralJawScale;
        }

        public void ResetOwnedChannels()
        {
            CaptureNeutralIfNeeded();
            foreach (var pair in neutralWeights)
            {
                if (pair.Key == null || pair.Key.sharedMesh == null) continue;
                for (var index = 0; index < pair.Value.Length; index++)
                    pair.Key.SetBlendShapeWeight(index, pair.Value[index]);
            }
        }

        public int SetPreset(string exactExportedControl, float weight)
        {
            CaptureNeutralIfNeeded();
            ResetOwnedChannels();
            if (string.IsNullOrWhiteSpace(exactExportedControl)) return 0;

            var matches = 0;
            foreach (var renderer in facialRenderers)
            {
                if (renderer == null || renderer.sharedMesh == null) continue;
                var mesh = renderer.sharedMesh;
                for (var index = 0; index < mesh.blendShapeCount; index++)
                {
                    var shapeName = mesh.GetBlendShapeName(index);
                    var separator = shapeName.LastIndexOf("__", StringComparison.Ordinal);
                    var controlName = separator < 0 ? shapeName : shapeName.Substring(separator + 2);
                    if (!string.Equals(controlName, exactExportedControl, StringComparison.OrdinalIgnoreCase)) continue;
                    renderer.SetBlendShapeWeight(index, weight);
                    matches++;
                }
            }
            return matches;
        }

        private void CaptureNeutralIfNeeded()
        {
            if (captured) return;
            if (rig == null) rig = GetComponent<CharacterRig>();
            if (facialRenderers == null || facialRenderers.Length == 0)
                facialRenderers = FindBodyRenderers();
            neutralWeights.Clear();
            foreach (var renderer in facialRenderers)
            {
                if (renderer == null || renderer.sharedMesh == null) continue;
                var weights = new float[renderer.sharedMesh.blendShapeCount];
                for (var index = 0; index < weights.Length; index++)
                    weights[index] = renderer.GetBlendShapeWeight(index);
                neutralWeights[renderer] = weights;
            }
            jaw = rig == null ? null : rig.TryGetBone(jawBoneName, out var resolvedJaw) ? resolvedJaw : null;
            if (jaw != null)
            {
                neutralJawPosition = jaw.localPosition;
                neutralJawRotation = jaw.localRotation;
                neutralJawScale = jaw.localScale;
            }
            captured = true;
        }

        private SkinnedMeshRenderer[] FindBodyRenderers()
        {
            if (rig == null || rig.BodySkeletonRoot == null) return Array.Empty<SkinnedMeshRenderer>();
            return rig.BodySkeletonRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }
    }
}
