using System;
using System.Threading;
using System.Threading.Tasks;

namespace TruthCardGame.Core
{
    public interface IGameDelay
    {
        Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }
}
