using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TruthCardGame.Content;
using TruthCardGame.Performance;
using UnityEditor;
using UnityEngine;

namespace TruthCardGame.EditorTools
{
    /// <summary>
    /// Ticket 01 authoring commands for the Unity-owned presentation registry.
    /// The registry is the factual authority for ingredient compatibility,
    /// anchors and reusable operations; the generated catalog is its read-only
    /// projection for Core and WPF. Tag *membership* is authored here too, but
    /// the tag vocabulary and its stable IDs belong to WPF/SQLite, so this file
    /// never mints a tag ID of its own — see SuggestedTagTitles.
    ///
    /// Lifetime split (matching the Phase 00 conventions):
    ///  - <c>Ensure V1 Performance Fixture</c> is idempotent and may create/repair
    ///    the dev fixture registry asset;
    ///  - <c>Validate Presentation Registry</c> is read-only;
    ///  - <c>Generate Presentation Catalog</c> validates then writes the artifact
    ///    atomically outside Assets.
    /// </summary>
    public static class PerformanceCatalogSetup
    {
        public const string RegistryFolder = "Assets/Content/Performance";
        public const string RegistryPath = RegistryFolder + "/V1PerformanceRegistry.asset";
        public const string CatalogFileName = "PresentationCatalog.json";

        /// <summary>
        /// Suggested V1 Performance Tag *titles* to author in the WPF Workbench.
        /// Unity deliberately keeps no tag IDs here: the vocabulary (and its
        /// opaque stable IDs) is WPF/SQLite-owned, so ingredient membership is
        /// assigned with the Performance Tag Picker, which reads the real IDs
        /// from the content database. Titles are affordances for a human, never
        /// identifiers — nothing joins on them.
        /// </summary>
        public static class SuggestedTagTitles
        {
            public const string Playful = "Playful";
            public const string Tease = "Tease";
            public const string Stern = "Stern";
            public const string Comforting = "Comforting";
        }

        [MenuItem("TruthCardGame/Performance/Ensure V1 Performance Fixture")]
        public static void EnsureV1Fixture()
        {
            Directory.CreateDirectory(RegistryFolder);
            var registry = AssetDatabase.LoadAssetAtPath<PerformanceRegistry>(RegistryPath);
            var created = registry == null;
            if (created)
            {
                registry = ScriptableObject.CreateInstance<PerformanceRegistry>();
                AssetDatabase.CreateAsset(registry, RegistryPath);
            }

            // Only build the fixture body when the registry is empty; otherwise
            // preserve the author's edits.
            if (registry.Ingredients.Count == 0 && registry.Anchors.Count == 0 && registry.Operations.Count == 0)
            {
                var clips = LoadQuaterniusClips();
                var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(Phase00RigSetup.MaskPath);

                var standing = new[] { PresentationPostures.Standing };
                var sitting = new[] { PresentationPostures.Sitting };
                var bothPostures = new[] { PresentationPostures.Standing, PresentationPostures.Sitting };

                // Ticket 01 / Ticket 03 alignment: Unity owns anchor and
                // operation identity, but NOT the Performance Tag vocabulary.
                // The fixture therefore ships expressive ingredients untagged
                // and disabled rather than hard-coding invented tag IDs that
                // could never match the WPF-owned vocabulary. The author
                // authors the tags in the Workbench, assigns the real stable
                // IDs with the Performance Tag Picker, then enables each
                // ingredient. Foundation ingredients express no tag by
                // definition and stay enabled.
                var entries = new List<PerformanceIngredientEntry>
                {
                    Foundation("V1 Standing Idle", clips, "Idle_Loop", standing),
                    Foundation("V1 Sitting Idle", clips, "Sitting_Idle_Loop", sitting),
                    Body("V1 Talking Idle", clips, "Idle_Talking_Loop", bothPostures),
                    Body("V1 Interact", clips, "Interact", standing),
                    Body("V1 Dance (later variety)", clips, "Dance_Loop", standing),
                    Face("V1 Smile", "ST Mika 8 Natural Smile", bothPostures),
                    Face("V1 Frown", "eCTRLFrown_HD", bothPostures),
                };

                var anchorEntries = new List<PerformanceAnchorEntry>
                {
                    Anchor("anchor-room-center", "Room Center", "center", standing, "anchor-chair"),
                    Anchor("anchor-chair", "Chair", "chair", bothPostures, "anchor-room-center"),
                };

                var operationEntries = new List<PerformanceOperationEntry>
                {
                    Operation("op-stand", "Stand", PresentationOperationKinds.Stand),
                    Operation("op-sit", "Sit", PresentationOperationKinds.Sit),
                    Operation("op-move-standing", "Move While Standing", PresentationOperationKinds.MoveWhileStanding),
                };

                registry.ReplaceContents(new PerformanceRegistryView(
                    mask, entries, anchorEntries, operationEntries));
                EditorUtility.SetDirty(registry);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log(
                    "[PERFORMANCE] Fixture built with untagged, disabled expressive ingredients. " +
                    "Author Performance Tags (" + string.Join(", ", new[]
                    {
                        SuggestedTagTitles.Playful, SuggestedTagTitles.Tease,
                        SuggestedTagTitles.Stern, SuggestedTagTitles.Comforting,
                    }) + ") in the WPF Workbench, assign them to ingredients with " +
                    "TruthCardGame/Performance/Open Performance Tag Picker, then enable each ingredient.");
            }

            Debug.Log("[PERFORMANCE] Registry ready at " + RegistryPath +
                      (created ? " (created)." : " (existing)."));
            ValidateRegistryInternal(registry);
        }

        [MenuItem("TruthCardGame/Performance/Validate Presentation Registry")]
        public static void ValidateRegistry()
        {
            var registry = AssetDatabase.LoadAssetAtPath<PerformanceRegistry>(RegistryPath);
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "No performance registry at " + RegistryPath + ". Run Ensure V1 Performance Fixture first.");
            }
            ValidateRegistryInternal(registry);
        }

        [MenuItem("TruthCardGame/Performance/Generate Presentation Catalog")]
        public static void GenerateCatalog()
        {
            var registry = AssetDatabase.LoadAssetAtPath<PerformanceRegistry>(RegistryPath);
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "No performance registry at " + RegistryPath + ". Run Ensure V1 Performance Fixture first.");
            }

            var view = registry.BuildView();
            var catalog = PresentationCatalogBuilder.BuildValidated(view, DateTime.UtcNow);
            var json = PresentationCatalogBuilder.ToJson(catalog);
            var outputPath = ResolveCatalogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

            var tempPath = outputPath + ".tmp";
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            if (File.Exists(outputPath))
            {
                File.Replace(tempPath, outputPath, null);
            }
            else
            {
                File.Move(tempPath, outputPath);
            }

            var warnings = PresentationCatalogBuilder.Warnings(catalog);
            var report = new StringBuilder();
            report.AppendLine($"[PERFORMANCE] Presentation catalog written: {outputPath}");
            report.AppendLine($"  ingredients={catalog.Ingredients.Count} " +
                              $"anchors={catalog.Anchors.Count} operations={catalog.Operations.Count}");
            foreach (var warning in warnings) report.AppendLine("  WARNING: " + warning);
            Debug.Log(report.ToString());
        }

        /// <summary>Repo-relative catalog location, outside Assets so Unity never imports the artifact.</summary>
        public static string ResolveCatalogPath()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.Combine(projectRoot, "Content", CatalogFileName);
        }

        private static void ValidateRegistryInternal(PerformanceRegistry registry)
        {
            var view = registry.BuildView();
            var catalog = view.BuildCatalog(DateTime.UtcNow);
            PresentationCatalogValidator.Validate(catalog);
            var warnings = PresentationCatalogBuilder.Warnings(catalog);
            var report = new StringBuilder();
            report.AppendLine("[PERFORMANCE] Registry valid: " +
                              $"{catalog.Ingredients.Count} ingredients, {catalog.Anchors.Count} anchors, " +
                              $"{catalog.Operations.Count} operations.");
            foreach (var warning in warnings) report.AppendLine("  WARNING: " + warning);
            if (warnings.Count == 0) report.AppendLine("  No warnings.");

            // The tag vocabulary belongs to WPF/SQLite. Validation therefore
            // checks the registry's stable IDs against the content database
            // rather than against any copy kept in Unity.
            var tagProblems = PerformanceTagPickerWindow.CurrentProblems();
            foreach (var problem in tagProblems) report.AppendLine("  TAG: " + problem);
            if (tagProblems.Count == 0) report.AppendLine("  All Performance Tags resolve.");

            Debug.Log(report.ToString());
        }

        private static Dictionary<string, AnimationClip> LoadQuaterniusClips()
        {
            return AssetDatabase.LoadAllAssetsAtPath(Phase00RigSetup.SourcePath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                .GroupBy(c => Phase00RigSetup.CanonicalClipName(c.name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }

        private static AnimationClip RequireClip(Dictionary<string, AnimationClip> clips, string role)
        {
            if (!clips.TryGetValue(role, out var clip))
            {
                throw new InvalidOperationException(
                    $"Quaternius clip role '{role}' is missing from {Phase00RigSetup.SourcePath}.");
            }
            return clip;
        }

        private static PerformanceIngredientEntry Foundation(
            string name, Dictionary<string, AnimationClip> clips, string role, string[] postures)
        {
            return Ingredient(name, PresentationIngredientKinds.Foundation, RequireClip(clips, role),
                postures, Array.Empty<string>(), enabled: true);
        }

        /// <summary>
        /// Expressive body ingredients start untagged and disabled: an enabled
        /// body/face ingredient with no Performance Tag is invalid, so the
        /// fixture leaves enabling to the author who assigns real tag IDs.
        /// </summary>
        private static PerformanceIngredientEntry Body(
            string name, Dictionary<string, AnimationClip> clips, string role, string[] postures)
        {
            return Ingredient(name, PresentationIngredientKinds.Body, RequireClip(clips, role),
                postures, Array.Empty<string>(), enabled: false);
        }

        private static PerformanceIngredientEntry Face(
            string name, string controlName, string[] postures)
        {
            return Ingredient(name, PresentationIngredientKinds.Face, null, postures,
                Array.Empty<string>(), enabled: false, controlName);
        }

        private static PerformanceIngredientEntry Ingredient(
            string name, string kind, AnimationClip clip, string[] postures, string[] tags,
            bool enabled, string faceControlName = "")
        {
            var entry = new PerformanceIngredientEntry();
            entry.Configure("", name, kind, enabled, tags, postures, false, clip, faceControlName);
            return entry;
        }

        private static PerformanceAnchorEntry Anchor(
            string id, string name, string group, string[] postures, params string[] connections)
        {
            var entry = new PerformanceAnchorEntry();
            entry.Configure(id, name, group, postures, connections);
            return entry;
        }

        private static PerformanceOperationEntry Operation(string id, string name, string kind)
        {
            var entry = new PerformanceOperationEntry();
            entry.Configure(id, name, kind, 10);
            return entry;
        }
    }
}
