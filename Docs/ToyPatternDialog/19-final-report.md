# Toy / Resource / Dialog Catalog — Ticket 19 Final Regression & Canary Handoff

Date: 2026-08-30
Branch: `final-pre-milestone-c-corrections` (unpushed)

## Regression totals — all green

| Suite | Passed | Failed |
|---|---:|---:|
| `Game.Core.Tests` | 161 | 0 |
| `Game.Content.Sqlite.Tests` | 134 | 0 |
| `Game.ReferenceHost.Wpf.Tests` | 36 | 0 |
| `Game.Profile.Sqlite.Tests` | 11 | 0 |
| **Total** | **342** | **0** |

- `dotnet build Game.Workbench.sln --no-restore` succeeded with 0 warnings and
  0 errors.
- The canonical DB is schema v8 with `integrity_check = ok` and zero
  `foreign_key_check` rows.
- The WPF host was launched after the application changes and left running for
  the human canary.

## Correction coverage

- Empty continuation-stack Return paths are loud runtime/content errors;
  explicit terminal actions are required.
- Set Toy Pattern runs inline, timed toy actions distinguish blocking and
  nonblocking execution, and Wait For All drains the current nonblocking set.
  Subsequent Milestone C readiness correction made PromptChoice always blocking
  and permits Wait For All in its nested sequences; SessionGoto remains illegal
  there and the three-option maximum remains.
- Toy shutdown has a real five-second cancellation timeout and diagnostic
  logging.
- Legacy toy migration IDs use exact IEEE-754 bit patterns.
- WPF supports Resource, Dialog Tag, and Dialog Snippet authoring, searchable
  resource filtering, typed resource drag/drop, and the new action editors.
- The stale Unity `CardDeck` conversion reference was removed. Full Unity
  editor batch compilation remains unverified in this environment.

## Canonical database

The database was already user-dirty and contained authored content before this
stack. It was preserved. The final state is 1 Session, 1 Phase, and 3 Cards;
schema ledger max 8; size 708,608 bytes; SHA-256
`E3CDD1E5686E36B015054B9E009A2C021173EBE7DBF2E7F63A6C3B0033DE67E6`.

Before the requested terminal-fixture repair, a backup was created at:

`C:\Users\artwh\AppData\Local\Temp\GameContent.db.final-pre-c.20260830-155912.bak`

The one existing top-level Return fixture was changed to an explicit End
Session action. No disposable canary content remains in the canonical DB.

## Canary handoff

The remaining human checks are:

1. Author and reopen a toy-pattern Resource, Dialog Tag, Dialog Snippet, and
   the new action types; confirm persistence and safe deletion.
2. Run dialog and toy actions in the reference player; confirm timed output and
   teardown behavior.
3. If Unity acceptance is required, run the pinned Unity editor in batch mode
   and record that result separately.
