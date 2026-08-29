# Pre-Milestone C Reliability — Ticket 10 Final Regression

## Automated regression

Run on 2026-08-29 from branch `pre-milestone-c-reliability`:

- `dotnet test Game.Workbench.sln --no-restore`: passed. Core 139, Profile 11,
  WPF 23, and SQLite 125 passed; SQLite has 1 intentional canonical playback
  skip.
- `dotnet build DotNet/Game.ReferenceHost.Wpf/Game.ReferenceHost.Wpf.csproj
  --no-restore`: passed with 0 warnings and 0 errors.
- Profile database read-only checks: `PRAGMA integrity_check` returned `ok`;
  `PRAGMA foreign_key_check` returned no rows.
- Canonical database preflight is recorded in
  [`09-canonical-db-preflight.md`](09-canonical-db-preflight.md), including the
  byte-for-byte backup and isolated disposable-card canary.

## Unity status

The required Unity batch check was attempted with Unity `6000.5.9f1`. Unity
initialized its license, imported the project, and reached script compilation,
but exited 1 because the existing Unity-side `CardDeck.cs` references the
missing `TruthCardGame.Content.CardDeckDefinition` at line 51. This stack did
not change the Unity bridge, so this is recorded as a failed environmental /
pre-existing compile gate rather than repaired or claimed as passed.

## Human gates still pending

No human acceptance is claimed. The remaining gates are:

1. Run the Ticket 04 log/profile manual checks.
2. Run the Ticket 06 same-seed trace check.
3. In the Workbench, perform the canonical disposable-card canary from
   [`09-canonical-db-preflight.md`](09-canonical-db-preflight.md).
4. Perform the final authoring/playback canary across both graph canvases,
   nested PromptChoice, Undo/Redo, and the runner.

Coding stops here pending those human checks or a newly reported in-scope
failure.
