# 01 — Extraction Map (Ticket 00)

Maps every relevant runtime type to its extraction treatment. Categories:

- `PORTABLE CONTENT DATA` — lives in `Assets/Scripts/Portable/Game.Content/`, inert data
- `PORTABLE CORE LOGIC` — lives in `Assets/Scripts/Portable/Game.Core/`
- `UNITY SERIALIZED WRAPPER` — ScriptableObject shell kept for asset identity; converts to a portable definition
- `UNITY HOST ADAPTER` — Unity-side host/service glue around Core
- `UNITY PRESENTATION ONLY` — UI/editor, no game rules
- `OBSOLETE AFTER PARITY` — superseded; remove only when unreferenced

| Current type/file (namespace `TruthCardGame`) | Category | Expected new/replacement type |
|---|---|---|
| `Cards/Card.cs` (SO) | UNITY SERIALIZED WRAPPER | `TruthCardGame.Content.CardDefinition` + `ToDefinition(...)` |
| `Cards/CardDeck.cs` (SO) | UNITY SERIALIZED WRAPPER | `TruthCardGame.Content.CardDeckDefinition` + conversion |
| `Cards/Phase.cs` (SO) | UNITY SERIALIZED WRAPPER | `TruthCardGame.Content.PhaseDefinition` + conversion |
| `Cards/Session.cs` (SO) | UNITY SERIALIZED WRAPPER | `TruthCardGame.Content.SessionDefinition` + conversion |
| `Cards/SessionLibrary.cs` (SO) | UNITY HOST ADAPTER | Stays Unity-side menu convenience; not forced into Core |
| `Actions/CardAction.cs` (abstract SO) | UNITY SERIALIZED WRAPPER | `TruthCardGame.Content.GameActionDefinition` base (`IsBlocking`) |
| `Actions/DebugAction.cs` | UNITY SERIALIZED WRAPPER (+ old Execute obsolete after parity) | `DebugActionDefinition` (message, delaySeconds) |
| `Actions/StatIncreaseAction.cs` | UNITY SERIALIZED WRAPPER (+ old Execute obsolete after parity) | `StatIncreaseActionDefinition` (statKey, amount) |
| `Actions/ChoiceAction.cs` | UNITY SERIALIZED WRAPPER (+ old Execute obsolete after parity) | `ChoiceActionDefinition` + `ChoiceOptionDefinition` (recursive child) |
| `Actions/CutsceneAction.cs` | UNITY SERIALIZED WRAPPER (+ old Execute obsolete after parity) | `CutsceneActionDefinition` (string ResourceId) + Ticket-07 runtime cutscene registry bridging the serialized `TimelineAsset` |
| `Game/GameContext.cs` | PORTABLE CORE LOGIC (move) | `TruthCardGame.Core.GameContext` |
| `Game/Player.cs` | PORTABLE CORE LOGIC (move) | `TruthCardGame.Core.Player` |
| `Game/PlayerStats.cs` | PORTABLE CORE LOGIC (move) | `TruthCardGame.Core.PlayerStats` (Get/Add semantics preserved) |
| `Game/GameServices.cs` | OBSOLETE AFTER PARITY | Portable Core services object: required `IGameDelay`, non-null `IGameLog` (NullGameLog), optional `IPromptService`/`ICutsceneService` |
| `Game/IPromptService.cs` (CustomYieldInstruction Ask) | OBSOLETE AFTER PARITY | `TruthCardGame.Core.IPromptService.Task<int?> AskAsync(prompt, options, ct)`; null = dismissed |
| `Game/PromptHandle.cs` | OBSOLETE AFTER PARITY | TaskCompletionSource bridge in the Unity prompt adapter |
| `Game/ICutscenePlayer.cs` (PlayableAsset Play) | OBSOLETE AFTER PARITY | `TruthCardGame.Core.ICutsceneService.PlayAsync(resourceId, ct)` |
| `Game/DirectorPlayer.cs` | UNITY HOST ADAPTER | Becomes Unity `ICutsceneService`: resolves registry key → asset → PlayableDirector playback |
| `Game/CardExecutor.cs` matching/draw portion | PORTABLE CORE LOGIC | Core card selector consuming `CardDeckDefinition` + include/exclude + injected card `IRandomSource` |
| `Game/CardExecutor.cs` ExecuteCard coroutine | PORTABLE CORE LOGIC | Core `ActionExecutor` async sequencing + `BackgroundActionTracker` for nonblocking actions |
| `ICoroutineRunner` (in CardExecutor.cs) | OBSOLETE AFTER PARITY | None — tracker is Core-owned (AD-16: missing-runner error path intentionally not reproduced) |
| `Game/SessionDriver.cs` rules | PORTABLE CORE LOGIC | `TruthCardGame.Core.SessionDriver` on `SessionDefinition`; phase-target `IRandomSource`; live `Func<float>` modifier; `IGameLog` warnings; explicit midpoint-to-even rounding |
| `Game/GameManager.cs` orchestration (draw/no-match loop, pacing, completion marking) | PORTABLE CORE LOGIC | `TruthCardGame.Core.GameSessionEngine.AdvanceOneCardAsync(ct)` + `AdvanceResult`/kind + lifecycle events |
| `Game/GameManager.cs` remainder (MonoBehaviour, scene return, 1.6 s delay, panel updates, prompt TCS bridge, lifetime/cancellation) | UNITY HOST ADAPTER | Thin controller; auto first advance at start; Draw Next forwarding |
| `Game/SessionConfig.cs` | UNITY HOST ADAPTER | Stays static Unity state; selected session converted once; `LengthModifier` fed to Core as delegate |
| `UI/GamePanel.cs` | UNITY PRESENTATION ONLY | Narrow inputs if needed (card title string / CardDefinition) |
| `UI/MenuController.cs` | UNITY PRESENTATION ONLY | — |
| `UI/GameSetupController.cs` | UNITY PRESENTATION ONLY | — |
| `UI/SettingsDialog.cs` | UNITY PRESENTATION ONLY | Slider keeps writing live modifier host-side |
| `Editor/SceneBuilder.cs` | UNITY PRESENTATION ONLY (editor tooling) | Minimal wiring updates only if host changes break references |
| `Editor/SampleContentBuilder.cs` | UNITY PRESENTATION ONLY (editor tooling) | Content assets remain source of truth for Unity |

New portable types expected (Tickets 02–06), none of which exist at HEAD:

```text
Game.Content: SessionDefinition, PhaseDefinition, CardDefinition,
              CardDeckDefinition, GameActionDefinition,
              DebugActionDefinition, StatIncreaseActionDefinition,
              ChoiceActionDefinition, ChoiceOptionDefinition,
              CutsceneActionDefinition
Game.Core:    Player, PlayerStats, GameContext, services record,
              IRandomSource/SystemRandomSource, IPromptService, ICutsceneService,
              IGameDelay, IGameLog/NullGameLog, SessionDriver, card selector,
              ActionExecutor, BackgroundActionTracker,
              GameSessionEngine, AdvanceResult(+AdvanceResultKind),
              CardStarted/CardFinished/PhaseChanged/SessionCompleted events
```

Unity-only additions expected:

```text
UnityRandomSource : IRandomSource        // UnityEngine.Random.Range for card draws
cutscene registry/binding                // TimelineAsset ⇄ opaque string key (runtime only)
ToDefinition(...) converters             // on the SO shells or a central adapter
Task-based prompt bridge (TCS)           // replaces PromptHandle
Unity ICutsceneService impl              // wraps DirectorPlayer
Unity IGameDelay impl                    // scaled-time, main-thread loop
Unity IGameLog impl                      // Debug.*/Debug.LogWarning/LogError
```

Desktop-only additions (Tickets 01, 11, 12):

```text
DotNet/Game.Content/Game.Content.csproj          netstandard2.1, linked shared source
DotNet/Game.Core/Game.Core.csproj                netstandard2.1, linked shared source
DotNet/Game.Core.Tests/Game.Core.Tests.csproj    net10.0 (NUnit stack)
DotNet/Game.Content.Json/Game.Content.Json.csproj net10.0, System.Text.Json spike
DotNet/Game.ReferenceHost.Wpf/…csproj            net10.0-windows reference player
DotNet/TestData/parity-content-v1.json           SFW parity fixture
Game.Workbench.sln
```

Mapping notes:

- The physical canonical source of all portable code will be under
  `Assets/Scripts/Portable/**`; SDK projects compile those same files via
  linked Compile includes (no second copy).
- Existing EditMode tests stay Unity-adapter-relevant only where they exercise
  ScriptableObject conversion or Unity service adapters; every engine behavior
  they assert gains a portable equivalent in `Game.Core.Tests`.
