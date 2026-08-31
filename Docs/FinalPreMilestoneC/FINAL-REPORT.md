# Final Pre-Milestone C Correction Stack — Final Report

Date: 2026-08-30
Branch: `final-pre-milestone-c-corrections`
Base: `65fa554d8ba5adf973e4de79be0f64e2f8f09d7a`
Remote push: not performed

## Scope completed

The correction stack is implemented across the portable runtime, SQLite
content/migration layer, WPF authoring host, and Unity compile-drift surface.

- Empty continuation-stack `RETURN` paths now fail loudly with session, phase,
  origin, and remediation details. Explicit terminal actions are required.
- `Set Toy Pattern` is acknowledged inline and is not tracked as a timed task.
  Blocking timed toy actions await completion; nonblocking timed toy actions are
  tracked. `Wait For All` drains the current nonblocking set. The subsequent
  Milestone C readiness correction made PromptChoice always blocking and permits
  Wait For All in nested PromptChoice sequences; SessionGoto remains illegal
  there and the three-option maximum remains.
- Toy shutdown uses a real cancellation timeout and logs when cleanup exceeds
  the five-second bound.
- Legacy toy migration IDs use exact IEEE-754 bit patterns, avoiding rounded
  intensity collisions.
- WPF action authoring supports the required Toy Pattern, Set Toy Pattern,
  Dialog, Dialog From Tags, Delay, and Wait For All fields and validation.
- WPF has a searchable Resource browser for `cutscene` and `toy_pattern`
  resources, plus Dialog Tags and Dialog Snippets catalogs with safe delete,
  rename/duplicate, and stable-ID assignment. Resource drag/drop is type
  checked and targets an authored action row.
- The stale Unity `CardDeck` conversion reference was removed. The shared
  portable source is covered by the .NET build; the full Unity editor batch
  compile remains unverified here.

## Automated verification

| Gate | Result |
|---|---:|
| `Game.Core.Tests` | 161 passed |
| `Game.Content.Sqlite.Tests` | 134 passed |
| `Game.Profile.Sqlite.Tests` | 11 passed |
| `Game.ReferenceHost.Wpf.Tests` | 36 passed |
| Full solution build | 0 warnings, 0 errors |
| Total tests | 342 passed |

Commands run from `repo/`:

```text
dotnet build Game.Workbench.sln --no-restore --nologo --verbosity minimal
dotnet test Game.Workbench.sln --no-build --no-restore --nologo --verbosity minimal
```

## Canonical database safety record

`Content/GameContent.db` was already user-dirty and contained authored content
before this stack. It was preserved; no reset, checkout, or stash was used.
Before the requested terminal-fixture correction, a byte-for-byte backup was
created at:

`C:\Users\artwh\AppData\Local\Temp\GameContent.db.final-pre-c.20260830-155912.bak`

Current canonical state:

- SHA-256: `E3CDD1E5686E36B015054B9E009A2C021173EBE7DBF2E7F63A6C3B0033DE67E6`
- Size: 708,608 bytes
- Schema ledger maximum: 8
- Sessions: 1; Phases: 1; Cards: 3
- Resources: 1; Dialog Tags: 0; Dialog Snippets: 0; Toy Capabilities: 1
- `PRAGMA integrity_check`: `ok`
- `PRAGMA foreign_key_check`: zero rows
- No `GameContent.db-wal` or `GameContent.db-shm` companion files

The one existing top-level Return fixture was converted to an explicit
End Session action so the authored session remains present while obeying the
corrected runtime contract. No disposable canary content was left in the
canonical database.

## Acceptance status

The WPF reference host was built and launched after the application changes and
is intentionally left running for the human canary. Automated WPF coverage is
green. A manual authoring/playback canary has not been claimed as completed,
and Unity editor batch compilation has not been claimed as completed.

The remaining human checks are therefore:

1. Author and reopen a toy-pattern Resource, Dialog Tag, Dialog Snippet, and
   the new action types; confirm save/reload and safe deletion.
2. Run the reference player through dialog and toy actions; confirm the timed
   output and teardown behavior.
3. If Unity acceptance is required, run the project through the pinned Unity
   editor in batch mode and record that result separately.
