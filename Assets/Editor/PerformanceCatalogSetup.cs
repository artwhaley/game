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
    /// The registry is the factual authority for ingredient membership,
    /// compatibility, anchors and reusable operations; the generated catalog is
    /// its read-only projection for Core and WPF.
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
        /// Shared V1 dev-fixture Performance Tag IDs. The real vocabulary is
        /// authored in WPF/SQLite (ticket 03); this fixture must use those exact
        /// IDs so ingredient membership and the authored event line up.
        /// </summary>
        public static class FixtureTags
        {
            public const string Playful = "perf-tag-playful";
            public const string Tease = "perf-tag-tease";
            public const string Stern = "perf-tag-stern";
            public const string Comforting = "perf-tag-comforting";
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

                var entries = new List<PerformanceIngredientEntry>
                {
                    Foundation("V1 Standing Idle", clips, "Idle_Loop", standing),
                    Foundation("V1 Sitting Idle", clips, "Sitting_Idle_Loop", sitting),
                    Body("V1 Talking Idle", clips, "Idle_Talking_Loop", bothPostures,
                        FixtureTags.Playful, FixtureTags.Tease),
                    Body("V1 Interact", clips, "Interact", standing, FixtureTags.Tease),
                    Body("V1 Dance (later variety)", clips, "Dance_Loop", standing,
                        FixtureTags.Playful, enabled: false),
                    Face("V1 Smile", "ST Mika 8 Natural Smile", bothPostures,
                        FixtureTags.Playful, FixtureTags.Tease),
                    Face("V1 Frown", "eCTRLFrown_HD", bothPostures, FixtureTags.Stern),
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

        private static PerformanceIngredientEntry Body(
            string name, Dictionary<string, AnimationClip> clips, string role, string[] postures,
            string tag, bool enabled = true)
        {
            return Ingredient(name, PresentationIngredientKinds.Body, RequireClip(clips, role),
                postures, new[] { tag }, enabled);
        }

        private static PerformanceIngredientEntry Body(
            string name, Dictionary<string, AnimationClip> clips, string role, string[] postures,
            string tagA, string tagB, bool enabled = true)
        {
            return Ingredient(name, PresentationIngredientKinds.Body, RequireClip(clips, role),
                postures, new[] { tagA, tagB }, enabled);
        }

        private static PerformanceIngredientEntry Face(
            string name, string controlName, string[] postures, params string[] tags)
        {
            return Ingredient(name, PresentationIngredientKinds.Face, null, postures, tags, true, controlName);
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
