# Toy / Resource / Dialog Catalog — Ticket 18 Canonical DB V8 Migration

## Status

The canonical DB `Content/GameContent.db` is migrated to **core schema version 8**
(`toy-pattern-dialog-catalog`). The migration ran through the normal app path
(`ConnectionInitializer` + `CoreMigrator.EnsureSchema`), one transaction per
migration, at `2026-08-30T03:26:35Z`. No data was dropped or reset; the
migration is additive DDL plus an in-transaction data transformation
(`Migration8Transform`) that rebuilds legacy intensity-based `toy_activity`
rows into the `pattern_resource_id` shape.

## Safety record

- The DB was user-dirty before this stack and remains uncommitted; no reset,
  checkout, or stash was ever used on it.
- Pre-v8 byte-for-byte backup (recorded in `00-baseline.md` and the prior
  `Docs/PreMilestoneC/09-canonical-db-preflight.md`):
  `C:\Users\artwh\OneDrive\Documents\game\pre-milestone-c-reliability-backups\GameContent-20260829-010005.db`
  — SHA-256 `38031CDEF64CE13F610E5450E9BF6068E209EACC4B24DEE02CA4D57C66AD0F87`.

## Post-v8 canonical state

- Ledger: `core_schema_migration` max version = **8**.
- `PRAGMA integrity_check`: `ok`.
- `PRAGMA foreign_key_check`: zero rows.
- Counts:
  - `resource` = 1 (the existing cutscene Resource).
  - `smart_toy_capability_definition` = 1.
  - `dialog_tag_definition` = 0, `dialog_snippet` = 0 (catalog ready to author).
  - `action_instance_toy_activity` = 0, `action_instance_toy_set_pattern` = 0.
- SHA-256 (canonical, post-v8): `685CB0C72486FE96F177AE0B0C70099CCA9313B40FA11450C1FFABD32256C6CC`
  - size 708,608 bytes.

## Why automated proof already covers this

- `CanonicalDatabaseTests.MigratesCopy_ToCurrentMigration` migrates a copy of
  the canonical DB all the way to v8 and re-verifies integrity + FK: green.
- `SchemaV8MigrationTests` cover the additive DDL, the legacy-intensity data
  transformation, the dialog `RequiredDialogTagIds` relation, and the new
  tables/repositories: green.
- SQLite suite total: **134 passed**.

## Human canonical canary

Pending (unchanged from prior record): the interactive Workbench pass — author a
toy-pattern Resource, a dialog Tag + Snippet, a `Timed Toy Pattern` /
`Set Toy Pattern` / `Dialog From Tags` action, save, reopen, confirm persistence,
then delete the disposable authored item. No human pass is claimed here.