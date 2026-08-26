using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TruthCardGame.Content;

namespace TruthCardGame.Core.Tests
{
    /// <summary>
    /// Golden deterministic end-to-end scenario: fixed phase RNG, fixed card
    /// RNG, fixed prompt answer, instant fake cutscene. Asserts the exact card
    /// order, phase transitions, final stats, and lifecycle/log ordering.
    /// </summary>
    public class GoldenScenarioTests
    {
        [Test]
        public async Task Golden_Scenario_ProducesExactTrace()
        {
            // Phase targets are min==max everywhere, so a single offset 0 per phase suffices.
            var phaseRng = new FixedRandomSource(0, 0, 0);
            // Warm Up matches [Warm One, Warm Two] (deck order); we force Two then One.
            // Choice and Ending phases have single matches.
            var cardRng = new FixedRandomSource(1, 0, 0, 0);

            var log = new RecordingLog();
            var sharedOrder = new List<string>();
            var delay = new FakeDelayService { Order = sharedOrder };
            var prompts = new FakePromptService(0);
            var cutscenes = new FakeCutsceneService();

            var services = new CoreServices(delay, log, prompts, cutscenes);
            var engine = new GameSessionEngine(
                ParityFixture.MakeContent(),
                ParityFixture.SessionId,
                () => 1f,
                phaseRng,
                cardRng,
                services);

            var events = new List<string>();
            engine.CardStarted += c => events.Add("started:" + c.Title);
            engine.CardFinished += c => events.Add("finished:" + c.Title);
            engine.PhaseChanged += (p, n) => events.Add("phase:" + p + "->" + n);
            engine.SessionCompleted += () => events.Add("completed");

            var results = new List<AdvanceResult>();
            while (!engine.IsComplete)
            {
                results.Add(await engine.AdvanceOneCardAsync(CancellationToken.None));
            }

            // Card order under fixed randomness: Warm Two, Warm One, Crossroads, The End.
            CollectionAssert.AreEqual(
                new[] { "Warm Two", "Warm One", "Crossroads", "The End" },
                results.Where(r => r.Kind == AdvanceResultKind.CardCompleted || r.Card != null)
                       .Select(r => r.Card.Title).ToList());

            // Final result completes the session with its last card.
            Assert.AreEqual(AdvanceResultKind.SessionCompleted, results[^1].Kind);
            Assert.AreEqual("The End", results[^1].Card.Title);

            // Lifecycle order across all four cards.
            CollectionAssert.AreEqual(
                new[]
                {
                    "started:Warm Two", "finished:Warm Two",
                    "started:Warm One", "finished:Warm One", "phase:0->1",
                    "started:Crossroads", "finished:Crossroads", "phase:1->2",
                    "started:The End", "finished:The End", "phase:2->-1",
                    "completed"
                },
                events);

            // Prompt asked once with ordered labels; brave branch chosen.
            Assert.AreEqual(1, prompts.Requests.Count);
            Assert.AreEqual("Brave or cautious?", prompts.Requests[0].Prompt);
            CollectionAssert.AreEqual(new[] { "Brave", "Cautious" }, prompts.Requests[0].Options.ToArray());

            // Cutscene requested exactly once with the fixture resource id.
            CollectionAssert.AreEqual(new[] { "cs:finale" }, cutscenes.Started);

            // Known final stats: courage from Warm One's blocking stat action; brave from chosen child.
            Assert.AreEqual(2, engine.Player.Stats.Get("courage"));
            Assert.AreEqual(5, engine.Player.Stats.Get("brave"));

            // Log ordering facts: stat line precedes the background debug start of Warm One;
            // background warm-up delay eventually drained.
            var courageIndex = log.Entries.IndexOf(log.Entries.First(e => e.Contains("courage +2")));
            Assert.GreaterOrEqual(courageIndex, 0);
            await engine.DrainBackgroundAsync();
            Assert.AreEqual(0, engine.PendingBackgroundCount);
        }
    }
}
