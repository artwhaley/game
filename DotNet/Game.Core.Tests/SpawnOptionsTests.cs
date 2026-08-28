using System.Collections.Generic;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Content.Samples;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// Ticket 20 spawn seam: WPF/Core session start accepts
    /// SessionSpawnOptions.TemperatureOverrides so a FUTURE profile loader can
    /// supply per-user starting temperatures without touching content. No
    /// override must mean the content definition's default (Happiness 50).
    /// </summary>
    [TestFixture]
    public class SpawnOptionsTests
    {
        private const string Happiness = SampleContent.TemperatureHappiness;

        private GameContentDefinition BuildContent()
        {
            var content = new GameContentDefinition
            {
                SessionTypes = { new SessionTypeDefinition { Id = SampleContent.TypeStandard, Title = "Standard" } },
                Temperatures =
                {
                    new TemperatureDefinition { Id = Happiness, Title = "Happiness", MinValue = 0f, MaxValue = 100f, DefaultValue = 50f },
                },
            };

            var start = new SessionStartNodeDefinition { Id = "n-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "n-start-out", Kind = GraphPortKind.Normal });
            var session = new SessionDefinition { Id = "s1", Title = "Alpha", SessionTypeId = SampleContent.TypeStandard };
            session.Graph.Nodes.Add(start);
            session.Graph.Nodes.Add(new SessionEndNodeDefinition { Id = "n-end" });
            session.Graph.Edges.Add(new GraphEdgeDefinition
            {
                Id = "e1", SourceOutputId = "n-start-out", TargetNodeId = "n-end",
            });
            content.Sessions.Add(session);
            return content;
        }

        [Test]
        public void Engine_WithoutOverride_StartsAtDefinitionDefault()
        {
            var engine = new GameSessionEngine(BuildContent(), "s1",
                new CoreServices(new FakeDelayService(), new RecordingLog(), new FakePromptService()));
            Assert.AreEqual(50f, engine.Temperatures.Get(Happiness), "no override => content default (50)");
            Assert.AreSame(SessionSpawnOptions.Default, engine.SpawnOptions, "default spawn options surfaced");
        }

        [Test]
        public void Engine_WithTemperatureOverride_StartsAtOverride()
        {
            var spawn = new SessionSpawnOptions().OverrideTemperature(Happiness, 5f);
            var engine = new GameSessionEngine(BuildContent(), "s1",
                new CoreServices(new FakeDelayService(), new RecordingLog(), new FakePromptService()), spawn);
            Assert.AreEqual(5f, engine.Temperatures.Get(Happiness), "override flows into the session state");
            Assert.AreSame(spawn, engine.SpawnOptions, "the supplied options are the seam identity");
        }

        [Test]
        public void Engine_OverrideLeavesOtherTemperaturesAtDefaults()
        {
            var spawn = new SessionSpawnOptions().OverrideTemperature(Happiness, 5f);
            var content = BuildContent();
            content.Temperatures.Add(new TemperatureDefinition { Id = "calm", Title = "Calm", MinValue = 0f, MaxValue = 100f, DefaultValue = 30f });
            var engine = new GameSessionEngine(content, "s1",
                new CoreServices(new FakeDelayService(), new RecordingLog(), new FakePromptService()), spawn);
            Assert.AreEqual(30f, engine.Temperatures.Get("calm"), "unlisted temperatures keep their defaults");
        }
    }
}
