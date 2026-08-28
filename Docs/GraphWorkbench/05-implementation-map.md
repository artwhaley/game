# Implementation Map — Current Source → Target Replacements (Ticket 01)

Verified against actual source at HEAD of this stack's start. This is the authority for what each ticket touches. Layer by layer:

## 1. Portable content model — `Assets/Scripts/Portable/Game.Content/`

| Current | Fate | Target |
|---|---|---|
| `GameContentDefinition.cs` (Deck, Sessions, Phases, Cards, Actions, Resources) | Adapt | Keep Sessions/Phases/Cards/Resources/Deck. **Delete** `Actions` list. Add `SessionTypes`, `TemperatureDefinitions`. |
| `SessionDefinition.cs` (Id, Title, Tags, PhaseSlots) | Reshape | Delete `PhaseSlots`. Add `NodeTypeId` fields per architecture §4: graph nodes list, typed sub-nodes (`SessionStart/End/Decision/PhaseReference`), outputs, edges, decision options, one `SessionTypeId`. |
| `PhaseDefinition.cs` (Id, Title, MustIncludeTags, MustExcludeTags, MinCards, MaxCards) | Reshape | Delete `MinCards`/`MaxCards`. Keep card-selection tags. Add exported `Exits` (`PhaseExitDefinition`: Id, Name, ordinal) + graph nodes/sub-nodes/outputs/edges + decision options per §5–8. |
| `PhaseSlotDefinition.cs`, `PhaseSlotCandidateDefinition.cs` | **Delete** | No equivalent; superseded entirely. |
| `CardDefinition.cs` (Id, Title, Tags, ActionIds) | Adapt | Replace `ActionIds` with an owned ordered `List<ActionInstanceDefinition>` sequence (per-card occurrences; default `IncrementProgress +10` on authoring). |
| `ChoiceOptionDefinition.cs` (Label, ChildActionId) | Repurpose | Becomes the option shape of `PromptChoiceInstance` (Label + owned action sequence); no more single-child reference into reusable Actions. |
| `CardDeckDefinition.cs` | Keep | Unchanged (draw pool plumbing retained). |
| `ResourceDefinition.cs` | Keep | Unchanged; referenced by cutscene instances. |
| `GameActionDefinition.cs` (base incl. `Name`) + `ChoiceActionDefinition`, `CutsceneActionDefinition`, `DebugActionDefinition`, `StatIncreaseActionDefinition` | **Replace** | Ten portable instance types per Architecture §16: `DebugInstance`, `StatIncreaseInstance`, `IncrementProgressInstance`, `ModifyTemperatureInstance`, `CutsceneInstance`, `PromptChoiceInstance` (+options), `PhaseGotoInstance` (references PhaseExitId), `SessionGotoInstance` (Label; owns port mapping via session output), `ReturnInstance`, `EndSessionInstance`. Common base carries `Id`, sequence ordinal, `IsBlocking`. |

## 2. Core runtime — `Assets/Scripts/Portable/Game.Core/`

| Current | Fate | Target |
|---|---|---|
| `ContentCatalog.cs` | Adapt | Index Sessions/Phases/Cards/Resources + new SessionTypes/TemperatureDefinitions. Drop reusable-action index and all slot concepts. Id-based relationship resolution. |
| `SessionDriver.cs` | **Replace** | Graph VM: `SessionRun` stepping SessionStart → PhaseReference/SessionDecision/SessionEnd per Execution Semantics; owns Temperatures, continuation stack, active PhaseRun. |
| (new) | Add | `PhaseRun` + `PhaseGraphVM` local stepping (Entry/CardExecutor/VariableCheck/ActionNode/PhaseDecision/Return) with per-run progress + RNG state; GOTO/RETURN continuation mechanics. |
| `CardSelector.cs` | Move/adapt | Selection becomes PhaseRun-local state (fresh RNG object/run via `IRandomSource` factory on spawn); tag include/exclude logic reused against Phase metadata instead of PhaseSlot candidates. |
| `ActionExecutor.cs` | Replace internals | Executes **instances** dispatched through an explicit `ActionTypeRegistry` (stable keys, scopes, defaults, blocking rules). Legacy configured-action execution disappears. |
| `CoreServices.cs` / `IRandomSource.cs` / `SystemRandomSource.cs` | Extend | Add RNG factory seam (`Func<IRandomSource>`) so each PhaseRun mints its own source deterministically; existing Delay/Log/Prompt/Cutscene services remain. |
| `GameSessionEngine.cs` | Adapt facade | `RunUntilYieldAsync` / `ContinueAsync`, `Player`, completion/events; internals delegate to the graph VM; add `SessionSpawnOptions` (temperature overrides, seed/factory) + Events for phase-node context where hosts need it. |
| `BackgroundActionTracker.cs`, host service interfaces, `Player.cs`, `PlayerStats.cs`, `GameContext.cs`, `AdvanceResult.cs` | Keep/adapt | Retained; `AdvanceResult` reports authored WaitForContinue, completion, busy, and errors; flow-control actions are always blocking per §10 semantics. |

## 3. SQLite — `DotNet/Game.Content.Sqlite/`

| Current | Fate | Target |
|---|---|---|
| `SQLITE-SCHEMA-V1.sql`, `CoreMigrator/Migrations` | Extend | History untouched. **Migration 2** implements `03-schema-v2-design.md`: session_type, temperature_definition, phase_exit, phase/session graph node+port+edge tables, node subtype tables, action_sequence/action_instance + subtype tables, `card.action_sequence_id`. Legacy slot/action tables left physically present but unused (loader/repositories ignore them; no destructive drop). |
| `GameContentSnapshotLoader.cs` | Rewrite queries | Load v2 graph + sequences/instances into the reshaped snapshot; stop reading slots/actions/candidates. Legacy wpf_* tables ignored here (separate ledger, read only by Workbench store). |
| `SessionRepository.cs`, `PhaseRepository.cs` | Rewrite scope | Own v2 persistence: sessions (+type ref), phases (+exits), graph nodes/ports/edges, decisions/options, transactions for delete-with-owned-sequences per Schema contract. |
| `PhaseSlotRepository.cs` | **Delete** after migration lands | Replaced by graph repos (session graph, phase graph, exits, sequences/instances as needed). Slot manipulation commands disappear. |
| `ConnectionInitializer.cs`, `Sql.cs`, `StableIds.cs`, `DatabaseInitializer.cs`, `ActionType.cs` | Keep/reuse | Retain patterns; string-based node/port/type discriminators may reuse or replace `ActionType` helpers as v2 code shapes it. |
| (new) | Add | WPF extension ledger migrator (`wpf_session_node_layout`, `wpf_phase_node_layout`, `wpf_graph_view_state`, `wpf_workspace_state`) — separate version marker; core migrations ignore these tables. |

## 4. Tests

| Current | Fate | Target |
|---|---|---|
| `DotNet/Game.Core.Tests/*` | Rewrite fixtures | Code-built v2 sample graphs replace slot-era fixture; `SessionDriverTests` → session-graph VM tests (deterministic seeds); executor tests target instances via registry; golden scenarios re-expressed as authored graphs (Entry→CardExecutor→checks→GOTO Complete/Fail). |
| `DotNet/Game.Content.Sqlite.Tests/*` | Extend | Migration-2 behavior (including legacy-table survival, unknown-table safety, canonical DB upgrade path), v2 loader round-trip, repository CRUD contracts, transformation test from seeded legacy rows where exercised. |
| `Assets/Tests/EditMode/ContentAdapterTests.cs` | Rewritten in Ticket 11 | Unity builder tests against v2 snapshot (slot-era tests die with the model). |

## 5. Hosts

| Current | Fate | Target |
|---|---|---|
| `DotNet/Game.ReferenceHost.Wpf/MainWindow.xaml(.cs)` | Evolve (remediation tickets) | Stacked Session/Phase Nodify center shell; runtime playing code remains behind preview/debug surfaces. `Nodify 7.3.0` pinned. |
| `Assets/Scripts/Game/UnityContentGraphBuilder.cs` + GameManager wiring | Adapt (Ticket 11) | Thin-host builder emits/consumes the v2 graph snapshot; no SQLite provider; presentation stays scriptable-object-driven. |

## Sequence gates honored

Each HARD ticket's gate runs the relevant subset; final regression (Ticket 10) proves old removals had no orphan references (compile must fail if any slot/min-max/reusable-action usage survives anywhere).
