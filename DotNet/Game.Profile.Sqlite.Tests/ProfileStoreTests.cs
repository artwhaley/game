using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Core;
using TruthCardGame.Profile;
using TruthCardGame.Profile.Sqlite;

namespace TruthCardGame.Profile.Sqlite.Tests
{
    /// <summary>
    /// Ticket 04 HARD gate: the separate UserProfile.db behaves per
    /// USER-PROFILE-CONTRACT.md — migration creates its own ledger, missing
    /// rows mean Unconfigured/not-owned/unavailable, unknown content ids are
    /// tolerated (not crashed on, not removed), reopen persists everything,
    /// and the profile is completely independent of GameContent.db.
    /// </summary>
    [TestFixture]
    public class ProfileStoreTests
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "profile-tests-" + Guid.NewGuid().ToString("N") + ".db");
            _connection = new SqliteConnection("Data Source=" + _dbPath);
            _connection.Open();
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Test]
        public void EnsureSchema_CreatesOwnLedger_AtVersion1()
        {
            var version = ProfileStore.EnsureSchema(_connection);
            Assert.AreEqual(1, version);
            Assert.AreEqual(ProfileMigrations.MaxVersion, version);

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM profile_schema_migration;";
                Assert.AreEqual(1L, Convert.ToInt64(command.ExecuteScalar()), "one ledger row");
            }
        }

        [Test]
        public void EnsureSchema_IsIdempotent()
        {
            ProfileStore.EnsureSchema(_connection);
            ProfileStore.EnsureSchema(_connection);
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM profile_schema_migration;";
                Assert.AreEqual(1L, Convert.ToInt64(command.ExecuteScalar()));
            }
        }

        [Test]
        public void EmptyProfile_AllKinksUnconfigured_NothingOwned()
        {
            ProfileStore.EnsureSchema(_connection);

            var profile = ProfileStore.Load(_connection);
            Assert.AreEqual(0, profile.KinkPreferences.Count);
            Assert.AreEqual(0, profile.OwnedEquipmentIds.Count);
            Assert.AreEqual(0, profile.AvailableCapabilityIds.Count);
        }

        [Test]
        public void KinkPreference_RoundTrips_AndRemovalMeansUnconfigured()
        {
            ProfileStore.EnsureSchema(_connection);

            ProfileStore.SetKinkPreference(_connection, "kink-1", KinkPreference.Love);
            ProfileStore.SetKinkPreference(_connection, "kink-2", KinkPreference.Torture);
            ProfileStore.SetKinkPreference(_connection, "kink-3", KinkPreference.DontConsent);

            var profile = ProfileStore.Load(_connection);
            Assert.AreEqual(KinkPreference.Love, profile.KinkPreferences["kink-1"]);
            Assert.AreEqual(KinkPreference.Torture, profile.KinkPreferences["kink-2"]);
            Assert.AreEqual(KinkPreference.DontConsent, profile.KinkPreferences["kink-3"]);

            // Removing the row returns the kink to Unconfigured.
            ProfileStore.SetKinkPreference(_connection, "kink-1", null);
            profile = ProfileStore.Load(_connection);
            Assert.IsFalse(profile.KinkPreferences.ContainsKey("kink-1"), "removed row = Unconfigured");
        }

        [Test]
        public void AllFourPreferences_RoundTrip()
        {
            ProfileStore.EnsureSchema(_connection);

            foreach (var preference in new[] { KinkPreference.Love, KinkPreference.Like, KinkPreference.Torture, KinkPreference.DontConsent })
            {
                ProfileStore.SetKinkPreference(_connection, "kink-x", preference);
                Assert.AreEqual(preference, ProfileStore.Load(_connection).KinkPreferences["kink-x"]);
            }
        }

        [Test]
        public void EquipmentAndCapabilities_RoundTrip_ToggleOffRemoves()
        {
            ProfileStore.EnsureSchema(_connection);

            ProfileStore.SetEquipmentOwned(_connection, "eq-1", true);
            ProfileStore.SetCapabilityAvailable(_connection, "cap-1", true);

            var profile = ProfileStore.Load(_connection);
            Assert.IsTrue(profile.OwnedEquipmentIds.Contains("eq-1"));
            Assert.IsTrue(profile.AvailableCapabilityIds.Contains("cap-1"));

            ProfileStore.SetEquipmentOwned(_connection, "eq-1", false);
            ProfileStore.SetCapabilityAvailable(_connection, "cap-1", false);
            profile = ProfileStore.Load(_connection);
            Assert.IsFalse(profile.OwnedEquipmentIds.Contains("eq-1"));
            Assert.IsFalse(profile.AvailableCapabilityIds.Contains("cap-1"));
        }

        [Test]
        public void Reopen_PersistsEverything()
        {
            ProfileStore.EnsureSchema(_connection);
            ProfileStore.SetKinkPreference(_connection, "kink-1", KinkPreference.Like);
            ProfileStore.SetEquipmentOwned(_connection, "eq-1", true);
            ProfileStore.SetCapabilityAvailable(_connection, "cap-1", true);

            _connection.Dispose();
            SqliteConnection.ClearAllPools();

            using (var reopened = new SqliteConnection("Data Source=" + _dbPath))
            {
                reopened.Open();
                ProfileStore.EnsureSchema(reopened);
                var profile = ProfileStore.Load(reopened);
                Assert.AreEqual(KinkPreference.Like, profile.KinkPreferences["kink-1"]);
                Assert.IsTrue(profile.OwnedEquipmentIds.Contains("eq-1"));
                Assert.IsTrue(profile.AvailableCapabilityIds.Contains("cap-1"));
            }
        }

        [Test]
        public void UnknownContentIds_Tolerated_NotRemoved()
        {
            // The profile DB has no cross-database FKs by design: content ids
            // that no longer exist in GameContent.db persist harmlessly.
            ProfileStore.EnsureSchema(_connection);
            ProfileStore.SetKinkPreference(_connection, "kink-deleted-from-content", KinkPreference.Love);
            ProfileStore.SetEquipmentOwned(_connection, "eq-gone", true);

            var profile = ProfileStore.Load(_connection);
            Assert.AreEqual(KinkPreference.Love, profile.KinkPreferences["kink-deleted-from-content"]);
            Assert.IsTrue(profile.OwnedEquipmentIds.Contains("eq-gone"));

            // Re-ensuring schema never removes them either.
            ProfileStore.EnsureSchema(_connection);
            profile = ProfileStore.Load(_connection);
            Assert.AreEqual(1, profile.KinkPreferences.Count);
            Assert.AreEqual(1, profile.OwnedEquipmentIds.Count);
        }

        [Test]
        public void InvalidPreferenceValue_IsRejectedBySchema()
        {
            ProfileStore.EnsureSchema(_connection);
            Assert.Throws<SqliteException>(() =>
            {
                using (var command = _connection.CreateCommand())
                {
                    command.CommandText = "INSERT INTO kink_preference (kink_id, preference) VALUES ('k', 'banana');";
                    command.ExecuteNonQuery();
                }
            }, "preference CHECK allows only the four configured values");
        }

        [Test]
        public void ToSnapshot_MirrorsProfileForSelectionPipeline()
        {
            ProfileStore.EnsureSchema(_connection);
            ProfileStore.SetKinkPreference(_connection, "kink-1", KinkPreference.Love);
            ProfileStore.SetEquipmentOwned(_connection, "eq-1", true);
            ProfileStore.SetCapabilityAvailable(_connection, "cap-1", true);

            var snapshot = ProfileStore.Load(_connection).ToSnapshot();
            Assert.AreEqual(KinkPreference.Love, snapshot.KinkPreferences["kink-1"]);
            Assert.IsTrue(snapshot.OwnedEquipmentIds.Contains("eq-1"));
            Assert.IsTrue(snapshot.AvailableCapabilityIds.Contains("cap-1"));
        }

        [Test]
        public void ProfileIntegrity_CheckPasses()
        {
            ProfileStore.EnsureSchema(_connection);
            ProfileStore.SetKinkPreference(_connection, "kink-1", KinkPreference.Like);

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "PRAGMA integrity_check;";
                Assert.AreEqual("ok", command.ExecuteScalar());
                command.CommandText = "PRAGMA foreign_key_check;";
                using (var reader = command.ExecuteReader())
                {
                    Assert.IsFalse(reader.Read(), "no FK violations");
                }
            }
        }
    }
}
