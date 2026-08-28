using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>Card-draw randomness backed by UnityEngine.Random.Range, matching baseline draw semantics.</summary>
    public sealed class UnityRandomSource : TruthCardGame.Core.IRandomSource
    {
        public int NextInt(int minInclusive, int maxExclusive)
        {
            return UnityEngine.Random.Range(minInclusive, maxExclusive);
        }

        public float NextFloat(float minInclusive, float maxExclusive)
        {
            return UnityEngine.Random.Range(minInclusive, maxExclusive);
        }
    }

    /// <summary>Scaled-game-time delay (WaitForSeconds equivalent), main-thread via Task.Yield.</summary>
    public sealed class UnityGameDelay : TruthCardGame.Core.IGameDelay
    {
        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var endTime = Time.time + (float)delay.TotalSeconds;
            while (Time.time < endTime)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }
        }
    }

    /// <summary>Routes Core logging to Unity console.</summary>
    public sealed class UnityGameLog : TruthCardGame.Core.IGameLog
    {
        public void Info(string message) => Debug.Log(message);
        public void Warning(string message) => Debug.LogWarning(message);
        public void Error(string message) => Debug.LogError(message);
    }

    /// <summary>
    /// Bridges GamePanel's choice overlay to the Core Task-based prompt
    /// contract. Resolves exactly once; cancellation cancels the pending task
    /// and hides the overlay. Continuations stay on the Unity main thread.
    /// </summary>
    public sealed class UnityPromptService : TruthCardGame.Core.IPromptService
    {
        private readonly GamePanel _panel;

        public UnityPromptService(GamePanel panel)
        {
            _panel = panel;
        }

        public Task<int?> AskAsync(string prompt, IReadOnlyList<string> options, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<int?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            if (cancellationToken.CanBeCanceled)
            {
                var registration = cancellationToken.Register(() =>
                {
                    _panel.HidePrompt();
                    completion.TrySetCanceled(cancellationToken);
                });
                completion.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
            }

            _panel.ShowPrompt(prompt, options, chosen => completion.TrySetResult(chosen));
            return completion.Task;
        }
    }
}
