# 00 — Baseline (pre-correction)

Established per corrective stack Ticket 00, before any corrective code change.

## Repository state

- Repo root: `repo/` (contains `Game.Workbench.sln`).
- Branch at preflight start: `main`.
- HEAD: `1a8f3ee50667155afb3a16a9b747b77f002770ae`
  ("fix: harden cancellation boundary, fail-noisy conversion, host ergonomics; truth-up root docs").
- Working branch created for the corrective run: `content-graph-phase-slots`
  (created via `git switch -c`, no commits, working tree carried untouched).

## Dirty-file state

- 48 modified files + 1 untracked (`DotNet/TestData/parity-content-v2.json`), 51 changed paths total.
- The entire dirty set is attributable to the two prior approved passes:
  1. **Stable-ID / schema-v2 pass** — `Id` on every portable definition, Unity SO mint-once
     (`EnsureId`/`OnValidate`), schema bump 1→2, `parity-content-v2.json` fixture, portable
     ID tests, regenerated sample content with minted ids.
  2. **Cutscene binding pass** — `CutsceneAction.ResourceId` authored stable id
     (`cs:intro` on the sample), `CutsceneBindingRegistry` as a stable-id → TimelineAsset table.
- No unrelated local modifications found.
- One incidental change: `ProjectSettings/ProjectAuditorSettings.asset` — touched by Unity
  itself during headless batch runs, not by any intentional edit. Flagged, not part of the passes.

## Test baseline (pre-correction, all green)

| Suite | Command | Result |
|---|---|---|
| Portable (.NET) | `dotnet test Game.Workbench.sln` | **92/92 passed** |
| Unity EditMode | editor batch `-runTests -testPlatform EditMode` (Unity 6000.5.9f1) | **15/15 passed** |
| Unity PlayMode | not run in this preflight | (2 tests exist; headless PlayMode not part of this baseline) |

Unity EditMode was driven by invoking the pinned editor binary directly in batch mode
(`.../6000.5.9f1/Editor/Unity.exe -projectPath . -batchmode -runTests -testPlatform
EditMode -testResults editmode-results.xml` — no `-quit`, which exits before the runner
engages). The `unity` CLI is not on PATH in this environment.

## Current portable model shape (the distortion being corrected)

```text
ContentDocument (schemaVersion 2)
    Deck: CardDeckDefinition
        Id, Cards: List<CardDefinition>          <- deck EMBEDS cards
    Sessions: List<SessionDefinition>
        Id, Title, Tags, Phases: List<PhaseDefinition>   <- session EMBEDS phases

CardDefinition
    Id, Title, Tags, Actions: List<GameActionDefinition> <- card EMBEDS actions

GameActionDefinition (abstract: Id, IsBlocking)
    DebugActionDefinition        Message, DelaySeconds
    StatIncreaseActionDefinition StatKey, Amount
    ChoiceActionDefinition       Prompt, Options: List<ChoiceOptionDefinition>
    CutsceneActionDefinition     ResourceId

ChoiceOptionDefinition
    Id, Label, Child: GameActionDefinition         <- choice option EMBEDS child action
```

## Current Unity-side shape (already a reference graph)

- `Session` asset → `List<Phase>` object references (independently authored `Phase_*.asset` files).
- `Card` asset → `List<CardAction>` object references (independently authored action assets).
- `CardDeck` asset → `List<Card>` object references.
- `ChoiceAction.ChoiceOption` → child `CardAction` object reference.
- Sessions already share Phase assets: `109ad389655f7cf448752bf911595a72` is referenced by
  both `Intense.asset` and `Relaxing.asset`.
- Flattening happens only in the portable conversion layer (`ToDefinition(...)` recursively
  embeds definitions by value).

## Stable-ID status

- Complete and uncommitted: every portable entity carries `Id`; Unity SOs mint-once
  (`EnsureId`/`OnValidate`); JSON requires ids from schemaVersion 2; sample content assets
  all carry minted ids (22 assets; `SessionLibrary` intentionally excluded as pure glue).
- Cutscene binding uses authored stable `ResourceId` (`cs:intro`) keyed registry.

## Notes

- No checkpoint commit was made: per the user's choice, Ticket 00/01 ran as documentation
  only; the working tree remains uncommitted on `content-graph-phase-slots`.
- Line-ending warnings (LF→CRLF) on modified `.asset`/`.cs` files are Git autocrlf noise,
  not content changes.
