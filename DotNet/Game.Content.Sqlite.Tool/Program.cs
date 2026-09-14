using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using TruthCardGame.Content;
using TruthCardGame.Content.Samples;
using TruthCardGame.Content.Sqlite;

namespace TruthCardGame.Content.Sqlite.Tool
{
    /// <summary>
    /// Seeds the canonical Content/GameContent.db from SampleContent (schema v2),
    /// upgrades an existing core database in place with --migrate, or creates the
    /// V1 dev-fixture Performance Tag vocabulary with --seed-performance-tags.
    /// Usage: dotnet run --project DotNet/Game.Content.Sqlite.Tool [--db &lt;path&gt;] [--migrate] [--seed-performance-tags] [--author-v1-performance]
    /// Default path is Content/GameContent.db relative to the working directory.
    /// Seeding refuses to touch a database that already has core content.
    /// </summary>
    public static class Program
    {
        /// <summary>Stable id of the V1 Conversation Performance Event.</summary>
        private const string V1PerformanceEventId = "perf-event-playful-tease";

        /// <summary>The Phase 00 card the V1 fixture exercises.</summary>
        private const string V1PerformanceCardId = "phase00-card";

        /// <summary>Stable id of the Perform action the fixture inserts into that card.</summary>
        private const string V1PerformanceActionId = "phase00-perform";

        /// <summary>Stable id of the second ordinary tagged dialogue line in the fixture card.</summary>
        private const string V1SecondDialogActionId = "phase00-dialog-tease";

        /// <summary>The existing first tagged dialogue line that the second line follows.</summary>
        private const string V1FirstDialogActionId = "phase00-dialog";

        /// <summary>Performance Tag titles the V1 event queries with ANY semantics.</summary>
        private static readonly string[] V1PerformanceEventTagTitles = { "Playful", "Tease" };

        /// <summary>
        /// V1 dev-fixture Performance Tag vocabulary, mirroring
        /// PerformanceCatalogSetup.SuggestedTagTitles in Unity. These are titles
        /// for a human; the minted opaque IDs are what Unity ingredient
        /// membership stores. The vocabulary itself is WPF/SQLite-owned, so this
        /// command exists only so a developer (or a fresh checkout) can reach a
        /// working picker without hand-authoring, and it is a no-op once the
        /// titles exist.
        /// </summary>
        private static readonly string[] V1PerformanceTagTitles =
        {
            "Playful",
            "Tease",
            "Stern",
            "Comforting",
        };

        public static int Main(string[] args)
        {
            try
            {
                var dbPath = DefaultDbPath();
                var migrateOnly = false;
                var seedPerformanceTags = false;
                var authorV1Performance = false;
                for (var i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--db" && i + 1 < args.Length) dbPath = args[i + 1];
                    if (args[i] == "--migrate") migrateOnly = true;
                    if (args[i] == "--seed-performance-tags") seedPerformanceTags = true;
                    if (args[i] == "--author-v1-performance") authorV1Performance = true;
                }

                var fullPath = Path.GetFullPath(dbPath);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                using (var connection = new SqliteConnection("Data Source=" + fullPath))
                {
                    ConnectionInitializer.Initialize(connection);
                    if (seedPerformanceTags)
                    {
                        var version = CoreMigrator.EnsureSchema(connection);
                        var created = SeedPerformanceTags(connection);
                        Console.WriteLine(created == 0
                            ? $"Performance Tag vocabulary already present in '{fullPath}' (core schema v{version})."
                            : $"Created {created} Performance Tag(s) in '{fullPath}' (core schema v{version}).");
                    }
                    else if (authorV1Performance)
                    {
                        var version = CoreMigrator.EnsureSchema(connection);
                        var exitCode = AuthorV1Performance(connection);
                        if (exitCode != 0) return exitCode;
                        Console.WriteLine($"Authored the V1 performance fixture in '{fullPath}' (core schema v{version}).");
                    }
                    else if (migrateOnly)
                    {
                        var version = CoreMigrator.EnsureSchema(connection);
                        Console.WriteLine($"Migrated '{fullPath}' in place (core schema v{version}).");
                    }
                    else
                    {
                        DatabaseInitializer.InitializeEmptyDatabaseFromSnapshot(connection, SampleContent.Create());
                        var version = CoreMigrator.EnsureSchema(connection);
                        Console.WriteLine($"Seeded '{fullPath}' (core schema v{version}) from SampleContent.");
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// Creates any missing fixture tag and returns how many were added.
        /// Matching is by title, so re-running never mints a second ID for a
        /// tag that already exists.
        /// </summary>
        private static int SeedPerformanceTags(DbConnection connection)
        {
            var tags = PerformanceCatalogRepository.ListTags(connection);
            var existingTitles = new HashSet<string>(
                tags.Select(tag => tag.Title ?? string.Empty), StringComparer.Ordinal);
            var nextSortOrder = tags.Count;

            var created = 0;
            foreach (var title in V1PerformanceTagTitles)
            {
                if (!existingTitles.Add(title)) continue;
                PerformanceCatalogRepository.CreateTag(connection, new PerformanceTagDefinition
                {
                    Id = StableIds.New(),
                    Title = title,
                    SortOrder = nextSortOrder++,
                });
                created++;
            }
            return created;
        }

        /// <summary>
        /// Authors the representative V1 performance content: one Conversation
        /// Performance Event whose ANY-of tag query matches several expressive
        /// ingredients, plus the Perform action that plays it as the card's first
        /// action, so the card's blocking dialogue then refreshes onto the
        /// performance it just established. Idempotent: tag membership is looked
        /// up by title (never hardcoded), the event upserts, and the sequence is
        /// diffed by stable id.
        /// </summary>
        private static int AuthorV1Performance(DbConnection connection)
        {
            var idsByTitle = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in PerformanceCatalogRepository.ListTags(connection))
            {
                if (tag == null || tag.IsRetired) continue;
                if (string.IsNullOrEmpty(tag.Title) || string.IsNullOrEmpty(tag.Id)) continue;
                if (!idsByTitle.ContainsKey(tag.Title)) idsByTitle.Add(tag.Title, tag.Id);
            }

            var missing = V1PerformanceEventTagTitles
                .Where(title => !idsByTitle.ContainsKey(title))
                .ToList();
            if (missing.Count > 0)
            {
                Console.Error.WriteLine(
                    "Missing Performance Tag(s): " + string.Join(", ", missing) +
                    ". Run --seed-performance-tags first.");
                return 1;
            }

            var performanceEvent = new ConversationPerformanceEventDefinition
            {
                Id = V1PerformanceEventId,
                Name = "Playful Tease",
                // ANY-of: several ingredients satisfy it, so ingredient selection
                // is a real choice rather than a single forced answer.
                RequireAllTags = false,
                StagingPolicy = PerformanceStagingPolicy.ChooseCompatible,
                RefreshAtDialogueStart = true,
            };
            foreach (var title in V1PerformanceEventTagTitles)
            {
                performanceEvent.PerformanceTagIds.Add(idsByTitle[title]);
            }
            PerformanceCatalogRepository.SaveEvent(connection, performanceEvent);

            var card = GameContentSnapshotLoader.Load(connection, ensureSchema: false)
                .Cards.Find(candidate => candidate.Id == V1PerformanceCardId);
            if (card == null)
            {
                Console.Error.WriteLine(
                    "Card '" + V1PerformanceCardId + "' is not in this database; the V1 fixture extends it.");
                return 1;
            }

            var desired = new ActionSequenceDefinition { Id = card.Sequence.Id };
            desired.Instances.Add(new PerformInstanceDefinition
            {
                Id = V1PerformanceActionId,
                EventId = V1PerformanceEventId,
                // Perform suspends the run until the host acknowledges, so it can
                // never be authored nonblocking.
                IsBlocking = true,
            });
            var insertedSecondDialog = false;
            foreach (var instance in card.Sequence.Instances)
            {
                if (instance.Id == V1PerformanceActionId || instance.Id == V1SecondDialogActionId) continue;
                desired.Instances.Add(instance);

                if (instance.Id == V1FirstDialogActionId)
                {
                    if (!(instance is DialogFromTagsInstanceDefinition firstDialog))
                    {
                        throw new InvalidOperationException(
                            $"card '{V1PerformanceCardId}' action '{V1FirstDialogActionId}' is not a tagged dialogue action.");
                    }
                    var secondDialog = new DialogFromTagsInstanceDefinition
                    {
                        Id = V1SecondDialogActionId,
                        IsBlocking = true,
                    };
                    secondDialog.RequiredDialogTagIds.AddRange(
                        firstDialog.RequiredDialogTagIds);
                    desired.Instances.Add(secondDialog);
                    insertedSecondDialog = true;
                }
            }
            if (!insertedSecondDialog)
            {
                throw new InvalidOperationException(
                    $"card '{V1PerformanceCardId}' has no '{V1FirstDialogActionId}' tagged dialogue action " +
                    "to extend with the V1 performance fixture.");
            }
            ActionSequenceRepository.Save(connection, desired);

            var reloaded = GameContentSnapshotLoader.Load(connection, ensureSchema: false);
            var authoredCard = reloaded.Cards.Find(candidate => candidate.Id == V1PerformanceCardId);
            var authoredEvent = reloaded.PerformanceEvents.Find(
                candidate => candidate.Id == V1PerformanceEventId);
            var authoredAction = authoredCard == null
                ? null
                : authoredCard.Sequence.Instances.Find(instance => instance.Id == V1PerformanceActionId);

            if (authoredEvent == null || authoredEvent.PerformanceTagIds.Count != V1PerformanceEventTagTitles.Length)
            {
                throw new InvalidOperationException("the V1 performance event did not survive a loader round trip.");
            }
            if (!(authoredAction is PerformInstanceDefinition perform) ||
                !string.Equals(perform.EventId, V1PerformanceEventId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("the V1 Perform action did not survive a loader round trip.");
            }
            if (authoredCard.Sequence.Instances[0].Id != V1PerformanceActionId)
            {
                throw new InvalidOperationException("the V1 Perform action is not the card's first action.");
            }
            var taggedDialogues = authoredCard.Sequence.Instances
                .OfType<DialogFromTagsInstanceDefinition>()
                .ToList();
            if (taggedDialogues.Count != 2 ||
                taggedDialogues.Any(dialog => !dialog.IsBlocking || dialog.RequiredDialogTagIds.Count == 0))
            {
                throw new InvalidOperationException(
                    "the V1 card does not contain the two blocking tagged dialogue actions required by the fixture.");
            }

            using (var integrity = connection.CreateCommand())
            {
                integrity.CommandText = "PRAGMA integrity_check;";
                if (!string.Equals(Convert.ToString(integrity.ExecuteScalar()), "ok", StringComparison.Ordinal))
                    throw new InvalidOperationException("integrity_check failed after authoring the V1 performance fixture.");
            }
            using (var foreignKeys = connection.CreateCommand())
            {
                foreignKeys.CommandText = "PRAGMA foreign_key_check;";
                using (var rows = foreignKeys.ExecuteReader())
                {
                    if (rows.Read())
                        throw new InvalidOperationException("foreign_key_check returned rows after authoring the V1 performance fixture.");
                }
            }

            Console.WriteLine(
                $"event={authoredEvent.Id} tags={authoredEvent.PerformanceTagIds.Count} " +
                $"card={authoredCard.Id} actions={authoredCard.Sequence.Instances.Count} first={authoredAction.Id}");
            return 0;
        }

        private static string DefaultDbPath()
        {
            return Path.Combine("Content", "GameContent.db");
        }
    }
}
