using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Playables;

namespace TruthCardGame
{
    /// <summary>
    /// Unity ICutsceneService: resolves a Core resource key through the
    /// Ticket-07 registry back to the registered asset, plays it on the scene's
    /// PlayableDirector, and completes when playback stops. Main-thread only.
    /// </summary>
    [DefaultExecutionOrder(-100)] // resolve before GameManager.Awake
    public sealed class DirectorPlayer : MonoBehaviour, TruthCardGame.Core.ICutsceneService
    {
        [SerializeField] private PlayableDirector director;

        private CutsceneBindingRegistry _registry;

        public bool IsPlaying => director != null && director.state == PlayState.Playing;

        public void Bind(CutsceneBindingRegistry registry)
        {
            _registry = registry;
        }

        public async Task PlayAsync(string resourceId, CancellationToken cancellationToken)
        {
            if (director == null)
            {
                Debug.LogError("[TruthCardGame] DirectorPlayer has no PlayableDirector. Rebuild with TruthCardGame → Build Scenes.");
                return;
            }
            if (_registry == null || !_registry.TryResolve(resourceId, out var asset) || asset == null)
            {
                Debug.LogError($"[TruthCardGame] No cutscene registered for resource '{resourceId}'.");
                return;
            }

            director.Play(asset);
            while (IsPlaying)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    director.Stop();
                    return;
                }
                await Task.Yield();
            }
        }
    }
}
