using UnityEngine;

namespace TruthCardGame
{
    /// <summary>Authored PhaseProgress mutation. Data shell; execution lives in Game.Core.</summary>
    [CreateAssetMenu(fileName = "IncrementProgressAction", menuName = "TruthCardGame/Actions/Increment Progress")]
    public sealed class IncrementProgressAction : CardAction
    {
        [SerializeField] private float amount = 10f;

        public float Amount => amount;

        public override TruthCardGame.Content.ActionInstanceDefinition ToDefinition(UnityContentGraphBuilder builder)
        {
            return new TruthCardGame.Content.IncrementProgressInstanceDefinition
            {
                Id = Id,
                IsBlocking = IsBlocking,
                Amount = amount,
            };
        }
    }
}
