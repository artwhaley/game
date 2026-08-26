using System.Collections;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// Instantly adds an amount to a named stat on the current player,
    /// then completes without delay.
    /// </summary>
    [CreateAssetMenu(fileName = "StatIncreaseAction", menuName = "TruthCardGame/Actions/Stat Increase")]
    public sealed class StatIncreaseAction : CardAction
    {
        [SerializeField] private string statKey = "courage";
        [SerializeField] private int amount = 1;

        public override IEnumerator Execute(GameContext context)
        {
            context.Player.Stats.Add(statKey, amount);
            Debug.Log($"[TruthCardGame] {context.Player.Name} {statKey} +{amount} (now {context.Player.Stats.Get(statKey)})");
            yield break;
        }

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
