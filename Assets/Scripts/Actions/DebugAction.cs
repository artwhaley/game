using System.Collections;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// Logs a message to the console, optionally waits, then completes.
    /// Stand-in for cutscenes/voiceover until those action types exist.
    /// </summary>
    [CreateAssetMenu(fileName = "DebugAction", menuName = "TruthCardGame/Actions/Debug Action")]
    public sealed class DebugAction : CardAction
    {
        [SerializeField, TextArea] private string message = "Debug action ran.";
        [Tooltip("How long to wait before completing. 0 = complete immediately.")]
        [SerializeField] private float delaySeconds = 0f;

        public override IEnumerator Execute(GameContext context)
        {
            Debug.Log($"[TruthCardGame] {context.Player.Name}: {message}");
            if (delaySeconds > 0f)
            {
                yield return new WaitForSeconds(delaySeconds);
            }
        }

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
