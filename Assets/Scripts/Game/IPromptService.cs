using System;
using System.Collections.Generic;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// Presents a player choice and resumes the awaiting action with the
    /// selected index. Implemented by the game scene (via the game panel);
    /// actions call this through GameContext so they never touch the scene.
    ///
    /// Ask returns a Unity yield instruction: the action does
    /// `yield return context.Services.Prompts.Ask(...)` and stays suspended
    /// until the player clicks an option, at which point onChosen fires with
    /// the selected index and the coroutine resumes.
    /// </summary>
    public interface IPromptService
    {
        CustomYieldInstruction Ask(string prompt, IReadOnlyList<string> options, Action<int> onChosen);
    }
}