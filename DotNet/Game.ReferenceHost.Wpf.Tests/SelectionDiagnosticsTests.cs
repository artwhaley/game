using System;
using System.IO;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Core;
using TruthCardGame.ReferenceHost.Wpf;

namespace TruthCardGame.ReferenceHost.Wpf.Tests
{
    [TestFixture]
    public sealed class SelectionDiagnosticsTests
    {
        [Test]
        public void RejectionFormatterUsesReadableNamesAndRetainsStableIds()
        {
            var content = new GameContentDefinition();
            content.CardTagDefinitions.Add(new CardTagDefinition { Id = "tag-1", Title = "Warmup" });
            content.KinkDefinitions.Add(new KinkDefinition { Id = "kink-1", Title = "Example Kink" });
            content.EquipmentDefinitions.Add(new EquipmentDefinition { Id = "equipment-1", Title = "Example Equipment" });
            content.SmartToyCapabilityDefinitions.Add(new SmartToyCapabilityDefinition { Id = "cap-1", Title = "Vibration" });

            Assert.That(SelectionDiagnosticsFormatter.Rejection(content,
                new CardRejectionReason(CardRejectionReasonKind.MissingAllTag, "tag-1")),
                Is.EqualTo("missing required tag 'Warmup [tag-1]'"));
            Assert.That(SelectionDiagnosticsFormatter.Rejection(content,
                new CardRejectionReason(CardRejectionReasonKind.KinkDontConsent, "kink-1")),
                Is.EqualTo("kink 'Example Kink [kink-1]' is not consented to"));
            Assert.That(SelectionDiagnosticsFormatter.Rejection(content,
                new CardRejectionReason(CardRejectionReasonKind.MissingEquipment, "missing-equipment")),
                Is.EqualTo("requires missing equipment 'missing-equipment'"));
            Assert.That(SelectionDiagnosticsFormatter.Rejection(content,
                new CardRejectionReason(CardRejectionReasonKind.MissingCapability, "cap-1")),
                Is.EqualTo("requires unavailable smart toy capability 'Vibration [cap-1]'"));
        }

        [Test]
        public void ExistingUnreadableProfileDatabaseThrowsInsteadOfBecomingEmpty()
        {
            var path = Path.Combine(Path.GetTempPath(), "truth-card-invalid-profile-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                File.WriteAllText(path, "this is not a SQLite database");
                Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => UserProfileSelectionLoader.LoadSnapshot(path));
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
