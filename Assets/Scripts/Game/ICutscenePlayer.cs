using UnityEngine.Playables;

namespace TruthCardGame
{
    /// <summary>
    /// Plays a cutscene timeline. Typed against the core PlayableAsset so this
    /// interface has zero dependency on the Timeline package; timeline assets
    /// derive from PlayableAsset. A concrete mono-driven implementation
    /// (wrapping PlayableDirector) is supplied by the game scene.
    /// </summary>
    public interface ICutscenePlayer
    {
        void Play(PlayableAsset timeline);
        bool IsPlaying { get; }
    }
}