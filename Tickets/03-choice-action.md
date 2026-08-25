# Ticket 3 — Choice action

## Goal
`ChoiceAction`: a card action that prompts the player with UI buttons and
branches to the chosen child action. Blocking while the prompt is up.

## Files
- `Assets/Scripts/Actions/ChoiceAction.cs` — NEW: serialized prompt string +
  ordered `ChoiceOption` list (label + child `CardAction`); `[CreateAssetMenu]`.
- `Assets/Scripts/UI/GamePanel.cs` — `ShowPrompt(...)`: spawns one button per
  option at runtime (same pattern as setup-screen toggles), resolves on click.
- `Assets/Scripts/Game/GameManager.cs` — implements `IPromptService` (the action
  yields while buttons are up).
- `Assets/Tests/EditMode/ChoiceActionTests.cs` — NEW: fake prompt service.
- `README.md` — dev-log entry.

## Design
- `IPromptService.Ask(string prompt, IReadOnlyList<string> options,
  Action<int> onChosen)` returns an enumerator the action yields on; the click
  callback sets the result; the action resumes with the chosen index.
- Chosen child runs per its own flag: blocking → `yield return` (awaited);
  continuous → dispatched via the runner.
- Card-level choice (pick the next card) is explicitly out of scope — it
  branches in the driver, not an action.

## Acceptance criteria
- [ ] Prompt UI appears; card is blocked until a choice is made.
- [ ] Chosen blocking child is awaited; chosen continuous child is dispatched.
- [ ] Tests cover: choice resolves to correct child; continuous child not
      awaited; prompt with zero options fails loudly.
- [ ] Existing tests still green.

## Verification
`unity test . --mode EditMode` + playtest (a sample choice card in the deck).

## Commit message
`ChoiceAction: branch card execution on player choice via prompt UI`
