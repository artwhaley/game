# Toy Patterns + Dialog Catalog — Ticket 00 Baseline

Date: 2026-08-29
Branch: `pre-milestone-c-reliability` (HEAD `2db47f9`) → working branch
`toy-pattern-dialog-catalog` created from it. Not pushed.

## Recorded state

- git status at start: only `Content/GameContent.db` modified (pre-existing
  user-dirty state, preserved untouched — never reset/stashed by this stack).
- Latest test counts (from README, this session confirmed same): 142 Core,
  125→130 SQLite, 11 profile, 32 WPF tests.
- Canonical DB `Content/GameContent.db` (708,608 bytes):
  - `PRAGMA user_version` = 0 (ledger-based versioning)
  - `core_schema_migration`: v1–v7 applied (latest: v7 `card-folders`)
  - Counts: 1 Session (`session-8313bc9add84`, type `type-standard`),
    3 Cards, 1 Phase, 1 Resource (cutscene), 0 ToyActivity rows,
    0 Dialog rows, 0 `session_card_weighting` rows.
- `PRAGMA integrity_check`: ok; `PRAGMA foreign_key_check`: empty.

## Reconciliation (Audit Finding 1)

- Prior preflight (`Docs/PreMilestoneC/09-canonical-db-preflight.md`) recorded
  10 Cards / 2 Sessions / 6 Phases at v5.
- Current DB: 1 Session / 3 Cards / 1 Phase at v7. The session
  `session-8313bc9add84` still exists; content was evidently consolidated by
  the user during later authoring (v6/v7 migrations are recent, applied by the
  running app). The old session id surviving shows the data was edited, not
  wiped: this is explained drift, not unexplained loss. No stop condition met.
- No content was restored, reset, or seeded.

## Byte-for-byte canonical backup before v8

- Created `pre-milestone-c-reliability-backups/GameContent-20260829-*.db`
  (copied before any v8 migration run; ledger currently at v7).

## Unity compile attempt

- Not run this session (environment): deferred to Ticket 17 record.
