using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Content.Sqlite;
using TruthCardGame.Core;
using TruthCardGame.Core.Tests;
using TruthCardGame.ReferenceHost.Wpf;

namespace TruthCardGame.ReferenceHost.Wpf.Tests
{
    /// <summary>
    /// Milestone B round-2 verification: the CardEditBuffer semantics and the
    /// composite Save path (one undo step reverting title, body, relations,
    /// AND the Action sequence together).
    /// </summary>
    [TestFixture]
    [Apartment(System.Threading.ApartmentState.STA)]
    public class MilestoneBWpfTests
    {
        private static CardDefinition Card(string id, string title, string[] tags = null, string[] kinks = null,
            string[] equipment = null, string[] capabilities = null)
        {
            var card = new CardDefinition { Id = id, Title = title };
            if (tags != null) card.CardTagIds.AddRange(tags);
            if (kinks != null) card.KinkIds.AddRange(kinks);
            if (equipment != null) card.RequiredEquipmentIds.AddRange(equipment);
            if (capabilities != null) card.RequiredCapabilityIds.AddRange(capabilities);
            card.Sequence = new ActionSequenceDefinition { Id = "seq-" + id };
            card.Sequence.Instances.Add(new WaitForContinueInstanceDefinition { Id = "wait-" + id, IsBlocking = true });
            card.Sequence.Instances.Add(new IncrementProgressInstanceDefinition { Id = "prog-" + id, Amount = 10f });
            return card;
        }

        // ---------- Ticket 15 (carried over): reason mapping + diagnostics ----------

        [Test]
        public void RejectionReasons_MapToTypedKinds()
        {
            var phase = new PhaseDefinition { Id = "p1" };
            phase.MustHaveAllCardTags.Add("tag-a");

            var card = Card("c1", "Rejected", tags: new string[0], kinks: new[] { "k1" },
                equipment: new[] { "eq1" }, capabilities: new[] { "cap1" });
            var profile = new CardSelectionProfile(); // everything missing

            var eligibility = CardEligibilityEngine.EvaluateOne(card, phase, profile);

            Assert.IsFalse(eligibility.IsEligible);
            var kinds = eligibility.Reasons.Select(r => r.Kind).ToList();
            Assert.Contains(CardRejectionReasonKind.MissingAllTag, kinds);
            Assert.Contains(CardRejectionReasonKind.KinkUnconfigured, kinds);
            Assert.Contains(CardRejectionReasonKind.MissingEquipment, kinds);
            Assert.Contains(CardRejectionReasonKind.MissingCapability, kinds);
        }

        [Test]
        public void DiagnosticsWeights_ChangeWithHappiness()
        {
            var tortureCard = Card("c-t", "Torture Card", kinks: new[] { "k1" });
            var profile = new CardSelectionProfile();
            profile.KinkPreferences["k1"] = KinkPreference.Torture;
            var weighting = new SessionCardWeightingDefinition();

            var low = CardWeightCalculator.ComputeWeight(tortureCard, profile, weighting, 0f);
            var high = CardWeightCalculator.ComputeWeight(tortureCard, profile, weighting, 100f);

            Assert.Greater(low, high, "torture weight falls as happiness rises");
            Assert.AreEqual(2f, low, 0.0001f);
            Assert.AreEqual(1f, high, 0.0001f);
        }

        // ---------- Ticket 16 (carried over): play-by-type selection ----------

        [Test]
        public void PlayByType_SelectsUniformlyAmongEligibleSessions()
        {
            var content = new GameContentDefinition();
            content.SessionTypes.Add(new SessionTypeDefinition { Id = "type-a", Title = "A" });
            content.Temperatures.Add(new TemperatureDefinition { Id = "happiness", Title = "Happiness", MinValue = 0f, MaxValue = 100f, DefaultValue = 50f });

            foreach (var (id, title) in new[] { ("s1", "First"), ("s2", "Second") })
            {
                var session = new SessionDefinition { Id = id, Title = title, SessionTypeId = "type-a" };
                session.Graph.Nodes.Add(new SessionStartNodeDefinition { Id = "n-start" });
                session.Graph.Nodes.Add(new SessionEndNodeDefinition { Id = "n-end" });
                session.Graph.Edges.Add(new GraphEdgeDefinition { Id = "e", SourceOutputId = "n-start-out", TargetNodeId = "n-end" });
                session.Graph.Nodes[0].Outputs.Add(new GraphOutputDefinition { Id = "n-start-out", Kind = GraphPortKind.Normal });
                content.Sessions.Add(session);
            }

            var catalog = new ContentCatalog(content);
            var eligibility = new SessionTypeEligibility(catalog);
            var profile = new CardSelectionProfile();

            Assert.IsTrue(eligibility.TrySelectSession("type-a", profile, new FixedRandomSource(0), out var first));
            Assert.AreEqual("s1", first.Id, "offset 0 -> first session");
            Assert.IsTrue(eligibility.TrySelectSession("type-a", profile, new FixedRandomSource(1), out var second));
            Assert.AreEqual("s2", second.Id, "offset 1 -> second session");
        }

        [Test]
        public void PlayByType_TypeRequirements_GateSelection()
        {
            var content = new GameContentDefinition();
            content.SessionTypes.Add(new SessionTypeDefinition
            {
                Id = "type-toy",
                Title = "Toy",
                RequiredCapabilityIds = { "cap-vibrate" },
            });
            content.Temperatures.Add(new TemperatureDefinition { Id = "happiness", Title = "Happiness", MinValue = 0f, MaxValue = 100f, DefaultValue = 50f });
            var session = new SessionDefinition { Id = "s-toy", Title = "Toy", SessionTypeId = "type-toy" };
            session.Graph.Nodes.Add(new SessionStartNodeDefinition { Id = "n-start" });
            session.Graph.Nodes.Add(new SessionEndNodeDefinition { Id = "n-end" });
            session.Graph.Edges.Add(new GraphEdgeDefinition { Id = "e", SourceOutputId = "n-start-out", TargetNodeId = "n-end" });
            session.Graph.Nodes[0].Outputs.Add(new GraphOutputDefinition { Id = "n-start-out", Kind = GraphPortKind.Normal });
            content.Sessions.Add(session);

            var catalog = new ContentCatalog(content);
            var eligibility = new SessionTypeEligibility(catalog);

            var withoutCapability = new CardSelectionProfile();
            Assert.IsFalse(eligibility.TrySelectSession("type-toy", withoutCapability, new FixedRandomSource(0), out _),
                "missing capability blocks selection");

            var withCapability = new CardSelectionProfile();
            withCapability.AvailableCapabilityIds.Add("cap-vibrate");
            Assert.IsTrue(eligibility.TrySelectSession("type-toy", withCapability, new FixedRandomSource(0), out var selected));
            Assert.AreEqual("s-toy", selected.Id);
        }

        // ---------- Round 2: the buffered card editor ----------

        [Test]
        public void CardEditBuffer_StartsClean_AndTracksEveryChange()
        {
            var card = Card("c1", "Original", tags: new[] { "t1" });
            var buffer = new CardEditBuffer(card);

            Assert.IsFalse(buffer.IsDirty, "fresh buffer matches the card");

            buffer.Title = "Renamed";
            Assert.IsTrue(buffer.IsDirty, "title change dirties");
            Assert.IsTrue(buffer.FieldsChanged);
            Assert.IsFalse(buffer.SequenceChanged);

            buffer = new CardEditBuffer(card);
            buffer.BodyText = "Body";
            Assert.IsTrue(buffer.IsDirty, "body change dirties");

            buffer = new CardEditBuffer(card);
            buffer.CardTagIds.Add("t2");
            Assert.IsTrue(buffer.IsDirty, "relation change dirties");

            buffer = new CardEditBuffer(card);
            buffer.ApplyRowValue("prog-c1", null, 25f);
            Assert.IsTrue(buffer.IsDirty, "row value change dirties");
            Assert.IsTrue(buffer.SequenceChanged);
            Assert.IsFalse(buffer.FieldsChanged);
        }

        [Test]
        public void CardEditBuffer_RowOperations_EditTheCloneOnly()
        {
            var card = Card("c1", "Original");
            var buffer = new CardEditBuffer(card);

            buffer.AddAction(TruthCardGame.Content.ActionTypeKeys.Debug,
                id => new DebugInstanceDefinition { Id = id, Message = "hello" });
            Assert.AreEqual(3, buffer.Sequence.Instances.Count, "buffered sequence gained the row");
            Assert.AreEqual(2, card.Sequence.Instances.Count, "the CARD's sequence is untouched");

            buffer.RemoveAction("wait-c1");
            Assert.AreEqual(2, buffer.Sequence.Instances.Count);

            // Move the debug row up one slot.
            var debugId = buffer.Sequence.Instances.First(i => i is DebugInstanceDefinition).Id;
            var debugIndex = buffer.IndexOf(debugId);
            Assert.IsTrue(buffer.MoveAction(debugId, -1));

            buffer.ApplyRowValue(debugId, "changed", null);
            Assert.AreEqual("changed",
                ((DebugInstanceDefinition)buffer.Sequence.Instances.First(i => i is DebugInstanceDefinition)).Message);
        }

        [Test]
        public void CardEditBuffer_DoesNotAliasTheCard()
        {
            var card = Card("c1", "Original");
            var buffer = new CardEditBuffer(card);

            buffer.Title = "Buffered";
            buffer.ApplyRowValue("prog-c1", null, 50f);

            Assert.AreEqual("Original", card.Title, "buffer edits never mutate the definition");
            Assert.AreEqual(10f, ((IncrementProgressInstanceDefinition)card.Sequence.Instances[1]).Amount,
                "sequence clone is a copy, not a reference");
        }

        // ---------- composite Save: one undo step for the whole card ----------

        [Test]
        public void SaveCard_CompositeRoundTrip_AndSingleStepUndo()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), "card-buffer-save-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + dbPath))
                {
                    connection.Open();
                    ConnectionInitializer.Initialize(connection);
                    CoreMigrator.EnsureSchema(connection);

                    CatalogRepositories.CreateCardTag(connection, new CardTagDefinition { Id = "t1", Title = "One" });
                    CatalogRepositories.CreateCardTag(connection, new CardTagDefinition { Id = "t2", Title = "Two" });
                    CatalogRepositories.CreateKink(connection, new KinkDefinition { Id = "k1", Title = "Kink" });

                    var card = Card("c-save", "Original", tags: new[] { "t1" });
                    CardRepository.Create(connection, card);

                    // Simulate the user's buffered edits.
                    var buffer = new CardEditBuffer(card);
                    buffer.Title = "Saved Title";
                    buffer.BodyText = "Saved body.";
                    buffer.CardTagIds.Remove("t1");
                    buffer.CardTagIds.Add("t2");
                    buffer.KinkIds.Add("k1");
                    buffer.AddAction(TruthCardGame.Content.ActionTypeKeys.Debug,
                        id => new DebugInstanceDefinition { Id = id, Message = "added by buffer" });

                    // Commands need a fresh connection per execution (the base
                    // class disposes what the factory returns).
                    System.Data.Common.DbConnection Factory()
                    {
                        var open = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + dbPath);
                        open.Open();
                        ConnectionInitializer.Initialize(open);
                        return open;
                    }

                    // The exact composite Save builds.
                    var commands = new List<IAuthoringCommand>
                    {
                        new RenameCardCommand(Factory, card.Id, "Original", buffer.Title),
                        new SetCardBodyCommand(Factory, card.Id, "", buffer.BodyText),
                        new SetCardRelationsCommand(Factory, card,
                            card.CardTagIds, card.KinkIds, card.RequiredEquipmentIds, card.RequiredCapabilityIds,
                            buffer.CardTagIds, buffer.KinkIds, buffer.RequiredEquipmentIds, buffer.RequiredCapabilityIds),
                        new ReplaceCardSequenceCommand(Factory, card.Id,
                            buffer.OriginalSequence, buffer.Sequence),
                    };
                    var stack = new AuthoringCommandStack();
                    stack.PushOrMerge(new CompositeCommand("Edit card", commands.ToArray()));

                    // Reload and assert every field persisted.
                    var reloaded = GameContentSnapshotLoader.Load(connection).Cards.First(c => c.Id == "c-save");
                    Assert.AreEqual("Saved Title", reloaded.Title);
                    Assert.AreEqual("Saved body.", reloaded.BodyText);
                    CollectionAssert.AreEqual(new[] { "t2" }, reloaded.CardTagIds);
                    CollectionAssert.AreEqual(new[] { "k1" }, reloaded.KinkIds);
                    Assert.AreEqual(3, reloaded.Sequence.Instances.Count, "sequence gained the Debug row");
                    Assert.IsInstanceOf<DebugInstanceDefinition>(reloaded.Sequence.Instances[2]);

                    // ONE undo reverts the entire card edit.
                    stack.Undo();
                    var undone = GameContentSnapshotLoader.Load(connection).Cards.First(c => c.Id == "c-save");
                    Assert.AreEqual("Original", undone.Title, "undo restored title");
                    Assert.AreEqual("", undone.BodyText, "undo restored body");
                    CollectionAssert.AreEqual(new[] { "t1" }, undone.CardTagIds, "undo restored relations");
                    Assert.AreEqual(0, undone.KinkIds.Count);
                    Assert.AreEqual(2, undone.Sequence.Instances.Count, "undo restored the sequence");
                }
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }
        }

        // ---------- carried over: card sequence host builds ----------

        [Test]
        public void CardEditorSequenceHost_BuildsFromCardAndBuffer()
        {
            var card = Card("c1", "Editable");
            var content = new GameContentDefinition();
            content.Cards.Add(card);

            var fromCard = CardEditorSequenceHost.Build(card, content);
            Assert.AreEqual(ActionOwnerScope.CardSequence, fromCard.OwnerScope);
            Assert.AreEqual(2, fromCard.Rows.Count, "Wait + Progress rows");

            var buffer = new CardEditBuffer(card);
            var fromBuffer = CardEditorSequenceHost.Build(buffer.Sequence, card.Id, buffer.Title, content);
            Assert.AreEqual(2, fromBuffer.Rows.Count, "buffer path builds the same editor");
        }
    }
}
