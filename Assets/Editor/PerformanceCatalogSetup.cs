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
        /// The Performance Tag *titles* the V1 fixture intends each ingredient to
        /// express. Unity stores no tag IDs here: the vocabulary (and its opaque
        /// stable IDs) is WPF/SQLite-owned, so the fixture looks these titles up
        /// in the content database through the same read-only bridge the picker
        /// uses and stores the real stable IDs it finds. A title is only ever a
        /// lookup key during fixture authoring; nothing at runtime joins on it,
        /// and a rename in WPF changes the title without changing identity.
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
            // preserve the author's edits. Rebuild replaces content deliberately.
            if (registry.Ingredients.Count == 0 && registry.Anchors.Count == 0 && registry.Operations.Count == 0)
            {
                ApplyFixtureBody(registry);
            }
            else
            {
                Debug.Log("[PERFORMANCE] Registry at " + RegistryPath +
                          " already has content, so it was left alone. Use " +
                          "TruthCardGame/Performance/Rebuild V1 Performance Fixture to replace it.");
            }

            Debug.Log("[PERFORMANCE] Registry ready at " + RegistryPath +
                      (created ? " (created)." : " (existing)."));
            ValidateRegistryInternal(registry);
        }

        /// <summary>
        /// Development fixture only: replaces the registry contents outright.
        /// Kept apart from the idempotent Ensure so that discarding authored
        /// content is always a deliberate act.
        /// </summary>
        [MenuItem("TruthCardGame/Performance/Rebuild V1 Performance Fixture (replaces contents)")]
        public static void RebuildV1Fixture()
        {
            Directory.CreateDirectory(RegistryFolder);
            var registry = AssetDatabase.LoadAssetAtPath<PerformanceRegistry>(RegistryPath);
            if (registry == null)
            {
                EnsureV1Fixture();
                return;
            }

            ApplyFixtureBody(registry);
            Debug.Log("[PERFORMANCE] Fixture rebuilt, replacing the previous contents at " + RegistryPath + ".");
            ValidateRegistryInternal(registry);
        }

        /// <summary>
        /// Installs the V1 fixture body, resolving Performance Tag membership from
        /// the content database so the registry stores the real WPF-owned stable
        /// IDs and copies none of them. Called by Ensure (empty registry only) and
        /// by Rebuild (always).
        /// </summary>
        private static void ApplyFixtureBody(PerformanceRegistry registry)
        {
            var clips = LoadQuaterniusClips();
                var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(Phase00RigSetup.MaskPath);

                var standing = new[] { PresentationPostures.Standing };
                var sitting = new[] { PresentationPostures.Sitting };
                var bothPostures = new[] { PresentationPostures.Standing, PresentationPostures.Sitting };

                // Ticket 01 / Ticket 03 alignment: Unity owns anchor and
                // operation identity, but NOT the Performance Tag vocabulary.
                // Membership is therefore resolved from the content database
                // through the same read-only bridge the picker uses, so the
                // fixture stores the real WPF-owned stable IDs and still copies
                // none. An ingredient whose tags cannot all be resolved stays
                // disabled and is reported, because an enabled body/face
                // ingredient with no tag can never be selected. Foundation
                // ingredients express no tag by definition and stay enabled.
                var vocabulary = ResolveTagVocabulary();

                var entries = new List<PerformanceIngredientEntry>
                {
                    Foundation("V1 Standing Idle", clips, "Idle_Loop", standing),
                    Foundation("V1 Sitting Idle", clips, "Sitting_Idle_Loop", sitting),
                    Body("V1 Talking Idle", clips, "Idle_Talking_Loop", bothPostures, vocabulary,
                        enableWhenResolved: true,
                        SuggestedTagTitles.Playful, SuggestedTagTitles.Tease),
                    Body("V1 Interact", clips, "Interact", standing, vocabulary,
                        enableWhenResolved: true,
                        SuggestedTagTitles.Tease),
                    Body("V1 Dance (later variety)", clips, "Dance_Loop", standing, vocabulary,
                        enableWhenResolved: false,
                        SuggestedTagTitles.Playful),
                    Face("V1 Smile", "ST Mika 8 Natural Smile", bothPostures, vocabulary,
                        enableWhenResolved: true,
                        SuggestedTagTitles.Playful, SuggestedTagTitles.Tease),
                    Face("V1 Frown", "eCTRLFrown_HD", bothPostures, vocabulary,
                        enableWhenResolved: true,
                        SuggestedTagTitles.Stern),
                };

                var anchorEntries = new List<PerformanceAnchorEntry>
                {
                    Anchor("anchor-room-center", "Room Center", "center", standing, "anchor-chair"),
                    Anchor("anchor-chair", "Chair", "chair", bothPostures, "anchor-room-center"),
                };

                // Reusable operations carry their Unity transition clip here. The
                // host refuses a composed path whose operations are unbound, so a
                // fixture without these cannot stage anyone anywhere.
                var operationEntries = new List<PerformanceOperationEntry>
                {
                    Operation("op-stand", "Stand", PresentationOperationKinds.Stand, clips, "Sitting_Exit"),
                    Operation("op-sit", "Sit", PresentationOperationKinds.Sit, clips, "Sitting_Enter"),
                    Operation("op-move-standing", "Move While Standing",
                        PresentationOperationKinds.MoveWhileStanding, clips, "Walk_Loop"),
                };

                registry.ReplaceContents(new PerformanceRegistryView(
                    mask, entries, anchorEntries, operationEntries));
                EditorUtility.SetDirty(registry);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

            ReportVocabulary(vocabulary);
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
        /// Title-to-stable-ID lookup built from the content database. Titles are
        /// matched case-insensitively and only for unretired tags, since a
        /// retired tag must not be handed to new content.
        /// </summary>
        private sealed class TagVocabulary
        {
            private readonly Dictionary<string, string> _idsByTitle =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public readonly List<string> UnresolvedTitles = new List<string>();

            public TagVocabulary(IEnumerable<PerformanceTagInfo> tags)
            {
                foreach (var tag in tags ?? new List<PerformanceTagInfo>())
                {
                    if (tag == null || tag.IsRetired) continue;
                    if (string.IsNullOrEmpty(tag.Title) || string.IsNullOrEmpty(tag.Id)) continue;
                    if (!_idsByTitle.ContainsKey(tag.Title)) _idsByTitle.Add(tag.Title, tag.Id);
                }
            }

            public int TagCount => _idsByTitle.Count;

            /// <summary>
            /// Stable IDs for every requested title, or an empty list when any is
            /// missing: a partial membership would silently change what the
            /// ingredient means, so it is reported instead of stored.
            /// </summary>
            public List<string> ResolveAll(string[] titles)
            {
                var ids = new List<string>();
                var complete = true;
                foreach (var title in titles ?? Array.Empty<string>())
                {
                    if (_idsByTitle.TryGetValue(title, out var id))
                    {
                        ids.Add(id);
                        continue;
                    }
                    complete = false;
                    if (!UnresolvedTitles.Contains(title)) UnresolvedTitles.Add(title);
                }
                if (!complete) ids.Clear();
                return ids;
            }
        }

        private static TagVocabulary ResolveTagVocabulary()
        {
            return new TagVocabulary(
                PerformanceTagCatalogBridge.ReadTags(PerformanceTagCatalogBridge.CanonicalDatabasePath));
        }

        private static void ReportVocabulary(TagVocabulary vocabulary)
        {
            if (vocabulary.UnresolvedTitles.Count == 0)
            {
                Debug.Log("[PERFORMANCE] Fixture resolved every Performance Tag against the content database (" +
                          vocabulary.TagCount + " tag(s) available).");
                return;
            }

            Debug.LogWarning(
                "[PERFORMANCE] The content database has no unretired Performance Tag named: " +
                string.Join(", ", vocabulary.UnresolvedTitles) +
                ". Those ingredients were left disabled. Author the tags in the WPF Workbench, then re-run " +
                "TruthCardGame/Performance/Ensure V1 Performance Fixture.");
        }

        /// <summary>
        /// Expressive body ingredient. Its membership comes from the resolved
        /// vocabulary; it is enabled only when its whole intended tag set exists,
        /// since an enabled body/face ingredient with no tag is invalid.
        /// </summary>
        private static PerformanceIngredientEntry Body(
            string name, Dictionary<string, AnimationClip> clips, string role, string[] postures,
            TagVocabulary vocabulary, bool enableWhenResolved, params string[] tagTitles)
        {
            var tags = vocabulary.ResolveAll(tagTitles);
            return Ingredient(name, PresentationIngredientKinds.Body, RequireClip(clips, role),
                postures, tags, enabled: enableWhenResolved && tags.Count > 0);
        }

        private static PerformanceIngredientEntry Face(
            string name, string controlName, string[] postures,
            TagVocabulary vocabulary, bool enableWhenResolved, params string[] tagTitles)
        {
            var tags = vocabulary.ResolveAll(tagTitles);
            return Ingredient(name, PresentationIngredientKinds.Face, null, postures,
                tags, enabled: enableWhenResolved && tags.Count > 0, controlName);
        }

        private static PerformanceIngredientEntry Ingredient(
            string name, string kind, AnimationClip clip, string[] postures, IEnumerable<string> tags,
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

        private static PerformanceOperationEntry Operation(
            string id, string name, string kind, Dictionary<string, AnimationClip> clips, string clipRole)
        {
            var entry = new PerformanceOperationEntry();
            entry.Configure(id, name, kind, 10, anchorIds: null, animationClip: RequireClip(clips, clipRole));
            return entry;
        }
    }
}
