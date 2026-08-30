# Toy / Resource / Dialog Catalog — Ticket 17 Unity Compile Drift

## Scope

Ticket 17 was to repair any compile drift the stack's portable-Core changes may
have introduced into the Unity editor scripts (`Assets/Scripts`, excluding the
shared `Portable/` tree).

## Result: no drift found (static audit)

The `.NET` solution compiles **all** portable `Game.Core` and `Game.Content`
sources (the `Game.Core.csproj` and `Game.Content.csproj` include the
`Assets/Scripts/Portable/**` trees directly), so the entire changed surface is
already compiled and green by `dotnet build Game.Workbench.sln`.

A static audit of the **non-portable** Unity scripts found no references to the
changed portable surface:

- The only non-portable consumer of the changed types is
  `Assets/Scripts/Game/GameManager.cs`. It builds a `CoreServices` using the
  optional-slots (`dialog` and `toyActivity` left null, which the portable Core
  explicitly supports with tested missing-service no-ops) and drives only the
  stable `GameSessionEngine` surface — no drift.
- No `.Intensity` reference remains anywhere in `Assets/Scripts` (the removed
  toy `Intensity` field).
- No non-portable Unity code constructs `ActionExecutionContext`,
  `SessionGraphVm`, or `PhaseGraphVm` directly; request paths route through
  `GameSessionEngine`.
- `Assets/Scripts/Cards/CardDeck.cs` builds `CardDeckDefinition` only and does
  not touch toy/dialog types.
- `Assets/Scripts/Portable/**` is the shared compile surface already verified by
  the .NET build and the 158 Core tests.

## Deferred

Full Unity **batch-mode** editor compile (`unity -batchmode -quit` over the
project) is not run here: it launches the editor, imports assets, and mutates
`Library/` (a heavyweight, side-effecting operation with possible licensing and
NID), and `agents.md` rule 4 requires asking before driving the editor /
installing pipeline tooling. The static drift audit above plus the green shared
compile are the verifiable, low-risk evidence for this ticket.