using System.Collections;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace TruthCardGame
{
    /// <summary>
    /// A blocking card action that plays a Timeline cutscene and returns control
    /// to the executor when the timeline finishes. The scene-side cutscene
    /// player (DirectorPlayer) is reached through context.Cutscene, so this
    /// action never touches the scene directly.
    ///
    /// The end-of-cutscene bluetooth toy hook plugs into the timeline itself
    /// (a SignalEmitter fires at the end) — no action code is involved.
    /// </summary>
    [CreateAssetMenu(fileName = "CutsceneAction", menuName = "TruthCardGame/Actions/Cutscene")]
    public sealed class CutsceneAction : CardAction
    {
        [SerializeField, Tooltip("The Timeline cutscene to play. Assign in the Timeline window.")]
        private TimelineAsset timeline;

        public override IEnumerator Execute(GameContext context)
        {
            if (timeline == null)
            {
                Debug.LogError("[TruthCardGame] CutsceneAction has no timeline assigned. Assign one on the action asset.");
                yield break;
            }

            var player = context.Services?.Cutscene;
            if (player == null)
            {
                Debug.LogError("[TruthCardGame] CutsceneAction: no ICutscenePlayer in context. Is the Game scene wired (rebuild with Build Scenes)?");
                yield break;
            }

            player.Play(timeline);
            // Wait until the director is no longer playing, then return control.
            while (player.IsPlaying)
            {
                yield return null;
            }
        }
    }
}