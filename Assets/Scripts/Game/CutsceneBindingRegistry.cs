using System.Collections.Generic;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace TruthCardGame
{
    /// <summary>
    /// Runtime resolution table bridging portable cutscene resource ids to
    /// Unity TimelineAssets. Keys are stable authored strings (minted once on
    /// the CutsceneAction asset when the author leaves them empty) — the same
    /// strings portable JSON/WPF content references, so cutscene links survive
    /// authoring round trips. Populated during ScriptableObject→definition
    /// conversion; duplicate ids are authoring errors and fail loudly.
    /// </summary>
    public sealed class CutsceneBindingRegistry
    {
        private readonly Dictionary<string, PlayableAsset> _assets = new Dictionary<string, PlayableAsset>();

        /// <summary>Registers an asset under its stable authored resource id. Null asset is a no-op; duplicate ids throw.</summary>
        public void Register(string resourceId, TimelineAsset asset)
        {
            if (asset == null) return;
            if (string.IsNullOrEmpty(resourceId))
            {
                UnityEngine.Debug.LogError("[TruthCardGame] Refusing to register a cutscene under an empty resource id.");
                return;
            }
            if (_assets.ContainsKey(resourceId))
            {
                throw new System.InvalidOperationException(
                    $"[TruthCardGame] Duplicate cutscene resource id '{resourceId}' — two actions claim the same id. Rename one.");
            }
            _assets[resourceId] = asset;
        }

        /// <summary>Resolves a resource id produced by Register. Unknown/null keys resolve to null.</summary>
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
