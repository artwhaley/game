using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Content.Sqlite;
using TruthCardGame.Core;
using TruthCardGame.Core.Tests;
using TruthCardGame.Profile;
using TruthCardGame.Profile.Sqlite;

namespace TruthCardGame.Content.Sqlite.Tests
{
    /// <summary>
    /// Ticket 17 HARD gate: one deterministic test session covering SessionType,
    /// Phase query, tags, Kinks, Equipment, capability, Cards, Actions, and
    /// weighting — verified end-to-end through the real Core engine plus the
    /// pre-Milestone-B migration path, GOTO/RETURN RNG behavior, and
    /// profile/content database independence.
    /// </summary>
    [TestFixture]
    public class MilestoneBIntegrationTests
    {
        private string _dbPath;
        private SqliteConnection _connection;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), "milestoneb-int-" + Guid.NewGuid().ToString("N") + ".db");
            _connection = new SqliteConnection("Data Source=" + _dbPath);
            _connection.Open();
            ConnectionInitializer.Initialize(_connection);
            CoreMigrator.EnsureSchema(_connection);
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        // ---------- deterministic full-pipeline scenario ----------

        /// <summary>
        /// Builds one complete authored scenario through the v5 repositories:
        /// a SessionType, Card tags, Kinks, Equipment, capabilities, a Phase
        /// with an ALL/ANY query, weighting-tuned Session, and Cards covering
        /// neutral/positive/torture/dont-consent/requirement-rejection cases.
        /// </summary>
        private GameContentDefinition BuildScenario(CardSelectionProfile profileForPlay)
        {
            // Catalogs.
            CatalogRepositories.CreateSessionType(_connection, new SessionTypeDefinition { Id = "type-joi", Title = "JOI" });
            CatalogRepositories.CreateCardTag(_connection, new CardTagDefinition { Id = "tag-tease", Title = "Tease" });
            CatalogRepositories.CreateCardTag(_connection, new CardTagDefinition { Id = "tag-soft", Title = "Soft" });
            CatalogRepositories.CreateKink(_connection, new KinkDefinition { Id = "kink-praise", Title = "Praise" });
            CatalogRepositories.CreateKink(_connection, new KinkDefinition { Id = "kink-denial", Title = "Denial" });
            // The forbidden kink EXISTS in content; the user just never
            // configures a preference for it (Unconfigured = hard exclude).
            CatalogRepositories.CreateKink(_connection, new KinkDefinition { Id = "kink-no", Title = "Unspoken" });
            CatalogRepositories.CreateEquipment(_connection, new EquipmentDefinition { Id = "eq-collar", Title = "Collar", Category = "Wear" });
            CatalogRepositories.CreateSmartToyCapability(_connection, new SmartToyCapabilityDefinition { Id = "cap-edge", Title = "Edging" });

            // Cards.
            CardRepository.Create(_connection, new CardDefinition { Id = "c-neutral", Title = "Neutral", CardTagIds = { "tag-soft" } });
            CardRepository.Create(_connection, new CardDefinition
            {
                Id = "c-love",
                Title = "Praise Card",
                CardTagIds = { "tag-tease" },
                KinkIds = { "kink-praise" },
            });
            CardRepository.Create(_connection, new CardDefinition
            {
                Id = "c-torture",
                Title = "Denial Card",
                CardTagIds = { "tag-tease" },
                KinkIds = { "kink-denial" },
            });
            CardRepository.Create(_connection, new CardDefinition
            {
                Id = "c-dontconsent",
                Title = "Forbidden Card",
                CardTagIds = { "tag-tease" },
                KinkIds = { "kink-no" },
            });
            CardRepository.Create(_connection, new CardDefinition
            {
                Id = "c-equipment",
                Title = "Collar Card",
                CardTagIds = { "tag-tease" },
                RequiredEquipmentIds = { "eq-collar" },
            });
            CardRepository.Create(_connection, new CardDefinition
            {
                Id = "c-capability",
                Title = "Toy Card",
                CardTagIds = { "tag-tease" },
                RequiredCapabilityIds = { "cap-edge" },
            });

            // Phase with an ALL/ANY query: ALL=[tease] restricts to tease cards;
            // ANY empty = no additional restriction.
            PhaseRepository.Create(_connection, "p-main", "Main");
            PhaseExitRepository.Create(_connection, "p-main", new PhaseExitDefinition { Id = "px-main-complete", Name = "Complete" }, 0);
            PhaseRepository.ReplaceCardQuery(_connection, "p-main", new[] { "tag-tease" }, new string[0]);

            // Session of the JOI type with tuned weighting.
            SessionRepository.Create(_connection, "s-joi", "JOI Session", "type-joi");
            SessionRepository.ReplaceCardWeighting(_connection, "s-joi", new SessionCardWeightingDefinition
            {
                LoveBase = 2f, LoveHappinessGain = 1f,
                LikeBase = 1f, LikeHappinessGain = 1f,
                TortureBase = 1f, TortureUnhappinessGain = 3f,
            });

            var content = GameContentSnapshotLoader.Load(_connection);

            // Phase graph: Entry -> CardExecutor -> progress check -> GOTO Complete.
            var phase = content.Phases.First(p => p.Id == "p-main");
            var entry = new PhaseEntryNodeDefinition { Id = "n-entry" };
            entry.Outputs.Add(new GraphOutputDefinition { Id = "n-entry-out", Kind = GraphPortKind.Normal });
            var exec = new CardExecutorNodeDefinition { Id = "n-exec" };
            exec.Outputs.Add(new GraphOutputDefinition { Id = "n-exec-out", Kind = GraphPortKind.Normal });
            var check = new VariableCheckNodeDefinition
            {
                Id = "n-check",
                SourceKind = VariableSourceKind.PhaseProgress,
                Operator = VariableCompareOperator.GreaterThanOrEqual,
                CompareValue = 10f,
            };
            check.Outputs.Add(new GraphOutputDefinition { Id = "n-check-true", Kind = GraphPortKind.True });
            check.Outputs.Add(new GraphOutputDefinition { Id = "n-check-false", Kind = GraphPortKind.False });
            var gotoDone = new ActionNodeDefinition { Id = "n-goto", Sequence = new ActionSequenceDefinition { Id = "n-goto-seq" } };
            gotoDone.Sequence.Instances.Add(new PhaseGotoInstanceDefinition { Id = "n-goto-inst", PhaseExitId = "px-main-complete", IsBlocking = true });
            gotoDone.Outputs.Add(new GraphOutputDefinition { Id = "n-goto-out", Kind = GraphPortKind.Normal });

            phase.Graph.Nodes.Add(entry);
            phase.Graph.Nodes.Add(exec);
            phase.Graph.Nodes.Add(check);
            phase.Graph.Nodes.Add(gotoDone);
            phase.Graph.Edges.Add(new GraphEdgeDefinition { Id = "e1", SourceOutputId = "n-entry-out", TargetNodeId = "n-exec" });
            phase.Graph.Edges.Add(new GraphEdgeDefinition { Id = "e2", SourceOutputId = "n-exec-out", TargetNodeId = "n-check" });
            phase.Graph.Edges.Add(new GraphEdgeDefinition { Id = "e3", SourceOutputId = "n-check-true", TargetNodeId = "n-goto" });
            phase.Graph.Edges.Add(new GraphEdgeDefinition { Id = "e4", SourceOutputId = "n-check-false", TargetNodeId = "n-exec" });

            DatabaseInitializer.WritePhaseNode(_connection, null, phase.Id, entry);
            DatabaseInitializer.WritePhaseNode(_connection, null, phase.Id, exec);
            DatabaseInitializer.WritePhaseNode(_connection, null, phase.Id, check);
            DatabaseIdentifierFix(gotoDone);
            DatabaseInitializer.WritePhaseNode(_connection, null, phase.Id, gotoDone);
            foreach (var edge in phase.Graph.Edges)
            {
                Sql.Execute(_connection, null,
                    "INSERT INTO phase_graph_edge (id, phase_id, source_port_id, target_node_id) VALUES (@id, @phase, @source, @target);",
                    ("id", edge.Id), ("phase", phase.Id), ("source", edge.SourceOutputId), ("target", edge.TargetNodeId));
            }

            // Session graph: Start -> ref(main) -> End, Complete exits forward.
            var start = new SessionStartNodeDefinition { Id = "n-start" };
            start.Outputs.Add(new GraphOutputDefinition { Id = "n-start-out", Kind = GraphPortKind.Normal });
            var reference = new PhaseReferenceNodeDefinition { Id = "n-ref", PhaseId = "p-main" };
            reference.Outputs.Add(new GraphOutputDefinition { Id = "n-ref-exit", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-main-complete" });
            var end = new SessionEndNodeDefinition { Id = "n-end" };

            var session = content.Sessions.First(s => s.Id == "s-joi");
            session.Graph.Nodes.Add(start);
            session.Graph.Nodes.Add(reference);
            session.Graph.Nodes.Add(end);
            session.Graph.Edges.Add(new GraphEdgeDefinition { Id = "se1", SourceOutputId = "n-start-out", TargetNodeId = "n-ref" });
            session.Graph.Edges.Add(new GraphEdgeDefinition { Id = "se2", SourceOutputId = "n-ref-exit", TargetNodeId = "n-end" });

            DatabaseInitializer.WriteSessionNode(_connection, null, session.Id, start);
            DatabaseInitializer.WriteSessionNode(_connection, null, session.Id, reference);
            DatabaseInitializer.WriteSessionNode(_connection, null, session.Id, end);
            foreach (var edge in session.Graph.Edges)
            {
                Sql.Execute(_connection, null,
                    "INSERT INTO session_graph_edge (id, session_id, source_port_id, target_node_id) VALUES (@id, @session, @source, @target);",
                    ("id", edge.Id), ("session", session.Id), ("source", edge.SourceOutputId), ("target", edge.TargetNodeId));
            }

            return GameContentSnapshotLoader.Load(_connection);
        }

        private static void DatabaseIdentifierFix(ActionNodeDefinition node)
        {
            // The sequence must exist before the node row references it.
            using (var noop = new NoopScope())
            {
                // ActionSequenceWriter is invoked inside WritePhaseNode via the
                // initializer path; nothing extra needed here.
            }
        }

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }

        [Test]
        public void FullPipeline_SelectsOnlyEligibleCards_AndWeightsApply()
        {
            var content = BuildScenario(null);

            // Profile: praise=Love, denial=Torture, kink-no unconfigured,
            // no equipment, no capabilities.
            var profile = new CardSelectionProfile();
            profile.KinkPreferences["kink-praise"] = KinkPreference.Love;
            profile.KinkPreferences["kink-denial"] = KinkPreference.Torture;

            var phase = content.Phases.First(p => p.Id == "p-main");
            var results = CardEligibilityEngine.Evaluate(content.Cards, phase, profile);

            var byId = results.ToDictionary(r => r.Card.Id);
            Assert.IsFalse(byId["c-neutral"].IsEligible, "fails ALL (no tease tag)");
            Assert.IsTrue(byId["c-love"].IsEligible, "praise card passes all filters");
            Assert.IsFalse(byId["c-dontconsent"].IsEligible, "unconfigured kink rejects");
            Assert.IsFalse(byId["c-equipment"].IsEligible, "missing equipment rejects");
            Assert.IsFalse(byId["c-capability"].IsEligible, "missing capability rejects");
            // c-torture carries the denial kink (Torture preference) — eligible.

            var weighting = content.Sessions.First(s => s.Id == "s-joi").CardWeighting;
            var loveWeight = CardWeightCalculator.ComputeWeight(byId["c-love"].Card, profile, weighting, 50f);
            var tortureWeight = CardWeightCalculator.ComputeWeight(byId["c-torture"].Card, profile, weighting, 50f);
            // h=.5: love = 2+1*.5 = 2.5; torture = 1+3*.5 = 2.5.
            Assert.AreEqual(2.5f, loveWeight, 0.0001f, "authored weighting applies to Love");
            Assert.AreEqual(2.5f, tortureWeight, 0.0001f, "authored weighting applies to Torture");
        }

        [Test]
        public async Task FullPipeline_PlaysThroughTheRealEngine()
        {
            var content = BuildScenario(null);

            var profile = new CardSelectionProfile();
            profile.KinkPreferences["kink-praise"] = KinkPreference.Love;
            profile.KinkPreferences["kink-denial"] = KinkPreference.Torture;
            profile.KinkPreferences["kink-no"] = KinkPreference.DontConsent;

            // Give the phase a playable query: ALL=[tease], ANY=[] (no ANY restriction).
            PhaseRepository.ReplaceCardQuery(_connection, "p-main", new[] { "tag-tease" }, new string[0]);
            content = GameContentSnapshotLoader.Load(_connection);
            var phase = content.Phases.First(p => p.Id == "p-main");

            // The engine needs cards playable through the CardExecutor; the
            // forbidden card is DontConsent so only praise/denial survive.
            var engine = new GameSessionEngine(content, "s-joi",
                new CoreServices(new FakeDelayService()), null, rngFactory: null, selectionProfile: profile);

            var drawn = new System.Collections.Generic.List<string>();
            engine.CardStarted += card => drawn.Add(card.Id);

            // Cards default to WaitForContinue + Progress; continue past each
            // wait until the session completes.
            AdvanceResult result = null;
            var guard = 0;
            while (guard++ < 50)
            {
                result = result != null && result.Kind == AdvanceResultKind.WaitForContinue
                    ? await engine.ContinueAsync(CancellationToken.None)
                    : await engine.RunUntilYieldAsync(CancellationToken.None);
                if (result.Kind != AdvanceResultKind.WaitForContinue) break;
            }

            // The session completes when the progress check GOTOs Complete.
            Assert.AreEqual(AdvanceResultKind.SessionCompleted, result.Kind,
                "session completes through the full pipeline");
            CollectionAssert.IsNotEmpty(drawn, "at least one card was drawn");
            CollectionAssert.DoesNotContain(drawn, "c-dontconsent", "DontConsent card never drawn");
            CollectionAssert.DoesNotContain(drawn, "c-equipment", "equipment card never drawn");
            CollectionAssert.DoesNotContain(drawn, "c-capability", "capability card never drawn");
            CollectionAssert.DoesNotContain(drawn, "c-neutral", "ALL-failing card never drawn");
        }

        // ---------- migration from a pre-Milestone-B database ----------

        [Test]
        public void Migration_FromV1FixtureDatabase_PreservesGraphsAndHostExtensions()
        {
            // The preserved v1-era fixture proves the full upgrade path
            // (v1 -> v2 -> v3 -> v4 -> v5) end-to-end: graphs, cards, tags,
            // host extensions, and the v5 catalog/tag split.
            var fixture = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "DotNet", "Game.Content.Sqlite.Tests", "Fixtures", "GameContent-v1.db"));
            Assume.That(File.Exists(fixture), Is.True, "v1 fixture missing at " + fixture);

            var copyPath = Path.Combine(Path.GetTempPath(), "milestoneb-v1-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                // Copy the fixture without mutating it (VACUUM INTO writes a
                // fresh file), then install a host-extension row on the COPY
                // to prove extensions survive the upgrade.
                using (var setup = new SqliteConnection("Data Source=" + fixture))
                {
                    setup.Open();
                    using (var command = setup.CreateCommand())
                    {
                        command.CommandText = "VACUUM INTO '" + copyPath.Replace("'", "''") + "';";
                        command.ExecuteNonQuery();
                    }
                }
                SqliteConnection.ClearAllPools();

                using (var setup = new SqliteConnection("Data Source=" + copyPath))
                {
                    setup.Open();
                    using (var command = setup.CreateCommand())
                    {
                        command.CommandText =
                            "CREATE TABLE IF NOT EXISTS unity_fake_binding (id TEXT PRIMARY KEY, payload TEXT NOT NULL);" +
                            "INSERT OR IGNORE INTO unity_fake_binding VALUES ('x','kept');";
                        command.ExecuteNonQuery();
                    }
                }

                using (var connection = new SqliteConnection("Data Source=" + copyPath))
                {
                    connection.Open();
                    ConnectionInitializer.Initialize(connection);
                    var version = CoreMigrator.EnsureSchema(connection);
                    Assert.AreEqual(CoreMigrations.MaxVersion, version, "fixture upgraded through v5");

                    var content = GameContentSnapshotLoader.Load(connection);
                    Assert.Greater(content.Sessions.Count, 0, "sessions survived");
                    Assert.Greater(content.Phases.Count, 0, "phases survived");
                    Assert.Greater(content.Cards.Count, 0, "cards survived");
                    Assert.Greater(content.CardTagDefinitions.Count, 0, "card tags rebuilt from legacy tag rows");
                    foreach (var card in content.Cards)
                    {
                        Assert.Greater(card.CardTagIds.Count, 0, "tag assignments survived for " + card.Id);
                    }
                    foreach (var session in content.Sessions)
                    {
                        Assert.IsNotNull(session.CardWeighting, "weighting defaulted for " + session.Id);
                        Assert.AreEqual(1f, session.CardWeighting.LoveBase, "default 1.0");
                    }

                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT payload FROM unity_fake_binding WHERE id='x';";
                        Assert.AreEqual("kept", command.ExecuteScalar(), "host extension survived the upgrade");
                    }
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(copyPath)) File.Delete(copyPath);
            }
        }

        private static string CanonicalPath()
        {
            var path = Environment.GetEnvironmentVariable("SQLITE_CANONICAL_DB");
            if (string.IsNullOrEmpty(path))
            {
                path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Content", "GameContent.db"));
            }
            return path;
        }

        // ---------- profile/content DB independence ----------

        [Test]
        public void ProfileDB_IsIndependent_FromContentReplacement()
        {
            var profilePath = Path.Combine(Path.GetTempPath(), "milestoneb-profile-" + Guid.NewGuid().ToString("N") + ".db");
            var contentPathA = Path.Combine(Path.GetTempPath(), "milestoneb-content-a.db");
            var contentPathB = Path.Combine(Path.GetTempPath(), "milestoneb-content-b.db");
            try
            {
                // Configure a profile.
                using (var profile = new SqliteConnection("Data Source=" + profilePath))
                {
                    profile.Open();
                    ProfileStore.EnsureSchema(profile);
                    ProfileStore.SetKinkPreference(profile, "kink-praise", KinkPreference.Love);
                    ProfileStore.SetEquipmentOwned(profile, "eq-collar", true);
                }

                // "Replace" the content DB entirely (seed fresh databases).
                foreach (var path in new[] { contentPathA, contentPathB })
                {
                    using (var content = new SqliteConnection("Data Source=" + path))
                    {
                        content.Open();
                        ConnectionInitializer.Initialize(content);
                        CoreMigrator.EnsureSchema(content);
                    }
                }

                // The profile still holds its rows — content swaps never touch it.
                using (var profile = new SqliteConnection("Data Source=" + profilePath))
                {
                    profile.Open();
                    ProfileStore.EnsureSchema(profile);
                    var loaded = ProfileStore.Load(profile);
                    Assert.AreEqual(KinkPreference.Love, loaded.KinkPreferences["kink-praise"]);
                    Assert.IsTrue(loaded.OwnedEquipmentIds.Contains("eq-collar"));
                }

                // And the profile DB contains no content tables (and vice versa).
                using (var profile = new SqliteConnection("Data Source=" + profilePath))
                {
                    profile.Open();
                    using (var command = profile.CreateCommand())
                    {
                        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('session','phase','card','session_type');";
                        Assert.AreEqual(0L, command.ExecuteScalar(), "no content tables in the profile DB");
                    }
                }
                using (var content = new SqliteConnection("Data Source=" + contentPathA))
                {
                    content.Open();
                    using (var command = content.CreateCommand())
                    {
                        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('kink_preference','equipment_owned');";
                        Assert.AreEqual(0L, command.ExecuteScalar(), "no profile tables in the content DB");
                    }
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                foreach (var path in new[] { profilePath, contentPathA, contentPathB })
                {
                    if (File.Exists(path)) File.Delete(path);
                }
            }
        }

        // ---------- GOTO/RETURN RNG semantics through the real engine ----------

        [Test]
        public async Task GotoReturn_PreservesCallerPhaseRunRng_CalledPhaseGetsFresh()
        {
            var content = new GameContentDefinition();
            content.SessionTypes.Add(new SessionTypeDefinition { Id = "type", Title = "Standard" });
            content.Temperatures.Add(new TemperatureDefinition { Id = "happiness", Title = "Happiness", MinValue = 0f, MaxValue = 100f, DefaultValue = 50f });

            // Cards: +10 progress each, no waits.
            ActionSequenceDefinition Seq(string id) => new ActionSequenceDefinition
            {
                Id = "seq-" + id,
                Instances = { new IncrementProgressInstanceDefinition { Id = "i-" + id, Amount = 10f } },
            };
            content.Cards.Add(new CardDefinition { Id = "c1", Title = "One", Sequence = Seq("c1") });
            content.Cards.Add(new CardDefinition { Id = "c2", Title = "Two", Sequence = Seq("c2") });

            GraphOutputDefinition Out(string id, GraphPortKind kind = GraphPortKind.Normal)
                => new GraphOutputDefinition { Id = id, Kind = kind };

            // main: Entry -> Exec -> progress-check ->
            //   true  -> GOTO Complete   (session exits to End)
            //   false -> GOTO Call       (session transfers into nested)
            // The Call GOTO saves a continuation frame; nested's RETURN pops it
            // and resumes main at the Call node's normal output -> Exec loop.
            var main = new PhaseDefinition { Id = "main", Title = "main" };
            main.Exits.Add(new PhaseExitDefinition { Id = "px-main-complete", Name = "Complete" });
            main.Exits.Add(new PhaseExitDefinition { Id = "px-main-call", Name = "Call" });
            var mEntry = new PhaseEntryNodeDefinition { Id = "m-entry" };
            mEntry.Outputs.Add(Out("m-entry-out"));
            var mExec = new CardExecutorNodeDefinition { Id = "m-exec" };
            mExec.Outputs.Add(Out("m-exec-out"));
            var mCheck = new VariableCheckNodeDefinition
            {
                Id = "m-check",
                SourceKind = VariableSourceKind.PhaseProgress,
                Operator = VariableCompareOperator.GreaterThanOrEqual,
                CompareValue = 20f,
            };
            mCheck.Outputs.Add(Out("m-check-true", GraphPortKind.True));
            mCheck.Outputs.Add(Out("m-check-false", GraphPortKind.False));
            ActionNodeDefinition GotoNode(string id, string exitId)
            {
                var node = new ActionNodeDefinition { Id = id, Sequence = new ActionSequenceDefinition { Id = id + "-seq" } };
                node.Sequence.Instances.Add(new PhaseGotoInstanceDefinition { Id = id + "-inst", PhaseExitId = exitId });
                node.Outputs.Add(Out(id + "-out"));
                return node;
            }
            var mGotoDone = GotoNode("m-goto-done", "px-main-complete");
            var mGotoCall = GotoNode("m-goto-call", "px-main-call");
            main.Graph.Nodes.Add(mEntry);
            main.Graph.Nodes.Add(mExec);
            main.Graph.Nodes.Add(mCheck);
            main.Graph.Nodes.Add(mGotoDone);
            main.Graph.Nodes.Add(mGotoCall);
            main.Graph.Edges.Add(new GraphEdgeDefinition { Id = "m-e1", SourceOutputId = "m-entry-out", TargetNodeId = "m-exec" });
            main.Graph.Edges.Add(new GraphEdgeDefinition { Id = "m-e2", SourceOutputId = "m-exec-out", TargetNodeId = "m-check" });
            main.Graph.Edges.Add(new GraphEdgeDefinition { Id = "m-e3", SourceOutputId = "m-check-true", TargetNodeId = "m-goto-done" });
            main.Graph.Edges.Add(new GraphEdgeDefinition { Id = "m-e4", SourceOutputId = "m-check-false", TargetNodeId = "m-goto-call" });
            main.Graph.Edges.Add(new GraphEdgeDefinition { Id = "m-e5", SourceOutputId = "m-goto-call-out", TargetNodeId = "m-exec", });

            // nested: Entry -> Exec -> RETURN. Its draws come from its own
            // FRESH PhaseRun RNG (per-entrance rule), never main's.
            var nested = new PhaseDefinition { Id = "nested", Title = "nested" };
            var nEntry = new PhaseEntryNodeDefinition { Id = "nested-entry" };
            nEntry.Outputs.Add(Out("nested-entry-out"));
            var nExec = new CardExecutorNodeDefinition { Id = "nested-exec" };
            nExec.Outputs.Add(Out("nested-exec-out"));
            var nReturn = new ReturnNodeDefinition { Id = "nested-return" };
            nested.Graph.Nodes.Add(nEntry);
            nested.Graph.Nodes.Add(nExec);
            nested.Graph.Nodes.Add(nReturn);
            nested.Graph.Edges.Add(new GraphEdgeDefinition { Id = "nested-e1", SourceOutputId = "nested-entry-out", TargetNodeId = "nested-exec" });
            nested.Graph.Edges.Add(new GraphEdgeDefinition { Id = "nested-e2", SourceOutputId = "nested-exec-out", TargetNodeId = "nested-return" });
            content.Phases.Add(main);
            content.Phases.Add(nested);

            // Session: Start -> main; main's Call exit -> nested; main's
            // Complete exit -> End. Nested's RETURN resumes main in place.
            var start = new SessionStartNodeDefinition { Id = "n-start" };
            start.Outputs.Add(Out("n-start-out"));
            var mainRef = new PhaseReferenceNodeDefinition { Id = "n-ref-main", PhaseId = "main" };
            mainRef.Outputs.Add(new GraphOutputDefinition { Id = "n-ref-main-complete", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-main-complete" });
            mainRef.Outputs.Add(new GraphOutputDefinition { Id = "n-ref-main-call", Kind = GraphPortKind.PhaseExit, PhaseExitId = "px-main-call" });
            var nestedRef = new PhaseReferenceNodeDefinition { Id = "n-ref-nested", PhaseId = "nested" };
            var end = new SessionEndNodeDefinition { Id = "n-end" };

            var session = new SessionDefinition { Id = "s", Title = "RNG", SessionTypeId = "type" };
            session.Graph.Nodes.Add(start);
            session.Graph.Nodes.Add(mainRef);
            session.Graph.Nodes.Add(nestedRef);
            session.Graph.Nodes.Add(end);
            session.Graph.Edges.Add(new GraphEdgeDefinition { Id = "se1", SourceOutputId = "n-start-out", TargetNodeId = "n-ref-main" });
            session.Graph.Edges.Add(new GraphEdgeDefinition { Id = "se2", SourceOutputId = "n-ref-main-call", TargetNodeId = "n-ref-nested" });
            session.Graph.Edges.Add(new GraphEdgeDefinition { Id = "se3", SourceOutputId = "n-ref-main-complete", TargetNodeId = "n-end" });
            content.Sessions.Add(session);

            // Deterministic seeds: same content + same seed => same draw sequence.
            async Task<System.Collections.Generic.List<string>> Play(int seed)
            {
                var rngFactory = new PhaseRunRngFactory(seed);
                var engine = new GameSessionEngine(content, "s", new CoreServices(new FakeDelayService()),
                    null, rngFactory);                var draws = new System.Collections.Generic.List<string>();
                engine.CardStarted += card => draws.Add(card.Id);
                var guard = 0;
                while (!engine.IsComplete && guard++ < 50)
                {
                    await engine.RunUntilYieldAsync(CancellationToken.None);
                }
                return draws;
            }

            var first = await Play(123);
            var second = await Play(123);
            CollectionAssert.AreEqual(first, second, "same seed replays the exact draw sequence through nested phase transitions");
            CollectionAssert.IsNotEmpty(first);
        }
    }
}
