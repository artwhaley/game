using System.Collections;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// One executable step a card can trigger. The entry point is Execute(); an
    /// action returns control to the executor when its coroutine completes.
    ///
    /// isBlocking (serialized, per asset instance):
    /// - true  → the executor waits for Execute() to finish before running the
    ///           card's next action.
    /// - false → fire-and-forget: the executor starts Execute() and moves on,
    ///           so several continuous actions can run concurrently.
    ///
    /// Implementer contract: do the work inside Execute() and end the coroutine
    /// when done. A blocking action that never completes will stall the card.
    /// </summary>
    public abstract class CardAction : ScriptableObject
    {
        [Tooltip("True: the executor waits for this action to finish before the next one. False: fire-and-forget (may overlap).")]
        [SerializeField] private bool isBlocking = true;

        public bool IsBlocking => isBlocking;

        public abstract IEnumerator Execute(GameContext context);
    }
}
