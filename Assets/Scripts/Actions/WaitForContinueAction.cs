using UnityEngine;

namespace TruthCardGame
{
    /// <summary>Explicit player-paced yield. Data shell; execution lives in Game.Core.</summary>
    [CreateAssetMenu(fileName = "WaitForContinueAction", menuName = "TruthCardGame/Actions/Wait For Continue")]
    public sealed class WaitForContinueAction : CardAction
    {
        public override TruthCardGame.Content.ActionInstanceDefinition ToDefinition(UnityContentGraphBuilder builder)
        {
            return new TruthCardGame.Content.WaitForContinueInstanceDefinition
            {
                Id = Id,
                IsBlocking = true,
            };
        }
    }
}
