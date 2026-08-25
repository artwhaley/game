# Ticket 6 — Screen rework + settings

## Goal
The user-facing integration: setup screen becomes a session picker, settings
gains the length-modifier slider, and the game loop runs through SessionDriver,
returning to the menu when the session ends.

## Files
- `Assets/Scripts/UI/GameSetupController.cs` — session picker (buttons from
  `SessionLibrary`) replaces tag toggles; Start writes `SelectedSession`.
- `Assets/Scripts/UI/SettingsDialog.cs` — real dialog: length-modifier slider
  (0.5–3.0, default 1) writing `SessionConfig.LengthModifier` live.
- `Assets/Editor/SceneBuilder.cs` — rebuild setup scene + settings dialog UI.
- `Assets/Scripts/Game/GameManager.cs` — build `SessionDriver` from
  `SelectedSession`; Draw Next Card flows through the driver; on
  session-complete → MainMenu.
- `README.md` — dev-log entry + status update.

## Design
- Remove the tag-toggle code and `SessionConfig.MustInclude/Exclude` — replaced,
  no backwards compatibility (agents.md rule 8).
- Modifier is read live by the driver, so the slider writes on change.

## Acceptance criteria
- [ ] Setup shows the session list; picking one starts it.
- [ ] Settings slider changes phase length on the next draw.
- [ ] Full loop: pick session → phases draw → ending card → back to menu.
- [ ] Early-advance warning visible in console when it fires.
- [ ] EditMode tests green.

## Verification
Playtest in the editor end-to-end + `unity test . --mode EditMode`.

## Commit message
`Session picker, settings length modifier, driver-driven game loop`
