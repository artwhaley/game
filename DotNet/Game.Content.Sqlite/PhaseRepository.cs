using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Granular reusable-Phase persistence for the future WPF authoring UI.
    /// Phases are independent entities; deleting one that is referenced by a
    /// candidate is blocked with a clear error (the FK backstop still
    /// enforces it). Host extension rows are never touched.
    /// </summary>
    public static class PhaseRepository
    {
        public static List<PhaseDefinition> List(DbConnection connection)
        {
            ConnectionInitializer.Initialize(connection);

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

            var byId = new Dictionary<string, PhaseDefinition>();
            foreach (var phase in phases) byId[phase.Id] = phase;

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT prt.phase_id, t.name FROM phase_required_tag prt " +
                    "JOIN tag t ON t.id = prt.tag_id ORDER BY prt.phase_id, prt.ordinal;";
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
                    "JOIN tag t ON t.id = pet.tag_id ORDER BY pet.phase_id, pet.ordinal;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        byId[reader.GetString(0)].MustExcludeTags.Add(reader.GetString(1));
                    }
                }
            }

            return phases;
        }

        public static PhaseDefinition Get(DbConnection connection, string phaseId)
        {
            foreach (var phase in List(connection))
            {
                if (phase.Id == phaseId) return phase;
            }
            throw new InvalidOperationException($"PhaseRepository: no phase with id '{phaseId}'.");
        }

        public static void Create(DbConnection connection, PhaseDefinition phase)
        {
            if (phase == null) throw new ArgumentNullException(nameof(phase));
            if (string.IsNullOrEmpty(phase.Id)) throw new ArgumentException("Phase id must not be empty.", nameof(phase));
            ConnectionInitializer.Initialize(connection);

            using (var transaction = connection.BeginTransaction())
            {
                Sql.EnsureTags(connection, transaction, phase.MustIncludeTags);
                Sql.EnsureTags(connection, transaction, phase.MustExcludeTags);
                Sql.Execute(connection, transaction,
                    "INSERT INTO phase (id, title, min_cards, max_cards) VALUES (@id, @title, @min, @max);",
                    ("id", phase.Id), ("title", phase.Title), ("min", phase.MinCards), ("max", phase.MaxCards));
                InsertTags(connection, transaction, "phase_required_tag", phase.Id, phase.MustIncludeTags);
                InsertTags(connection, transaction, "phase_excluded_tag", phase.Id, phase.MustExcludeTags);
                transaction.Commit();
            }
        }

        public static void Update(DbConnection connection, string phaseId, string title, int minCards, int maxCards)
        {
            ConnectionInitializer.Initialize(connection);
            Sql.Execute(connection, null,
                "UPDATE phase SET title = @title, min_cards = @min, max_cards = @max WHERE id = @id;",
                ("title", title), ("min", minCards), ("max", maxCards), ("id", phaseId));
        }

        public static void ReplaceMustIncludeTags(DbConnection connection, string phaseId, IReadOnlyList<string> tags)
        {
            ReplaceTags(connection, "phase_required_tag", phaseId, tags);
        }

        public static void ReplaceMustExcludeTags(DbConnection connection, string phaseId, IReadOnlyList<string> tags)
        {
            ReplaceTags(connection, "phase_excluded_tag", phaseId, tags);
        }

        /// <summary>Deletes only when FK rules permit; blocked clearly when referenced by a candidate.</summary>
        public static void Delete(DbConnection connection, string phaseId)
        {
            ConnectionInitializer.Initialize(connection);

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM phase_slot_candidate WHERE phase_id = @id;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@id";
                parameter.Value = phaseId;
                command.Parameters.Add(parameter);
                if (Convert.ToInt64(command.ExecuteScalar()) > 0L)
                {
                    throw new InvalidOperationException(
                        $"PhaseRepository: phase '{phaseId}' is referenced by a PhaseSlot candidate and cannot be deleted.");
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM phase WHERE id = @id;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@id";
                parameter.Value = phaseId;
                command.Parameters.Add(parameter);
                command.ExecuteNonQuery();
            }
        }

        private static void ReplaceTags(DbConnection connection, string table, string phaseId, IReadOnlyList<string> tags)
        {
            ConnectionInitializer.Initialize(connection);
            using (var transaction = connection.BeginTransaction())
            {
                Sql.Execute(connection, transaction,
                    $"DELETE FROM {table} WHERE phase_id = @id;",
                    ("id", phaseId));
                Sql.EnsureTags(connection, transaction, tags);
                InsertTags(connection, transaction, table, phaseId, tags);
                transaction.Commit();
            }
        }

        private static void InsertTags(DbConnection connection, DbTransaction transaction, string table, string phaseId, IReadOnlyList<string> tags)
        {
            if (tags == null) return;
            for (var i = 0; i < tags.Count; i++)
            {
                if (string.IsNullOrEmpty(tags[i])) continue;
                Sql.Execute(connection, transaction,
                    $"INSERT INTO {table} (phase_id, tag_id, ordinal) VALUES (@phase, @tag, @ordinal);",
                    ("phase", phaseId), ("tag", tags[i]), ("ordinal", i));
            }
        }
    }
}
