using System;
using System.Collections.Generic;
using UnityEngine;
using TruthCardGame.Content;

namespace TruthCardGame.Performance
{
    /// <summary>
    /// One presentation ingredient as authored in the Unity registry. Normal
    /// intake touches few fields: the clip/binding, its semantic Performance
    /// Tags, the artist-confirmed posture(s), and Enabled. ID and technical
    /// clip data are generated/inferred, and the standard gesture mask comes
    /// from the registry preset. Unity-only bindings never enter the exported
    /// catalog.
    /// </summary>
    [Serializable]
    public sealed class PerformanceIngredientEntry
    {
        [Tooltip("Stable ingredient ID. Auto-generated when empty; duplication must mint a new one.")]
        [SerializeField] private string ingredientId = "";

        [Tooltip("Author-facing name. Defaults to the clip name when empty.")]
        [SerializeField] private string displayName = "";

        [Tooltip("foundation | body | face (see PresentationIngredientKinds).")]
        [SerializeField] private string kind = PresentationIngredientKinds.Body;

        [Tooltip("Off until explicitly inspected and enabled.")]
        [SerializeField] private bool enabled;

        [Tooltip("Semantic Performance Tags this ingredient expresses (WPF/SQLite-owned vocabulary).")]
        [SerializeField] private List<string> performanceTagIds = new List<string>();

        [Tooltip("Postures the artist confirmed as compatible.")]
        [SerializeField] private List<string> supportedPostureIds = new List<string>();

        [Tooltip("Optional anchor restriction; empty means any anchor supporting the posture.")]
        [SerializeField] private List<string> supportedAnchorIds = new List<string>();

        [Tooltip("Exceptional head-owning gesture: suspends gaze while active.")]
        [SerializeField] private bool ownsHead;

        [Header("Unity binding (not exported)")]
        [SerializeField] private AnimationClip clip;
        [SerializeField] private string faceControlName = "";
        [SerializeField, Range(0f, 100f)] private float faceWeight = 100f;

        public string IngredientId => ingredientId;
        public string DisplayName => string.IsNullOrEmpty(displayName)
            ? (clip != null ? clip.name : ingredientId)
            : displayName;
        public string Kind => kind;
        public bool Enabled => enabled;
        public IReadOnlyList<string> PerformanceTagIds => performanceTagIds;
        public IReadOnlyList<string> SupportedPostureIds => supportedPostureIds;
        public IReadOnlyList<string> SupportedAnchorIds => supportedAnchorIds;
        public bool OwnsHead => ownsHead;
        public AnimationClip Clip => clip;
        public string FaceControlName => faceControlName;
        public float FaceWeight => faceWeight;

        public float ClipDurationSeconds => clip != null ? clip.length : 0f;
        public bool ClipLoops => clip != null && clip.isLooping;

        public void EnsureId()
        {
            if (string.IsNullOrEmpty(ingredientId))
            {
                ingredientId = Guid.NewGuid().ToString("N");
            }
        }

        /// <summary>
        /// Replaces the semantic tag membership. Unity owns membership, but the
        /// vocabulary itself is WPF/SQLite-owned, so callers pass stable tag IDs
        /// read from the content database — never copied titles.
        /// </summary>
        public void SetPerformanceTagIds(IEnumerable<string> tagIds)
        {
            performanceTagIds = new List<string>(tagIds ?? Array.Empty<string>());
        }

        /// <summary>Adds or removes one stable Performance Tag ID; returns the new membership state.</summary>
        public bool TogglePerformanceTag(string tagId)
        {
            if (string.IsNullOrEmpty(tagId)) return false;
            if (performanceTagIds.Contains(tagId))
            {
                performanceTagIds.Remove(tagId);
                return false;
            }
            performanceTagIds.Add(tagId);
            return true;
        }

        /// <summary>Authoring/fixture configuration; the inspector remains the ordinary intake path.</summary>
        public void Configure(string id, string name, string ingredientKind, bool isEnabled,
            IEnumerable<string> tagIds, IEnumerable<string> postureIds, bool headOwner,
            AnimationClip animationClip, string controlName = "", float weight = 100f)
        {
            if (!string.IsNullOrEmpty(id)) ingredientId = id;
            displayName = name ?? "";
            kind = ingredientKind;
            enabled = isEnabled;
            performanceTagIds = new List<string>(tagIds ?? Array.Empty<string>());
            supportedPostureIds = new List<string>(postureIds ?? Array.Empty<string>());
            ownsHead = headOwner;
            clip = animationClip;
            faceControlName = controlName ?? "";
            faceWeight = weight;
            EnsureId();
        }
    }

    /// <summary>
    /// One physical anchor. Adding an equivalent sit-capable anchor is this
    /// entry plus connections and calibration — not copies of every posture
    /// conversion.
    /// </summary>
    [Serializable]
    public sealed class PerformanceAnchorEntry
    {
        [SerializeField] private string anchorId = "";
        [SerializeField] private string displayName = "";
        [Tooltip("Location grouping for DifferentLocation staging; empty means this anchor is its own group.")]
        [SerializeField] private string locationGroup = "";
        [Tooltip("Postures this anchor supports (standing | sitting).")]
        [SerializeField] private List<string> supportedPostureIds = new List<string>();
        [Tooltip("Anchors reachable with MoveWhileStanding (symmetric).")]
        [SerializeField] private List<string> connectedAnchorIds = new List<string>();

        public string AnchorId => anchorId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? anchorId : displayName;
        public string LocationGroup => locationGroup;
        public IReadOnlyList<string> SupportedPostureIds => supportedPostureIds;
        public IReadOnlyList<string> ConnectedAnchorIds => connectedAnchorIds;

        public void EnsureId()
        {
            if (string.IsNullOrEmpty(anchorId))
            {
                anchorId = Guid.NewGuid().ToString("N");
            }
        }

        public void Configure(string id, string name, string group,
            IEnumerable<string> postureIds, IEnumerable<string> connections)
        {
            if (!string.IsNullOrEmpty(id)) anchorId = id;
            displayName = name ?? "";
            locationGroup = group ?? "";
            supportedPostureIds = new List<string>(postureIds ?? Array.Empty<string>());
            connectedAnchorIds = new List<string>(connections ?? Array.Empty<string>());
            EnsureId();
        }
    }

    /// <summary>
    /// One reusable transition binding, shared across compatible anchors.
    /// ApplicableAnchorIds empty means every anchor whose capabilities satisfy
    /// the kind's precondition; a non-empty list is an exceptional override.
    /// </summary>
    [Serializable]
    public sealed class PerformanceOperationEntry
    {
        [SerializeField] private string operationId = "";
        [SerializeField] private string displayName = "";
        [Tooltip("stand | sit | move_while_standing (see PresentationOperationKinds).")]
        [SerializeField] private string kind = PresentationOperationKinds.MoveWhileStanding;
        [SerializeField, Min(1)] private int cost = 10;
        [Tooltip("Optional exceptional anchor restriction; empty means shared by all compatible anchors.")]
        [SerializeField] private List<string> applicableAnchorIds = new List<string>();

        [Header("Unity binding (not exported)")]
        [Tooltip("Transition clip this operation plays. Required: a Perform whose plan composes this operation is refused without it.")]
        [SerializeField] private AnimationClip clip;

        public AnimationClip Clip => clip;

        public string OperationId => operationId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? kind : displayName;
        public string Kind => kind;
        public int Cost => cost;
        public IReadOnlyList<string> ApplicableAnchorIds => applicableAnchorIds;

        public void EnsureId()
        {
            if (string.IsNullOrEmpty(operationId))
            {
                operationId = Guid.NewGuid().ToString("N");
            }
        }

        public void Configure(string id, string name, string operationKind, int operationCost,
            IEnumerable<string> anchorIds = null, AnimationClip animationClip = null)
        {
            if (!string.IsNullOrEmpty(id)) operationId = id;
            displayName = name ?? "";
            kind = operationKind;
            cost = operationCost < 1 ? 1 : operationCost;
            applicableAnchorIds = new List<string>(anchorIds ?? Array.Empty<string>());
            clip = animationClip;
            EnsureId();
        }
    }

    /// <summary>
    /// The focused, hand-authored registry of presentation ingredients, anchors
    /// and reusable operations. It is the Unity-owned authority for factual
    /// compatibility; the generated PresentationCatalog is its read-only
    /// projection for Core and WPF.
    /// </summary>
    [CreateAssetMenu(menuName = "TruthCardGame/Performance/Performance Registry", fileName = "PerformanceRegistry")]
    public sealed class PerformanceRegistry : ScriptableObject
    {
        [Tooltip("Standard verified gesture mask applied to ordinary body ingredients.")]
        [SerializeField] private AvatarMask standardGestureMask;

        [SerializeField] private List<PerformanceIngredientEntry> ingredients = new List<PerformanceIngredientEntry>();
        [SerializeField] private List<PerformanceAnchorEntry> anchors = new List<PerformanceAnchorEntry>();
        [SerializeField] private List<PerformanceOperationEntry> operations = new List<PerformanceOperationEntry>();

        public AvatarMask StandardGestureMask => standardGestureMask;
        public IReadOnlyList<PerformanceIngredientEntry> Ingredients => ingredients;
        public IReadOnlyList<PerformanceAnchorEntry> Anchors => anchors;
        public IReadOnlyList<PerformanceOperationEntry> Operations => operations;

        private void OnValidate()
        {
            EnsureStableIds();
        }

        /// <summary>Mints IDs for new entries; reimport preserves existing IDs.</summary>
        public void EnsureStableIds()
        {
            foreach (var ingredient in ingredients) ingredient?.EnsureId();
            foreach (var anchor in anchors) anchor?.EnsureId();
            foreach (var operation in operations) operation?.EnsureId();
        }

        /// <summary>Fixture/authoring replacement of the whole registry body; preserves nothing by design.</summary>
        public void ReplaceContents(PerformanceRegistryView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            standardGestureMask = view.Mask;
            ingredients = new List<PerformanceIngredientEntry>(view.Ingredients);
            anchors = new List<PerformanceAnchorEntry>(view.Anchors);
            operations = new List<PerformanceOperationEntry>(view.Operations);
            EnsureStableIds();
        }

        public PerformanceRegistryView BuildView()
        {
            EnsureStableIds();
            return new PerformanceRegistryView(standardGestureMask, ingredients, anchors, operations);
        }
    }

    /// <summary>
    /// Read-only, engine-free view over a registry used to build and validate
    /// the PresentationCatalog. Kept separate from the ScriptableObject so the
    /// projection logic is testable and contains no asset-editing behavior.
    /// </summary>
    public sealed class PerformanceRegistryView
    {
        private readonly AvatarMask _mask;
        private readonly IReadOnlyList<PerformanceIngredientEntry> _ingredients;
        private readonly IReadOnlyList<PerformanceAnchorEntry> _anchors;
        private readonly IReadOnlyList<PerformanceOperationEntry> _operations;

        public PerformanceRegistryView(
            AvatarMask mask,
            IReadOnlyList<PerformanceIngredientEntry> ingredients,
            IReadOnlyList<PerformanceAnchorEntry> anchors,
            IReadOnlyList<PerformanceOperationEntry> operations)
        {
            _mask = mask;
            _ingredients = ingredients ?? new List<PerformanceIngredientEntry>();
            _anchors = anchors ?? new List<PerformanceAnchorEntry>();
            _operations = operations ?? new List<PerformanceOperationEntry>();
        }

        public AvatarMask Mask => _mask;
        public IReadOnlyList<PerformanceIngredientEntry> Ingredients => _ingredients;
        public IReadOnlyList<PerformanceAnchorEntry> Anchors => _anchors;
        public IReadOnlyList<PerformanceOperationEntry> Operations => _operations;

        /// <summary>
        /// Projects the current registry into the read-only catalog. Disabled
        /// ingredients are included with Enabled=false so authors can see
        /// unfinished work; only enabled declarations must be complete.
        /// </summary>
        public PresentationCatalogDefinition BuildCatalog(DateTime generatedAtUtc)
        {
            var catalog = new PresentationCatalogDefinition
            {
                CatalogVersion = PresentationCatalogDefinition.CurrentVersion,
                GeneratedAtUtc = generatedAtUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            };

            foreach (var ingredient in _ingredients)
            {
                if (ingredient == null) continue;
                var projected = new PresentationIngredientDefinition
                {
                    Id = ingredient.IngredientId,
                    DisplayName = ingredient.DisplayName,
                    Kind = ingredient.Kind,
                    Enabled = ingredient.Enabled,
                    OwnsHead = ingredient.OwnsHead,
                };
                projected.PerformanceTagIds.AddRange(ingredient.PerformanceTagIds);
                projected.SupportedPostureIds.AddRange(ingredient.SupportedPostureIds);
                projected.SupportedAnchorIds.AddRange(ingredient.SupportedAnchorIds);
                catalog.Ingredients.Add(projected);
            }

            foreach (var anchor in _anchors)
            {
                if (anchor == null) continue;
                var projected = new PresentationAnchorDefinition
                {
                    Id = anchor.AnchorId,
                    DisplayName = anchor.DisplayName,
                    LocationGroup = anchor.LocationGroup,
                };
                projected.SupportedPostureIds.AddRange(anchor.SupportedPostureIds);
                projected.ConnectedAnchorIds.AddRange(anchor.ConnectedAnchorIds);
                catalog.Anchors.Add(projected);
            }

            foreach (var operation in _operations)
            {
                if (operation == null) continue;
                var projected = new PresentationOperationDefinition
                {
                    Id = operation.OperationId,
                    DisplayName = operation.DisplayName,
                    Kind = operation.Kind,
                    Cost = operation.Cost,
                };
                projected.ApplicableAnchorIds.AddRange(operation.ApplicableAnchorIds);
                catalog.Operations.Add(projected);
            }

            return catalog;
        }
    }

    /// <summary>
    /// Validates and serializes the registry's catalog projection. Contains no
    /// editor API so it can be used by tests and diagnostics; the editor menu
    /// layer owns file IO.
    /// </summary>
    public static class PresentationCatalogBuilder
    {
        /// <summary>Throws with the exact offender when the catalog is malformed.</summary>
        public static PresentationCatalogDefinition BuildValidated(PerformanceRegistryView view, DateTime generatedAtUtc)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            var catalog = view.BuildCatalog(generatedAtUtc);
            PresentationCatalogValidator.Validate(catalog);
            return catalog;
        }

        public static string ToJson(PresentationCatalogDefinition catalog)
        {
            return PresentationCatalogJson.ToJson(catalog);
        }

        /// <summary>Non-fatal authoring warnings (for example, enabled body with no foundation).</summary>
        public static List<string> Warnings(PresentationCatalogDefinition catalog)
        {
            var warnings = new List<string>();
            if (catalog == null) return warnings;

            var postureSet = new HashSet<string>(StringComparer.Ordinal);
            var foundationPostures = new HashSet<string>(StringComparer.Ordinal);
            foreach (var anchor in catalog.Anchors)
            {
                foreach (var posture in anchor.SupportedPostureIds) postureSet.Add(posture);
            }
            foreach (var ingredient in catalog.Ingredients)
            {
                if (ingredient.Enabled && ingredient.Kind == PresentationIngredientKinds.Foundation)
                {
                    foreach (var posture in ingredient.SupportedPostureIds) foundationPostures.Add(posture);
                }
            }

            foreach (var posture in postureSet)
            {
                if (!foundationPostures.Contains(posture))
                {
                    warnings.Add($"Anchor posture '{posture}' has no enabled foundation ingredient.");
                }
            }

            foreach (var ingredient in catalog.Ingredients)
            {
                if (!ingredient.Enabled) continue;
                foreach (var posture in ingredient.SupportedPostureIds)
                {
                    if (!postureSet.Contains(posture))
                    {
                        warnings.Add(
                            $"Enabled ingredient '{ingredient.Id}' supports posture '{posture}' no anchor offers.");
                    }
                }
            }
            return warnings;
        }
    }
}
