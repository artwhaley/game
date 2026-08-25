# Ticket 5 — SessionDriver core

## Goal
The plain-C# driver that walks a session's phases: per-phase draw targets,
live length-modifier math, early advance, end-of-session signal. CardExecutor
stays "draw one matching card"; the driver owns the rest.

## Files
- `Assets/Scripts/Game/SessionDriver.cs` — NEW, plain C#.
- `Assets/Tests/EditMode/SessionDriverTests.cs` — NEW.
- `README.md` — dev-log entry.

## Design
- At phase start: `baseTarget = Random(min..max)` (unscaled), picked once.
- Advance condition, evaluated live on every card completion:
  `cardsDrawn >= Round(baseTarget * SessionConfig.LengthModifier)`, clamped ≥ 1.
  A mid-session modifier change (future choice action) takes effect on the next
  draw — growing extends the phase, shrinking can end it immediately. Nothing
  is baked.
- No matching card for the phase's filter → log warning + advance (early
  advance; expected never to happen with authored content).
- After the final phase completes → session complete (driver reports it; the
  game layer returns to menu — ticket 6).

## Acceptance criteria
- [ ] Draws pick baseTarget once per phase; phase advances at the scaled target.
- [ ] Modifier rebake live: changing `SessionConfig.LengthModifier` mid-phase
      grows/shrinks remaining draws correctly (tests for both).
- [ ] Early advance logged on empty match.
- [ ] Session-complete reported after the last phase.
- [ ] All EditMode tests green (existing + new).

## Verification
`unity test . --mode EditMode`.

## Commit message
`SessionDriver: phase progression with live length modifier`
