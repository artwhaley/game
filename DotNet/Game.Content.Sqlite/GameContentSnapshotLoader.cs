using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Reconstructs a complete portable GameContentDefinition from a SQLite
    /// core database. All ordered relationship queries ORDER BY ordinal; IDs
    /// are preserved; shared entities (Phase, Action, Resource) are loaded
    /// once and referenced by ID everywhere. Action mapping fails loudly for
    /// unknown action types, missing subtype rows, and contradictory subtype
    /// state. Host extension tables (unity_*, wpf_*) are ignored.
    /// </summary>
    public static class GameContentSnapshotLoader
    {
        public static GameContentDefinition Load(DbConnection connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));

            ConnectionInitializer.Initialize(connection);
            CoreMigrator.EnsureSchema(connection);

            var content = new GameContentDefinition();
            var tagNames = LoadTags(connection);

            content.Sessions.AddRange(LoadSessions(connection, tagNames));
            LoadSessionTags(connection, content.Sessions, tagNames);

            content.Phases.AddRange(LoadPhases(connection));
            LoadPhaseTags(connection, content.Phases, tagNames);
            LoadPhaseSlots(connection, content.Sessions, content.Phases);

            content.Cards.AddRange(LoadCards(connection));
            LoadCardTags(connection, content.Cards, tagNames);
            LoadCardActions(connection, content.Cards);

            content.Actions.AddRange(LoadActions(connection));
            content.Resources.AddRange(LoadResources(connection));
            content.Deck = LoadDeck(connection);

            return content;
        }

        private static Dictionary<string, string> LoadTags(DbConnection connection)
        {
            var tags = new Dictionary<string, string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, name FROM tag ORDER BY id;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        tags[reader.GetString(0)] = reader.GetString(1);
                    }
                }
            }
            return tags;
        }

        private static List<SessionDefinition> LoadSessions(DbConnection connection, Dictionary<string, string> tagNames)
        {
            var sessions = new List<SessionDefinition>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, title FROM session ORDER BY id;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        sessions.Add(new SessionDefinition { Id = reader.GetString(0), Title = reader.GetString(1) });
                    }
                }
            }
            return sessions;
        }

        private static void LoadSessionTags(DbConnection connection, List<SessionDefinition> sessions, Dictionary<string, string> tagNames)
        {
            var byId = new Dictionary<string, SessionDefinition>();
            foreach (var session in sessions) byId[session.Id] = session;

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT st.session_id, t.name FROM session_tag st " +
                    "JOIN tag t ON t.id = st.tag_id " +
                    "ORDER BY st.session_id, st.ordinal;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        byId[reader.GetString(0)].Tags.Add(reader.GetString(1));
                    }
                }
            }
        }

        private static List<PhaseDefinition> LoadPhases(DbConnection connection)
        {
            var phases = new List<PhaseDefinition>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, title, min_cards, max_cards FROM phase ORDER BY id;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        phases.Add(new PhaseDefinition
                        {
                            Id = reader.GetString(0),
                            Title = reader.GetString(1),
                            MinCards = reader.GetInt32(2),
                            MaxCards = reader.GetInt32(3)
                        });
                    }
                }
            }
            return phases;
        }

        private static void LoadPhaseTags(DbConnection connection, List<PhaseDefinition> phases, Dictionary<string, string> tagNames)
        {
            var byId = new Dictionary<string, PhaseDefinition>();
            foreach (var phase in phases) byId[phase.Id] = phase;

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT prt.phase_id, t.name FROM phase_required_tag prt " +
                    "JOIN tag t ON t.id = prt.tag_id " +
                    "ORDER BY prt.phase_id, prt.ordinal;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        byId[reader.GetString(0)].MustIncludeTags.Add(reader.GetString(1));
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT pet.phase_id, t.name FROM phase_excluded_tag pet " +
                    "JOIN tag t ON t.id = pet.tag_id " +
                    "ORDER BY pet.phase_id, pet.ordinal;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        byId[reader.GetString(0)].MustExcludeTags.Add(reader.GetString(1));
                    }
                }
            }
        }

        private static void LoadPhaseSlots(DbConnection connection, List<SessionDefinition> sessions, List<PhaseDefinition> phases)
        {
            var sessionsById = new Dictionary<string, SessionDefinition>();
            foreach (var session in sessions) sessionsById[session.Id] = session;

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, session_id, ordinal, title FROM phase_slot ORDER BY session_id, ordinal;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var slot = new PhaseSlotDefinition { Id = reader.GetString(0), Title = reader.GetString(3) };
                        sessionsById[reader.GetString(1)].PhaseSlots.Add(slot);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, phase_slot_id, ordinal, phase_id FROM phase_slot_candidate ORDER BY phase_slot_id, ordinal;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var candidate = new PhaseSlotCandidateDefinition { Id = reader.GetString(0), PhaseId = reader.GetString(3) };
                        FindSlot(sessions, reader.GetString(1)).Candidates.Add(candidate);
                    }
                }
            }
        }

        private static PhaseSlotDefinition FindSlot(List<SessionDefinition> sessions, string slotId)
        {
            foreach (var session in sessions)
            {
                foreach (var slot in session.PhaseSlots)
                {
                    if (slot.Id == slotId) return slot;
                }
            }
            throw new InvalidOperationException($"GameContentSnapshotLoader: phase_slot_candidate references unknown phase_slot '{slotId}'.");
        }

        private static List<CardDefinition> LoadCards(DbConnection connection)
        {
            var cards = new List<CardDefinition>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, title FROM card ORDER BY id;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        cards.Add(new CardDefinition { Id = reader.GetString(0), Title = reader.GetString(1) });
                    }
                }
            }
            return cards;
        }

        private static void LoadCardTags(DbConnection connection, List<CardDefinition> cards, Dictionary<string, string> tagNames)
        {
            var byId = new Dictionary<string, CardDefinition>();
            foreach (var card in cards) byId[card.Id] = card;

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT ct.card_id, t.name FROM card_tag ct " +
                    "JOIN tag t ON t.id = ct.tag_id " +
                    "ORDER BY ct.card_id, ct.ordinal;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        byId[reader.GetString(0)].Tags.Add(reader.GetString(1));
                    }
                }
            }
        }

        private static void LoadCardActions(DbConnection connection, List<CardDefinition> cards)
        {
            var byId = new Dictionary<string, CardDefinition>();
            foreach (var card in cards) byId[card.Id] = card;

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT card_id, ordinal, action_id FROM card_action ORDER BY card_id, ordinal;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        byId[reader.GetString(0)].ActionIds.Add(reader.GetString(2));
                    }
                }
            }
        }

        private static List<GameActionDefinition> LoadActions(DbConnection connection)
        {
            var bases = new List<(string Id, string Name, string Type, bool IsBlocking)>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, name, action_type, is_blocking FROM action ORDER BY id;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        bases.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetString(2), reader.GetInt32(3) != 0));
                    }
                }
            }

            var subtypeTableByAction = new Dictionary<string, string>();
            var debugRows = LoadDebugRows(connection, subtypeTableByAction);
            var statRows = LoadStatRows(connection, subtypeTableByAction);
            var choiceRows = LoadChoiceRows(connection, subtypeTableByAction);
            var cutsceneRows = LoadCutsceneRows(connection, subtypeTableByAction);
            var choiceOptions = LoadChoiceOptions(connection);

            var actions = new List<GameActionDefinition>();
            foreach (var (id, name, type, isBlocking) in bases)
            {
                var expectedTable = TypeToTable(type, id);
                if (!subtypeTableByAction.TryGetValue(id, out var actualTable))
                {
                    throw new InvalidOperationException(
                        $"GameContentSnapshotLoader: action '{id}' has type '{type}' but no {expectedTable} row.");
                }
                if (actualTable != expectedTable)
                {
                    throw new InvalidOperationException(
                        $"GameContentSnapshotLoader: action '{id}' declares type '{type}' but has a {actualTable} row (contradictory subtype state).");
                }

                switch (type)
                {
                    case ActionType.Debug:
                        actions.Add(new DebugActionDefinition
                        {
                            Id = id,
                            Name = name,
                            IsBlocking = isBlocking,
                            Message = debugRows[id].Message,
                            DelaySeconds = debugRows[id].DelaySeconds
                        });
                        break;
                    case ActionType.StatIncrease:
                        actions.Add(new StatIncreaseActionDefinition
                        {
                            Id = id,
                            Name = name,
                            IsBlocking = isBlocking,
                            StatKey = statRows[id].StatKey,
                            Amount = statRows[id].Amount
                        });
                        break;
                    case ActionType.Choice:
                        var choice = new ChoiceActionDefinition
                        {
                            Id = id,
                            Name = name,
                            IsBlocking = isBlocking,
                            Prompt = choiceRows[id]
                        };
                        if (choiceOptions.TryGetValue(id, out var options))
                        {
                            choice.Options.AddRange(options);
                        }
                        actions.Add(choice);
                        break;
                    case ActionType.Cutscene:
                        actions.Add(new CutsceneActionDefinition
                        {
                            Id = id,
                            Name = name,
                            IsBlocking = isBlocking,
                            ResourceId = cutsceneRows[id]
                        });
                        break;
                }
            }

            return actions;
        }

        private static string TypeToTable(string type, string actionId)
        {
            switch (type)
            {
                case ActionType.Debug: return "action_debug";
                case ActionType.StatIncrease: return "action_stat_increase";
                case ActionType.Choice: return "action_choice";
                case ActionType.Cutscene: return "action_cutscene";
                default:
                    throw new InvalidOperationException(
                        $"GameContentSnapshotLoader: unknown action_type '{type}' for action '{actionId}'.");
            }
        }

        private static Dictionary<string, (string Message, float DelaySeconds)> LoadDebugRows(DbConnection connection, Dictionary<string, string> subtypeTableByAction)
        {
            var rows = new Dictionary<string, (string, float)>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT action_id, message, delay_seconds FROM action_debug;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var id = reader.GetString(0);
                        rows[id] = (reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? 0f : reader.GetFloat(2));
                        RecordSubtype(subtypeTableByAction, id, "action_debug");
                    }
                }
            }
            return rows;
        }

        private static Dictionary<string, (string StatKey, int Amount)> LoadStatRows(DbConnection connection, Dictionary<string, string> subtypeTableByAction)
        {
            var rows = new Dictionary<string, (string, int)>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT action_id, stat_key, amount FROM action_stat_increase;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var id = reader.GetString(0);
                        rows[id] = (reader.GetString(1), reader.GetInt32(2));
                        RecordSubtype(subtypeTableByAction, id, "action_stat_increase");
                    }
                }
            }
            return rows;
        }

        private static Dictionary<string, string> LoadChoiceRows(DbConnection connection, Dictionary<string, string> subtypeTableByAction)
        {
            var rows = new Dictionary<string, string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT action_id, prompt FROM action_choice;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var id = reader.GetString(0);
                        rows[id] = reader.GetString(1);
                        RecordSubtype(subtypeTableByAction, id, "action_choice");
                    }
                }
            }
            return rows;
        }

        private static Dictionary<string, string> LoadCutsceneRows(DbConnection connection, Dictionary<string, string> subtypeTableByAction)
        {
            var rows = new Dictionary<string, string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT action_id, resource_id FROM action_cutscene;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var id = reader.GetString(0);
                        rows[id] = reader.GetString(1);
                        RecordSubtype(subtypeTableByAction, id, "action_cutscene");
                    }
                }
            }
            return rows;
        }

        private static void RecordSubtype(Dictionary<string, string> subtypeTableByAction, string actionId, string tableName)
        {
            if (subtypeTableByAction.TryGetValue(actionId, out var existing) && existing != tableName)
            {
                throw new InvalidOperationException(
                    $"GameContentSnapshotLoader: action '{actionId}' has rows in both {existing} and {tableName} (contradictory subtype state).");
            }
            subtypeTableByAction[actionId] = tableName;
        }

        private static Dictionary<string, List<ChoiceOptionDefinition>> LoadChoiceOptions(DbConnection connection)
        {
            var options = new Dictionary<string, List<ChoiceOptionDefinition>>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT id, choice_action_id, ordinal, label, child_action_id FROM choice_option " +
                    "ORDER BY choice_action_id, ordinal;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var choiceId = reader.GetString(1);
                        if (!options.TryGetValue(choiceId, out var list))
                        {
                            list = new List<ChoiceOptionDefinition>();
                            options[choiceId] = list;
                        }
                        list.Add(new ChoiceOptionDefinition
                        {
                            Id = reader.GetString(0),
                            Label = reader.GetString(3),
                            ChildActionId = reader.IsDBNull(4) ? null : reader.GetString(4)
                        });
                    }
                }
            }
            return options;
        }

        private static List<ResourceDefinition> LoadResources(DbConnection connection)
        {
            var resources = new List<ResourceDefinition>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, kind, name FROM resource ORDER BY id;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        resources.Add(new ResourceDefinition
                        {
                            Id = reader.GetString(0),
                            Kind = reader.GetString(1),
                            Name = reader.IsDBNull(2) ? null : reader.GetString(2)
                        });
                    }
                }
            }
            return resources;
        }

        private static CardDeckDefinition LoadDeck(DbConnection connection)
        {
            string deckId = null;
            string deckTitle = null;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, title FROM card_deck;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (deckId != null)
                        {
                            throw new InvalidOperationException(
                                "GameContentSnapshotLoader: more than one card_deck row; the current game model supports a single deck.");
                        }
                        deckId = reader.GetString(0);
                        deckTitle = reader.IsDBNull(1) ? null : reader.GetString(1);
                    }
                }
            }

            var deck = new CardDeckDefinition { Id = deckId ?? "", Title = deckTitle };
            if (deckId == null) return deck;

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT deck_id, ordinal, card_id FROM card_deck_card ORDER BY deck_id, ordinal;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        deck.CardIds.Add(reader.GetString(2));
                    }
                }
            }
            return deck;
        }
    }
}
