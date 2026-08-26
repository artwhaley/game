using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Core-owned tracker for nonblocking ("continuous") actions. Starts
    /// already-created tasks without Task.Run, retains them so faults are
    /// observed rather than becoming unobserved exceptions, and exposes a
    /// drain for deterministic tests/orderly host shutdown.
    ///
    /// Ordinary card progression never waits on this tracker; draining is an
    /// explicit test/shutdown operation only.
    /// </summary>
    public sealed class BackgroundActionTracker
    {
        private readonly object _gate = new object();
        private readonly List<Task> _active = new List<Task>();
        private readonly IGameLog _log;

        public BackgroundActionTracker(IGameLog log)
        {
            _log = log ?? new NullGameLog();
        }

        /// <summary>Number of currently active background tasks.</summary>
        public int ActiveCount
        {
            get
            {
                lock (_gate) return _active.Count;
            }
        }

        /// <summary>Registers an already-running task; faults are logged, never lost.</summary>
        public void Start(Task task)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            lock (_gate) _active.Add(task);
            var _ = ObserveAsync(task);
        }

        private async Task ObserveAsync(Task task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected during teardown; nothing to report.
            }
            catch (Exception ex)
            {
                _log.Error($"[TruthCardGame] Background action faulted: {ex}");
            }
            finally
            {
                lock (_gate) _active.Remove(task);
            }
        }

        /// <summary>Awaits until no background work remains (stable across newly spawned children).</summary>
        public async Task DrainAsync()
        {
            while (true)
            {
                Task[] snapshot;
                lock (_gate) snapshot = _active.ToArray();
                if (snapshot.Length == 0) return;
                try
                {
                    await Task.WhenAll(snapshot);
                }
                catch (OperationCanceledException)
                {
                    // Expected during teardown; completion/faults are handled by the observer.
                }
            }
        }
    }
}
