# Pre-Milestone C Reliability — Ticket 09 Canonical DB Preflight

## Safety record

- Exact target: `Content/GameContent.db`
- Branch: `pre-milestone-c-reliability`
- The DB was already user-dirty before this stack and remains uncommitted;
  no reset or checkout was used.
- Byte-for-byte backup created outside the repository canonical path:
  `C:\Users\artwh\OneDrive\Documents\game\pre-milestone-c-reliability-backups\GameContent-20260829-010005.db`
- Canonical and backup sizes: 708,608 bytes each.
- SHA-256 for both: `38031CDEF64CE13F610E5450E9BF6068E209EACC4B24DEE02CA4D57C66AD0F87`.

## Automated checks

- Real `ConnectionInitializer` + `CoreMigrator` path: passed; current core
  schema version is 5.
- Canonical read-only snapshot load: passed. Counts observed: 10 Cards, 2
  Sessions, 6 Phases, 27 ActionSequences, and 38 ActionInstances.
- `PRAGMA integrity_check`: `ok`.
- `PRAGMA foreign_key_check`: zero rows.
- Disposable-card repository canary on an isolated copy: passed. It created a
  default card, verified WaitForContinue + IncrementProgress, edited title/body,
  reopened, deleted, reopened again, and rechecked integrity/FKs.
- Existing canonical playback test remains skipped because the preserved
  user-authored DB has an unwired projected PhaseExit; this is reported rather
  than repaired as unrelated content.

## Human canonical canary

Pending. The exact Workbench flow still needs a human against the canonical DB:
create a clearly disposable Card, verify its default Actions, edit title/body,
save, navigate away and back, restart the Workbench, verify persistence, Undo
and Redo one safe edit if practical, delete the disposable Card, restart, and
verify deletion. Re-run the integrity/FK checks afterward. No human pass is
claimed here.
