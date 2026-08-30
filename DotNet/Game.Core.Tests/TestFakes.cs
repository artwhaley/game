using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TruthCardGame.Core;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// Returns queued values in order, interpreting each as an offset from
    /// minInclusive (so 0 always selects the range minimum). Throws when
    /// exhausted or when an offset falls outside the requested range.
    /// Floats default to the range minimum (weighted draws take the first
    /// eligible card when no float is scripted).
    /// </summary>
    public sealed class FixedRandomSource : IRandomSource
    {
        private readonly Queue<int> _values;

        public FixedRandomSource(params int[] values)
        {
            _values = new Queue<int>(values);
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (_values.Count == 0)
            {
                throw new InvalidOperationException("FixedRandomSource exhausted.");
            }
            var offset = _values.Dequeue();
            if (offset < 0 || minInclusive + offset >= maxExclusive)
            {
                throw new InvalidOperationException($"FixedRandomSource offset {offset} outside [{minInclusive},{maxExclusive}).");
            }
            return minInclusive + offset;
        }

        public float NextFloat(float minInclusive, float maxExclusive)
        {
            return minInclusive;
        }
    }

    public sealed class RecordingLog : IGameLog
    {
        public readonly List<string> Entries = new List<string>();
        public void Info(string message) => Entries.Add("info:" + message);
        public void Warning(string message) => Entries.Add("warn:" + message);
        public void Error(string message) => Entries.Add("error:" + message);
    }

    /// <summary>
    /// Controllable delay. When Gate is set, DelayAsync stays pending until the
    /// gate completes (or cancels via token); otherwise it completes instantly.
    /// Every call appends "delay:start"/"delay:end" markers to a shared order list.
    /// </summary>
    public sealed class FakeDelayService : IGameDelay
    {
        public TaskCompletionSource<bool> Gate;
        public List<string> Order;
        public int LastDelayCount;

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            LastDelayCount++;
            Order?.Add("delay:start");
            if (Gate != null)
            {
                var tcs = Gate;
                var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
                try
                {
                    await tcs.Task;
                }
                finally
                {
                    registration.Dispose();
                }
            }
            Order?.Add("delay:end");
        }
    }

    /// <summary>Prompt fake: returns queued answers in order; records requests.</summary>
    public sealed class FakePromptService : IPromptService
    {
        private readonly Queue<int?> _answers;
        public readonly List<(string Prompt, IReadOnlyList<string> Options)> Requests =
            new List<(string Prompt, IReadOnlyList<string> Options)>();

        public FakePromptService(params int?[] answers)
        {
            _answers = new Queue<int?>(answers);
        }

        public Task<int?> AskAsync(string prompt, IReadOnlyList<string> options, CancellationToken cancellationToken)
        {
            Requests.Add((prompt, options));
            return Task.FromResult(_answers.Count > 0 ? _answers.Dequeue() : (int?)null);
        }
    }

    /// <summary>Prompt fake that stays pending per request until Answer is called (for busy-state tests).</summary>
    public sealed class GatedPromptService : IPromptService
    {
        private TaskCompletionSource<int?> _pending;

        public int RequestCount { get; private set; }

        public Task<int?> AskAsync(string prompt, IReadOnlyList<string> options, CancellationToken cancellationToken)
        {
            RequestCount++;
            var tcs = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = tcs;
            var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            tcs.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
            return tcs.Task;
        }

        public void Answer(int? index)
        {
            _pending?.TrySetResult(index);
            _pending = null;
        }
    }

    /// <summary>Cutscene fake: playback stays pending until Finish is called; cancellation-aware.</summary>
    public sealed class FakeCutsceneService : ICutsceneService
    {
        public readonly List<string> Started = new List<string>();
        private readonly Queue<TaskCompletionSource<bool>> _gates = new Queue<TaskCompletionSource<bool>>();

        public FakeCutsceneService() { }

        public FakeCutsceneService(params TaskCompletionSource<bool>[] gates)
        {
            foreach (var gate in gates) _gates.Enqueue(gate);
        }

        public async Task PlayAsync(string resourceId, CancellationToken cancellationToken)
        {
            Started.Add(resourceId);
            if (_gates.Count == 0) return;
            var tcs = _gates.Dequeue();
            var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            try
            {
                await tcs.Task;
            }
            finally
            {
                registration.Dispose();
            }
        }

        public void FinishNext()
        {
            // Completes the oldest still-pending gate; used by tests to end "playback".
            foreach (var tcs in _gates)
            {
                if (tcs.Task.Status == TaskStatus.WaitingForActivation)
                {
                    tcs.TrySetResult(true);
                    return;
                }
            }
        }
    }

    public sealed class FakeToyActivityService : IToyActivityService
    {
        public readonly List<(string CapabilityId, string PatternResourceId, TimeSpan Duration)> TimedStarted =
            new List<(string CapabilityId, string PatternResourceId, TimeSpan Duration)>();
        public readonly List<(string CapabilityId, string PatternResourceId)> SetPatterns =
            new List<(string CapabilityId, string PatternResourceId)>();
        public int StopAllCount;

        public Task PlayForAsync(string capabilityId, string patternResourceId, TimeSpan duration,
            CancellationToken cancellationToken)
        {
            TimedStarted.Add((capabilityId, patternResourceId, duration));
            return Task.CompletedTask;
        }

        public Task SetPatternAsync(string capabilityId, string patternResourceId, CancellationToken cancellationToken)
        {
            SetPatterns.Add((capabilityId, patternResourceId));
            return Task.CompletedTask;
        }

        public Task StopAllAsync(CancellationToken cancellationToken)
        {
            StopAllCount++;
            return Task.CompletedTask;
        }
    }

    public sealed class GatedToyActivityService : IToyActivityService
    {
        public readonly TaskCompletionSource<bool> SetAcknowledgement =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> TimedAcknowledgement =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> StopAcknowledgement =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SetCount;
        public int TimedCount;

        public Task PlayForAsync(string capabilityId, string patternResourceId, TimeSpan duration,
            CancellationToken cancellationToken)
        {
            TimedCount++;
            return TimedAcknowledgement.Task;
        }

        public Task SetPatternAsync(string capabilityId, string patternResourceId, CancellationToken cancellationToken)
        {
            SetCount++;
            return SetAcknowledgement.Task;
        }

        public Task StopAllAsync(CancellationToken cancellationToken) => StopAcknowledgement.Task;
    }

    /// <summary>Dialog fake: records presented text; optionally gated for blocking tests.</summary>
    public sealed class FakeDialogService : IDialogService
    {
        public readonly List<string> Shown = new List<string>();

        public Task ShowAsync(string text, CancellationToken cancellationToken)
        {
            Shown.Add(text ?? "");
            return Task.CompletedTask;
        }
    }

    /// <summary>Bundles fakes into CoreServices and exposes the pieces.</summary>
    public static class TestServices
    {
        public static GameContext Create(
            RecordingLog log,
            FakeDelayService delay,
            IPromptService prompts = null,
            ICutsceneService cutscenes = null,
            string playerName = "Test")
        {
            var services = new CoreServices(delay, log, prompts, cutscenes);
            return new GameContext(new Player(playerName), services);
        }
    }
}
