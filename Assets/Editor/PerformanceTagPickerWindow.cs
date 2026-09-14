using System;
using System.Collections.Generic;
using System.Linq;
using TruthCardGame.Performance;
using UnityEditor;
using UnityEngine;

namespace TruthCardGame.EditorTools
{
    /// <summary>
    /// Assigns WPF-owned Performance Tags to Unity-owned ingredients, reading the
    /// vocabulary from the canonical content database.
    ///
    /// The point of the window is that membership is written as stable IDs. A tag
    /// renamed or retired in the Workbench is picked up by Refresh with no asset
    /// migration, and the window warns when an ingredient points at a tag the
    /// database no longer offers.
    /// </summary>
    public sealed class PerformanceTagPickerWindow : EditorWindow
    {
        private PerformanceRegistry _registry;
        private Vector2 _scroll;
        private string _status = "";
        private bool _statusIsError;

        [MenuItem("TruthCardGame/Performance/Open Performance Tag Picker")]
        public static void Open()
        {
            var window = GetWindow<PerformanceTagPickerWindow>("Performance Tags");
            window.minSize = new Vector2(460f, 360f);
            window.EnsureRegistry();
            window.RefreshVocabulary();
        }

        private void OnEnable()
        {
            EnsureRegistry();
            if (PerformanceTagCatalogBridge.CachedTags.Count == 0)
            {
                RefreshVocabulary();
            }
        }

        private void EnsureRegistry()
        {
            if (_registry != null) return;
            foreach (var guid in AssetDatabase.FindAssets("t:PerformanceRegistry"))
            {
                _registry = AssetDatabase.LoadAssetAtPath<PerformanceRegistry>(AssetDatabase.GUIDToAssetPath(guid));
                if (_registry != null) return;
            }
        }

        private void RefreshVocabulary()
        {
            _statusIsError = !PerformanceTagCatalogBridge.Refresh(out var message);
            _status = message;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Performance Tag vocabulary (WPF/SQLite-owned)", EditorStyles.boldLabel);
            _registry = (PerformanceRegistry)EditorGUILayout.ObjectField(
                "Registry", _registry, typeof(PerformanceRegistry), false);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh from content database", GUILayout.Width(240f))) RefreshVocabulary();
            EditorGUILayout.LabelField(
                PerformanceTagCatalogBridge.LoadedAtUtc == DateTime.MinValue
                    ? "not loaded"
                    : "loaded " + PerformanceTagCatalogBridge.LoadedAtUtc.ToLocalTime().ToString("HH:mm:ss"),
                GUILayout.Width(140f));
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, _statusIsError ? MessageType.Error : MessageType.Info);
            }

            if (_registry == null)
            {
                EditorGUILayout.HelpBox("No Performance Registry asset found.", MessageType.Warning);
                return;
            }

            var tags = PerformanceTagCatalogBridge.CachedTags;
            var problems = PerformanceTagCatalogBridge.ValidateRegistryTags(_registry, tags);
            if (problems.Count > 0)
            {
                EditorGUILayout.HelpBox(string.Join("\n", problems), MessageType.Warning);
            }

            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var ingredient in _registry.Ingredients)
            {
                if (ingredient == null) continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(
                    $"{ingredient.DisplayName}  ·  {ingredient.Kind}" +
                    (ingredient.Enabled ? "" : "  (disabled)"),
                    EditorStyles.boldLabel);

                if (tags.Count == 0)
                {
                    EditorGUILayout.LabelField(
                        "No Performance Tags in the content database yet. Create them in the Workbench Catalogs pane, save, then Refresh.",
                        EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.BeginHorizontal();
                    var used = 0;
                    foreach (var tag in tags)
                    {
                        var selected = ingredient.PerformanceTagIds.Contains(tag.Id);
                        var next = GUILayout.Toggle(selected, tag.DisplayName, EditorStyles.miniButton, GUILayout.Width(120f));
                        if (next != selected)
                        {
                            ingredient.TogglePerformanceTag(tag.Id);
                            EditorUtility.SetDirty(_registry);
                        }
                        if (next) used++;
                    }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField($"{used} selected", EditorStyles.miniLabel, GUILayout.Width(80f));
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            if (GUILayout.Button("Save registry", GUILayout.Width(140f)))
            {
                EditorUtility.SetDirty(_registry);
                AssetDatabase.SaveAssets();
                _status = "Registry saved.";
                _statusIsError = false;
            }
        }

        /// <summary>Read-only status used by the registry validation menu item.</summary>
        public static List<string> CurrentProblems()
        {
            PerformanceTagCatalogBridge.Refresh(out _);
            var registry = FindRegistry();
            if (registry == null) return new List<string>();
            return PerformanceTagCatalogBridge.ValidateRegistryTags(
                registry, PerformanceTagCatalogBridge.CachedTags);
        }

        private static PerformanceRegistry FindRegistry()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:PerformanceRegistry"))
            {
                var registry = AssetDatabase.LoadAssetAtPath<PerformanceRegistry>(AssetDatabase.GUIDToAssetPath(guid));
                if (registry != null) return registry;
            }
            return null;
        }
    }
}
