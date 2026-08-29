using System;
using System.Collections.Generic;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Host services an action/engine can reach through the context. Delay is
    /// required (the runtime always has timing available); the log defaults to
    /// NullGameLog so Core never sees null; prompt/cutscene remain optional
    /// because missing them has meaningful, tested no-op behavior.
    /// </summary>
    public sealed class CoreServices
    {
        public IGameDelay Delay { get; }
        public IGameLog Log { get; }
        public IPromptService Prompts { get; }
        public ICutsceneService Cutscene { get; }
        public IToyActivityService ToyActivity { get; }

        public CoreServices(IGameDelay delay, IGameLog log = null, IPromptService prompts = null,
            ICutsceneService cutscene = null, IToyActivityService toyActivity = null)
        {
            Delay = delay ?? throw new ArgumentNullException(nameof(delay));
            Log = log ?? new NullGameLog();
            Prompts = prompts;
            Cutscene = cutscene;
            ToyActivity = toyActivity;
        }
    }
}
