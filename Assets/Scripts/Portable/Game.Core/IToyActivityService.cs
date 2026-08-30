using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Portable host contract for toy output (Docs/ToyPatternDialog/
    /// TOY-COMMAND-SEMANTICS.md). Toy output state is keyed by capability ID;
    /// a new command for a capability supersedes the previous one, commands for
    /// different capabilities coexist. Hosts must honor the stale-timer rule:
    /// a superseded timed command may never stop a newer command when its own
    /// timer expires (per-capability generation ownership). StopAll is a
    /// lifecycle/safety operation invoked on every session teardown path, never
    /// an authored Action.
    /// </summary>
    public interface IToyActivityService
    {
        /// <summary>
        /// Applies a Toy Pattern Resource to a capability for an authored
        /// duration. Completes when the activity logically completes (natural
        /// expiry) or promptly when superseded/canceled.
        /// </summary>
        Task PlayForAsync(string capabilityId, string patternResourceId, TimeSpan duration, CancellationToken cancellationToken);

        /// <summary>
        /// Sets/replaces the persistent pattern state of a capability and
        /// returns promptly. The state remains active across Cards, Phase
        /// transitions, GOTO/RETURN, Delay, and WaitForContinue; it is never a
        /// background Task, so WaitForAll does not wait on it.
        /// </summary>
        Task SetPatternAsync(string capabilityId, string patternResourceId, CancellationToken cancellationToken);

        /// <summary>Stops/releases all active toy output (lifecycle safety).</summary>
        Task StopAllAsync(CancellationToken cancellationToken);
    }
}
