using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// The yield instruction a prompt action awaits: it keeps the coroutine
    /// suspended until Resolve() is called (when the player clicks an option).
    /// </summary>
    public sealed class PromptHandle : CustomYieldInstruction
    {
        public override bool keepWaiting => !_resolved;

        private bool _resolved;

        public void Resolve()
        {
            _resolved = true;
        }
    }
}