# Action Types — Registry and Instance Vocabulary

Status: **current** (Ticket 05, Graph Workbench stack).

## One explicit vocabulary

Every Action the game can author or run is named by a stable key in
`ActionTypeKeys` (portable `Game.Content`):

| Key | Instance type | Display | Blocking | Legal scopes |
|---|---|---|---|---|
| `debug` | `DebugInstanceDefinition` | Debug Log | configurable (default off) | all |
| `statIncrease` | `StatIncreaseInstanceDefinition` | Stat Increase | configurable (default off) | all |
| `increment_progress` | `IncrementProgressInstanceDefinition` | Increment Phase Progress | configurable (default off) | Card / PhaseAction / ChoiceOption |
| `modify_temperature` | `ModifyTemperatureInstanceDefinition` | Modify Temperature | configurable (default off) | all |
| `cutscene` | `CutsceneInstanceDefinition` | Cutscene | configurable (default on) | all |
| `prompt_choice` | `PromptChoiceInstanceDefinition` | Prompt Choice | always on (not configurable) | all |
| `phase_goto` | `PhaseGotoInstanceDefinition` | Phase GOTO | always on | PhaseAction / ChoiceOption |
| `session_goto` | `SessionGotoInstanceDefinition` | Session GOTO | always on | SessionDecisionOption |
| `return` | `ReturnInstanceDefinition` | Return | always on | all |
| `end_session` | `EndSessionInstanceDefinition` | End Session | always on | all |

Sources of truth:

- keys: `Assets/Scripts/Portable/Game.Content/ActionTypeKeys.cs` (shared by Core, SQLite, Unity);
- registry entry (labels, scopes, defaults, WPF editor discriminator): `Assets/Scripts/Portable/Game.Core/ActionTypeRegistry.cs`;
- SQLite persistence discriminators: `DotNet/Game.Content.Sqlite/ActionType.cs` aliases the same keys;
- no reflection-based discovery anywhere.

## Execution

`ActionExecutor` (Game.Core) runs instances against an `ActionExecutionContext`
(player, services, temperatures, active PhaseRun progress, owner scope):

- general instances run their behavior (log/delay, stat, progress, temperature, cutscene, choice);
- nonblocking instances start on the background tracker and never block continuation;
- flow instances are scope/blocking validated and reduced to a `ActionTransfer`
  request; the transfer mechanics themselves land with the graph VM (Tickets 08-09);
- **no implicit progress increment exists** — `IncrementProgress` instances are the only way progress changes.

## Adding a new Action Type — the vertical checklist

1. **Portable subtype** — new `XxxInstanceDefinition` in `Game.Content/ActionInstances.cs`.
2. **Registry** — `ActionTypeKeys` key + `ActionTypeRegistry` entry (label, scopes,
   blocking config/default, default instance factory, editor discriminator).
3. **SQLite** — schema subtype table (or column set), loader case, `ActionSequenceWriter`
   mapping, repository support.
4. **Core executor** — behavior case in `ActionExecutor` (or host semantic request
   for host-only effects).
5. **WPF editor** — discriminator-mapped editor control in the Workbench.
6. **Unity** — host implementation where host-specific.
7. **Tests** — registry coverage, persistence round-trip, execution behavior, scope guard.
