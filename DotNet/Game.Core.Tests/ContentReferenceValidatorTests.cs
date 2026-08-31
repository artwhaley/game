using System;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Core.Tests
{
    [TestFixture]
    public class ContentReferenceValidatorTests
    {
        [Test]
        public void Cutscene_MissingResource_IsRejected()
        {
            var content = ValidContent();
            content.Cards[0].Sequence.Instances.Add(new CutsceneInstanceDefinition
                { Id = "cut-missing", ResourceId = "missing" });
            AssertError(content, "cut-missing", "cutscene", "missing", "expected kind 'cutscene'");
        }

        [Test]
        public void Cutscene_ToyPatternResource_IsRejectedWithActualKind()
        {
            var content = ValidContent();
            content.Cards[0].Sequence.Instances.Add(new CutsceneInstanceDefinition
                { Id = "cut-wrong", ResourceId = "toy-pattern" });
            AssertError(content, "cut-wrong", "toy_pattern", "expected 'cutscene'");
        }

        [Test]
        public void TimedToy_MissingCapability_IsRejected()
        {
            var content = ValidContent();
            content.Cards[0].Sequence.Instances.Add(new ToyActivityInstanceDefinition
                { Id = "timed-cap", CapabilityId = "missing-cap", PatternResourceId = "toy-pattern" });
            AssertError(content, "timed-cap", "missing-cap", "Smart Toy Capability");
        }

        [Test]
        public void TimedToy_CutscenePattern_IsRejected()
        {
            var content = ValidContent();
            content.Cards[0].Sequence.Instances.Add(new ToyActivityInstanceDefinition
                { Id = "timed-wrong", CapabilityId = "vibrate", PatternResourceId = "cutscene" });
            AssertError(content, "timed-wrong", "cutscene", "expected 'toy_pattern'");
        }

        [Test]
        public void SetToy_MissingPattern_IsRejected()
        {
            var content = ValidContent();
            content.Cards[0].Sequence.Instances.Add(new ToySetPatternInstanceDefinition
                { Id = "set-missing", CapabilityId = "vibrate", PatternResourceId = "missing" });
            AssertError(content, "set-missing", "missing", "toy_set_pattern");
        }

        [Test]
        public void DialogFromTags_ZeroTags_IsRejected()
        {
            var content = ValidContent();
            content.Cards[0].Sequence.Instances.Add(new DialogFromTagsInstanceDefinition { Id = "dialog-zero" });
            AssertError(content, "dialog-zero", "at least one Dialog Tag");
        }

        [Test]
        public void DialogFromTags_UnknownTag_IsRejected()
        {
            var content = ValidContent();
            var action = new DialogFromTagsInstanceDefinition { Id = "dialog-unknown" };
            action.RequiredDialogTagIds.Add("missing-tag");
            content.Cards[0].Sequence.Instances.Add(action);
            AssertError(content, "dialog-unknown", "missing-tag", "unknown Dialog Tag");
        }

        [Test]
        public void DialogSnippet_UnknownTag_IsRejected()
        {
            var content = ValidContent();
            content.DialogSnippets[0].DialogTagIds.Add("missing-tag");
            AssertError(content, "snippet", "missing-tag", "unknown Dialog Tag");
        }

        [Test]
        public void DeepPromptChoice_InvalidHostAction_IsRejectedWithOwnerChain()
        {
            var content = ValidContent();
            var inner = new PromptChoiceInstanceDefinition { Id = "inner" };
            var invalid = new ActionSequenceDefinition { Id = "invalid-seq" };
            invalid.Instances.Add(new ToySetPatternInstanceDefinition
                { Id = "deep-set", CapabilityId = "vibrate", PatternResourceId = "missing" });
            inner.Options.Add(new PromptChoiceOptionDefinition { Id = "inner-option", Label = "Inner", Sequence = invalid });
            var outerSequence = new ActionSequenceDefinition { Id = "outer-seq" };
            outerSequence.Instances.Add(inner);
            var outer = new PromptChoiceInstanceDefinition { Id = "outer" };
            outer.Options.Add(new PromptChoiceOptionDefinition { Id = "outer-option", Label = "Outer", Sequence = outerSequence });
            content.Cards[0].Sequence.Instances.Add(outer);

            AssertError(content, "outer-option", "inner-option", "deep-set", "missing");
        }

        [Test]
        public void ValidContent_Passes()
        {
            var content = ValidContent();
            content.Cards[0].Sequence.Instances.Add(new CutsceneInstanceDefinition
                { Id = "cut", ResourceId = "cutscene" });
            content.Cards[0].Sequence.Instances.Add(new ToyActivityInstanceDefinition
                { Id = "timed", CapabilityId = "vibrate", PatternResourceId = "toy-pattern" });
            content.Cards[0].Sequence.Instances.Add(new ToySetPatternInstanceDefinition
                { Id = "set", CapabilityId = "vibrate", PatternResourceId = "toy-pattern", IsBlocking = false });
            var dialog = new DialogFromTagsInstanceDefinition { Id = "dialog" };
            dialog.RequiredDialogTagIds.Add("giggle");
            content.Cards[0].Sequence.Instances.Add(dialog);

            Assert.DoesNotThrow(() => ContentReferenceValidator.Validate(content));
        }

        [Test]
        public void EngineRejectsInvalidDirectSnapshot_BeforeHostDispatch()
        {
            var content = ValidContent();
            content.Cards[0].Sequence.Instances.Add(new CutsceneInstanceDefinition
                { Id = "invalid-engine-cut", ResourceId = "missing" });
            var cutscenes = new FakeCutsceneService();
            var toys = new FakeToyActivityService();
            var services = new CoreServices(new FakeDelayService(), new RecordingLog(),
                cutscene: cutscenes, toyActivity: toys);

            Assert.Throws<InvalidOperationException>(() => new GameSessionEngine(content, "session", services));
            Assert.That(cutscenes.Started, Is.Empty);
            Assert.That(toys.TimedStarted, Is.Empty);
            Assert.That(toys.SetPatterns, Is.Empty);
        }

        private static void AssertError(GameContentDefinition content, params string[] fragments)
        {
            var error = Assert.Throws<InvalidOperationException>(() => ContentReferenceValidator.Validate(content));
            foreach (var fragment in fragments) Assert.That(error.Message, Does.Contain(fragment));
        }

        private static GameContentDefinition ValidContent()
        {
            var content = new GameContentDefinition();
            content.Resources.Add(new ResourceDefinition { Id = "cutscene", Kind = ResourceKinds.Cutscene, Name = "Cut" });
            content.Resources.Add(new ResourceDefinition { Id = "toy-pattern", Kind = ResourceKinds.ToyPattern, Name = "Pulse" });
            content.SmartToyCapabilityDefinitions.Add(new SmartToyCapabilityDefinition { Id = "vibrate", Title = "Vibrate" });
            content.DialogTags.Add(new DialogTagDefinition { Id = "giggle", Title = "giggle" });
            var snippet = new DialogSnippetDefinition { Id = "snippet", Name = "Snippet", Text = "Hello" };
            snippet.DialogTagIds.Add("giggle");
            content.DialogSnippets.Add(snippet);
            content.Cards.Add(new CardDefinition
            {
                Id = "card",
                Title = "Card",
                Sequence = new ActionSequenceDefinition { Id = "card-sequence" }
            });

            var session = new SessionDefinition { Id = "session", Title = "Session" };
            var start = new SessionStartNodeDefinition { Id = "start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "start-out", Kind = GraphPortKind.Normal });
            session.Graph.Nodes.Add(start);
            session.Graph.Nodes.Add(new SessionEndNodeDefinition { Id = "end" });
            session.Graph.Edges.Add(new GraphEdgeDefinition
                { Id = "edge", SourceOutputId = "start-out", TargetNodeId = "end" });
            content.Sessions.Add(session);
            return content;
        }
    }
}
