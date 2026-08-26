using UnityEngine;
using UnityEngine.Timeline;

namespace TruthCardGame
{
    /// <summary>
    /// Plays a Timeline cutscene. Serialized TimelineAsset stays Unity-side and
    /// is bridged to a portable resource key by CutsceneBindingRegistry during
    /// conversion; execution lives in Game.Core + DirectorPlayer service.
    /// </summary>
    [CreateAssetMenu(fileName = "CutsceneAction", menuName = "TruthCardGame/Actions/Cutscene")]
    public sealed class CutsceneAction : CardAction
    {
        [SerializeField, Tooltip("The Timeline cutscene to play. Assign in the Timeline window.")]
        private TimelineAsset timeline;

        public override TruthCardGame.Content.GameActionDefinition ToDefinition(CutsceneBindingRegistry registry)
        {
            string resourceId = null;
            if (timeline != null)
            {
                if (registry == null)
                {
                    Debug.LogError("[TruthCardGame] CutsceneAction has a timeline but no registry to bind it; converting as missing resource.");
                }
                else
                {
                    resourceId = registry.Register(timeline);
                }
            }
            // Null/empty ResourceId preserves the current missing-timeline no-op behavior downstream.
            return new TruthCardGame.Content.CutsceneActionDefinition
            {
                IsBlocking = IsBlocking,
                ResourceId = resourceId
            };
        }
    }
}
