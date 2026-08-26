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
        [Tooltip("True: the executor waits for this action to finish before the next one. False: fire-and-forget (may overlap).")]
        [SerializeField] private bool isBlocking = true;

        public bool IsBlocking => isBlocking;

        /// <summary>Converts to the portable definition; base means "no action" for legacy/test subclasses.</summary>
        public virtual TruthCardGame.Content.GameActionDefinition ToDefinition(CutsceneBindingRegistry registry)
        {
            return null;
        }
    }
}
