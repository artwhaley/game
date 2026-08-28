# Content Database Versioning

`Content/GameContent.db` is authored game content and is a tracked, canonical
project asset. Treat a database commit like a source-content checkpoint: it must
be coherent, reviewable by its intent, and recoverable before the next mutation
or schema migration.

## Normal authoring checkpoint

1. Finish one meaningful authoring batch and close every process that can write
   the database, especially the WPF Workbench, Unity, and ad-hoc SQLite tools.
2. Confirm that `Content/` contains no `GameContent.db-wal` or
   `GameContent.db-shm` file. Never commit only the main file while live changes
   remain in a WAL.
3. Validate the closed database:

   ```text
   sqlite3 Content/GameContent.db "PRAGMA integrity_check; PRAGMA foreign_key_check;"
   dotnet test Game.Workbench.sln --no-restore --nologo --verbosity minimal
   ```

   `integrity_check` must return `ok`; `foreign_key_check` must return no rows.
4. Review the authored change in the Workbench or with focused semantic queries.
   A binary Git diff cannot explain which Session, Phase, Card, edge, or Action
   changed, so the commit message must.
5. Commit the database promptly with a content-focused message. Do not leave
   valuable authored work as a long-lived uncommitted local mutation.

## Schema migration checkpoint

Use two database checkpoints around an intentional migration:

1. Validate and commit the current database before migration.
2. Implement and test the migration against temporary copies first.
3. Close all writers and run the repository migrator against the canonical file.
4. Re-run integrity, foreign-key, loader, and playback tests.
5. Commit the migrated canonical database separately. The pre-migration commit
   is the exact rollback point if the migration later proves wrong.

Migration code and tests should already be committed before migrating the
canonical file. Never hand-edit migration ledger rows to simulate an upgrade.

## SQLite and Git constraints

- Git versions the complete database file; it cannot meaningfully line-merge two
  divergent SQLite files. Keep canonical content authoring single-writer. If two
  branches both change the database, do not resolve the conflict with a blind
  `ours`/`theirs` choice—select a base and replay the other authored changes
  through the Workbench or repository commands.
- Never make a filesystem copy of an actively written database. Close the writer
  first or use SQLite's online backup mechanism.
- Keep user preferences, saves, telemetry, and machine-specific state out of
  `GameContent.db`; it contains project content only.
- A local Git commit provides rollback history, not off-machine disaster
  recovery. The checkpoint becomes an off-machine copy only after the branch is
  pushed or another verified backup captures the repository. Agents must still
  obey the repository rule not to push unless the user asks.
- If the database grows enough to make ordinary Git history impractical, decide
  on Git LFS or semantic export tooling deliberately before changing storage.

## Current protected checkpoints

- `a5627eb` — authored content and schema v3 immediately before migration v4.
- The following content checkpoint migrates that same content to schema v4; no
  Session, Phase, Card, Action Instance, PhaseExit, or layout counts changed.
