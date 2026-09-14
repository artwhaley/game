using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TruthCardGame.EditorTools
{
    /// <summary>Turns Lara's temporary export-time clothing into independent wardrobe attachments.</summary>
    public static class LaraWardrobeAttachmentBuilder
    {
        public const string BraPrefabPath = "Assets/Characters/LaraAttachments/Bra_20266.prefab";
        public const string PantiesPrefabPath = "Assets/Characters/LaraAttachments/Panties_8559.prefab";

        [MenuItem("TruthCardGame/Phase 00/Rebuild Lara Placeholder Wardrobe (destructive)")]
        public static void ExtractAndStripBasePrefab()
        {
            if (!EditorUtility.DisplayDialog(
                    "Rebuild Lara Placeholder Wardrobe",
                    "This replaces the bra and panties attachment prefabs and strips those export renderers from the Lara base prefab. Authored scene instances keep their prefab links, but local instance overrides may need review. Continue?",
                    "Rebuild", "Cancel"))
                return;
            ExtractAndStripBasePrefabInternal();
        }

        private static void ExtractAndStripBasePrefabInternal()
        {
            // Recover clothing from the original Lara FBX, not from the
            // already-stripped material prefab. This keeps the preparation
            // command repairable if an attachment asset is lost later.
            DazHairAttachmentBuilder.ExtractAttachment(
                Phase00RigSetup.LaraPath, "Bra_20266.Shape", BraPrefabPath,
                "Lara Bra Attachment", false, false);
            DazHairAttachmentBuilder.ExtractAttachment(
                Phase00RigSetup.LaraPath, "Panties_8559.Shape", PantiesPrefabPath,
                "Lara Panties Attachment", false, false);
            StripExportOnlyRenderers();
        }

        public static void EnsureExtractedAndBaseStripped()
        {
            var bra = AssetDatabase.LoadAssetAtPath<GameObject>(BraPrefabPath);
            var panties = AssetDatabase.LoadAssetAtPath<GameObject>(PantiesPrefabPath);
            if (bra == null || panties == null ||
                bra.GetComponent<RiggedAttachment>() == null || panties.GetComponent<RiggedAttachment>() == null)
                ExtractAndStripBasePrefabInternal();
            else
                StripExportOnlyRenderers();
        }

        private static void StripExportOnlyRenderers()
        {
            var root = PrefabUtility.LoadPrefabContents(Phase00RigSetup.LaraMaterialPrefabPath);
            try
            {
                var names = new[] { "Natty Hair_66066.Shape", "Bra_20266.Shape", "Panties_8559.Shape" };
                var exportRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(renderer => names.Contains(renderer.name)).ToArray();
                foreach (var renderer in exportRenderers)
                    UnityEngine.Object.DestroyImmediate(renderer.gameObject);
                if (exportRenderers.Length > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, Phase00RigSetup.LaraMaterialPrefabPath);
                    AssetDatabase.SaveAssets();
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
