# Pre-Milestone C Reliability — Ticket 05 Body, Profile, and Diagnostics

## Implemented contract

- Card Started entries on the Reference Player and Preview use `CARD START`,
  `Title:`, `Body:`, and the full `BodyText`; the play surface continues to
  display BodyText normally.
- A missing `UserProfile.db` is treated as a legitimate new-user empty state.
  An existing profile database that cannot be opened, migrated, or read throws
  and blocks session start/selection; UI entry points report the failure in a
  labeled error instead of silently using an empty profile.
- Typed rejection IDs resolve to catalog names for Card Tags, Kinks,
  Equipment, and Smart Toy capabilities, while retaining `[stable-id]` detail
  when a name exists and falling back to the ID when it does not. Card, Phase,
  Session, and Session Type diagnostics use the same title-plus-ID format.

## Verification

- WPF tests cover readable rejection formatting and the unreadable-existing-
  profile failure contract.
- Runner log tests cover full structured card body output.
- Human gate remains pending: run a card with a multiline body, create/select an
  existing readable profile, then test a deliberately unreadable profile DB
  and confirm the run is blocked with an explicit error.
