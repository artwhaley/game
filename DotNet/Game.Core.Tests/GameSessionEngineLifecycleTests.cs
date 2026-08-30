using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using TruthCardGame.Content.Samples;

namespace TruthCardGame.Core.Tests
{
    [TestFixture]
    public sealed class GameSessionEngineLifecycleTests
    {
        [Test]
        public async Task ShutdownAsync_ConstrainsHungToyCleanup_AndLogsTimeout()
        {
            var log = new RecordingLog();
            var toy = new GatedToyActivityService();
            var engine = new GameSessionEngine(
                SampleContent.Create(), SampleContent.SessionIntense,
                new CoreServices(new FakeDelayService(), log, toyActivity: toy));

            var clock = Stopwatch.StartNew();
            await engine.ShutdownAsync("test timeout");

            Assert.That(clock.Elapsed, Is.GreaterThanOrEqualTo(System.TimeSpan.FromSeconds(4)));
            Assert.That(log.Entries.Exists(entry => entry.Contains("TOY STOP ALL timed out")), Is.True);
        }
    }
}
