using System;
using System.Threading;
using System.Threading.Tasks;
using TruthCardGame.Core;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>Cooperative player-side gate; pausing never cancels gameplay.</summary>
    public sealed class ReferencePlayerExecutionPauseGate : IExecutionPauseGate
    {
        private readonly object _sync = new object();
        private TaskCompletionSource<bool> _release;
        private ExecutionCheckpoint _checkpoint;
        private bool _manualPauseRequested;

        public bool IsPaused { get; private set; }
        public bool IsPauseRequested => _manualPauseRequested;
        public ExecutionCheckpoint PausedCheckpoint => _checkpoint;
        public Func<ExecutionCheckpoint, string> AutomaticPauseReason { get; set; }
        public Func<ExecutionCheckpoint, Task<bool>> ResumeGuard { get; set; }
        public event Action<bool, string> StateChanged;

        public async Task WaitAsync(ExecutionCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string reason = null;
            TaskCompletionSource<bool> release;
            lock (_sync)
            {
                if (IsPaused)
                {
                    release = _release;
                }
                else
                {
                    reason = AutomaticPauseReason?.Invoke(checkpoint);
                    if (reason == null && !_manualPauseRequested) return;
                    IsPaused = true;
                    _checkpoint = checkpoint;
                    _release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _manualPauseRequested = false;
                    release = _release;
                }
            }

            StateChanged?.Invoke(true, reason ?? "Paused by author. Click Resume to continue.");
            try
            {
                await release.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                lock (_sync)
                {
                    if (release.Task.IsCompleted && ReferenceEquals(_release, release))
                    {
                        IsPaused = false;
                        _checkpoint = null;
                        _release = null;
                    }
                }
                if (!IsPaused) StateChanged?.Invoke(false, null);
            }
        }

        public void RequestPause()
        {
            lock (_sync)
            {
                if (IsPaused) return;
                _manualPauseRequested = true;
            }
            StateChanged?.Invoke(false, "Pausing after the current action…");
        }

        public async Task<bool> ResumeAsync()
        {
            TaskCompletionSource<bool> release;
            ExecutionCheckpoint checkpoint;
            lock (_sync)
            {
                if (!IsPaused || _release == null) return false;
                release = _release;
                checkpoint = _checkpoint;
            }
            if (ResumeGuard != null && !await ResumeGuard(checkpoint)) return false;
            release.TrySetResult(true);
            return true;
        }

        public void Cancel()
        {
            TaskCompletionSource<bool> release;
            lock (_sync)
            {
                _manualPauseRequested = false;
                release = _release;
                _release = null;
                IsPaused = false;
                _checkpoint = null;
            }
            release?.TrySetCanceled();
            StateChanged?.Invoke(false, null);
        }
    }
}
