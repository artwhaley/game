using System;
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
    }
}
