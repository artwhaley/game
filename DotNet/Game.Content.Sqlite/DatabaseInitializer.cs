using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// One-shot bootstrap: populates an EMPTY core database from a portable
    /// snapshot, preserving IDs, in a single transaction. Refuses any database
    /// with existing core content and never touches host extension tables
    /// (unity_*, wpf_*). This is NOT the normal Save path — normal editing
    /// updates stable rows transactionally.
    /// </summary>
    public static class DatabaseInitializer
    {
        public static void InitializeEmptyDatabaseFromSnapshot(DbConnection connection, GameContentDefinition content)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (connection.State != ConnectionState.Open) connection.Open();

            CoreMigrator.EnsureSchema(connection);

            if (!IsCoreContentEmpty(connection))
            {
                throw new InvalidOperationException(
                    "DatabaseInitializer: refusing to initialize a non-empty core database. " +
                    "InitializeEmptyDatabaseFromSnapshot is only for blank core content tables.");
            }

            using (var transaction = connection.BeginTransaction())
            {
                var tagIds = InsertTags(connection, transaction, content);
                InsertResources(connection, transaction, content.Resources);
                InsertPhases(connection, transaction, content.Phases, tagIds);
                InsertActions(connection, transaction, content.Actions);
                InsertChoiceOptions(connection, transaction, content.Actions);
                InsertCards(connection, transaction, content.Cards, tagIds);
                InsertDeck(connection, transaction, content.Deck);
                InsertSessions(connection, transaction, content.Sessions, tagIds);
                InsertPhaseSlots(connection, transaction, content.Sessions);
                transaction.Commit();
            }
        }

        private static bool IsCoreContentEmpty(DbConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT (SELECT COUNT(*) FROM session) + (SELECT COUNT(*) FROM phase) + " +
                    "(SELECT COUNT(*) FROM card) + (SELECT COUNT(*) FROM action) + " +
                    "(SELECT COUNT(*) FROM resource) + (SELECT COUNT(*) FROM tag) + " +
                    "(SELECT COUNT(*) FROM card_deck);";
                return Convert.ToInt64(command.ExecuteScalar()) == 0L;
            }
        }

        // Tag ids are the tag names themselves (name is UNIQUE): deterministic,
        // lossless, and round-trips with the loader's string-tag projection.
        private static Dictionary<string, string> InsertTags(DbConnection connection, DbTransaction transaction, GameContentDefinition content)
        {
            var names = new List<string>();
            var seen = new HashSet<string>();

            void CollectTags(List<string> tags)
            {
                if (tags == null) return;
                foreach (var tag in tags)
                {
                    if (string.IsNullOrEmpty(tag)) continue;
                    if (seen.Add(tag)) names.Add(tag);
                }
            }

            foreach (var session in content.Sessions) CollectTags(session?.Tags);
            foreach (var phase in content.Phases)
            {
                if (phase == null) continue;
                CollectTags(phase.MustIncludeTags);
                CollectTags(phase.MustExcludeTags);
            }
            foreach (var card in content.Cards) CollectTags(card?.Tags);

            var tagIds = new Dictionary<string, string>();
            foreach (var name in names)
            {
                Execute(connection, transaction,
                    "INSERT INTO tag (id, name) VALUES (@id, @name);",
                    ("id", name), ("name", name));
                tagIds[name] = name;
            }
            return tagIds;
        }

        private static void InsertResources(DbConnection connection, DbTransaction transaction, List<ResourceDefinition> resources)
        {
            foreach (var resource in resources)
            {
                if (resource == null) continue;
                if (string.IsNullOrEmpty(resource.Id)) throw new InvalidOperationException("DatabaseInitializer: Resource with empty id.");
                Execute(connection, transaction,
                    "INSERT INTO resource (id, kind, name) VALUES (@id, @kind, @name);",
                    ("id", resource.Id), ("kind", resource.Kind), ("name", (object)resource.Name ?? DBNull.Value));
            }
        }

        private static void InsertPhases(DbConnection connection, DbTransaction transaction, List<PhaseDefinition> phases, Dictionary<string, string> tagIds)
        {
            foreach (var phase in phases)
            {
                if (phase == null) continue;
                if (string.IsNullOrEmpty(phase.Id)) throw new InvalidOperationException("DatabaseInitializer: Phase with empty id.");
                Execute(connection, transaction,
                    "INSERT INTO phase (id, title, min_cards, max_cards) VALUES (@id, @title, @min, @max);",
                    ("id", phase.Id), ("title", phase.Title), ("min", phase.MinCards), ("max", phase.MaxCards));

                for (var i = 0; i < phase.MustIncludeTags.Count; i++)
                {
                    Execute(connection, transaction,
                        "INSERT INTO phase_required_tag (phase_id, tag_id, ordinal) VALUES (@phase, @tag, @ordinal);",
                        ("phase", phase.Id), ("tag", tagIds[phase.MustIncludeTags[i]]), ("ordinal", i));
                }
                for (var i = 0; i < phase.MustExcludeTags.Count; i++)
                {
                    Execute(connection, transaction,
                        "INSERT INTO phase_excluded_tag (phase_id, tag_id, ordinal) VALUES (@phase, @tag, @ordinal);",
                        ("phase", phase.Id), ("tag", tagIds[phase.MustExcludeTags[i]]), ("ordinal", i));
                }
            }
        }

        private static void InsertActions(DbConnection connection, DbTransaction transaction, List<GameActionDefinition> actions)
        {
            foreach (var action in actions)
            {
                if (action == null) continue;
                if (string.IsNullOrEmpty(action.Id)) throw new InvalidOperationException("DatabaseInitializer: Action with empty id.");

                var type = TypeFor(action);
                Execute(connection, transaction,
                    "INSERT INTO action (id, name, action_type, is_blocking) VALUES (@id, @name, @type, @blocking);",
                    ("id", action.Id), ("name", (object)action.Name ?? DBNull.Value), ("type", type), ("blocking", action.IsBlocking ? 1 : 0));

                switch (action)
                {
                    case DebugActionDefinition debug:
                        Execute(connection, transaction,
                            "INSERT INTO action_debug (action_id, message, delay_seconds) VALUES (@id, @message, @delay);",
                            ("id", action.Id), ("message", (object)debug.Message ?? DBNull.Value), ("delay", (object)debug.DelaySeconds ?? DBNull.Value));
                        break;
                    case StatIncreaseActionDefinition stat:
                        Execute(connection, transaction,
                            "INSERT INTO action_stat_increase (action_id, stat_key, amount) VALUES (@id, @key, @amount);",
                            ("id", action.Id), ("key", stat.StatKey), ("amount", stat.Amount));
                        break;
                    case ChoiceActionDefinition choice:
                        Execute(connection, transaction,
                            "INSERT INTO action_choice (action_id, prompt) VALUES (@id, @prompt);",
                            ("id", action.Id), ("prompt", choice.Prompt));
                        break;
                    case CutsceneActionDefinition cutscene:
                        if (string.IsNullOrEmpty(cutscene.ResourceId))
                        {
                            throw new InvalidOperationException($"DatabaseInitializer: Cutscene action '{action.Id}' has no resource id.");
                        }
                        Execute(connection, transaction,
                            "INSERT INTO action_cutscene (action_id, resource_id) VALUES (@id, @resource);",
                            ("id", action.Id), ("resource", cutscene.ResourceId));
                        break;
                }
            }
        }

        private static void InsertChoiceOptions(DbConnection connection, DbTransaction transaction, List<GameActionDefinition> actions)
        {
            foreach (var action in actions)
            {
                if (!(action is ChoiceActionDefinition choice)) continue;

                for (var i = 0; i < choice.Options.Count; i++)
                {
                    var option = choice.Options[i];
                    if (option == null) continue;
                    if (string.IsNullOrEmpty(option.Id)) throw new InvalidOperationException($"DatabaseInitializer: Choice option of action '{choice.Id}' has empty id.");
                    Execute(connection, transaction,
                        "INSERT INTO choice_option (id, choice_action_id, ordinal, label, child_action_id) " +
                        "VALUES (@id, @choice, @ordinal, @label, @child);",
                        ("id", option.Id),
                        ("choice", choice.Id),
                        ("ordinal", i),
                        ("label", option.Label),
                        ("child", (object)option.ChildActionId ?? DBNull.Value));
                }
            }
        }

        private static void InsertCards(DbConnection connection, DbTransaction transaction, List<CardDefinition> cards, Dictionary<string, string> tagIds)
        {
            foreach (var card in cards)
            {
                if (card == null) continue;
                if (string.IsNullOrEmpty(card.Id)) throw new InvalidOperationException("DatabaseInitializer: Card with empty id.");
                Execute(connection, transaction,
                    "INSERT INTO card (id, title) VALUES (@id, @title);",
                    ("id", card.Id), ("title", card.Title));

                for (var i = 0; i < card.Tags.Count; i++)
                {
                    Execute(connection, transaction,
                        "INSERT INTO card_tag (card_id, tag_id, ordinal) VALUES (@card, @tag, @ordinal);",
                        ("card", card.Id), ("tag", tagIds[card.Tags[i]]), ("ordinal", i));
                }
                for (var i = 0; i < card.ActionIds.Count; i++)
                {
                    Execute(connection, transaction,
                        "INSERT INTO card_action (card_id, ordinal, action_id) VALUES (@card, @ordinal, @action);",
                        ("card", card.Id), ("ordinal", i), ("action", card.ActionIds[i]));
                }
            }
        }

        private static void InsertDeck(DbConnection connection, DbTransaction transaction, CardDeckDefinition deck)
        {
            if (deck == null) return;
            if (string.IsNullOrEmpty(deck.Id)) throw new InvalidOperationException("DatabaseInitializer: Deck with empty id.");
            Execute(connection, transaction,
                "INSERT INTO card_deck (id, title) VALUES (@id, @title);",
                ("id", deck.Id), ("title", (object)deck.Title ?? DBNull.Value));

            for (var i = 0; i < deck.CardIds.Count; i++)
            {
                Execute(connection, transaction,
                    "INSERT INTO card_deck_card (deck_id, ordinal, card_id) VALUES (@deck, @ordinal, @card);",
                    ("deck", deck.Id), ("ordinal", i), ("card", deck.CardIds[i]));
            }
        }

        private static void InsertSessions(DbConnection connection, DbTransaction transaction, List<SessionDefinition> sessions, Dictionary<string, string> tagIds)
        {
            foreach (var session in sessions)
            {
                if (session == null) continue;
                if (string.IsNullOrEmpty(session.Id)) throw new InvalidOperationException("DatabaseInitializer: Session with empty id.");
                Execute(connection, transaction,
                    "INSERT INTO session (id, title) VALUES (@id, @title);",
                    ("id", session.Id), ("title", session.Title));

                for (var i = 0; i < session.Tags.Count; i++)
                {
                    Execute(connection, transaction,
                        "INSERT INTO session_tag (session_id, tag_id, ordinal) VALUES (@session, @tag, @ordinal);",
                        ("session", session.Id), ("tag", tagIds[session.Tags[i]]), ("ordinal", i));
                }
            }
        }

        private static void InsertPhaseSlots(DbConnection connection, DbTransaction transaction, List<SessionDefinition> sessions)
        {
            foreach (var session in sessions)
            {
                if (session == null) continue;
                for (var i = 0; i < session.PhaseSlots.Count; i++)
                {
                    var slot = session.PhaseSlots[i];
                    if (slot == null) continue;
                    if (string.IsNullOrEmpty(slot.Id)) throw new InvalidOperationException($"DatabaseInitializer: PhaseSlot of session '{session.Id}' has empty id.");
                    Execute(connection, transaction,
                        "INSERT INTO phase_slot (id, session_id, ordinal, title) VALUES (@id, @session, @ordinal, @title);",
                        ("id", slot.Id), ("session", session.Id), ("ordinal", i), ("title", slot.Title));

                    for (var c = 0; c < slot.Candidates.Count; c++)
                    {
                        var candidate = slot.Candidates[c];
                        if (candidate == null) continue;
                        if (string.IsNullOrEmpty(candidate.Id)) throw new InvalidOperationException($"DatabaseInitializer: candidate of slot '{slot.Id}' has empty id.");
                        Execute(connection, transaction,
                            "INSERT INTO phase_slot_candidate (id, phase_slot_id, ordinal, phase_id) VALUES (@id, @slot, @ordinal, @phase);",
                            ("id", candidate.Id), ("slot", slot.Id), ("ordinal", c), ("phase", candidate.PhaseId));
                    }
                }
            }
        }

        private static string TypeFor(GameActionDefinition action)
        {
            if (action is DebugActionDefinition) return ActionType.Debug;
            if (action is StatIncreaseActionDefinition) return ActionType.StatIncrease;
            if (action is ChoiceActionDefinition) return ActionType.Choice;
            if (action is CutsceneActionDefinition) return ActionType.Cutscene;
            throw new InvalidOperationException($"DatabaseInitializer: no action type for {action.GetType().Name}.");
        }

        private static void Execute(DbConnection connection, DbTransaction transaction, string sql, params (string Name, object Value)[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var parameter in parameters)
                {
                    var dbParameter = command.CreateParameter();
                    dbParameter.ParameterName = parameter.Name;
                    dbParameter.Value = parameter.Value ?? DBNull.Value;
                    command.Parameters.Add(dbParameter);
                }
                command.ExecuteNonQuery();
            }
        }
    }
}
