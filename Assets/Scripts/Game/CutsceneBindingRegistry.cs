using System.Collections.Generic;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace TruthCardGame
{
    /// <summary>
    /// Extraction-era compatibility bridge between serialized TimelineAsset
    /// references on CutsceneAction assets and portable string resource keys.
    /// Keys are opaque, runtime-only (counter-based), and resolvable to the
    /// exact registered asset instance for the lifetime of this object. This
    /// is deliberately NOT the final content-ID design; nothing here uses
    /// AssetDatabase or serializes GUIDs into portable content.
    /// </summary>
    public sealed class CutsceneBindingRegistry
    {
        private readonly Dictionary<string, PlayableAsset> _assets = new Dictionary<string, PlayableAsset>();
        private int _counter;

        /// <summary>Registers an asset under a fresh opaque key. Null returns null so missing timelines stay missing.</summary>
        public string Register(TimelineAsset asset)
        {
            if (asset == null) return null;

            _counter++;
            var key = "cutscene:" + _counter;
            _assets[key] = asset;
            return key;
        }

        /// <summary>Resolves a key produced by Register. Unknown/null keys resolve to null.</summary>
        public bool TryResolve(string resourceId, out PlayableAsset asset)
        {
            if (!string.IsNullOrEmpty(resourceId) && _assets.TryGetValue(resourceId, out var found))
            {
                asset = found;
                return true;
            }
            asset = null;
            return false;
        }
    }
}
