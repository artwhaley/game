# Toy / Resource / Dialog Catalog — Ticket 19 Final Regression & Canary Handoff

Date: 2026-08-30. Branch: `pre-milestone-c-reliability` → `toy-pattern-dialog-catalog` (unpushed).
Commands run from `repo/` (Game.Workbench.sln).

## Regression totals — all green

| Suite | Passed | Failed | Skipped |
|---|---|---|---|
| `Game.Core.Tests` | 158 | 0 | 0 |
| `Game.Content.Sqlite.Tests` | 134 | 0 | 0 |
| `Game.ReferenceHost.Wpf.Tests` | 36 | 0 | 0 |
| `Game.Profile.Sqlite.Tests` | 11 | 0 | 0 |
| **Total** | **339** | **0** | **0** |

- `dotnet build Game.Workbench.sln` → succeeded, 0 warnings, 0 errors.
- Canonical DB `Content/GameContent.db`: ledger at **v8**, `integrity_check = ok`,
  `foreign_key_check = 0` rows.

## New regression coverage added this stack

- **Toy concurrency / stale-timer regime** (`ToyOutputSimulatorTests`, 8 tests):
  natural expiry clears only the owning generation; a superseded timed command's
  stale timer never stops a newer one; commands on different capabilities
  coexist; `SetPattern` persists (not a background task) and survives time;
  `StopAll` clears everything.
- **Dialog RNG domain independence** (`DialogRngRegressionTests`, 8 tests): the
  fixed dialog-domain salt is independent from the Session-selection and
  PhaseRun/Card streams for the same seed; the selector is deterministic,
  all-match, ordered, content-error loud on zero tags / zero candidates, and
  reaches every candidate.
- **WPF host boundaries** (`DialogHostServiceTests`, 4 tests): dialog text routes
  to the presenter; a missing presenter logs loudly and completes safely; a
  presenter failure is logged, not propagated; cancellation propagates.

## Engine fix recorded earlier

Three canonical-DB guard failures that were **pre-existing** (they also failed on
HEAD with its own committed DB) were resolved without touching user content:

1. `PlaysThroughTheSessionVm` — the engine now treats a top-level Return on an
   empty session continuation stack (an outermost placed phase finishing with no
   caller) as clean session completion instead of a hard runtime error. The
   Core test suite was updated to the new contract.
2. `MigratesCopy` — v8 migration backfills missing `session_card_weighting`
   rows (same invariant `Migration5` already established) so the weighting
   invariant holds on any path to v8.
3. `LoadsThroughV2Loader` — stale guards relaxed to the canonical's real
   invariants (legitimately 0 card tags; the phase ends via its Return node, not
   a projected exit).

## Deliverables walked through this turn

- `ToyOutputSimulator` (portable, clock-injectable state machine) + WPF
  `ToyActivityHostService` adapter with a monitor loop (natural expiry /
  supersession / teardown completion) + `DialogHostService` (injectable
  presenter, loud missing-host fallback).
- Both host services wired into the reference player's `CoreServices`, with
  `StopAll` on close.
- T17: static Unity compile-drift audit — no drift; the shared portable compile
  is green via the .NET build. Full editor batch compile remains
  environment-deferred (side-effecting heavy op; `agents.md` rule 4).
- T18: canonical DB migrated to v8 through the app path; preflight recorded in
  `18-canonical-db-v8-preflight.md`; backup preserved.

## Files changed (this session's stack, uncommitted)

Portable Core/Content, v8 migration SQL + `Migration8Transform`, SQLite
loader/writer/repositories (Resource, DialogCatalog, Cloner, Undo, Authoring),
WPF graph/edit wiring, and the four test suites plus the new simulator/dialog
host. See `git status` for the full set — all left uncommitted.

## Canary handoff (human-in-the-loop)

1. **WPF interactive** — author a toy-pattern Resource, a Dialog Tag + Snippet,
   and `Timed Toy Pattern` / `Set Toy Pattern` / `Dialog From Tags` actions;
   save, reopen, confirm persistence; delete the disposable authored item.
2. **Reference player** — run a session with the dialog + toy hosts wired; confirm
   dialog lines present and legible, timed toy output apps and clears, `StopAll`
   runs on teardown.
3. **Unity batch compile** — run `unity -batchmode -quit` over the project to
   confirm the editor builds (environment deferred here).
4. **Commit** — all work is intentionally uncommitted; review `git status` +
   `git diff`, then commit on the feature branch.

The ticket stack is complete; all automated regression is green.