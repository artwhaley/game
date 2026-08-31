using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using NUnit.Framework;
using TruthCardGame.Content;
using TruthCardGame.Core;

namespace TruthCardGame.Content.Sqlite.Tests
{
    [TestFixture]
    public sealed class ActionBlockTests
    {
        [Test]
        public void Serializer_RoundTripsEveryCurrentActionAndNestedPromptChoice()
        {
            var prompt = new PromptChoiceInstanceDefinition { Id = "prompt", Prompt = "Choose", IsBlocking = true };
            prompt.Options.Add(new PromptChoiceOptionDefinition
            {
                Id = "option", Label = "Yes", Sequence = new ActionSequenceDefinition { Id = "option-seq" }
            });
            prompt.Options[0].Sequence.Instances.Add(new DelayInstanceDefinition { Id = "nested", DurationSeconds = 2, IsBlocking = true });
            var actions = new ActionInstanceDefinition[]
            {
                new DebugInstanceDefinition { Id = "debug", Message = "log", DelaySeconds = 1, IsBlocking = false },
                new StatIncreaseInstanceDefinition { Id = "stat", StatKey = "strength", Amount = 3 },
                new IncrementProgressInstanceDefinition { Id = "progress", Amount = 4 },
                new ModifyTemperatureInstanceDefinition { Id = "temperature", TemperatureId = "happiness", Amount = 5 },
                new CutsceneInstanceDefinition { Id = "cutscene", ResourceId = "resource" },
                new DialogInstanceDefinition { Id = "dialog", Text = "Hello" },
                new DialogFromTagsInstanceDefinition { Id = "dialog-tags" },
                new DelayInstanceDefinition { Id = "delay", DurationSeconds = 6 },
                new ToyActivityInstanceDefinition { Id = "toy", CapabilityId = "cap", PatternResourceId = "pattern", DurationSeconds = 7 },
                new ToySetPatternInstanceDefinition { Id = "toy-set", CapabilityId = "cap", PatternResourceId = "pattern", IsBlocking = false },
                prompt,
                new WaitForContinueInstanceDefinition { Id = "continue" },
                new WaitForAllInstanceDefinition { Id = "all" },
                new PhaseGotoInstanceDefinition { Id = "phase-goto", PhaseExitId = "exit" },
                new SessionGotoInstanceDefinition { Id = "session-goto", Label = "path" },
                new ReturnInstanceDefinition { Id = "return" },
                new EndSessionInstanceDefinition { Id = "end" },
            };
            ((DialogFromTagsInstanceDefinition)actions[6]).RequiredDialogTagIds.Add("teasing");

            var json = ActionBlockSerializer.Serialize(actions, "phase-source");
            var template = ActionBlockSerializer.Deserialize(json);
            var clone = ActionBlockSerializer.Materialize(template, i => "inserted-" + i);

            Assert.That(template.SourcePhaseId, Is.EqualTo("phase-source"));
            Assert.That(clone, Has.Count.EqualTo(actions.Length));
            Assert.That(clone.Select(action => action.Id), Is.EqualTo(Enumerable.Range(0, actions.Length).Select(i => "inserted-" + i)));
            var clonedPrompt = clone.OfType<PromptChoiceInstanceDefinition>().Single();
            Assert.That(clonedPrompt.Options[0].Id, Is.EqualTo("inserted-10-option-1"));
            Assert.That(clonedPrompt.Options[0].Sequence.Instances[0].Id, Is.EqualTo("inserted-10-option-1-action-1"));
            Assert.That(((DialogFromTagsInstanceDefinition)clone[6]).RequiredDialogTagIds, Is.EqualTo(new[] { "teasing" }));
        }

        [Test]
        public void Repository_CrudAndSearch_PersistsEditorOnlyTemplate()
        {
            var path = Path.Combine(Path.GetTempPath(), "action-block-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var connection = new SqliteConnection("Data Source=" + path))
                {
                    ConnectionInitializer.Initialize(connection);
                    CoreMigrator.EnsureSchema(connection);
                    var block = new ActionBlockDefinition
                    {
                        Id = "block-1", Name = "Strong Tease", FormatVersion = 1,
                        TemplateJson = ActionBlockSerializer.Serialize(new[] { new WaitForAllInstanceDefinition { Id = "wait" } })
                    };
                    ActionBlockRepository.Create(connection, block);
                    Assert.That(ActionBlockRepository.List(connection, "tease").Single().Id, Is.EqualTo("block-1"));
                    ActionBlockRepository.Rename(connection, "block-1", "Renamed");
                    Assert.That(ActionBlockRepository.Get(connection, "block-1").Name, Is.EqualTo("Renamed"));
                    ActionBlockRepository.Delete(connection, "block-1");
                    Assert.That(ActionBlockRepository.Get(connection, "block-1"), Is.Null);
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void InsertionValidation_RejectsCrossPhaseAtomically()
        {
            var source = new ActionSequenceDefinition { Id = "source" };
            source.Instances.Add(new PhaseGotoInstanceDefinition { Id = "goto", PhaseExitId = "exit", IsBlocking = true });
            var block = new ActionBlockDefinition
            {
                Id = "block", Name = "Goto", TemplateJson = ActionBlockSerializer.Serialize(source.Instances, "phase-a"), FormatVersion = 1
            };
            var content = new GameContentDefinition();
            content.Phases.Add(new PhaseDefinition { Id = "phase-b", Exits = { new PhaseExitDefinition { Id = "exit", Name = "Exit" } } });
            var result = ActionBlockInsertionService.ValidateAndClone(block,
                new ActionBlockDestination { Scope = ActionOwnerScope.PhaseActionSequence, PhaseId = "phase-b" }, content, "new");
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ClonedActions, Is.Empty);
            Assert.That(result.Errors.Any(error => error.Contains("same source Phase")), Is.True);
        }

        [Test]
        public void InsertionValidation_AcceptsSamePhaseGoto()
        {
            var source = new ActionSequenceDefinition { Id = "source" };
            source.Instances.Add(new PhaseGotoInstanceDefinition
                { Id = "goto", PhaseExitId = "exit", IsBlocking = true });
            var content = new GameContentDefinition();
            content.Phases.Add(new PhaseDefinition
            {
                Id = "phase-a",
                Exits = { new PhaseExitDefinition { Id = "exit", Name = "Exit" } }
            });
            var block = new ActionBlockDefinition
            {
                Id = "block", Name = "Goto",
                TemplateJson = ActionBlockSerializer.Serialize(source.Instances, "phase-a"),
                FormatVersion = 1
            };

            var result = ActionBlockInsertionService.ValidateAndClone(block,
                new ActionBlockDestination { Scope = ActionOwnerScope.PhaseActionSequence, PhaseId = "phase-a" },
                content, "new");

            Assert.That(result.IsValid, Is.True, string.Join(" ", result.Errors));
            Assert.That(result.ClonedActions, Has.Count.EqualTo(1));
        }

        [Test]
        public void InsertionValidation_RejectsZeroDialogTagsAndBlockingSetPattern()
        {
            var dialog = new DialogFromTagsInstanceDefinition { Id = "dialog", IsBlocking = true };
            var validJson = ActionBlockSerializer.Serialize(new ActionInstanceDefinition[]
            {
                new ToySetPatternInstanceDefinition
                    { Id = "set", CapabilityId = "cap", PatternResourceId = "pattern", IsBlocking = false },
                dialog
            });
            var block = new ActionBlockDefinition
            {
                Id = "block", Name = "Invalid",
                TemplateJson = validJson,
                FormatVersion = 1
            };
            var content = new GameContentDefinition();
            content.SmartToyCapabilityDefinitions.Add(new SmartToyCapabilityDefinition { Id = "cap", Title = "Capability" });
            content.Resources.Add(new ResourceDefinition { Id = "pattern", Kind = ResourceKinds.ToyPattern, Name = "Pattern" });

            var result = ActionBlockInsertionService.ValidateAndClone(block,
                new ActionBlockDestination { Scope = ActionOwnerScope.CardSequence }, content, "new");

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.ClonedActions, Is.Empty);
            Assert.That(result.Errors.Any(error => error.Contains("at least one Dialog Tag")), Is.True);

            block.TemplateJson = validJson.Replace("\"IsBlocking\":false", "\"IsBlocking\":true");
            var malformedSetResult = ActionBlockInsertionService.ValidateAndClone(block,
                new ActionBlockDestination { Scope = ActionOwnerScope.CardSequence }, content, "new");
            Assert.That(malformedSetResult.IsValid, Is.False);
            Assert.That(malformedSetResult.ClonedActions, Is.Empty);
            Assert.That(malformedSetResult.Errors.Any(error => error.Contains("must be nonblocking")), Is.True);
        }

        [Test]
        public void Repository_DoesNotCreateSchemaOnUnmigratedDatabase()
        {
            var path = Path.Combine(Path.GetTempPath(), "action-block-unmigrated-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var connection = new SqliteConnection("Data Source=" + path))
                {
                    connection.Open();
                    var error = Assert.Throws<InvalidOperationException>(() => ActionBlockRepository.List(connection));
                    Assert.That(error.Message, Does.Contain("CoreMigrator.EnsureSchema"));
                    Assert.That(TableExists(connection, "wpf_action_block"), Is.False);
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void SessionGotoInsertion_CreatesFreshSocketsWithoutEdges_AndUndoRedoKeepsIds()
        {
            var path = Path.Combine(Path.GetTempPath(), "action-block-goto-" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                using (var connection = new SqliteConnection("Data Source=" + path))
                {
                    connection.Open();
                    ConnectionInitializer.Initialize(connection);
                    CoreMigrator.EnsureSchema(connection);
                    SessionRepository.Create(connection, "session", "Session", "type-standard");
                    Execute(connection, "INSERT INTO session_graph_node VALUES ('decision','session','Decision');");
                    Execute(connection, "INSERT INTO session_node_decision VALUES ('decision','Choose');");
                    SessionDecisionRepository.AddOption(connection, "decision", "option", "Option", "option-seq");
                    Execute(connection, "INSERT INTO session_node_output (id,node_id,port_kind,ordinal,label,phase_exit_id,session_goto_action_instance_id) VALUES ('existing','decision','normal',0,'Existing',NULL,NULL);");

                    var source = new ActionBlockDefinition
                    {
                        Id = "block", Name = "Two Gotos", FormatVersion = 1,
                        TemplateJson = ActionBlockSerializer.Serialize(new ActionInstanceDefinition[]
                        {
                            new SessionGotoInstanceDefinition { Id = "source-a", Label = "A", IsBlocking = true },
                            new SessionGotoInstanceDefinition { Id = "source-b", Label = "B", IsBlocking = true },
                        })
                    };
                    var validation = ActionBlockInsertionService.ValidateAndClone(source,
                        new ActionBlockDestination
                        {
                            Scope = ActionOwnerScope.SessionDecisionOptionSequence,
                            SessionDecisionNodeId = "decision",
                            SessionDecisionOptionId = "option"
                        }, new GameContentDefinition(), "insert");
                    Assert.That(validation.IsValid, Is.True, string.Join(" ", validation.Errors));

                    var before = new ActionSequenceDefinition { Id = "option-seq" };
                    var after = new ActionSequenceDefinition { Id = "option-seq" };
                    after.Instances.AddRange(validation.ClonedActions);
                    Func<DbConnection> open = () =>
                    {
                        var db = new SqliteConnection("Data Source=" + path);
                        db.Open();
                        ConnectionInitializer.Initialize(db);
                        return db;
                    };
                    var command = new InsertActionBlockCommand(open, "option-seq", "decision", "option", before, after);
                    command.Execute();

                    Assert.That(Count(connection, "session_node_output WHERE port_kind='session_goto'"), Is.EqualTo(2));
                    Assert.That(Count(connection, "session_graph_edge"), Is.EqualTo(0));
                    var insertedIds = QueryStrings(connection, "SELECT session_goto_action_instance_id FROM session_node_output WHERE port_kind='session_goto' ORDER BY ordinal");
                    Assert.That(insertedIds, Is.EqualTo(validation.ClonedActions.OfType<SessionGotoInstanceDefinition>().Select(action => action.Id).ToList()));

                    command.Undo();
                    Assert.That(Count(connection, "session_node_output WHERE port_kind='session_goto'"), Is.EqualTo(0));
                    command.Execute();
                    Assert.That(QueryStrings(connection, "SELECT session_goto_action_instance_id FROM session_node_output WHERE port_kind='session_goto' ORDER BY ordinal"),
                        Is.EqualTo(insertedIds));
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private static bool TableExists(SqliteConnection connection, string table)
        {
            return Count(connection, "sqlite_master WHERE type='table' AND name='" + table + "'") > 0;
        }

        private static int Count(SqliteConnection connection, string suffix)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM " + suffix;
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        private static List<string> QueryStrings(SqliteConnection connection, string sql)
        {
            var result = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                using (var reader = command.ExecuteReader())
                    while (reader.Read()) result.Add(reader.GetString(0));
            }
            return result;
        }

        private static void Execute(SqliteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
    }
}
