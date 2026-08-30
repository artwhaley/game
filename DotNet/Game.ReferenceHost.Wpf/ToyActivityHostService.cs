using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TruthCardGame.Core;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// Concrete WPF-host IToyActivityService that drives the pure
    /// <see cref="ToyOutputSimulator"/> with a real monotonic clock and exposes
    /// playable Tasks for DisplayTimed. A single background monitor loop ticks
    /// the simulator and completes timed Tasks on natural expiry, supersession,
    /// or teardown. SetPattern never runs a background Task. StopAll clears all
    /// capabilities and waits for the monitor to exit unless cleanup is canceled.
    /// </summary>
    public sealed class ToyActivityHostService : IToyActivityService, IDisposable
    {
        private const int MonitorIntervalMs = 15;

        private sealed class Pending
        {
            public string CapabilityId;
            public long Generation;
            public long ExpiryTicks;
            public TaskCompletionSource<bool> Tcs;
        }

        private readonly ToyOutputSimulator _simulator;
        private readonly Func<long> _ticks;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly object _gate = new object();

        private readonly Dictionary<string, List<Pending>> _pendingByCapability = new Dictionary<string, List<Pending>>();
        private Task _monitor;
        private CancellationTokenSource _cts;
        private bool _started;
        private bool _stopped;

        public ToyActivityHostService(
            Func<long> ticks = null,
            Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            _ticks = ticks ?? StopwatchTicks;
            _simulator = new ToyOutputSimulator(_ticks);
            _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
        }

        private static long StopwatchTicks() => Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;

        public Task PlayForAsync(string capabilityId, string patternResourceId, TimeSpan duration,
            CancellationToken cancellationToken)
        {
            EnsureStarted();
            lock (_gate)
            {
                var generation = _simulator.PlayFor(capabilityId, patternResourceId, duration);
                SupersedeLocked(capabilityId);
                var pending = new Pending
                {
                    CapabilityId = capabilityId,
                    Generation = generation,
                    ExpiryTicks = _simulator.NowTicks + (long)duration.TotalMilliseconds,
                    Tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)
                };
                GetPendingLocked(capabilityId).Add(pending);

                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() =>
                    {
                        lock (_gate)
                        {
                            var list = GetPendingLocked(capabilityId);
                            if (list.Remove(pending)) pending.Tcs.TrySetCanceled(cancellationToken);
                        }
                    });
                }
                return pending.Tcs.Task;
            }
        }

        public Task SetPatternAsync(string capabilityId, string patternResourceId, CancellationToken cancellationToken)
        {
            EnsureStarted();
            lock (_gate)
            {
                _simulator.SetPattern(capabilityId, patternResourceId);
                // Persistent commands supersede any timed command on the capability.
                SupersedeLocked(capabilityId);
                return Task.CompletedTask;
            }
        }

        public async Task StopAllAsync(CancellationToken cancellationToken)
        {
            Task monitor;
            lock (_gate)
            {
                _stopped = true;
                _simulator.StopAll();
                foreach (var list in _pendingByCapability.Values)
                {
                    foreach (var pending in list) pending.Tcs.TrySetResult(true);
                    list.Clear();
                }
                _pendingByCapability.Clear();
                _cts?.Cancel();
                monitor = _monitor;
            }
            if (monitor == null || monitor.IsCompleted) return;
            var canceled = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (await Task.WhenAny(monitor, canceled) != monitor) return;
            await monitor;
        }

        public void Dispose()
        {
            StopAllAsync(CancellationToken.None).GetAwaiter().GetResult();
            _cts?.Dispose();
        }

        /// <summary>Current live toy output per capability (for the WPF simulator UI).</summary>
        public IReadOnlyCollection<ToyCapabilityState> Snapshot() => _simulator.Snapshot();

        private void EnsureStarted()
        {
            lock (_gate)
            {
                if (_stopped) return;
                if (_started) return;
                _started = true;
                _cts = new CancellationTokenSource();
                _monitor = Task.Run(() => MonitorLoopAsync(_cts.Token));
            }
        }

        private void SupersedeLocked(string capabilityId)
        {
            if (_pendingByCapability.TryGetValue(capabilityId, out var list))
            {
                foreach (var pending in list) pending.Tcs.TrySetResult(true);
                list.Clear();
            }
        }

        private List<Pending> GetPendingLocked(string capabilityId)
        {
            if (!_pendingByCapability.TryGetValue(capabilityId, out var list))
            {
                list = new List<Pending>();
                _pendingByCapability[capabilityId] = list;
            }
            return list;
        }

        private async Task MonitorLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var due = new List<Pending>();
                lock (_gate)
                {
                    _simulator.Elapse();
                    foreach (var kvp in _pendingByCapability)
                    {
                        var list = kvp.Value;
                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            var pending = list[i];
                            // Natural expiry or superseded (no longer generation owner) -> complete.
                            if (!_simulator.OwnsGeneration(pending.CapabilityId, pending.Generation)
                                || pending.ExpiryTicks <= _simulator.NowTicks)
                            {
                                list.RemoveAt(i);
                                due.Add(pending);
                            }
                        }
                    }
                }
                foreach (var pending in due) pending.Tcs.TrySetResult(true);
                try
                {
                    await _delay(TimeSpan.FromMilliseconds(MonitorIntervalMs), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
