# Ticket 1 — Runtime services plumbing

## Goal
Give `GameContext` the service seam that cutscenes (ticket 2) and choices
(ticket 3) both need: a coroutine runner, a prompt service, and a cutscene
player. No behavior change — this is plumbing.

## Why now
Both upcoming features need `GameContext` to reach scene-side services without
actions reaching into the scene. Do the seam once, up front.

## Files
- `Assets/Scripts/Game/GameContext.cs` — gains `Runner`, `Prompts`, `Cutscene`
  services; constructor updated (services optional, null-safe for tests that
  don't need them).
- `Assets/Scripts/Game/ICutscenePlayer.cs` — NEW pure interface (no package
  deps): `void Play(TimelineAsset)` + `bool IsPlaying`. The `TimelineAsset`
  type lives in the Timeline package, so the concrete impl lands in ticket 2
  after the package is added.
- `Assets/Scripts/Game/GameManager.cs` — constructs `GameContext` with real
  services (runner = self; prompts/cutscene impls land in tickets 2–3; for now
  wire what exists and leave the rest null).
- `Assets/Tests/EditMode/ExecutorTests.cs` — update `GameContext` constructions.
- `README.md` — dev-log entry.

## Design
- `GameContext(Player player, GameServices services = null)` where `GameServices`
  (or equivalent) carries `ICoroutineRunner Runner`, `IPromptService Prompts`,
  `ICutscenePlayer Cutscene`. Existing `ICoroutineRunner` stays in
  `CardExecutor.cs`; `IPromptService` definition lands with ticket 3 (or here if
  trivial — decide in implementation, keep this file count unchanged).
- Tests that don't exercise services pass `null` and keep working.

## Acceptance criteria
- [ ] `GameContext` exposes the three services; null services are safe for
      executor-only tests.
- [ ] `GameManager` supplies the runner; `ICutscenePlayer` interface exists.
- [ ] All existing EditMode tests pass with the new constructor.
- [ ] No runtime behavior change (game still plays exactly as before).

## Verification
`unity test . --mode EditMode` → 8/8 green.

## Commit message
`GameContext gains runtime services seam for cutscenes and choices`
