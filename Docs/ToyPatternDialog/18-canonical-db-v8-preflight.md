# Toy / Resource / Dialog Catalog — Ticket 18 Canonical DB V8 Migration

## Status

The canonical DB `Content/GameContent.db` is at **core schema version 8**
(`toy-pattern-dialog-catalog`). The migration ran through the normal app path
(`ConnectionInitializer` + `CoreMigrator.EnsureSchema`), one transaction per
migration. The database was already user-dirty and authored before this stack;
its authored content was preserved.

## Safety record

- No reset, checkout, or stash was used on the canonical database.
- The earlier pre-v8 backup remains recorded in the historical checkpoint.
- Before the final terminal-fixture repair, a byte-for-byte backup was created
  at:
  `C:\Users\artwh\AppData\Local\Temp\GameContent.db.final-pre-c.20260830-155912.bak`.

## Final canonical state

- Ledger: `core_schema_migration` max version = **8**.
- `PRAGMA integrity_check`: `ok`.
- `PRAGMA foreign_key_check`: zero rows.
- Counts: sessions = 1, phases = 1, cards = 3; resources = 1;
  dialog tags = 0; dialog snippets = 0; smart toy capabilities = 1.
- `action_instance_toy_activity` = 0 and
  `action_instance_toy_set_pattern` = 0.
- SHA-256:
  `E3CDD1E5686E36B015054B9E009A2C021173EBE7DBF2E7F63A6C3B0033DE67E6`.
- Size: 708,608 bytes.
- No `GameContent.db-wal` or `GameContent.db-shm` companion files are present.

The one existing top-level Return fixture was converted to an explicit End
Session action so the authored session remains present while obeying the
corrected runtime contract. No disposable canary content was left in the
canonical database.

## Automated proof

`CanonicalDatabaseTests.MigratesCopy_ToCurrentMigration` migrates a copy of the
canonical DB to v8 and verifies integrity and foreign keys. `SchemaV8MigrationTests`
cover the additive DDL, collision-safe legacy toy transformation, dialog tag
relations, and new repositories. SQLite suite total: **134 passed**; full
solution total: **342 passed**.

## Human canary

The WPF host is running for the interactive authoring canary. The canary is
not claimed complete until a human authors and reopens a disposable toy-pattern
Resource, Dialog Tag, Dialog Snippet, and the new actions, then deletes the
disposable items.
