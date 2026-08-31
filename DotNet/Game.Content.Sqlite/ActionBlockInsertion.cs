using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.Common;
using TruthCardGame.Content;
using TruthCardGame.Core;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>Destination facts used when validating an editor-only Block.</summary>
    public sealed class ActionBlockDestination
    {
        public ActionOwnerScope Scope { get; set; }
        public string PhaseId { get; set; }
        public string SessionDecisionNodeId { get; set; }
        public string SessionDecisionOptionId { get; set; }
    }

    public sealed class ActionBlockValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public List<string> Errors { get; } = new List<string>();
        public List<ActionInstanceDefinition> ClonedActions { get; } = new List<ActionInstanceDefinition>();
    }

    /// <summary>
    /// Validates and materializes an Action Block without touching its
    /// destination. All checks are explicit and recursive so insertion can be
    /// atomic at the UI/command boundary.
    /// </summary>
    public static class ActionBlockInsertionService
    {
        public static ActionBlockValidationResult ValidateAndClone(
            ActionBlockDefinition block, ActionBlockDestination destination,
            GameContentDefinition content, string idPrefix)
        {
            var result = new ActionBlockValidationResult();
            if (block == null) { result.Errors.Add("Action Block is unavailable."); return result; }
            if (destination == null) { result.Errors.Add("Action Block destination is unavailable."); return result; }
            try
            {
                var template = ActionBlockSerializer.Deserialize(block.TemplateJson);
                var actions = ActionBlockSerializer.Materialize(template,
                    ordinal => (idPrefix ?? "block") + "-action-" + (ordinal + 1));
                for (var i = 0; i < actions.Count; i++)
                    ValidateAction(actions[i], destination.Scope, destination, content, template.SourcePhaseId,
                        "Action " + (i + 1), result);
                if (result.IsValid) result.ClonedActions.AddRange(actions);
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
            }
            return result;
        }

        private static void ValidateAction(ActionInstanceDefinition action,
            ActionOwnerScope scope, ActionBlockDestination destination,
            GameContentDefinition content, string sourcePhaseId, string path,
            ActionBlockValidationResult result)
        {
            try { ActionTypeRegistry.ValidateScope(action, scope); }
            catch (Exception ex) { result.Errors.Add(path + ": " + ex.Message); }

            content = content ?? new GameContentDefinition();
            switch (action)
            {
                case ModifyTemperatureInstanceDefinition temperature:
                    if (!content.Temperatures.Any(item => item.Id == temperature.TemperatureId))
                        result.Errors.Add(path + ": temperature '" + temperature.TemperatureId + "' no longer exists.");
                    break;
                case CutsceneInstanceDefinition cutscene:
                    if (!content.Resources.Any(item => item.Id == cutscene.ResourceId && string.Equals(item.Kind, ResourceKinds.Cutscene, StringComparison.OrdinalIgnoreCase)))
                        result.Errors.Add(path + ": cutscene Resource '" + cutscene.ResourceId + "' no longer exists.");
                    break;
                case ToyActivityInstanceDefinition toy:
                    ValidateToy(toy.CapabilityId, toy.PatternResourceId, content, path, result);
                    break;
                case ToySetPatternInstanceDefinition toySet:
                    ValidateToy(toySet.CapabilityId, toySet.PatternResourceId, content, path, result);
                    break;
                case DialogFromTagsInstanceDefinition tags:
                    foreach (var tag in tags.RequiredDialogTagIds ?? new List<string>())
                        if (!content.DialogTags.Any(item => item.Id == tag)) result.Errors.Add(path + ": Dialog Tag '" + tag + "' no longer exists.");
                    break;
                case PhaseGotoInstanceDefinition phaseGoto:
                    if (string.IsNullOrWhiteSpace(destination.PhaseId)) result.Errors.Add(path + ": PhaseGoto requires a Phase destination.");
                    else if (!string.Equals(sourcePhaseId ?? "", destination.PhaseId, StringComparison.Ordinal))
                        result.Errors.Add(path + ": PhaseGoto Blocks can only be inserted into the same source Phase ('" + (sourcePhaseId ?? "") + "').");
                    else if (string.IsNullOrWhiteSpace(phaseGoto.PhaseExitId) ||
                        !content.Phases.Any(phase => phase.Id == destination.PhaseId && phase.Exits.Any(exit => exit.Id == phaseGoto.PhaseExitId)))
                        result.Errors.Add(path + ": PhaseExit '" + phaseGoto.PhaseExitId + "' is not available on the destination Phase.");
                    break;
                case PromptChoiceInstanceDefinition choice:
                    if (choice.Options == null || choice.Options.Count < 1 || choice.Options.Count > 3)
                        result.Errors.Add(path + ": PromptChoice must contain 1 to 3 options.");
                    var nestedScope = ActionOwnerScopes.NestedPromptChoice(scope);
                    for (var i = 0; i < (choice.Options?.Count ?? 0); i++)
                    {
                        var option = choice.Options[i];
                        for (var j = 0; j < (option.Sequence?.Instances?.Count ?? 0); j++)
                            ValidateAction(option.Sequence.Instances[j], nestedScope, destination, content, sourcePhaseId,
                                path + ".Option " + (i + 1) + ".Action " + (j + 1), result);
                    }
                    break;
                case SessionGotoInstanceDefinition _:
                    if (scope != ActionOwnerScope.SessionDecisionOptionSequence ||
                        string.IsNullOrWhiteSpace(destination.SessionDecisionNodeId) ||
                        string.IsNullOrWhiteSpace(destination.SessionDecisionOptionId))
                        result.Errors.Add(path + ": SessionGoto requires a direct SessionDecision option destination.");
                    break;
            }
        }

        private static void ValidateToy(string capabilityId, string patternId,
            GameContentDefinition content, string path, ActionBlockValidationResult result)
        {
            if (!content.SmartToyCapabilityDefinitions.Any(item => item.Id == capabilityId))
                result.Errors.Add(path + ": Smart Toy Capability '" + capabilityId + "' no longer exists.");
            if (!content.Resources.Any(item => item.Id == patternId && string.Equals(item.Kind, ResourceKinds.ToyPattern, StringComparison.OrdinalIgnoreCase)))
                result.Errors.Add(path + ": Toy Pattern Resource '" + patternId + "' no longer exists.");
        }
    }

    /// <summary>One compound persistence command for a graph-owned sequence.</summary>
    public sealed class InsertActionBlockCommand : AuthoringCommandBase
    {
        private readonly string _sequenceId;
        private readonly string _nodeId;
        private readonly string _optionId;
        private readonly ActionSequenceDefinition _before;
        private readonly ActionSequenceDefinition _after;

        public InsertActionBlockCommand(Func<DbConnection> connection, string sequenceId,
            string nodeId, string optionId, ActionSequenceDefinition before,
            ActionSequenceDefinition after) : base(connection)
        {
            _sequenceId = sequenceId; _nodeId = nodeId; _optionId = optionId;
            _before = before ?? throw new ArgumentNullException(nameof(before));
            _after = after ?? throw new ArgumentNullException(nameof(after));
        }

        public override string Name => "Insert Action Block";

        protected override void ExecuteCore(DbConnection connection) => Apply(connection, _after);
        protected override void UndoCore(DbConnection connection) => Apply(connection, _before);

        private void Apply(DbConnection connection, ActionSequenceDefinition sequence)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    ActionSequenceWriter.Sync(connection, transaction, sequence);
                    if (!string.IsNullOrWhiteSpace(_nodeId) && !string.IsNullOrWhiteSpace(_optionId))
                    {
                        foreach (var action in sequence.Instances.OfType<SessionGotoInstanceDefinition>())
                        {
                            var hasSocket = false;
                            Sql.QueryAll(connection, transaction,
                                "SELECT 1 FROM session_node_output WHERE session_goto_action_instance_id = @id;",
                                _ => hasSocket = true, ("id", action.Id));
                            if (!hasSocket)
                            {
                                var ordinal = 0;
                                Sql.QueryAll(connection, transaction,
                                    "SELECT COALESCE(MAX(ordinal), -1) + 1 FROM session_node_output WHERE node_id = @node;",
                                    reader => ordinal = reader.GetInt32(0), ("node", _nodeId));
                                Sql.Execute(connection, transaction,
                                    "INSERT INTO session_node_output (id, node_id, port_kind, ordinal, label, phase_exit_id, session_goto_action_instance_id) " +
                                    "VALUES (@id, @node, @kind, @ordinal, @label, NULL, @goto);",
                                    ("id", action.Id + "-port"), ("node", _nodeId),
                                    ("kind", DatabaseInitializer.PortKindName(GraphPortKind.SessionGoto)),
                                    ("ordinal", ordinal), ("label", action.Label ?? ""), ("goto", action.Id));
                            }
                        }
                    }
                    transaction.Commit();
                }
                catch { transaction.Rollback(); throw; }
            }
        }
    }
}
