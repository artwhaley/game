using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// One executable step a card can trigger — serialized data shell only.
    /// Execution lives in portable Game.Core; this wrapper converts to its
    /// definition via ToDefinition(...). The old coroutine Execute engine was
    /// removed in Ticket 09 so exactly one implementation exists.
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
        /// Converts this asset to its portable definition (shallow; referenced
        /// children are collected through the builder). Abstract on purpose:
        /// every action wrapper must participate in the portable engine or the
        /// project does not compile (fail noisy over silently vanishing content).
        /// </summary>
        public abstract TruthCardGame.Content.GameActionDefinition ToDefinition(UnityContentGraphBuilder builder);
    }
}
