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

namespace TruthCardGame.ReferenceHost.Wpf.Tests
{
    /// <summary>
    /// Milestone B WPF-side verification: reason mapping for the diagnostics
    /// view model, the Card editor's sequence host construction, and the
    /// consumer-style Play-by-Type selection flow against a temp DB.
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

        // ---------- Ticket 15: reason mapping is machine-readable, not strings ----------

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

            // Describe() renders readable text for each typed reason.
            foreach (var reason in eligibility.Reasons)
            {
                Assert.IsFalse(string.IsNullOrEmpty(reason.Describe()));
            }
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

        // ---------- Ticket 16: play-by-type selection flow ----------

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

        // ---------- Ticket 13: card editor sequence host builds ----------

        [Test]
        public void CardEditorSequenceHost_BuildsReusableEditor()
        {
            var card = Card("c1", "Editable");
            var content = new GameContentDefinition();
            content.Cards.Add(card);

            var editor = TruthCardGame.ReferenceHost.Wpf.CardEditorSequenceHost.Build(card, content);

            Assert.IsNotNull(editor);
            Assert.AreEqual(ActionOwnerScope.CardSequence, editor.OwnerScope);
            Assert.AreEqual(2, editor.Rows.Count, "Wait + Progress rows");
        }
    }
}
