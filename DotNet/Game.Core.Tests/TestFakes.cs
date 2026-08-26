using System;
using System.Collections.Generic;
using TruthCardGame.Core;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// Returns queued values in order, interpreting each as an offset from
    /// minInclusive (so 0 always selects the range minimum). Throws when
    /// exhausted or when an offset falls outside the requested range.
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
    }

    public sealed class RecordingLog : IGameLog
    {
        public readonly List<string> Entries = new List<string>();
        public void Info(string message) => Entries.Add("info:" + message);
        public void Warning(string message) => Entries.Add("warn:" + message);
        public void Error(string message) => Entries.Add("error:" + message);
    }
}
