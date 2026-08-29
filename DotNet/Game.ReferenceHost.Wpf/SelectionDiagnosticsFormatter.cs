using System;
using System.Collections.Generic;
using System.Linq;
using TruthCardGame.Content;
using TruthCardGame.Core;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>Resolves typed diagnostic IDs to readable names without losing the stable ID.</summary>
    public static class SelectionDiagnosticsFormatter
    {
        public static string Card(CardDefinition card)
        {
            return Display(card?.Title, card?.Id);
        }

        public static string Phase(PhaseDefinition phase)
        {
            return Display(phase?.Title, phase?.Id);
        }

        public static string Session(SessionDefinition session)
        {
            return Display(session?.Title, session?.Id);
        }

        public static string SessionType(SessionTypeDefinition type)
        {
            return Display(type?.Title, type?.Id);
        }

        public static string CardTag(GameContentDefinition content, string id)
        {
            return Display(Find(content?.CardTagDefinitions, id)?.Title, id);
        }

        public static string Kink(GameContentDefinition content, string id)
        {
            return Display(Find(content?.KinkDefinitions, id)?.Title, id);
        }

        public static string Equipment(GameContentDefinition content, string id)
        {
            return Display(Find(content?.EquipmentDefinitions, id)?.Title, id);
        }

        public static string SmartToyCapability(GameContentDefinition content, string id)
        {
            return Display(Find(content?.SmartToyCapabilityDefinitions, id)?.Title, id);
        }

        public static string Rejection(GameContentDefinition content, CardRejectionReason reason)
        {
            if (reason == null) return "unknown rejection";
            switch (reason.Kind)
            {
                case CardRejectionReasonKind.MissingAllTag:
                    return "missing required tag '" + CardTag(content, reason.Id) + "'";
                case CardRejectionReasonKind.MissingAnyTag:
                    return "has none of the any-of tags (needs '" + CardTag(content, reason.Id) + "' or another)";
                case CardRejectionReasonKind.KinkDontConsent:
                    return "kink '" + Kink(content, reason.Id) + "' is not consented to";
                case CardRejectionReasonKind.KinkUnconfigured:
                    return "kink '" + Kink(content, reason.Id) + "' has no configured preference";
                case CardRejectionReasonKind.MissingEquipment:
                    return "requires missing equipment '" + Equipment(content, reason.Id) + "'";
                case CardRejectionReasonKind.MissingCapability:
                    return "requires unavailable smart toy capability '" + SmartToyCapability(content, reason.Id) + "'";
                default:
                    return reason.Kind + " [" + reason.Id + "]";
            }
        }

        public static string Capabilities(GameContentDefinition content, IEnumerable<string> ids)
        {
            return string.Join(", ", (ids ?? Enumerable.Empty<string>()).Select(id => SmartToyCapability(content, id)));
        }

        public static string Display(string title, string id)
        {
            if (string.IsNullOrEmpty(id)) return string.IsNullOrEmpty(title) ? "<missing>" : title;
            if (string.IsNullOrEmpty(title) || string.Equals(title, id, StringComparison.Ordinal)) return id;
            return title + " [" + id + "]";
        }

        private static CardTagDefinition Find(IEnumerable<CardTagDefinition> definitions, string id)
            => definitions?.FirstOrDefault(definition => definition.Id == id);

        private static KinkDefinition Find(IEnumerable<KinkDefinition> definitions, string id)
            => definitions?.FirstOrDefault(definition => definition.Id == id);

        private static EquipmentDefinition Find(IEnumerable<EquipmentDefinition> definitions, string id)
            => definitions?.FirstOrDefault(definition => definition.Id == id);

        private static SmartToyCapabilityDefinition Find(IEnumerable<SmartToyCapabilityDefinition> definitions, string id)
            => definitions?.FirstOrDefault(definition => definition.Id == id);
    }
}
