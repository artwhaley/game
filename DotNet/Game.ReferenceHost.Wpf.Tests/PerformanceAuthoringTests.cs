using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.ReferenceHost.Wpf;

namespace TruthCardGame.ReferenceHost.Wpf.Tests
{
    /// <summary>
    /// Ticket 03 vertical path for the Perform action: the common Action editor
    /// treats the Performance Event as an ordinary FK-backed strict choice, and
    /// buffering/cloning/equality keep the binding without special cases.
    /// </summary>
    [TestFixture]
    [Apartment(System.Threading.ApartmentState.STA)]
    public class PerformanceAuthoringTests
    {
        private static readonly List<ActionParameterOption> Events = new List<ActionParameterOption>
        {
            new ActionParameterOption { Id = "evt-tease", Name = "Playful Tease" },
            new ActionParameterOption { Id = "evt-stern", Name = "Stern Correction" },
        };

        private static CardDefinition PerformCard()
        {
            var card = new CardDefinition { Id = "card-perform", Title = "Performance" };
            card.Sequence = new ActionSequenceDefinition { Id = "seq-perform" };
            card.Sequence.Instances.Add(new PerformInstanceDefinition
                { Id = "perform-1", EventId = "evt-tease", IsBlocking = true });
            card.Sequence.Instances.Add(new WaitForContinueInstanceDefinition { Id = "wait-1", IsBlocking = true });
            return card;
        }

        [Test]
        public void PerformRow_UsesTheStrictChoiceEditorOverPerformanceEvents()
        {
            var owner = new GraphNodeViewModel { Id = "owner" };
            var editor = new ActionSequenceEditorViewModel(owner, "seq-perform", ActionOwnerScope.CardSequence,
                PerformCard().Sequence.Instances, null, null, null,
                performanceEventOptions: Events);

            var row = editor.Rows.Single(item => item.TypeKey == ActionTypeKeys.Perform);
            Assert.That(row.HasStrictChoiceEditor, Is.True, "Perform binds its event through the closed choice editor");
            Assert.That(row.Editor.ChoiceLabel, Is.EqualTo("Performance Event"));
            Assert.That(row.ChoiceOptions.Select(option => option.Id), Is.EqualTo(new[] { "evt-tease", "evt-stern" }));
            Assert.That(row.SelectedChoice?.Id, Is.EqualTo("evt-tease"), "the persisted event survives row projection");
            Assert.That(row.HasBlockingEditor, Is.False, "Perform is always blocking");
            Assert.That(row.ValidationMessage, Is.Null);
        }

        [Test]
        public void PerformRow_FlagsAnUnknownEventAndStopsFlaggingOnceChosen()
        {
            var owner = new GraphNodeViewModel { Id = "owner" };
            var card = PerformCard();
            ((PerformInstanceDefinition)card.Sequence.Instances[0]).EventId = "evt-missing";
            var editor = new ActionSequenceEditorViewModel(owner, "seq-perform", ActionOwnerScope.CardSequence,
                card.Sequence.Instances, null, null, null,
                performanceEventOptions: Events);

            var row = editor.Rows.Single(item => item.TypeKey == ActionTypeKeys.Perform);
            Assert.That(row.ValidationMessage, Is.EqualTo("Select an existing Performance Event."));

            row.TextValue = "evt-stern";
            Assert.That(row.ValidationMessage, Is.Null);
            Assert.That(((PerformInstanceDefinition)row.Definition).EventId, Is.EqualTo("evt-stern"));
        }

        [Test]
        public void CardEditBuffer_TracksAndAppliesThePerformEventBinding()
        {
            var buffer = new CardEditBuffer(PerformCard());
            Assert.That(buffer.IsDirty, Is.False, "a fresh buffer matches the card");
            Assert.That(buffer.ReferencesPerformanceEvent("evt-tease"), Is.True);
            Assert.That(buffer.ReferencesPerformanceEvent("evt-stern"), Is.False);

            buffer.ApplyRowValue("seq-perform", "perform-1", "evt-stern", null);
            Assert.That(buffer.IsDirty, Is.True, "retargeting the event dirties the card");
            Assert.That(buffer.ReferencesPerformanceEvent("evt-stern"), Is.True);

            // Reverting the binding through the buffer's own row edit makes it
            // clean again, which is what dirty-edit preservation depends on.
            buffer.ApplyRowValue("seq-perform", "perform-1", "evt-tease", null);
            Assert.That(buffer.IsDirty, Is.False);
        }

        [Test]
        public void DefaultPerformInstance_PreselectsTheFirstEventAndRequiresOneToExist()
        {
            var owner = new GraphNodeViewModel { Id = "owner" };
            var editor = new ActionSequenceEditorViewModel(owner, "seq-perform", ActionOwnerScope.CardSequence,
                new List<ActionInstanceDefinition>(), null, null, null,
                performanceEventOptions: Events);

            var created = (PerformInstanceDefinition)editor.CreateDefaultInstance(ActionTypeKeys.Perform, "perform-new");
            Assert.That(created.EventId, Is.EqualTo("evt-tease"));
            Assert.That(created.IsBlocking, Is.True);
            Assert.That(ActionAuthoringGuards.CreationError(editor, ActionTypeKeys.Perform), Is.Null);

            var empty = new ActionSequenceEditorViewModel(owner, "seq-empty", ActionOwnerScope.CardSequence,
                new List<ActionInstanceDefinition>(), null, null, null);
            Assert.That(ActionAuthoringGuards.CreationError(empty, ActionTypeKeys.Perform),
                Is.EqualTo("Add a Performance Event before authoring a Perform action."));
        }

        [Test]
        public void BufferedSequenceClone_PreservesPerformInstancesWithoutAliasingTheCard()
        {
            var card = PerformCard();
            var buffer = new CardEditBuffer(card);

            var buffered = buffer.Sequence.Instances.OfType<PerformInstanceDefinition>().Single();
            Assert.That(buffered.Id, Is.EqualTo("perform-1"));
            Assert.That(buffered.EventId, Is.EqualTo("evt-tease"));
            Assert.That(buffered.IsBlocking, Is.True);

            buffered.EventId = "evt-stern";
            Assert.That(((PerformInstanceDefinition)card.Sequence.Instances[0]).EventId, Is.EqualTo("evt-tease"),
                "editing the buffer must not alias the loaded card");
        }

        [Test]
        public void PerformIsOfferedByTheActionPickerInEveryOwnerScope()
        {
            foreach (var scope in new[]
                     {
                         ActionOwnerScope.CardSequence,
                         ActionOwnerScope.PhaseActionSequence,
                         ActionOwnerScope.ChoiceOptionSequence,
                         ActionOwnerScope.SessionDecisionOptionSequence,
                     })
            {
                Assert.That(ActionEditorRegistry.PickerChoices(scope)
                        .Any(choice => choice.TypeKey == ActionTypeKeys.Perform),
                    Is.True, "Perform should be pickable for " + scope);
            }
        }
    }
}
