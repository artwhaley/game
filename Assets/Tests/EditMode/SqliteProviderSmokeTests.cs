using System;
using System.Data.Common;
using System.IO;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using SQLitePCL;
using UnityEngine;

namespace TruthCardGame.Tests
{
    /// <summary>
    /// Ticket 00 provider smoke test — the first true Unity test of the
    /// Conversation Performance V1 stack.
    ///
    /// Proves, inside the pinned Unity editor, that:
    ///   1. the vendored Microsoft.Data.Sqlite.Core + SQLitePCLRaw pair loads
    ///      its native e_sqlite3 engine;
    ///   2. the provider it produces is a <see cref="DbConnection"/>, which is
    ///      the exact type GameContentSnapshotLoader.Load requires;
    ///   3. the loader's parameterized-SQL style (@name parameters, explicit
    ///      read transactions, rollback) behaves correctly against it;
    ///   4. Content/GameContent.db opens read-only, passes integrity and
    ///      foreign-key checks, and refuses a write.
    ///
    /// Scope is deliberately the provider only. Loading through
    /// GameContentSnapshotLoader and asserting Content/Core type identity
    /// arrives with the shared-source arrangement for the loader assembly;
    /// this test must pass first so that arrangement builds on proven ground.
    /// </summary>
    public class SqliteProviderSmokeTests
    {
        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string CanonicalDatabasePath =>
            Path.Combine(ProjectRoot, "Content", "GameContent.db");

        [SetUp]
        public void SetUp()
        {
            // We vendor Microsoft.Data.Sqlite.Core rather than the
            // Microsoft.Data.Sqlite metapackage, so nothing initializes
            // SQLitePCLRaw on our behalf. This explicit call is part of the
            // arrangement being proven: if the managed/native wiring is wrong,
            // it must fail here loudly instead of surfacing later as a
            // confusing missing-table error.
            Batteries_V2.Init();
        }

        [Test]
        public void VendoredProvider_LoadsItsNativeEngine_AndReportsVersion()
        {
            using (var connection = new SqliteConnection("Data Source=:memory:"))
            {
                connection.Open();

                Assert.That(connection, Is.InstanceOf<DbConnection>(),
                    "GameContentSnapshotLoader.Load takes a DbConnection");

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT sqlite_version();";
                    var version = command.ExecuteScalar() as string;
                    Assert.That(version, Is.Not.Null.And.Not.Empty,
                        "the native e_sqlite3 engine answered a query");
                    Debug.Log("[SqliteProviderSmoke] e_sqlite3 engine version: " + version);
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA integrity_check;";
                    Assert.AreEqual("ok", command.ExecuteScalar() as string);
                }
            }
        }

        [Test]
        public void DisposableDatabase_RoundTripsTheLoadersParameterStyle()
        {
            var path = Path.Combine(Path.GetTempPath(),
                "truthcardgame-provider-smoke-" + Guid.NewGuid().ToString("N") + ".db");

            try
            {
                // Pooling=False is deliberate. Microsoft.Data.Sqlite pools
                // connections by default, and a pooled connection keeps the
                // database file handle open after Dispose. Left on, that both
                // masks whether the handle was really released and leaves the
                // canonical database locked for WPF after a Unity read.
                using (var connection = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
                {
                    connection.Open();

                    Execute(connection, "PRAGMA foreign_keys = ON;");
                    Execute(connection,
                        "CREATE TABLE action_sequence (id TEXT PRIMARY KEY);");
                    Execute(connection,
                        "CREATE TABLE card (id TEXT PRIMARY KEY, title TEXT NOT NULL, " +
                        "action_sequence_id TEXT NULL REFERENCES action_sequence(id));");

                    // Rolled-back work must leave nothing behind: the loader's
                    // read transaction relies on exactly this guarantee.
                    using (var transaction = connection.BeginTransaction())
                    {
                        Execute(connection, transaction,
                            "INSERT INTO card (id, title, action_sequence_id) VALUES (@id, @title, @seq);",
                            ("id", "card-rolled-back"), ("title", "rolled back"), ("seq", null));
                        transaction.Rollback();
                    }

                    using (var transaction = connection.BeginTransaction())
                    {
                        Execute(connection, transaction,
                            "INSERT INTO action_sequence (id) VALUES (@id);", ("id", "seq-1"));
                        Execute(connection, transaction,
                            "INSERT INTO card (id, title, action_sequence_id) VALUES (@id, @title, @seq);",
                            ("id", "card-sqlite-smoke"), ("title", "Playful Tease"), ("seq", "seq-1"));
                        transaction.Commit();
                    }

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT title FROM card WHERE id = @id;";
                        AddParameter(command, "@id", "card-sqlite-smoke");
                        Assert.AreEqual("Playful Tease", command.ExecuteScalar() as string,
                            "@name parameters resolve for both reads and writes");
                    }

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT COUNT(*) FROM card;";
                        Assert.AreEqual(1L, Convert.ToInt64(command.ExecuteScalar()),
                            "the rolled-back insert left no row behind");
                    }
                }

                Assert.That(File.Exists(path), Is.True, "the disposable database was written to disk");

                // Deleting must work now that the connection is disposed: the
                // loader's contract is to release the database before playback,
                // and a surviving handle would silently break that.
                Assert.DoesNotThrow(() => File.Delete(path),
                    "the connection released the database file when it was disposed");
                Assert.That(File.Exists(path), Is.False, "the disposable database was removed");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void CanonicalContentDatabase_OpensReadOnly_AndPassesIntegrityChecks()
        {
            Assert.That(File.Exists(CanonicalDatabasePath), Is.True,
                "canonical content database not found at " + CanonicalDatabasePath);

            using (var connection = new SqliteConnection(
                "Data Source=" + CanonicalDatabasePath + ";Mode=ReadOnly"))
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA integrity_check;";
                    Assert.AreEqual("ok", command.ExecuteScalar() as string,
                        "canonical integrity_check");
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA foreign_key_check;";
                    using (var reader = command.ExecuteReader())
                    {
                        Assert.IsFalse(reader.Read(), "canonical foreign_key_check reports no violations");
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT MAX(version) FROM core_schema_migration;";
                    var version = command.ExecuteScalar();
                    Assert.That(version, Is.Not.Null.And.Not.EqualTo(DBNull.Value),
                        "core_schema_migration is queryable through the shared schema");
                    Debug.Log("[SqliteProviderSmoke] canonical content schema version: " + version);
                }

                // The canonical database is WPF-owned authored content. Unity
                // opens it read-only, and that must be enforced rather than
                // assumed — a silent write here would corrupt authored content.
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "CREATE TABLE unity_must_not_write (id TEXT);";
                    Assert.Throws<SqliteException>(() => command.ExecuteNonQuery(),
                        "the canonical database must reject writes on this connection");
                }
            }
        }

        private static void Execute(DbConnection connection, string sql,
            params (string Name, object Value)[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                AddParameters(command, parameters);
                command.ExecuteNonQuery();
            }
        }

        private static void Execute(DbConnection connection, DbTransaction transaction, string sql,
            params (string Name, object Value)[] parameters)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                AddParameters(command, parameters);
                command.ExecuteNonQuery();
            }
        }

        private static void AddParameters(DbCommand command, (string Name, object Value)[] parameters)
        {
            foreach (var parameter in parameters)
            {
                AddParameter(command, parameter.Name, parameter.Value);
            }
        }

        /// <summary>Provider-neutral parameter binding, mirroring the loader's Sql helper.</summary>
        private static void AddParameter(DbCommand command, string name, object value)
        {
            var dbParameter = command.CreateParameter();
            dbParameter.ParameterName = name;
            dbParameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(dbParameter);
        }
    }
}
