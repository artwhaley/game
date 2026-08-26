using UnityEngine;

namespace TruthCardGame
{
    /// <summary>Logs a message, optionally waits, then completes. Data shell; execution lives in Game.Core.</summary>
    [CreateAssetMenu(fileName = "DebugAction", menuName = "TruthCardGame/Actions/Debug Action")]
    public sealed class DebugAction : CardAction
    {
        [SerializeField, TextArea] private string message = "Debug action ran.";
        [Tooltip("How long to wait before completing. 0 = complete immediately.")]
        [SerializeField] private float delaySeconds = 0f;

        public override TruthCardGame.Content.GameActionDefinition ToDefinition(CutsceneBindingRegistry registry)
        {
            return new TruthCardGame.Content.DebugActionDefinition
            {
                IsBlocking = IsBlocking,
                Message = message,
                DelaySeconds = delaySeconds
            };
        }
    }
}
