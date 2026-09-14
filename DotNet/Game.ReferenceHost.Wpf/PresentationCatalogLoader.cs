using System.Collections.Generic;
using System.Linq;
using TruthCardGame.Content;
using TruthCardGame.Core;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// Reads the Unity-generated presentation catalog that sits beside the
    /// content database. The artifact is optional and read-only: a missing or
    /// malformed file must never block content editing or playback, so failures
    /// degrade to "not generated" instead of surfacing an error.
    /// </summary>
    public static class PresentationCatalogLoader
    {
        public static PresentationCatalogDefinition TryLoad(string databasePath)
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(databasePath);
                if (string.IsNullOrEmpty(directory)) return null;
                var catalogPath = System.IO.Path.Combine(directory, "PresentationCatalog.json");
                if (!System.IO.File.Exists(catalogPath)) return null;
                return PresentationCatalogJson.FromJson(System.IO.File.ReadAllText(catalogPath));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The starting anchor/posture for a simulated run: the first anchor that
        /// supports standing when one exists, otherwise the first anchor with any
        /// posture. Returns null when the catalog declares no usable anchor.
        /// </summary>
        public static PerformanceActorState TryDefaultInitialState(PresentationCatalogDefinition catalog)
        {
            var anchors = (catalog?.Anchors ?? new List<PresentationAnchorDefinition>())
                .Where(anchor => anchor != null && anchor.SupportedPostureIds.Count > 0)
                .OrderBy(anchor => anchor.Id, System.StringComparer.Ordinal)
                .ToList();
            if (anchors.Count == 0) return null;

            var standing = anchors.FirstOrDefault(anchor =>
                anchor.SupportedPostureIds.Contains(PresentationPostures.Standing));
            var chosen = standing ?? anchors[0];
            var posture = chosen.SupportedPostureIds.Contains(PresentationPostures.Standing)
                ? PresentationPostures.Standing
                : chosen.SupportedPostureIds[0];
            return new PerformanceActorState(chosen.Id, posture);
        }
    }
}
