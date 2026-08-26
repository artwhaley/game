# 00 — SQLite Content Graph Checkpoint (Ticket 02)

Safety checkpoint of the inherited dirty tree before the SQLite canonical-content
stack begins. Recorded per `new tickets/Game_SQLite_ContentGraph_Corrective_Stack/tickets/TICKET-02-SAFETY-CHECKPOINT-CURRENT-WORKTREE.md`.

## Pre-checkpoint state

- Repo root: `repo/` (contains `Game.Workbench.sln`).
- Pre-checkpoint HEAD: `1a8f3ee50667155afb3a16a9b747b77f002770ae`
  ("fix: harden cancellation boundary, fail-noisy conversion, host ergonomics; truth-up root docs").
- Pre-checkpoint branch: `content-graph-phase-slots`.
- Checkpoint branch: `sqlite-content-graph` (created via `git switch -c`, working tree carried untouched).

## Checkpoint commit

- SHA: `5aa8413bd08f2957a199c553ad180be5e5c56671`
- Message: `checkpoint: content graph correction through ticket 01`
- Contents: 51 files — the inherited stable-ID + cutscene-binding passes
  (portable definitions with `Id`, Unity SO `EnsureId`/`OnValidate` minting,
  regenerated sample content, `CutsceneBindingRegistry`), the Ticket 00/01 docs
  (`Docs/ContentGraphCorrection/00-baseline.md`, `01-domain-map.md`), the
  `parity-content-v2.json` fixture (added), and the `parity-content-v1.json`
  removal. `ProjectSettings/ProjectAuditorSettings.asset` showed as modified at
  preflight but carries zero content diff (Unity batch-run touch, EOL/stat noise)
  and resolved to clean on `git add`.

## Safety artifacts (outside the repo)

- `pre-sqlite-working-tree.patch` — `git diff --binary HEAD` of the full inherited
  dirty set (68 KB), at the workspace root next to `repo/`.
- `pre-sqlite-backup/` — copies of the untracked files
  (`Docs/ContentGraphCorrection/00-baseline.md`, `01-domain-map.md`,
  `DotNet/TestData/parity-content-v2.json`).

## Intentionally uncommitted

- Nothing in the working tree at checkpoint time (clean).
- `Docs/SqliteContentGraph/00-checkpoint.md` (this file) is created after the
  checkpoint commit and will ride along with the next commit (Ticket 03).
- `Library/`, `Logs/`, `Temp/`, `UserSettings/` remain untracked per `.gitignore`
  (regenerable Unity state).

## Test baselines

- Portable (.NET): `dotnet test Game.Workbench.sln` → **92/92 passed** (re-run
  after checkpoint, matches the pre-correction baseline).
- Unity EditMode: **15/15 passed** documented in `Docs/ContentGraphCorrection/00-baseline.md`
  (Unity 6000.5.9f1 headless batch). Not re-run for this checkpoint; Unity is
  expected to go red when the portable model reshapes (Ticket 05) and be restored
  by Ticket 10.
