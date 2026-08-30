# Toy / Resource / Dialog Catalog — Ticket 17 Unity Compile Drift

## Scope

Ticket 17 repaired compile drift introduced or exposed by the portable-Core
changes in the Unity editor scripts (`Assets/Scripts`, excluding the shared
`Portable/` tree).

## Result: stale reference repaired; editor compile remains unverified

The shared .NET solution compiles all portable `Game.Core` and `Game.Content`
sources, and the full solution build is green. A static audit of the
non-portable Unity scripts found one stale conversion reference:

- `Assets/Scripts/Cards/CardDeck.cs` contained an obsolete
  `ToDefinition(UnityContentGraphBuilder)` conversion referencing the missing
  `TruthCardGame.Content.CardDeckDefinition`. That method was removed; the
  current `CardDeck` no longer references the missing type.
- The changed Unity-facing portable surface has no remaining `.Intensity`
  references in `Assets/Scripts`.
- No non-portable Unity code constructs `ActionExecutionContext`,
  `SessionGraphVm`, or `PhaseGraphVm` directly; request paths route through
  `GameSessionEngine`.
- `Assets/Scripts/Portable/**` is the shared compile surface verified by the
  .NET build and the 161 Core tests.

## Deferred

Full Unity **batch-mode** editor compilation (`unity -batchmode -quit`) was not
run here. It launches the editor, imports assets, and mutates `Library/`; the
editor-side acceptance gate remains a human/environment verification step.
The static audit, stale-reference repair, and green shared compile are the
verifiable results for this ticket.
