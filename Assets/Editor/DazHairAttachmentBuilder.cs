using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TruthCardGame;
using UnityEditor;
using UnityEngine;

namespace TruthCardGame.EditorTools
{
    /// <summary>Extracts one selected hair renderer from a bridge export without retaining its duplicate character tree.</summary>
    public static class DazHairAttachmentBuilder
    {
        public const string SourcePrefabPath = "Assets/Daz3D/202102genesis8hair/Prefabs/202102genesis8hair_Prefab.prefab";
        public const string HairRendererName = "2021-02Hair_189597.Shape";
        public const string AttachmentPrefabPath = "Assets/Characters/LaraAttachments/2021-02Hair_189597.prefab";
        public const string InvisibleCapMaterialPath = "Assets/Characters/LaraAttachments/InvisibleHairCap.mat";

        [MenuItem("TruthCardGame/Phase 00/Rebuild 2021-02 Hair Attachment (destructive)")]
        public static void Extract202102Hair()
        {
            if (!EditorUtility.DisplayDialog(
                    "Rebuild 2021-02 Hair Attachment",
                    "This replaces the reusable hair prefab from the bridge export. Authored scene instances keep their prefab links, but local instance overrides may need review. Continue?",
                    "Rebuild", "Cancel"))
                return;
            Extract202102HairInternal();
        }

        private static void Extract202102HairInternal()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (source == null) throw new InvalidOperationException("Missing bridge prefab: " + SourcePrefabPath);
            var sourceInstance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (sourceInstance == null) throw new InvalidOperationException("Could not instantiate " + SourcePrefabPath);
            try
            {
                // The bridge prefab is only an import source. Break the
                // prefab relationship before pruning it so the prepared hair
                // asset cannot silently pull the full bridge tree back into
                // authored scenes on a later source reimport.
                PrefabUtility.UnpackPrefabInstance(
                    sourceInstance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                var hair = sourceInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .SingleOrDefault(candidate => candidate.name == HairRendererName);
                if (hair == null || hair.sharedMesh == null)
                    throw new InvalidOperationException("Could not find a valid renderer named " + HairRendererName);

                // Keep the exact FBX skeleton hierarchy used to calculate this
                // mesh's bind poses. Remove every other visible export object.
                foreach (var otherRenderer in sourceInstance.GetComponentsInChildren<Renderer>(true)
                             .Where(candidate => candidate != hair).ToArray())
                    UnityEngine.Object.DestroyImmediate(otherRenderer.gameObject);
                foreach (var animator in sourceInstance.GetComponentsInChildren<Animator>(true))
                    UnityEngine.Object.DestroyImmediate(animator);

                var invisibleCap = EnsureInvisibleCapMaterial();
                hair.sharedMaterials = hair.sharedMaterials
                    .Select(material => material != null && material.name == "Cap_2" ? invisibleCap : material)
                    .ToArray();
                hair.updateWhenOffscreen = true;
                if (hair.bones.Length == 0 || hair.bones.Length != hair.sharedMesh.bindposeCount)
                    throw new InvalidOperationException(
                        $"The source hair's bone list ({hair.bones.Length}) does not match its bind poses ({hair.sharedMesh.bindposeCount}).");

                var proxyBones = hair.bones
                    .Where(bone => bone != null && IsBodyBoneName(bone.name))
                    .Distinct()
                    .OrderBy(GetHierarchyDepth)
                    .ToArray();
                if (proxyBones.Length == 0)
                    throw new InvalidOperationException("The selected hair has no Genesis body bones to follow.");
                var binding = sourceInstance.AddComponent<RiggedAttachment>();
                binding.ConfigurePreserved(
                    hair,
                    proxyBones,
                    proxyBones.Select(bone => bone.name).ToArray(),
                    hair.rootBone,
                    "Genesis8Female");

                sourceInstance.name = "2021-02 Hair Attachment";
                sourceInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                sourceInstance.transform.localScale = Vector3.one;
                Directory.CreateDirectory(Path.GetDirectoryName(AttachmentPrefabPath) ?? throw new InvalidOperationException("No prefab directory."));
                PrefabUtility.SaveAsPrefabAsset(sourceInstance, AttachmentPrefabPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                // Reload the serialized result. This catches the exact class of
                // broken attachment that previously reached the scene: a valid
                // mesh saved with an empty or disconnected bone array.
                var saved = AssetDatabase.LoadAssetAtPath<GameObject>(AttachmentPrefabPath);
                var savedHair = saved == null
                    ? null
                    : saved.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .SingleOrDefault(candidate => candidate.name == HairRendererName);
                if (savedHair == null || savedHair.sharedMesh == null || savedHair.rootBone == null || savedHair.bones.Length == 0 ||
                    savedHair.bones.Length != savedHair.sharedMesh.bindposeCount ||
                    saved.GetComponent<RiggedAttachment>() == null)
                    throw new InvalidOperationException(
                        "The serialized hair attachment did not preserve its complete source bind skeleton.");
                Debug.Log("[PHASE00-ATTACHMENT] Extracted " + HairRendererName +
                          " with its original bind skeleton to " + AttachmentPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceInstance);
            }
        }

        /// <summary>
        /// Keeps the reusable preparation command idempotent. An explicit
        /// Extract menu click is the destructive rebuild; ordinary foundation
        /// preparation only creates the asset when it is absent or still on
        /// the retired follower component.
        /// </summary>
        public static void Ensure202102Hair()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AttachmentPrefabPath);
            if (prefab == null || prefab.GetComponent<RiggedAttachment>() == null ||
                PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Variant)
                Extract202102HairInternal();
        }

        private static readonly HashSet<string> BodyBoneNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "hip", "pelvis", "abdomenLower", "abdomenUpper", "chestLower", "chestUpper",
            "neckLower", "neckUpper", "head", "lCollar", "rCollar", "lShldrBend",
            "rShldrBend", "lShldrTwist", "rShldrTwist", "lForearmBend", "rForearmBend",
            "lForearmTwist", "rForearmTwist", "lHand", "rHand", "lThighBend", "rThighBend",
            "lThighTwist", "rThighTwist", "lShin", "rShin", "lFoot", "rFoot",
            "lMetatarsals", "rMetatarsals", "lToe", "rToe", "lEye", "rEye"
        };

        private static bool IsBodyBoneName(string name) => BodyBoneNames.Contains(name);

        private static int GetHierarchyDepth(Transform bone)
        {
            var depth = 0;
            for (var current = bone; current != null; current = current.parent) depth++;
            return depth;
        }

        private static Material EnsureInvisibleCapMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(InvisibleCapMaterialPath);
            if (material == null)
            {
                var shader = Shader.Find("HDRP/Lit");
                if (shader == null) throw new InvalidOperationException("HDRP/Lit is unavailable for the invisible hair cap material.");
                material = new Material(shader) { name = "Invisible Hair Cap" };
                AssetDatabase.CreateAsset(material, InvisibleCapMaterialPath);
            }

            material.SetFloat("_SurfaceType", 1f);
            material.SetFloat("_BlendMode", 0f);
            material.SetFloat("_AlphaCutoffEnable", 0f);
            material.SetFloat("_TransparentZWrite", 0f);
            material.SetColor("_BaseColor", Color.clear);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        public static void ExtractAttachment(
            string sourcePrefabPath,
            string rendererName,
            string attachmentPrefabPath,
            string attachmentName,
            bool rigidHeadFollow,
            bool removeScalpCapMaterial)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);
            if (source == null) throw new InvalidOperationException("Missing bridge prefab: " + sourcePrefabPath);

            var sourceInstance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            if (sourceInstance == null) throw new InvalidOperationException("Could not instantiate " + sourcePrefabPath);
            try
            {
                var renderer = sourceInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .SingleOrDefault(candidate => candidate.name == rendererName);
                if (renderer == null) throw new InvalidOperationException("Could not find renderer " + rendererName);
                if (renderer.sharedMesh == null) throw new InvalidOperationException(rendererName + " has no mesh.");

                var attachment = new GameObject(attachmentName);
                attachment.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                attachment.transform.localScale = renderer.transform.lossyScale;
                var extracted = attachment.AddComponent<SkinnedMeshRenderer>();
                extracted.sharedMesh = renderer.sharedMesh;
                extracted.sharedMaterials = renderer.sharedMaterials;
                extracted.quality = renderer.quality;
                extracted.skinnedMotionVectors = renderer.skinnedMotionVectors;
                extracted.updateWhenOffscreen = true;

                var sourceBones = renderer.bones;
                if (sourceBones.Any(bone => bone == null))
                    throw new InvalidOperationException(rendererName + " contains a missing source bone.");
                var binding = attachment.AddComponent<RiggedAttachment>();
                binding.ConfigureShared(
                    extracted,
                    sourceBones.Select(bone => bone.name).ToArray(),
                    renderer.rootBone == null ? null : renderer.rootBone.name,
                    "Genesis8Female");

                // Cap_2 is a fitted scalp shell for the original character.
                // Lara already has her own forehead/scalp, so rendering it here
                // produces the observed z-fighting shell.
                if (removeScalpCapMaterial)
                    extracted.sharedMaterials = renderer.sharedMaterials
                        .Select(material => material != null && material.name == "Cap_2" ? EnsureInvisibleCapMaterial() : material)
                        .ToArray();

                Directory.CreateDirectory(Path.GetDirectoryName(attachmentPrefabPath) ?? throw new InvalidOperationException("No prefab directory."));
                PrefabUtility.SaveAsPrefabAsset(attachment, attachmentPrefabPath);
                UnityEngine.Object.DestroyImmediate(attachment);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[PHASE00-ATTACHMENT] Extracted " + rendererName + " to " + attachmentPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceInstance);
            }
        }
    }
}
