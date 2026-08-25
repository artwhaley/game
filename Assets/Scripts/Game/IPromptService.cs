using System;
using System.Collections.Generic;

namespace TruthCardGame
{
    /// <summary>
    /// Presents a player choice and resumes the awaiting action with the
    /// selected index. Implemented by the game scene (via the game panel);
    /// actions call this through GameContext so they never touch the scene.
    /// Implementations must not block the coroutine themselves — they return
    /// an enumerator the action yields on until a choice resolves.
    /// </summary>
    public interface IPromptService
    {
        IEnumerator<object> Ask(string prompt, IReadOnlyList<string> options, Action<int> onChosen);
    }
}