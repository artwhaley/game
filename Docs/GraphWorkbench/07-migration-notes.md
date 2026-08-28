# Migration Notes (Ticket 21)

How the SQLite content schema got from the PhaseSlot era to the graph model,
and how to move an existing database forward.

## Ledger mechanics

`Game.Content.Sqlite` runs embedded migration scripts in order
(`SQLITE-SCHEMA-V1.sql` → `V2.sql` → `V3-WPF-AUTHORING.sql` → `V4`), each recorded in
a `schema_migration` ledger by `CoreMigrator.EnsureSchema`. Any connection
opened through `ConnectionInitializer` + `EnsureSchema` migrates an older file
in place to the current version. The canonical `Content/GameContent.db` is
kept at the current version by the seed/migrate tool
(`DotNet/Game.Content.Sqlite.Tool`, `--migrate`).

## v1 → v2 (graph model)

- Added the two-level graph tables: `session_graph_node` (with subtype rows
  for decisions/phase references), `phase_graph_node` (entry / card executor /
  variable check / action / decision / return subtypes), `*_graph_edge`,
  `session_node_output` (normal / phase_exit / session_goto ports),
  `action_sequence` + typed `action_instance` rows replacing top-level
  configured actions.
- `Migration2Transform` converts legacy data in one pass: PhaseSlot placements
  become PhaseReference nodes with projected exit sockets; top-level
  `action`/`card_action` rows are copied into owned `action_sequence` +
  `action_instance` rows; slot-era rows are then cleared (the physical
  `phase_slot` / `phase_slot_candidate` tables may remain empty for host
  compatibility; no code path uses them).
- Verified by `SchemaV2MigrationTests.CanonicalCopy_MigratesToV2_AndPassesIntegrityChecks`
  against the preserved `Fixtures/GameContent-v1.db`, plus
  `CanonicalDatabase_IsAtCurrentMigration`.

## v2 → v3 (WPF authoring layout)

- Added `wpf_session_node_layout`, `wpf_phase_node_layout` (node X/Y per
  parent), and `wpf_viewport_state` (pan/zoom per editor scope). These tables
  are authoring-only: never part of the snapshot, ignored by the Core VM.
- Adding a migration is safe on already-migrated files; the canonical DB was
  migrated in place and re-committed.

## v3 → v4 (nullable PhaseGoto assignment)

- Rebuilt the core-owned `action_instance_phase_goto` table so
  `phase_exit_id` may be NULL while an Action Instance is being authored.
- Force-deleting an exit clears referenced GOTO assignments and removes its
  projected session sockets/edges in one transaction; undo restores the exact
  assignments and projected edge identities.

## WPF extension tables vs core migrations

Host/authoring tables (`wpf_*`, future `unity_*`) are preserved by every core
operation: `HostExtensionSafetyTests` prove `EnsureSchema`, whole-graph
replace, and session/phase delete leave unrelated tables and rows intact.

## Moving a DB forward (any install)

1. Point the tool at the file: `dotnet run --project DotNet/Game.Content.Sqlite.Tool -- --db <path> --migrate`.
2. Run `PRAGMA integrity_check;` and `PRAGMA foreign_key_check;` — both must
   be clean (guarded by `CanonicalDatabase_PassesIntegrityChecks`).
3. Load-check through the snapshot loader: `GameContentSnapshotLoader.Load`.
