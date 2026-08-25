# Ticket 2 — Cutscene action (Timeline)

## Goal
A `CutsceneAction` card action that plays a `TimelineAsset` through the
`ICutscenePlayer` and returns control when the timeline finishes. Adds Timeline
+ Cinemachine packages, scene wiring, and one hand-authored proof-of-concept
timeline.

## Files
- `Packages/manifest.json` — add `com.unity.timeline` + `com.unity.cinemachine`
  (first-party; versions resolved by UPM for 6000.5 — record exact versions in
  this ticket when known).
- `Assets/Scripts/Actions/CutsceneAction.cs` — NEW: `[CreateAssetMenu]` under
  TruthCardGame/Actions; serialized `TimelineAsset`; `Execute` plays via
  `context.Cutscene` and yields while playing. Blocking.
- `Assets/Scripts/Game/DirectorPlayer.cs` — NEW: `ICutscenePlayer` impl wrapping
  `PlayableDirector` (requires the Timeline package for `TimelineAsset`).
- `Assets/Editor/SceneBuilder.cs` — Game scene gains: director GameObject with
  `PlayableDirector`, `CinemachineBrain` on the camera, one virtual camera.
- PoC timeline asset — hand-authored in the Timeline window (the one
  non-generated asset; committed). Cube stand-in NPC + transform track, virtual
  camera cut, end-of-timeline signal. No audio track — VO + lipsync deferred
  to a future ticket gated on the NPC character decision.
- `README.md` — dev-log entry.

## Design
- `CutsceneAction.Execute(GameContext)`: `context.Cutscene.Play(timeline);`
  then `yield return new WaitWhile(() => context.Cutscene.IsPlaying);`.
- `GameManager` supplies the `DirectorPlayer` (wired to the scene director GO).
- The future bluetooth toy hooks via a SignalEmitter at the timeline's end — no
  action code.

## Acceptance criteria
- [ ] Packages resolve; project compiles.
- [ ] A card referencing a `CutsceneAction` asset plays the PoC timeline in
      Play mode; the panel shows "Done." after it finishes.
- [ ] `GameManager` handles a missing timeline/director loudly (log, no crash).
- [ ] Existing EditMode tests still green.

## Verification
Manual playtest in the editor (this is the one thing unit tests can't cover) +
`unity test . --mode EditMode`.

## Commit message
`CutsceneAction: Timeline-driven card action with PoC timeline`
