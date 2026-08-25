using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// The set of scene-side services an action can reach through GameContext.
    /// All are optional: executor-only tests pass null and keep working.
    /// Services are supplied once by the game scene and shared across actions.
    /// </summary>
    public sealed class GameServices
    {
        public ICoroutineRunner Runner { get; }
        public IPromptService Prompts { get; }
        public ICutscenePlayer Cutscene { get; }

        public GameServices(ICoroutineRunner runner = null, IPromptService prompts = null, ICutscenePlayer cutscene = null)
        {
            Runner = runner;
            Prompts = prompts;
            Cutscene = cutscene;
        }
    }
}