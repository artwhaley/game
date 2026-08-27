using System;
using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    /// <summary>
    /// One saved continuation frame — the exact state RETURN restores. Holds the
    /// graph locus (phase node or session node), the active PhaseRun reference
    /// (progress/RNG/history resume untouched), the action chain with next
    /// indices, and the card context if a card sequence was interrupted.
    /// Explicit data, not recursive C# calls, so the flow is inspectable/debuggable.
    /// </summary>
    public sealed class ContinuationFrame
    {
        /// <summary>The phase graph node to resume at (null for session-level frames).</summary>
        public GraphNodeDefinition GraphLocus { get; }

        /// <summary>The PhaseRun to restore as active (null when resuming a session-level sequence).</summary>
        public PhaseRun PhaseRun { get; }

        /// <summary>Resume points, innermost sequence first.</summary>
        public IReadOnlyList<ContinuationPoint> Chain { get; }

        /// <summary>The card whose sequence was interrupted (null otherwise).</summary>
        public CardDefinition Card { get; }

        public ContinuationFrame(GraphNodeDefinition graphLocus, PhaseRun phaseRun, IReadOnlyList<ContinuationPoint> chain, CardDefinition card)
        {
            GraphLocus = graphLocus;
            PhaseRun = phaseRun;
            Chain = chain ?? Array.Empty<ContinuationPoint>();
            Card = card;
        }
    }

    /// <summary>
    /// The explicit VM continuation stack. RETURN pops the newest frame and
    /// resumes it exactly; empty-stack RETURN is a clear runtime error;
    /// EndSession clears the whole stack.
    /// </summary>
    public sealed class ContinuationStack
    {
        private readonly List<ContinuationFrame> _frames = new List<ContinuationFrame>();

        public int Count => _frames.Count;

        public bool IsEmpty => _frames.Count == 0;

        public void Push(ContinuationFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            _frames.Add(frame);
        }

        public ContinuationFrame Peek()
        {
            if (_frames.Count == 0)
            {
                throw new InvalidOperationException("RETURN with an empty continuation stack is a runtime error.");
            }
            return _frames[_frames.Count - 1];
        }

        public ContinuationFrame Pop()
        {
            var frame = Peek();
            _frames.RemoveAt(_frames.Count - 1);
            return frame;
        }

        public void Clear()
        {
            _frames.Clear();
        }
    }
}
