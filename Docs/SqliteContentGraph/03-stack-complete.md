# SQLite Content Graph — Stack Complete

Status: **complete**. All corrective tickets (02–13) executed on branch
`sqlite-content-graph`; every hard gate green at each step. Working tree
clean.

## Ticket → commit index

| Ticket | Commit | Summary |
|--------|--------|---------|
| 02 — Safety checkpoint | `5aa8413` | Inherited work checkpointed (external patch + backup + commit); 92/92 baseline |
| 03 — Shared SQLite project + migrator | `7a8af59` | `Game.Content.Sqlite` (netstandard2.1, provider-neutral), schema v1, `CoreMigrator`, tests |
| 04 — Core schema v1 constraints | `8011271` | Constraint/cascade tests; nullable-FK decision documented (`01-schema-v1.md`) |
| 05 — Portable reference graph | `c2c2175` | `GameContentDefinition`, PhaseSlots, ID relationships, dense lists, tag entities |
| 08 — Game.Core catalog parity | `62f1263` | `ContentCatalog` + Core adaptation; JSON project removed; tests re-pointed to `Game.Content.Samples` |
| 06 — Snapshot loader + canonical DB | `0933d56` | `GameContentSnapshotLoader`, `DatabaseInitializer`, seed tool, `Content/GameContent.db` |
| 07 — Authoring repositories | `4d4ed7f` | Session / PhaseSlot / Phase repositories + tests |
| 09 — WPF loads canonical SQLite | `c2e1800` | WPF host + DB→snapshot→engine integration tests |
| 10 — Unity stays viable | `95590e8` | `UnityContentGraphBuilder` bridge; Unity EditMode 24/24 headless |
| 11 — Integrity/host-extension audit | `9ca7e38` | Nine safety properties proven; v1 DDL hardened (`IF NOT EXISTS`); `02-integrity-audit.md` |
| 12 — Demote JSON, correct docs | `bf16cd4` | README/PROJECT-OVERVIEW/agents.md/domain-map updated; JSON labeled legacy |
| 13 — WPF authoring handoff | `f7249b4` | `NEXT-WPF-HIGH-LEVEL-AUTHORING.md` |

## Gates at completion

- `dotnet build Game.Workbench.sln` — 0 errors, 0 warnings.
- `dotnet test Game.Workbench.sln` — **140/140**: 94 engine (`Game.Core.Tests`)
  + 46 SQLite (`Game.Content.Sqlite.Tests`).
- Unity EditMode headless (6000.5.9f1) — 24/24 (run at ticket 10; no Unity
  script changes since).
- Canonical `Content/GameContent.db` — loads, `integrity_check` = `ok`,
  `foreign_key_check` = no rows (guarded by `CanonicalDatabaseTests`).

## Standing state

- SQLite at `Content/GameContent.db` is the canonical content source.
- `Game.Content.Sqlite` owns schema/migrations/loading/authoring (provider-neutral).
- `GameContentDefinition` is the in-memory snapshot; `Game.Core` runs it.
- WPF = reference player + future primary author; Unity = SO→snapshot bridge
  for now, same schema + `unity_*` extensions later.
- JSON spike removed; no JSON schema v3.

## Known open items (deliberately out of scope)

- PhaseSlot multi-candidate selection rules (§6 of the handoff) — design with
  the user before implementing; needs the dedicated flow RNG.
- Card/Action/Deck authoring repositories + UI (handoff §9).
- Content validator before WPF becomes the authoring workstation
  (`Docs/CoreExtraction/DEFERRED.md` item 1, now against the SQLite model).
