using UnityEngine;

namespace TruthCardGame
{
    /// <summary>Adds an amount to a named player stat. Data shell; execution lives in Game.Core.</summary>
    [CreateAssetMenu(fileName = "StatIncreaseAction", menuName = "TruthCardGame/Actions/Stat Increase")]
    public sealed class StatIncreaseAction : CardAction
    {
        [SerializeField] private string statKey = "courage";
        [SerializeField] private int amount = 1;

        public override TruthCardGame.Content.GameActionDefinition ToDefinition(CutsceneBindingRegistry registry)
        {
            return new TruthCardGame.Content.StatIncreaseActionDefinition
            {
                IsBlocking = IsBlocking,
                StatKey = statKey,
                Amount = amount
            };
        }
    }
}
