using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using SQLitePCL;
using TruthCardGame.EditorTools;
using TruthCardGame.Performance;

namespace TruthCardGame.Tests
{
    /// <summary>
    /// Ticket 03 acceptance: a tag created or renamed in the Workbench appears in
    /// Unity without copied IDs. These tests prove the Unity-side bridge reads
    /// the vocabulary straight from SQLite as stable IDs, tolerates a database
    /// that has not been migrated yet, and reports references that no longer
    /// resolve instead of silently accepting them.
    /// </summary>
    public class PerformanceTagCatalogTests
    {
        private string _dbPath;

        [SetUp]
        public void SetUp()
        {
            Batteries_V2.Init();
            _dbPath = Path.Combine(Path.GetTempPath(),
                "truthcardgame-tagcat-" + Guid.NewGuid().ToString("N") + ".db");
        }

        [TearDown]
        public void TearDown()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Test]
        public void ReadTags_ReturnsStableIdsTitlesAndRetirement()
        {
            using (var connection = new SqliteConnection("Data Source=" + _dbPath + ";Pooling=False"))
            {
                connection.Open();
                Execute(connection,
                    "CREATE TABLE performance_tag_definition (" +
                    "id TEXT PRIMARY KEY, title TEXT NOT NULL, sort_order INTEGER NOT NULL DEFAULT 0, " +
                    "is_retired INTEGER NOT NULL DEFAULT 0);");
                Execute(connection,
                    "INSERT INTO performance_tag_definition (id, title, sort_order, is_retired) " +
                    "VALUES ('tag-playful', 'Playful', 1, 0);");
                Execute(connection,
                    "INSERT INTO performance_tag_definition (id, title, sort_order, is_retired) " +
                    "VALUES ('tag-stern', 'Stern', 2, 1);");
            }

            var tags = PerformanceTagCatalogBridge.ReadTags(_dbPath);

            Assert.AreEqual(2, tags.Count);
            var playful = tags.Single(tag => tag.Id == "tag-playful");
            Assert.AreEqual("Playful", playful.Title);
            Assert.IsFalse(playful.IsRetired);
            Assert.AreEqual("Playful", playful.DisplayName);

            var stern = tags.Single(tag => tag.Id == "tag-stern");
            Assert.IsTrue(stern.IsRetired);
            Assert.AreEqual("Stern (retired)", stern.DisplayName,
                "retired tags stay listed so existing membership is explainable");
        }

        [Test]
        public void ReadTags_RenamingInTheDatabaseChangesTheUnityFacingTitleWithoutCopiedIds()
        {
            using (var connection = new SqliteConnection("Data Source=" + _dbPath + ";Pooling=False"))
            {
                connection.Open();
                Execute(connection,
                    "CREATE TABLE performance_tag_definition (" +
                    "id TEXT PRIMARY KEY, title TEXT NOT NULL, sort_order INTEGER NOT NULL DEFAULT 0, " +
                    "is_retired INTEGER NOT NULL DEFAULT 0);");
                Execute(connection,
                    "INSERT INTO performance_tag_definition (id, title, sort_order, is_retired) " +
                    "VALUES ('tag-playful', 'Playful', 0, 0);");
            }

            using (var connection = new SqliteConnection("Data Source=" + _dbPath + ";Pooling=False"))
            {
                connection.Open();
                Execute(connection,
                    "UPDATE performance_tag_definition SET title = 'Mischievous' WHERE id = 'tag-playful';");
            }

            var tags = PerformanceTagCatalogBridge.ReadTags(_dbPath);

            Assert.AreEqual(1, tags.Count);
            Assert.AreEqual("tag-playful", tags[0].Id, "the stable ID is what Unity stores");
            Assert.AreEqual("Mischievous", tags[0].Title, "the title follows the database");
        }

        [Test]
        public void ReadTags_ToleratesADatabaseWithoutThePerformanceTables()
        {
            using (var connection = new SqliteConnection("Data Source=" + _dbPath + ";Pooling=False"))
            {
                connection.Open();
                Execute(connection, "CREATE TABLE card (id TEXT PRIMARY KEY, title TEXT NOT NULL);");
            }

            Assert.DoesNotThrow(() => PerformanceTagCatalogBridge.ReadTags(_dbPath));
            Assert.IsEmpty(PerformanceTagCatalogBridge.ReadTags(_dbPath));
        }

        [Test]
        public void Refresh_ReportsAMissingDatabaseWithoutThrowing()
        {
            var missing = Path.Combine(Path.GetTempPath(), "truthcardgame-missing-" + Guid.NewGuid().ToString("N") + ".db");

            var ok = PerformanceTagCatalogBridge.RefreshFrom(missing, out var message);

            Assert.IsTrue(ok, "a missing database is an empty vocabulary, not a failure");
            Assert.IsEmpty(PerformanceTagCatalogBridge.CachedTags);
            StringAssert.Contains(missing, message);
        }

        [Test]
        public void ValidateRegistryTags_FlagsUnknownAndRetiredMembership()
        {
            var tags = new[]
            {
                new PerformanceTagInfo { Id = "tag-playful", Title = "Playful" },
                new PerformanceTagInfo { Id = "tag-stern", Title = "Stern", IsRetired = true },
            };

            var registry = UnityEngine.ScriptableObject.CreateInstance<PerformanceRegistry>();
            registry.ReplaceContents(new PerformanceRegistryView(null,
                new[]
                {
                    PerformanceIngredient("a", "tag-playful"),
                    PerformanceIngredient("b", "tag-stern"),
                    PerformanceIngredient("c", "tag-unknown"),
                }, null, null));

            var problems = PerformanceTagCatalogBridge.ValidateRegistryTags(registry, tags);

            Assert.AreEqual(2, problems.Count);
            Assert.IsTrue(problems.Any(p => p.Contains("retired") && p.Contains("Stern")));
            Assert.IsTrue(problems.Any(p => p.Contains("unknown") && p.Contains("tag-unknown")));
        }

        private static PerformanceIngredientEntry PerformanceIngredient(string name, params string[] tagIds)
        {
            var ingredient = new PerformanceIngredientEntry();
            ingredient.Configure("ing-" + name, name, "body", true, tagIds, new string[0], false, null);
            return ingredient;
        }

        private static void Execute(DbConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
    }
}
