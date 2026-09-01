using System;
using System.IO;
using NUnit.Framework;

namespace TruthCardGame.ReferenceHost.Wpf.Tests
{
    /// <summary>
    /// WPF UI tests show MainWindow, whose Loaded handler migrates and loads
    /// content. Keep that expected write isolated from the tracked canonical
    /// database. The variable name matches the other SQLite test assemblies.
    /// </summary>
    [SetUpFixture]
    public sealed class CanonicalDatabaseIsolation
    {
        private const string OverrideVariable = "SQLITE_CANONICAL_DB";
        private string _previousOverride;
        private string _temporaryDatabase;

        [OneTimeSetUp]
        public void SetUpDatabaseOverride()
        {
            var canonical = Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "..", "Content", "GameContent.db"));
            Assert.That(File.Exists(canonical), Is.True, "Canonical content database is missing: " + canonical);

            _temporaryDatabase = Path.Combine(
                Path.GetTempPath(), "gwb-wpf-test-content-" + Guid.NewGuid().ToString("N") + ".db");
            File.Copy(canonical, _temporaryDatabase);

            _previousOverride = Environment.GetEnvironmentVariable(OverrideVariable);
            Environment.SetEnvironmentVariable(OverrideVariable, _temporaryDatabase);
        }

        [OneTimeTearDown]
        public void RestoreDatabaseOverride()
        {
            Environment.SetEnvironmentVariable(OverrideVariable, _previousOverride);

            if (string.IsNullOrEmpty(_temporaryDatabase)) return;

            try
            {
                File.Delete(_temporaryDatabase);
                File.Delete(_temporaryDatabase + "-wal");
                File.Delete(_temporaryDatabase + "-shm");
            }
            catch (IOException ex)
            {
                TestContext.Progress.WriteLine("Could not delete temporary WPF test database: " + ex.Message);
            }
        }
    }
}
