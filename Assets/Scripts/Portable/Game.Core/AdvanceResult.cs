using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    public enum AdvanceResultKind
    {
        /// <summary>The session became complete.</summary>
        SessionCompleted,

        /// <summary>An authored WaitForContinue action paused the current run.</summary>
        WaitForContinue,

        /// <summary>A second advance arrived while one was already running; no work was started.</summary>
        BusyIgnored
    }

    public sealed class AdvanceResult
    {
        public AdvanceResultKind Kind { get; }
        public CardDefinition Card { get; }

        public AdvanceResult(AdvanceResultKind kind, CardDefinition card = null)
        {
            Kind = kind;
            Card = card;
        }
    }
}
