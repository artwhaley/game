using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Narrow v2 repository: SessionType rows. Stable IDs, no generic
    /// abstraction — just the domain operations the Workbench needs.
    /// </summary>
    public static class SessionTypeRepository
    {
        public static void Create(DbConnection connection, string id, string title)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("SessionType id required.", nameof(id));
            Sql.Execute(connection, null,
                "INSERT OR IGNORE INTO session_type (id, title) VALUES (@id, @title);",
                ("id", id), ("title", (object)title ?? DBNull.Value));
        }

        public static void Rename(DbConnection connection, string id, string title)
        {
            Sql.Execute(connection, null,
                "UPDATE session_type SET title = @title WHERE id = @id;",
                ("title", (object)title ?? DBNull.Value), ("id", id));
        }

        public static List<SessionTypeDefinition> List(DbConnection connection)
        {
            var result = new List<SessionTypeDefinition>();
            Sql.QueryAll(connection, "SELECT id, title FROM session_type ORDER BY id;",
                reader => result.Add(new SessionTypeDefinition
                {
                    Id = reader.GetString(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                }));
            return result;
        }
    }

    /// <summary>Narrow v2 repository: Temperature definitions.</summary>
    public static class TemperatureRepository
    {
        public static void Create(DbConnection connection, TemperatureDefinition temperature)
        {
            if (temperature == null) throw new ArgumentNullException(nameof(temperature));
            if (string.IsNullOrEmpty(temperature.Id)) throw new ArgumentException("Temperature id required.", nameof(temperature));

            Sql.Execute(connection, null,
                "INSERT OR IGNORE INTO temperature_definition (id, title, min_value, max_value, default_value) " +
                "VALUES (@id, @title, @min, @max, @defaultValue);",
                ("id", temperature.Id), ("title", (object)temperature.Title ?? DBNull.Value),
                ("min", (double)temperature.MinValue), ("max", (double)temperature.MaxValue),
                ("defaultValue", (double)temperature.DefaultValue));
        }

        public static void Update(DbConnection connection, TemperatureDefinition temperature)
        {
            if (temperature == null) throw new ArgumentNullException(nameof(temperature));
            Sql.Execute(connection, null,
                "UPDATE temperature_definition SET title = @title, min_value = @min, max_value = @max, default_value = @defaultValue " +
                "WHERE id = @id;",
                ("title", (object)temperature.Title ?? DBNull.Value),
                ("min", (double)temperature.MinValue), ("max", (double)temperature.MaxValue),
                ("defaultValue", (double)temperature.DefaultValue), ("id", temperature.Id));
        }

        public static List<TemperatureDefinition> List(DbConnection connection)
        {
            var result = new List<TemperatureDefinition>();
            Sql.QueryAll(connection, "SELECT id, title, min_value, max_value, default_value FROM temperature_definition ORDER BY id;",
                reader => result.Add(new TemperatureDefinition
                {
                    Id = reader.GetString(0),
                    Title = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    MinValue = Convert.ToSingle(reader.GetValue(2)),
                    MaxValue = Convert.ToSingle(reader.GetValue(3)),
                    DefaultValue = Convert.ToSingle(reader.GetValue(4)),
                }));
            return result;
        }
    }

    /// <summary>
    /// Narrow v2 repository: Phase exits (the export sockets of a phase graph).
    /// Deleting an exit cascades projected session sockets and their edges —
    /// the v2 live-projection behavior proven by SchemaV2MigrationTests.
    /// </summary>
    public static class PhaseExitRepository
    {
        public static void Create(DbConnection connection, string phaseId, PhaseExitDefinition exit, int ordinal)
        {
            if (string.IsNullOrEmpty(phaseId)) throw new ArgumentException("Phase id required.", nameof(phaseId));
            if (exit == null) throw new ArgumentNullException(nameof(exit));
            if (string.IsNullOrEmpty(exit.Id)) throw new ArgumentException("Exit id required.", nameof(exit));

            Sql.Execute(connection, null,
                "INSERT INTO phase_exit (id, phase_id, ordinal, name) VALUES (@id, @phase, @ordinal, @name);",
                ("id", exit.Id), ("phase", phaseId), ("ordinal", ordinal), ("name", exit.Name));
        }

        public static List<PhaseExitDefinition> List(DbConnection connection, string phaseId)
        {
            var result = new List<PhaseExitDefinition>();
            Sql.QueryAll(connection, "SELECT id, name FROM phase_exit WHERE phase_id = @phase ORDER BY ordinal;",
                reader => result.Add(new PhaseExitDefinition
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                }),
                ("phase", phaseId));
            return result;
        }

        public static void Delete(DbConnection connection, string exitId)
        {
            Sql.Execute(connection, null, "DELETE FROM phase_exit WHERE id = @id;", ("id", exitId));
        }

        /// <summary>Deletes an exit and clears its editable references and projected edges atomically.</summary>
        public static void ForceDelete(DbConnection connection, string exitId)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "DELETE FROM session_graph_edge WHERE source_port_id IN " +
                        "(SELECT id FROM session_node_output WHERE phase_exit_id = @exit);",
                        ("exit", exitId));
                    Sql.Execute(connection, transaction,
                        "UPDATE action_instance_phase_goto SET phase_exit_id = NULL WHERE phase_exit_id = @exit;",
                        ("exit", exitId));
                    Sql.Execute(connection, transaction,
                        "DELETE FROM phase_exit WHERE id = @exit;", ("exit", exitId));
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public static void Rename(DbConnection connection, string exitId, string name)
        {
            if (string.IsNullOrEmpty(exitId)) throw new ArgumentException("Exit id required.", nameof(exitId));
            Sql.Execute(connection, null,
                "UPDATE phase_exit SET name = @name WHERE id = @id;",
                ("name", (object)name ?? DBNull.Value), ("id", exitId));
        }

        /// <summary>
        /// Live projection sync (Ticket 15): after a phase gains a new exit, insert
        /// one projected phase_exit socket row on every Session-graph placement of
        /// that phase. Socket IDs are stable per placement+exit
        /// ({nodeId}-exit-{exitId}), so renaming never breaks wiring and re-sync is
        /// idempotent. Exit deletion needs no sync: the schema's ON DELETE CASCADE
        /// chain removes the projected sockets and their edges in one transaction.
        /// </summary>
        public static void SyncProjectedSockets(DbConnection connection, string phaseId, string exitId, int ordinal)
        {
            if (string.IsNullOrEmpty(phaseId)) throw new ArgumentException("Phase id required.", nameof(phaseId));
            if (string.IsNullOrEmpty(exitId)) throw new ArgumentException("Exit id required.", nameof(exitId));

            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Sql.Execute(connection, transaction,
                        "INSERT INTO session_node_output (id, node_id, port_kind, ordinal, label, phase_exit_id, session_goto_action_instance_id) " +
                        "SELECT n.id || '-exit-' || @exit, n.id, 'phase_exit', @ordinal, NULL, @exit, NULL " +
                        "FROM session_node_phase p JOIN session_graph_node n ON n.id = p.node_id " +
                        "WHERE p.phase_id = @phase AND NOT EXISTS (" +
                        "SELECT 1 FROM session_node_output o WHERE o.id = n.id || '-exit-' || @exit);",
                        ("phase", phaseId), ("exit", exitId), ("ordinal", ordinal));
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }
    }
}
