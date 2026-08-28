using System;
using System.Collections.Generic;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Ticket 17: deep-clone engine for reuse. Every owned entity (phase, exits,
    /// nodes, outputs, edges, sequences, instances, decision options) receives a
    /// NEW stable id; shared reference entities (Resources, Temperature ids,
    /// SessionTypes, referenced Phase ids) stay shared. Edge source output ids
    /// are remapped through the cloned nodes' output lists so wiring survives.
    /// </summary>
    public static class ContentCloner
    {
        /// <summary>Result of cloning one Phase.</summary>
        public sealed class PhaseClone
        {
            public PhaseDefinition Phase { get; set; }
            public Dictionary<string, string> ExitIdMap { get; } = new Dictionary<string, string>();
            public Dictionary<string, string> NodeIdMap { get; } = new Dictionary<string, string>();
        }

        /// <summary>Deep-clones a phase: new phase id, new exit/node/output/edge/instance ids.</summary>
        public static PhaseClone ClonePhase(PhaseDefinition source, string newPhaseId)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrEmpty(newPhaseId)) throw new ArgumentException("New phase id required.", nameof(newPhaseId));

            var clone = new PhaseClone
            {
                Phase = new PhaseDefinition
                {
                    Id = newPhaseId,
                    Title = source.Title,
                },
            };
            clone.Phase.MustIncludeTags.AddRange(source.MustIncludeTags);
            clone.Phase.MustExcludeTags.AddRange(source.MustExcludeTags);

            for (var i = 0; i < source.Exits.Count; i++)
            {
                var exit = source.Exits[i];
                var newExitId = newPhaseId + "-exit-" + (i + 1);
                clone.ExitIdMap[exit.Id] = newExitId;
                clone.Phase.Exits.Add(new PhaseExitDefinition { Id = newExitId, Name = exit.Name });
            }

            // First pass: clone nodes + outputs, remembering old output id -> new output id.
            var oldOutputToNew = new Dictionary<string, string>();
            foreach (var node in source.Graph.Nodes)
            {
                var newNodeId = newPhaseId + "-node-" + (clone.Phase.Graph.Nodes.Count + 1);
                clone.NodeIdMap[node.Id] = newNodeId;
                var cloned = ClonePhaseNode(node, newNodeId, clone.ExitIdMap);
                for (var i = 0; i < node.Outputs.Count; i++)
                {
                    oldOutputToNew[node.Outputs[i].Id] = cloned.Outputs[i].Id;
                }
                clone.Phase.Graph.Nodes.Add(cloned);
            }

            // Second pass: edges through the remap.
            foreach (var edge in source.Graph.Edges)
            {
                clone.Phase.Graph.Edges.Add(new GraphEdgeDefinition
                {
                    Id = newPhaseId + "-edge-" + (clone.Phase.Graph.Edges.Count + 1),
                    SourceOutputId = oldOutputToNew.TryGetValue(edge.SourceOutputId, out var newOutput)
                        ? newOutput : edge.SourceOutputId,
                    TargetNodeId = clone.NodeIdMap.TryGetValue(edge.TargetNodeId, out var newTarget)
                        ? newTarget : edge.TargetNodeId,
                });
            }

            return clone;
        }

        /// <summary>Result of cloning one Session.</summary>
        public sealed class SessionClone
        {
            public SessionDefinition Session { get; set; }
            public Dictionary<string, string> NodeIdMap { get; } = new Dictionary<string, string>();
        }

        /// <summary>
        /// Deep-clones a session: new session id, node/edge/option/instance ids;
        /// PhaseReference nodes keep their Phase id (reuse stays reuse).
        /// </summary>
        public static SessionClone CloneSession(SessionDefinition source, string newSessionId, string title = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrEmpty(newSessionId)) throw new ArgumentException("New session id required.", nameof(newSessionId));

            var clone = new SessionClone
            {
                Session = new SessionDefinition
                {
                    Id = newSessionId,
                    Title = title ?? source.Title,
                    SessionTypeId = source.SessionTypeId,
                },
            };
            clone.Session.Tags.AddRange(source.Tags);

            var oldNodeToNew = new Dictionary<string, string>();
            var oldOutputToNew = new Dictionary<string, string>();
            foreach (var node in source.Graph.Nodes)
            {
                var newNodeId = newSessionId + "-node-" + (clone.Session.Graph.Nodes.Count + 1);
                oldNodeToNew[node.Id] = newNodeId;
                clone.NodeIdMap[node.Id] = newNodeId;
                var cloned = CloneSessionNode(node, newNodeId);
                for (var i = 0; i < node.Outputs.Count; i++)
                {
                    oldOutputToNew[node.Outputs[i].Id] = cloned.Outputs[i].Id;
                }
                clone.Session.Graph.Nodes.Add(cloned);
            }

            // SessionGoto outputs on decision nodes reference instance ids; those
            // instances were re-cloned, so remap the port references too.
            for (var i = 0; i < source.Graph.Nodes.Count; i++)
            {
                if (!(source.Graph.Nodes[i] is SessionDecisionNodeDefinition oldDecision)) continue;
                var newDecision = (SessionDecisionNodeDefinition)clone.Session.Graph.Nodes[i];
                var oldToNewInstance = new Dictionary<string, string>();
                for (var o = 0; o < oldDecision.Options.Count; o++)
                {
                    var oldOption = oldDecision.Options[o];
                    var newOption = newDecision.Options[o];
                    for (var k = 0; k < oldOption.Sequence.Instances.Count; k++)
                    {
                        oldToNewInstance[oldOption.Sequence.Instances[k].Id] = newOption.Sequence.Instances[k].Id;
                    }
                }
                for (var o = 0; o < oldDecision.Outputs.Count; o++)
                {
                    var oldOutput = oldDecision.Outputs[o];
                    if (string.IsNullOrEmpty(oldOutput.SessionGotoActionInstanceId)) continue;
                    if (oldToNewInstance.TryGetValue(oldOutput.SessionGotoActionInstanceId, out var newInstanceId))
                    {
                        ((SessionDecisionNodeDefinition)clone.Session.Graph.Nodes[i]).Outputs[o].SessionGotoActionInstanceId = newInstanceId;
                    }
                }
            }

            foreach (var edge in source.Graph.Edges)
            {
                clone.Session.Graph.Edges.Add(new GraphEdgeDefinition
                {
                    Id = newSessionId + "-edge-" + (clone.Session.Graph.Edges.Count + 1),
                    SourceOutputId = oldOutputToNew.TryGetValue(edge.SourceOutputId, out var newOutput)
                        ? newOutput : edge.SourceOutputId,
                    TargetNodeId = oldNodeToNew.TryGetValue(edge.TargetNodeId, out var newTarget)
                        ? newTarget : edge.TargetNodeId,
                });
            }

            return clone;
        }

        // ---- node cloning ----

        private static GraphNodeDefinition ClonePhaseNode(GraphNodeDefinition node, string newNodeId, Dictionary<string, string> exitIdMap)
        {
            GraphNodeDefinition result;
            switch (node)
            {
                case PhaseEntryNodeDefinition entry: result = new PhaseEntryNodeDefinition(); break;
                case CardExecutorNodeDefinition cardExecutor: result = new CardExecutorNodeDefinition(); break;
                case VariableCheckNodeDefinition check:
                    result = new VariableCheckNodeDefinition
                    {
                        SourceKind = check.SourceKind,
                        VariableKey = check.VariableKey,
                        Operator = check.Operator,
                        CompareValue = check.CompareValue,
                    };
                    break;
                case ActionNodeDefinition action:
                    result = new ActionNodeDefinition
                    {
                        Sequence = CloneSequence(action.Sequence, newNodeId + "-seq", exitIdMap),
                    };
                    break;
                case PhaseDecisionNodeDefinition decision:
                    result = ClonePhaseDecision(decision, newNodeId, exitIdMap);
                    break;
                case ReturnNodeDefinition returnNode: result = new ReturnNodeDefinition(); break;
                default:
                    throw new InvalidOperationException($"ContentCloner: unknown phase node '{node.GetType().Name}'.");
            }
            result.Id = newNodeId;
            for (var i = 0; i < node.Outputs.Count; i++)
            {
                result.Outputs.Add(new GraphOutputDefinition
                {
                    Id = newNodeId + "-out-" + (i + 1),
                    Kind = node.Outputs[i].Kind,
                });
            }
            return result;
        }

        private static PhaseDecisionNodeDefinition ClonePhaseDecision(PhaseDecisionNodeDefinition source, string newNodeId, Dictionary<string, string> exitIdMap)
        {
            var decision = new PhaseDecisionNodeDefinition { Prompt = source.Prompt };
            for (var i = 0; i < source.Options.Count; i++)
            {
                var option = source.Options[i];
                decision.Options.Add(new PhaseDecisionOptionDefinition
                {
                    Id = newNodeId + "-opt-" + (i + 1),
                    Label = option.Label,
                    Sequence = CloneSequence(option.Sequence, newNodeId + "-opt-" + (i + 1) + "-seq", exitIdMap),
                });
            }
            return decision;
        }

        private static SessionGraphNodeDefinition CloneSessionNode(SessionGraphNodeDefinition node, string newNodeId)
        {
            SessionGraphNodeDefinition result;
            switch (node)
            {
                case SessionStartNodeDefinition startNode: result = new SessionStartNodeDefinition(); break;
                case PhaseReferenceNodeDefinition reference:
                    result = new PhaseReferenceNodeDefinition { PhaseId = reference.PhaseId };
                    break;
                case SessionDecisionNodeDefinition decision:
                    result = CloneSessionDecision(decision, newNodeId);
                    break;
                case SessionEndNodeDefinition endNode: result = new SessionEndNodeDefinition(); break;
                default:
                    throw new InvalidOperationException($"ContentCloner: unknown session node '{node.GetType().Name}'.");
            }
            result.Id = newNodeId;
            for (var i = 0; i < node.Outputs.Count; i++)
            {
                var output = node.Outputs[i];
                result.Outputs.Add(new GraphOutputDefinition
                {
                    Id = newNodeId + "-out-" + (i + 1),
                    Kind = output.Kind,
                    PhaseExitId = output.PhaseExitId,
                    Label = output.Label,
                    SessionGotoActionInstanceId = output.SessionGotoActionInstanceId,
                });
            }
            return result;
        }

        private static SessionDecisionNodeDefinition CloneSessionDecision(SessionDecisionNodeDefinition source, string newNodeId)
        {
            var decision = new SessionDecisionNodeDefinition { Prompt = source.Prompt };
            for (var i = 0; i < source.Options.Count; i++)
            {
                var option = source.Options[i];
                decision.Options.Add(new SessionDecisionOptionDefinition
                {
                    Id = newNodeId + "-opt-" + (i + 1),
                    Label = option.Label,
                    Sequence = CloneSequence(option.Sequence, newNodeId + "-opt-" + (i + 1) + "-seq", null),
                });
            }
            return decision;
        }

        // ---- sequence / instance cloning ----

        private static ActionSequenceDefinition CloneSequence(ActionSequenceDefinition source, string newSequenceId, Dictionary<string, string> exitIdMap)
        {
            var sequence = new ActionSequenceDefinition { Id = newSequenceId };
            for (var i = 0; i < source.Instances.Count; i++)
            {
                sequence.Instances.Add(CloneInstance(source.Instances[i], newSequenceId + "-i-" + (i + 1), exitIdMap));
            }
            return sequence;
        }

        private static ActionInstanceDefinition CloneInstance(ActionInstanceDefinition source, string newId, Dictionary<string, string> exitIdMap)
        {
            ActionInstanceDefinition result;
            switch (source)
            {
                case DebugInstanceDefinition debug:
                    result = new DebugInstanceDefinition { Message = debug.Message, DelaySeconds = debug.DelaySeconds };
                    break;
                case StatIncreaseInstanceDefinition stat:
                    result = new StatIncreaseInstanceDefinition { StatKey = stat.StatKey, Amount = stat.Amount };
                    break;
                case IncrementProgressInstanceDefinition progress:
                    result = new IncrementProgressInstanceDefinition { Amount = progress.Amount };
                    break;
                case ModifyTemperatureInstanceDefinition temperature:
                    result = new ModifyTemperatureInstanceDefinition { TemperatureId = temperature.TemperatureId, Amount = temperature.Amount };
                    break;
                case CutsceneInstanceDefinition cutscene:
                    result = new CutsceneInstanceDefinition { ResourceId = cutscene.ResourceId };
                    break;
                case PromptChoiceInstanceDefinition choice:
                {
                    var clone = new PromptChoiceInstanceDefinition { Prompt = choice.Prompt };
                    for (var i = 0; i < choice.Options.Count; i++)
                    {
                        clone.Options.Add(new PromptChoiceOptionDefinition
                        {
                            Id = newId + "-opt-" + (i + 1),
                            Label = choice.Options[i].Label,
                            Sequence = CloneSequence(choice.Options[i].Sequence, newId + "-opt-" + (i + 1) + "-seq", exitIdMap),
                        });
                    }
                    result = clone;
                    break;
                }
                case PhaseGotoInstanceDefinition phaseGoto:
                    result = new PhaseGotoInstanceDefinition
                    {
                        PhaseExitId = exitIdMap != null && exitIdMap.TryGetValue(phaseGoto.PhaseExitId, out var mapped)
                            ? mapped : phaseGoto.PhaseExitId,
                    };
                    break;
                case SessionGotoInstanceDefinition sessionGoto:
                    result = new SessionGotoInstanceDefinition { Label = sessionGoto.Label };
                    break;
                case ReturnInstanceDefinition returnInstance: result = new ReturnInstanceDefinition(); break;
                case EndSessionInstanceDefinition endInstance: result = new EndSessionInstanceDefinition(); break;
                default:
                    throw new InvalidOperationException($"ContentCloner: unknown instance '{source.GetType().Name}'.");
            }
            result.Id = newId;
            result.IsBlocking = source.IsBlocking;
            return result;
        }
    }
}
