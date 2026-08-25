using UnityEngine;
using UnityEngine.Playables;

namespace TruthCardGame
{
    /// <summary>
    /// Concrete ICutscenePlayer backed by a PlayableDirector in the game scene.
    /// The director's playableAsset is assigned per sure; this player just
    /// starts playback and reports when the director is no longer playing.
    /// </summary>
    [DefaultExecutionOrder(-100)] // resolve before GameManager.Awake
    public sealed class DirectorPlayer : MonoBehaviour, ICutscenePlayer
    {
        [SerializeField] private PlayableDirector director;

        public bool IsPlaying => director != null && director.state == PlayState.Playing;

        public void Play(PlayableAsset timeline)
        {
            if (timeline == null)
            {
                Debug.LogError("[TruthCardGame] CutsceneAction: timeline asset is null. Assign it on the action asset.");
                return;
            }
            if (director == null)
            {
                Debug.LogError("[TruthCardGame] CutsceneAction: DirectorPlayer has no PlayableDirector. Rebuild with TruthCardGame → Build Scenes.");
                return;
            }
            director.Play(timeline);
        }
    }
}