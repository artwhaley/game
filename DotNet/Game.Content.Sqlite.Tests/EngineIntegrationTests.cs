using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content.Samples;
using TruthCardGame.Core;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// End-to-end canonical flow (the WPF preview path minus the window):
    /// seed a DB from SampleContent, load the snapshot, run Game.Core against
    /// it. Exercises real sample content: tag matching, no-match phase
    /// skipping (the deck contains no 'ending' card), choices, stats.
    /// </summary>
    [TestFixture]
    public class EngineIntegrationTests
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "sqlite-engine-" + Guid.NewGuid().ToString("N") + ".db");
            _connection = new SqliteConnection("Data Source=" + _dbPath);
            ConnectionInitializer.Initialize(_connection);
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [TestCase("9ac9fbe9ced7463a8627356e21644096")] // Intense: Build -> High Intensity -> The End
        [TestCase("8741477f677e443f828f87267c32d537")] // Relaxing: Warm Up -> Teasing -> Wind Down
        public async Task SampleSession_RunsToCompletion_FromCanonicalFlow(string sessionId)
        {
            DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(_connection, SampleContent.Build());
            var content = GameContentSnapshotLoader.Load(_connection);

            var log = new RecordingLog();
            var services = new CoreServices(new InstantDelay(), log, new FirstPrompt(), new NoopCutscene());
            var engine = new GameSessionEngine(
                content,
                sessionId,
                () => 1f,
                new QueuedRandom(0, 0, 0),
                new QueuedRandom(Enumerable.Repeat(0, 64).ToArray()),
                services);

            var cards = 0;
            while (!engine.IsComplete)
            {
                var result = await engine.AdvanceOneCardAsync(CancellationToken.None);
                if (result.Kind == AdvanceResultKind.CardCompleted) cards++;
            }

            Assert.Greater(cards, 0, "expected at least one card draw");
            Assert.IsTrue(engine.IsComplete);
            // Both sessions end with an 'ending' phase but the deck has no
            // 'ending' card, so Core must skip it with a warning (parity with
            // the no-match early-advance rule).
            Assert.That(log.Warnings, Has.Some.Contains("advancing early"));
        }

        private sealed class QueuedRandom : IRandomSource
        {
            private readonly Queue<int> _offsets;
            public QueuedRandom(params int[] offsets) => _offsets = new Queue<int>(offsets);

            public int NextInt(int minInclusive, int maxExclusive)
            {
                if (_offsets.Count == 0) return minInclusive;
                var offset = _offsets.Dequeue();
                return Math.Min(minInclusive + offset, maxExclusive - 1);
            }
        }

        private sealed class InstantDelay : IGameDelay
        {
            public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private sealed class RecordingLog : IGameLog
        {
            public readonly List<string> Warnings = new List<string>();
            public void Info(string message) { }
            public void Warning(string message) => Warnings.Add(message);
            public void Error(string message) { }
        }

        private sealed class FirstPrompt : IPromptService
        {
            public Task<int?> AskAsync(string prompt, IReadOnlyList<string> options, CancellationToken cancellationToken)
                => Task.FromResult<int?>(0);
        }

        private sealed class NoopCutscene : ICutsceneService
        {
            public Task PlayAsync(string resourceId, CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
