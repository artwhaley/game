# Ticket 11 — Integrity and Host-Extension Safety Audit

Status: complete. All nine host-extension safety properties are proven by
`DotNet/Game.Content.Sqlite.Tests/HostExtensionSafetyTests.cs` against the
ticket's exact `unity_fake_action_binding` extension table, the canonical DB
passes both PRAGMA audits, sidecars are git-ignored, and a codebase-wide scan
found no wholesale delete/rebuild path.

## Host-extension safety (proven by tests)

| # | Property | Test | Result |
|---|----------|------|--------|
| 1 | Updating an Action in place preserves its host binding | `UpdateActionInPlace_PreservesHostBinding` | pass |
| 2 | Unrelated Session/Phase edits preserve bindings | `UnrelatedSessionAndPhaseEdits_PreserveHostBinding` | pass |
| 3 | Card-action reorder preserves Action rows and bindings | `CardActionReorder_PreservesActionRowsAndBindings` | pass |
| 4 | PhaseSlot reorder preserves Phase rows | `PhaseSlotReorder_PreservesPhaseRows` | pass |
| 5 | Intentional Action delete cascades its owned host binding | `IntentionalActionDelete_CascadesOwnedHostBinding` | pass |
| 6 | Referenced Action delete is blocked until core refs removed | `ReferencedActionDelete_IsBlocked_UntilCoreRefsRemoved` | pass |
| 7 | Core migrations preserve unknown host tables (incl. re-application over a stale ledger) | `CoreMigrations_PreserveUnknownHostTables` | pass |
| 8 | Snapshot loading ignores host tables | `SnapshotLoading_IgnoresHostTables` | pass |
| 9 | Normal authoring never truncates/rebuilds all core content | `NormalAuthoring_NeverTruncatesOrRebuildsCoreContent` | pass |

Supporting guarantees already in place:

- `card_action.action_id`, `phase_slot_candidate.phase_id`, and
  `choice_option.child_action_id` use `ON DELETE RESTRICT` — core content that
  is still referenced cannot be deleted. Owned children (choice options,
  card tags, session tags, phase slots/candidates) cascade on parent delete.
- `ConnectionInitializer` forces `PRAGMA foreign_keys = ON` on every
  connection and hard-fails if enforcement is not active.

## Wholesale-delete scan

Every `DELETE` statement in the codebase was reviewed:

- `SessionRepository.Delete` — scoped `DELETE FROM session WHERE id = @id`
  (plus its owned `session_tag` rows), parameterized.
- `PhaseRepository.Delete` — guarded by a reference-count check, then a
  scoped parameterized single-row delete; child rows are removed per-table
  with parameterized `WHERE phase_id = @id` inside one transaction.
- `PhaseSlotRepository.Delete` / `RemoveCandidate` — scoped single-row deletes.
- No path deletes all entities or rebuilds the DB wholesale. The seed tool
  (`Game.Content.Sqlite.Tool`) creates the DB only when it does not exist and
  refuses to overwrite an existing one.

## Migration hardening (finding + fix)

The audit surfaced one real gap: re-running the v1 migration over an existing
schema with a stale/absent migration ledger failed loudly (`CREATE TABLE` on
an existing table), because the ledger — not the script — was the only thing
preventing re-application. Fixed by making every v1 `CREATE TABLE` statement
`CREATE TABLE IF NOT EXISTS`. Re-application over existing core tables or host
extension tables now re-asserts the schema instead of failing, and never
touches existing rows. Verified by `CoreMigrations_PreserveUnknownHostTables`,
which wipes the ledger and re-applies the migration over a live host binding.

## Canonical DB audits

`CanonicalDatabaseTests.CanonicalDatabase_Loads_AndPassesIntegrityChecks`
runs against the committed `Content/GameContent.db`:

- `PRAGMA integrity_check` → `ok`
- `PRAGMA foreign_key_check` → zero rows

Both must stay green on every test run; the test fails if the canonical DB is
missing, corrupted, or violates any FK.

## SQLite sidecars

`.gitignore` covers `*.db-wal`, `*.db-shm`, `*.db-journal`. Verified via
`git check-ignore`; the working tree currently has no sidecar files.
