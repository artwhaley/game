using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// One executable step a card can trigger — serialized data shell only.
    /// Execution lives in portable Game.Core; this wrapper converts to its
    /// ActionInstanceDefinition via ToDefinition(...). Every occurrence
    /// converts to its own instance: instances are never shared between
    /// sequences or cards (v2 model).
    /// </summary>
    public abstract class CardAction : ScriptableObject
    {
        [Tooltip("Stable content ID, minted once at authoring time. Never regenerated; used by cross-host references.")]
        [SerializeField] private string id;

        [Tooltip("True: the executor waits for this action to finish before the next one. False: fire-and-forget (may overlap).")]
        [SerializeField] private bool isBlocking = true;

        public string Id => id;
        public bool IsBlocking => isBlocking;

        /// <summary>Mints the stable ID on first call; no-op once set. Called by OnValidate and authoring tooling.</summary>
        public virtual void EnsureId()
        {
            if (string.IsNullOrEmpty(id)) id = System.Guid.NewGuid().ToString("N");
        }

        private void OnValidate()
        {
            EnsureId();
        }

        /// <summary>
        /// Converts this asset to its portable Action Instance (v2). Abstract on
        /// purpose: every action wrapper must participate in the portable engine
        /// or the project does not compile (fail noisy over silently vanishing content).
        /// </summary>
        public abstract TruthCardGame.Content.ActionInstanceDefinition ToDefinition(UnityContentGraphBuilder builder);
    }
}
