# Ticket 4 — Session/Phase data

## Goal
The two data concepts above cards: `Session` (a "game type") and `Phase`
(a draw filter + duration). Plus `SessionLibrary`, `SessionConfig` changes, and
sample content.

## Files
- `Assets/Scripts/Cards/Session.cs` — NEW SO: title, top-level tags (metadata
  only — never consulted by phases), ordered `Phase` list.
- `Assets/Scripts/Cards/Phase.cs` — NEW SO: title, must-include/must-exclude
  tags, min/max cards.
- `Assets/Scripts/Cards/SessionLibrary.cs` — NEW SO: the list of sessions the
  setup screen offers.
- `Assets/Scripts/Game/SessionConfig.cs` — replace tag-list handoff with
  `SelectedSession` + `LengthModifier` (float, default 1). No compat cruft.
- `Assets/Editor/SampleContentBuilder.cs` — generate two sessions ("Relaxing",
  "Intense") with phases; each session ends with an authored EndPhase whose tags
  match ending cards; add one ending card using existing actions.
- `README.md` — dev-log entry.

## Design
- Session tags are vocabulary: "relaxing" / "intense" pick a session from the
  library. Phase tags drive the executor filter. The two vocabularies never
  mix.
- EndPhase is a normal phase; only the driver (ticket 5) knows the last phase
  ends the session.
- Keep existing starter deck/cards; Phase 6 removes the old tag-toggle screen.

## Acceptance criteria
- [ ] Builder generates both sessions with phases and the ending card.
- [ ] `SessionConfig` has `SelectedSession` + `LengthModifier`; old tag lists
      gone.
- [ ] Distinct-tag helper available on Session (or phases) for future UI.
- [ ] EditMode tests green.

## Verification
Run `SampleContentBuilder` headless; inspect assets; `unity test . --mode
EditMode`.

## Commit message
`Session and Phase ScriptableObjects with sample sessions and ending card`
