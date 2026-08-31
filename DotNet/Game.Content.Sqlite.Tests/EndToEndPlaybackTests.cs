using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Core;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 21 FINAL end-to-end gate: author a session + phase through the
    /// authoring repositories, reload the portable snapshot, and play it to
    /// completion through the REAL Core GameSessionEngine — the exact pipeline
    /// the Workbench preview runs (DB -> snapshot -> Core graph VM).
    /// </summary>
    [TestFixture]
    public class EndToEndPlaybackTests : IDisposable
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"gwb-e2e-{Guid.NewGuid():N}.db");
            _connection = new SqliteConnection("Data Source=" + _dbPath);
            _connection.Open();
            ConnectionInitializer.Initialize(_connection);
            CoreMigrator.EnsureSchema(_connection);
        }

        [TearDown]
        public void Dispose()
        {
            _connection.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Test]
        public void AuthoredPhase_SurvivesReload_AndPlaysToCompletion()
        {
            // --- author: one phase with Entry -> Exec -> progress-check -> goto ---
            var phase = new PhaseDefinition { Id = "p-main", Title = "Main" };
            phase.Exits.Add(new PhaseExitDefinition { Id = "px-complete", Name = "Complete" });
            PhaseRepository.Create(_connection, phase.Id, phase.Title);
            PhaseExitRepository.Create(_connection, phase.Id, phase.Exits[0], 0);

            var entry = new PhaseEntryNodeDefinition { Id = "n-entry" };
            entry.Outputs.Add(new GraphOutputDefinition { Id = "n-entry-out", Kind = GraphPortKind.Normal });
            var exec = new CardExecutorNodeDefinition { Id = "n-exec" };
            exec.Outputs.Add(new GraphOutputDefinition { Id = "n-exec-out", Kind = GraphPortKind.Normal });
            var check = new VariableCheckNodeDefinition
            {
                Id = "n-check",
                SourceKind = VariableSourceKind.PhaseProgress,
                Operator = VariableCompareOperator.GreaterThanOrEqual,
                CompareValue = 50f,
            };
            check.Outputs.Add(new GraphOutputDefinition { Id = "n-check-true", Kind = GraphPortKind.True });
            check.Outputs.Add(new GraphOutputDefinition { Id = "n-check-false", Kind = GraphPortKind.False });
            var action = new ActionNodeDefinition { Id = "n-goto", Sequence = new ActionSequenceDefinition { Id = "n-goto-seq" } };
            action.Sequence.Instances.Add(new PhaseGotoInstanceDefinition { Id = "n-goto-inst", PhaseExitId = "px-complete" });
            action.Outputs.Add(new GraphOutputDefinition { Id = "n-goto-out", Kind = GraphPortKind.Normal });

            PhaseGraphRepository.AddNode(_connection, phase.Id, entry);
            PhaseGraphRepository.AddNode(_connection, phase.Id, exec);
            PhaseGraphRepository.AddNode(_connection, phase.Id, check);
            PhaseGraphRepository.AddNode(_connection, phase.Id, action);
            PhaseGraphRepository.AddEdge(_connection, phase.Id, new GraphEdgeDefinition { Id = "e1", SourceOutputId = "n-entry-out", TargetNodeId = "n-exec" });
            PhaseGraphRepository.AddEdge(_connection, phase.Id, new GraphEdgeDefinition { Id = "e2", SourceOutputId = "n-exec-out", TargetNodeId = "n-check" });
            PhaseGraphRepository.AddEdge(_connection, phase.Id, new GraphEdgeDefinition { Id = "e3", SourceOutputId = "n-check-false", TargetNodeId = "n-exec" });
            PhaseGraphRepository.AddEdge(_connection, phase.Id, new GraphEdgeDefinition { Id = "e4", SourceOutputId = "n-check-true", TargetNodeId = "n-goto" });

            // --- author: one session Start -> ref -> End ---
            SessionRepository.Create(_connection, "s1", "E2E Session", "type-standard");
            var start = new SessionStartNodeDefinition { Id = "sn-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "sn-start-out", Kind = GraphPortKind.Normal });
            var reference = new PhaseReferenceNodeDefinition { Id = "sn-ref", PhaseId = "p-main" };
            reference.Outputs.Add(new GraphOutputDefinition { Id = "sn-ref-out", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-complete" });
            var end = new SessionEndNodeDefinition { Id = "sn-end" };
            SessionGraphRepository.AddNode(_connection, "s1", start);
            SessionGraphRepository.AddNode(_connection, "s1", reference);
            SessionGraphRepository.AddNode(_connection, "s1", end);
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition { Id = "se1", SourceOutputId = "sn-start-out", TargetNodeId = "sn-ref" });
            SessionGraphRepository.AddEdge(_connection, "s1", new GraphEdgeDefinition { Id = "se2", SourceOutputId = "sn-ref-out", TargetNodeId = "sn-end" });

            // --- a card that yields +10 progress per draw ---
            // (v5: no deck — the CardExecutor draws from the whole catalog
            // through the eligibility/weighting pipeline.)
            var card = new CardDefinition { Id = "c1", Title = "Push" };
            card.Sequence = new ActionSequenceDefinition { Id = "c1-seq" };
            card.Sequence.Instances.Add(new IncrementProgressInstanceDefinition { Id = "c1-inc", Amount = 10f });
            Sql.Execute(_connection, null,
                "INSERT INTO action_sequence (id) VALUES (@id);", ("id", "c1-seq"));
            Sql.Execute(_connection, null,
                "INSERT INTO action_instance (id, action_sequence_id, ordinal, action_type, is_blocking) " +
                "VALUES (@id, @seq, 0, @type, 0);",
                ("id", "c1-inc"), ("seq", "c1-seq"), ("type", ActionType.IncrementProgressV2));
            Sql.Execute(_connection, null,
                "INSERT INTO action_instance_increment_progress (action_instance_id, amount) VALUES (@i, 10);",
                ("i", "c1-inc"));
            Sql.Execute(_connection, null,
                "INSERT INTO card (id, title, action_sequence_id) VALUES (@id, @title, @seq);",
                ("id", card.Id), ("title", card.Title), ("seq", "c1-seq"));

            // --- DB -> snapshot -> real Core engine, to completion ---
            var content = GameContentSnapshotLoader.Load(_connection);
            Assert.AreEqual(1, content.Sessions.Count);
            Assert.AreEqual(1, content.Phases.Count);

            var engine = new TruthCardGame.Core.GameSessionEngine(content, "s1",
                new TruthCardGame.Core.CoreServices(new NoOpDelay()));
            var cardsDrawn = 0;
            engine.CardStarted += _ => cardsDrawn++;
            var guard = 0;
            var waitingForContinue = false;
            while (!engine.IsComplete)
            {
                guard++;
                Assert.Less(guard, 200, "end-to-end run did not complete (loop guard)");
                var result = (waitingForContinue
                        ? engine.ContinueAsync(default)
                        : engine.RunUntilYieldAsync(default))
                    .GetAwaiter().GetResult();
                waitingForContinue = result.Kind == TruthCardGame.Core.AdvanceResultKind.WaitForContinue;
                Assert.That(result.Kind, Is.AnyOf(
                    TruthCardGame.Core.AdvanceResultKind.WaitForContinue,
                    TruthCardGame.Core.AdvanceResultKind.SessionCompleted));
            }

            // 5 draws (5 x +10 progress = 50 >= check target), then the GOTO
            // exits the phase and the session ends.
            Assert.AreEqual(5, cardsDrawn, "5 draws reach the progress-check target");
            Assert.IsTrue(engine.IsComplete, "authored session plays to completion");
        }

        [Test]
        public async Task MilestoneC_TwentyCardTwoPhaseAuthoringCanary_ReloadsAndExecutesEveryCard()
        {
            CatalogRepositories.CreateSessionType(_connection,
                new SessionTypeDefinition { Id = "type-milestone-c", Title = "Milestone C" });
            CatalogRepositories.CreateCardTag(_connection,
                new CardTagDefinition { Id = "tag-phase-one", Title = "Phase One" });
            CatalogRepositories.CreateCardTag(_connection,
                new CardTagDefinition { Id = "tag-phase-two", Title = "Phase Two" });
            CatalogRepositories.CreateSmartToyCapability(_connection,
                new SmartToyCapabilityDefinition { Id = "cap-vibrate", Title = "Vibrate" });
            TemperatureRepository.Create(_connection, new TemperatureDefinition
            {
                Id = "happiness", Title = "Happiness", MinValue = 0f, MaxValue = 100f, DefaultValue = 50f,
            });
            ResourceRepository.Create(_connection,
                new ResourceDefinition { Id = "res-cutscene", Kind = ResourceKinds.Cutscene, Name = "Intro" });
            ResourceRepository.Create(_connection,
                new ResourceDefinition { Id = "res-toy-pattern", Kind = ResourceKinds.ToyPattern, Name = "Pulse" });
            DialogCatalogRepository.CreateTag(_connection,
                new DialogTagDefinition { Id = "dialog-playful", Title = "Playful" });
            var snippet = new DialogSnippetDefinition
            {
                Id = "snippet-playful", Name = "Playful line", Text = "A selected catalog line.",
            };
            snippet.DialogTagIds.Add("dialog-playful");
            DialogCatalogRepository.CreateSnippet(_connection, snippet);

            AuthorProgressPhase("phase-one", "Warmup", "tag-phase-one", "exit-one");
            AuthorProgressPhase("phase-two", "Finale", "tag-phase-two", "exit-two");
            AuthorTwoPhaseSession();

            for (var number = 1; number <= 20; number++)
            {
                var id = "card-" + number.ToString("00");
                var card = new CardDefinition
                {
                    Id = id,
                    Title = "Milestone C Card " + number,
                    BodyText = "Authored canary card " + number,
                    FolderPath = number <= 10 ? "Milestone C/Phase One" : "Milestone C/Phase Two",
                    Sequence = BuildCanaryCardSequence(id, number),
                };
                card.CardTagIds.Add(number <= 10 ? "tag-phase-one" : "tag-phase-two");
                CardRepository.Create(_connection, card);
            }

            var content = GameContentSnapshotLoader.Load(_connection);
            Assert.That(content.Cards, Has.Count.EqualTo(20));
            Assert.That(content.Phases, Has.Count.EqualTo(2));
            Assert.That(content.Cards.Select(card => card.Sequence.Id).Distinct().Count(), Is.EqualTo(20),
                "every authored Card owns a fresh ActionSequence");
            Assert.That(content.Cards.All(card => card.FolderPath.StartsWith("Milestone C/", StringComparison.Ordinal)), Is.True);

            var toy = new RecordingToyService();
            var dialog = new RecordingDialogService(toy);
            var prompt = new FirstPromptService();
            var cutscene = new RecordingCutsceneService();
            var engine = new GameSessionEngine(content, "session-milestone-c",
                new CoreServices(new NoOpDelay(), prompts: prompt, cutscene: cutscene,
                    toyActivity: toy, dialog: dialog),
                SessionSpawnOptions.Default,
                new PhaseRunRngFactory(() => new CyclingCardRandom()));

            var cardsDrawn = new List<string>();
            var phasesEntered = new List<string>();
            engine.CardStarted += card => cardsDrawn.Add(card.Id);
            engine.PhaseEntered += phaseId => phasesEntered.Add(phaseId);

            var waitingForContinue = false;
            var guard = 0;
            while (!engine.IsComplete)
            {
                Assert.That(++guard, Is.LessThan(100), "Milestone C canary exceeded its execution guard");
                var result = waitingForContinue
                    ? await engine.ContinueAsync(CancellationToken.None)
                    : await engine.RunUntilYieldAsync(CancellationToken.None);
                waitingForContinue = result.Kind == AdvanceResultKind.WaitForContinue;
                Assert.That(result.Kind, Is.AnyOf(AdvanceResultKind.WaitForContinue, AdvanceResultKind.SessionCompleted));
            }
            await engine.ShutdownAsync("Milestone C canary complete");

            CollectionAssert.AreEqual(
                Enumerable.Range(1, 20).Select(number => "card-" + number.ToString("00")), cardsDrawn,
                "the deterministic run must execute every authored Card exactly once");
            CollectionAssert.AreEqual(new[] { "phase-one", "phase-two" }, phasesEntered);
            Assert.That(prompt.Count, Is.EqualTo(2), "nested PromptChoice ran in both phases");
            Assert.That(toy.TimedStarted, Is.EqualTo(2));
            Assert.That(toy.TimedCompleted, Is.EqualTo(2));
            Assert.That(toy.PatternsSet, Is.EqualTo(2));
            Assert.That(toy.StopAllCalls, Is.EqualTo(1), "session teardown stops all toy output");
            Assert.That(dialog.AfterWaitObservedCompletedToy, Is.True,
                "nested WaitForAll held the post-wait dialog until timed toy work completed");
            Assert.That(dialog.Lines, Does.Contain("A selected catalog line."));
            Assert.That(cutscene.ResourceIds, Is.EqualTo(new[] { "res-cutscene", "res-cutscene" }));
            Assert.That(engine.Player.Stats.Get("courage"), Is.EqualTo(4));
            Assert.That(engine.Temperatures.Get("happiness"), Is.EqualTo(60f));
        }

        private void AuthorProgressPhase(string phaseId, string title, string cardTagId, string exitId)
        {
            PhaseRepository.Create(_connection, phaseId, title);
            PhaseRepository.ReplaceCardQuery(_connection, phaseId, new[] { cardTagId }, Array.Empty<string>());
            PhaseExitRepository.Create(_connection, phaseId,
                new PhaseExitDefinition { Id = exitId, Name = "Complete" }, 0);

            var entry = new PhaseEntryNodeDefinition { Id = phaseId + "-entry" };
            entry.Outputs.Add(new GraphOutputDefinition { Id = entry.Id + "-out", Kind = GraphPortKind.Normal });
            var cards = new CardExecutorNodeDefinition { Id = phaseId + "-cards" };
            cards.Outputs.Add(new GraphOutputDefinition { Id = cards.Id + "-out", Kind = GraphPortKind.Normal });
            var check = new VariableCheckNodeDefinition
            {
                Id = phaseId + "-check",
                SourceKind = VariableSourceKind.PhaseProgress,
                Operator = VariableCompareOperator.GreaterThanOrEqual,
                CompareValue = 10f,
            };
            check.Outputs.Add(new GraphOutputDefinition { Id = check.Id + "-true", Kind = GraphPortKind.True });
            check.Outputs.Add(new GraphOutputDefinition { Id = check.Id + "-false", Kind = GraphPortKind.False });
            var exit = new ActionNodeDefinition
            {
                Id = phaseId + "-exit",
                Sequence = new ActionSequenceDefinition { Id = phaseId + "-exit-sequence" },
            };
            exit.Sequence.Instances.Add(new PhaseGotoInstanceDefinition
                { Id = phaseId + "-goto", PhaseExitId = exitId });
            exit.Outputs.Add(new GraphOutputDefinition { Id = exit.Id + "-out", Kind = GraphPortKind.Normal });

            PhaseGraphRepository.AddNode(_connection, phaseId, entry);
            PhaseGraphRepository.AddNode(_connection, phaseId, cards);
            PhaseGraphRepository.AddNode(_connection, phaseId, check);
            PhaseGraphRepository.AddNode(_connection, phaseId, exit);
            PhaseGraphRepository.AddEdge(_connection, phaseId, new GraphEdgeDefinition
                { Id = phaseId + "-edge-entry", SourceOutputId = entry.Id + "-out", TargetNodeId = cards.Id });
            PhaseGraphRepository.AddEdge(_connection, phaseId, new GraphEdgeDefinition
                { Id = phaseId + "-edge-cards", SourceOutputId = cards.Id + "-out", TargetNodeId = check.Id });
            PhaseGraphRepository.AddEdge(_connection, phaseId, new GraphEdgeDefinition
                { Id = phaseId + "-edge-loop", SourceOutputId = check.Id + "-false", TargetNodeId = cards.Id });
            PhaseGraphRepository.AddEdge(_connection, phaseId, new GraphEdgeDefinition
                { Id = phaseId + "-edge-exit", SourceOutputId = check.Id + "-true", TargetNodeId = exit.Id });
        }

        private void AuthorTwoPhaseSession()
        {
            SessionRepository.Create(_connection, "session-milestone-c", "Milestone C Authoring Canary", "type-milestone-c");
            var start = new SessionStartNodeDefinition { Id = "session-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "session-start-out", Kind = GraphPortKind.Normal });
            var first = new PhaseReferenceNodeDefinition { Id = "session-phase-one", PhaseId = "phase-one" };
            first.Outputs.Add(new GraphOutputDefinition
                { Id = "session-phase-one-out", Kind = GraphPortKind.PhaseExit, PhaseExitId = "exit-one" });
            var second = new PhaseReferenceNodeDefinition { Id = "session-phase-two", PhaseId = "phase-two" };
            second.Outputs.Add(new GraphOutputDefinition
                { Id = "session-phase-two-out", Kind = GraphPortKind.PhaseExit, PhaseExitId = "exit-two" });
            var end = new SessionEndNodeDefinition { Id = "session-end" };

            SessionGraphRepository.AddNode(_connection, "session-milestone-c", start);
            SessionGraphRepository.AddNode(_connection, "session-milestone-c", first);
            SessionGraphRepository.AddNode(_connection, "session-milestone-c", second);
            SessionGraphRepository.AddNode(_connection, "session-milestone-c", end);
            SessionGraphRepository.AddEdge(_connection, "session-milestone-c", new GraphEdgeDefinition
                { Id = "session-edge-start", SourceOutputId = "session-start-out", TargetNodeId = first.Id });
            SessionGraphRepository.AddEdge(_connection, "session-milestone-c", new GraphEdgeDefinition
                { Id = "session-edge-first", SourceOutputId = "session-phase-one-out", TargetNodeId = second.Id });
            SessionGraphRepository.AddEdge(_connection, "session-milestone-c", new GraphEdgeDefinition
                { Id = "session-edge-second", SourceOutputId = "session-phase-two-out", TargetNodeId = end.Id });
        }

        private static ActionSequenceDefinition BuildCanaryCardSequence(string cardId, int number)
        {
            var sequence = new ActionSequenceDefinition { Id = cardId + "-sequence" };
            switch ((number - 1) % 10)
            {
                case 0:
                {
                    var choice = new PromptChoiceInstanceDefinition
                        { Id = cardId + "-choice", Prompt = "Choose the canary path", IsBlocking = true };
                    var selected = new ActionSequenceDefinition { Id = cardId + "-choice-selected" };
                    selected.Instances.Add(new ToyActivityInstanceDefinition
                    {
                        Id = cardId + "-timed-toy", CapabilityId = "cap-vibrate",
                        PatternResourceId = "res-toy-pattern", DurationSeconds = 0.01f, IsBlocking = false,
                    });
                    selected.Instances.Add(new DialogInstanceDefinition
                        { Id = cardId + "-before-wait", Text = "Before wait", IsBlocking = true });
                    selected.Instances.Add(new WaitForAllInstanceDefinition
                        { Id = cardId + "-wait-all", IsBlocking = true });
                    selected.Instances.Add(new DialogInstanceDefinition
                        { Id = cardId + "-after-wait", Text = "After wait", IsBlocking = true });
                    choice.Options.Add(new PromptChoiceOptionDefinition
                        { Id = cardId + "-option-selected", Label = "Run", Sequence = selected });
                    choice.Options.Add(new PromptChoiceOptionDefinition
                    {
                        Id = cardId + "-option-skip", Label = "Skip",
                        Sequence = new ActionSequenceDefinition { Id = cardId + "-choice-skip" },
                    });
                    sequence.Instances.Add(choice);
                    break;
                }
                case 1:
                    sequence.Instances.Add(new ToySetPatternInstanceDefinition
                    {
                        Id = cardId + "-set-toy", CapabilityId = "cap-vibrate",
                        PatternResourceId = "res-toy-pattern", IsBlocking = false,
                    });
                    break;
                case 2:
                {
                    var fromTags = new DialogFromTagsInstanceDefinition { Id = cardId + "-dialog-tags" };
                    fromTags.RequiredDialogTagIds.Add("dialog-playful");
                    sequence.Instances.Add(fromTags);
                    break;
                }
                case 3:
                    sequence.Instances.Add(new CutsceneInstanceDefinition
                        { Id = cardId + "-cutscene", ResourceId = "res-cutscene" });
                    break;
                case 4:
                    sequence.Instances.Add(new DelayInstanceDefinition
                        { Id = cardId + "-delay", DurationSeconds = 0.01f });
                    break;
                case 5:
                    sequence.Instances.Add(new ModifyTemperatureInstanceDefinition
                        { Id = cardId + "-temperature", TemperatureId = "happiness", Amount = 5f });
                    break;
                case 6:
                    sequence.Instances.Add(new StatIncreaseInstanceDefinition
                        { Id = cardId + "-stat", StatKey = "courage", Amount = 2f });
                    break;
                case 7:
                    sequence.Instances.Add(new DialogInstanceDefinition
                        { Id = cardId + "-dialog", Text = "A direct authored line." });
                    break;
                case 8:
                    sequence.Instances.Add(new DebugInstanceDefinition
                        { Id = cardId + "-debug", Message = "Milestone C canary" });
                    break;
                case 9:
                    sequence.Instances.Add(new WaitForContinueInstanceDefinition
                        { Id = cardId + "-continue", IsBlocking = true });
                    break;
            }
            sequence.Instances.Add(new IncrementProgressInstanceDefinition
                { Id = cardId + "-progress", Amount = 1f });
            return sequence;
        }

        private sealed class CyclingCardRandom : IRandomSource
        {
            private int _next;
            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
            public float NextFloat(float minInclusive, float maxExclusive)
            {
                var count = Math.Max(1, (int)Math.Round(maxExclusive - minInclusive));
                return minInclusive + ((_next++ % count) + 0.5f);
            }
        }

        private sealed class FirstPromptService : IPromptService
        {
            public int Count { get; private set; }
            public Task<int?> AskAsync(string prompt, IReadOnlyList<string> options, CancellationToken cancellationToken)
            {
                Count++;
                return Task.FromResult<int?>(0);
            }
        }

        private sealed class RecordingCutsceneService : ICutsceneService
        {
            public List<string> ResourceIds { get; } = new List<string>();
            public Task PlayAsync(string resourceId, CancellationToken cancellationToken)
            {
                ResourceIds.Add(resourceId);
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingToyService : IToyActivityService
        {
            private int _timedStarted;
            private int _timedCompleted;
            public int TimedStarted => Volatile.Read(ref _timedStarted);
            public int TimedCompleted => Volatile.Read(ref _timedCompleted);
            public int PatternsSet { get; private set; }
            public int StopAllCalls { get; private set; }

            public async Task PlayForAsync(string capabilityId, string patternResourceId, TimeSpan duration,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _timedStarted);
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref _timedCompleted);
            }

            public Task SetPatternAsync(string capabilityId, string patternResourceId,
                CancellationToken cancellationToken)
            {
                PatternsSet++;
                return Task.CompletedTask;
            }

            public Task StopAllAsync(CancellationToken cancellationToken)
            {
                StopAllCalls++;
                return Task.CompletedTask;
            }
        }

        private sealed class RecordingDialogService : IDialogService
        {
            private readonly RecordingToyService _toy;
            public RecordingDialogService(RecordingToyService toy) => _toy = toy;
            public List<string> Lines { get; } = new List<string>();
            public bool AfterWaitObservedCompletedToy { get; private set; } = true;

            public Task ShowAsync(string text, CancellationToken cancellationToken)
            {
                Lines.Add(text);
                if (text == "After wait")
                    AfterWaitObservedCompletedToy &= _toy.TimedCompleted == _toy.TimedStarted;
                return Task.CompletedTask;
            }
        }

        private sealed class NoOpDelay : TruthCardGame.Core.IGameDelay
        {
            public System.Threading.Tasks.Task DelayAsync(TimeSpan delay, System.Threading.CancellationToken cancellationToken)
                => System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
