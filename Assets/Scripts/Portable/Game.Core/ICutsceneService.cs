using System.Threading;
using System.Threading.Tasks;

namespace TruthCardGame.Core
{
    public interface ICutsceneService
    {
        /// <summary>Plays the resource; completes when it is logically complete.</summary>
        Task PlayAsync(string resourceId, CancellationToken cancellationToken);
    }
}
