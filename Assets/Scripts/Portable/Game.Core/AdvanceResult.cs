using TruthCardGame.Content;

namespace TruthCardGame.Core
{
    public enum AdvanceResultKind
    {
        /// <summary>Exactly one card finished and the session remains active.</summary>
        CardCompleted,

        /// <summary>The session became complete. Card holds the finishing card, or null when completion came from skipping no-match phases.</summary>
        SessionCompleted,

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
