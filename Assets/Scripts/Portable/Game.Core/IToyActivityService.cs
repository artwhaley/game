using System;
using System.Threading;
using System.Threading.Tasks;

namespace TruthCardGame.Core
{
    public interface IToyActivityService
    {
        /// <summary>Runs a configured capability and completes when the activity is logically complete.</summary>
        Task PlayAsync(string capabilityId, float intensity, TimeSpan duration, CancellationToken cancellationToken);
    }
}
