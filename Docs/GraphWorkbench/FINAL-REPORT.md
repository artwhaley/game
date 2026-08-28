# Graph Workbench / SQLite Graph Content — Final Report (Ticket 21)

Milestone 0.3 final handoff. Branch: `sqlite-content-graph` (pushed to
`origin/sqlite-content-graph`). Ticket stack:
`new tickets/Game_GraphVM_WPF_Authoring_Stack/` (T00–T21, all complete).

## Commits (short SHAs, newest first)

| Commit | Ticket |
| --- | --- |
| `96a74fd` | T20 user-profile boundary + spawn seam |
| `b0bdd99` | T19 live Core preview / graph debugger |
| `c344809` | T18 semantic undo/redo command layer |
| `7b9b357` | T17 copy / duplicate / make-unique / port lock |
| `373a2dc` | T16 decision action lists + SessionGoto |
| `910144e` | T15 phase exits, inline GOTO, live projection |
| `d8c0700` | T14 phase library + phase graph authoring |
| `f7c670d` / `62a9e40` | T13 session library/graph authoring + migration v3 |
| `74e0907` / `b5dae8c` | feedback rounds (dark mode, connectors, exit naming) |
| `c0baae8` | T12 Nodify four-pane workbench shell |
| `fbd2e8b` | T11 Unity thin host |
| `bcaa86d` | T10 Core integration regression suite |
| `a543a2f` … `0a640ad` | T09–T02 graph VMs, continuation stack, action registry, v2 migration/loader, portable model |
| (prior) | T00/T01 preflight baseline + install architecture contract |

## Schema

- **SQLite core schema version 3.** v1 = PhaseSlot-era legacy (migrated, not
  taught); v2 = two-level graph model (session/phase nodes, edges, ordered
  `action_sequence` + typed `action_instance`, session-node outputs with
  phase-exit projections and session-goto ports); v3 = `wpf_*` authoring layout
  tables (node positions, viewport state). Migrations are embedded scripts in
  `Game.Content.Sqlite` with a ledger; the canonical `Content/GameContent.db`
  is migrated to v3 and guarded by tests.

## Implemented vocabulary

- **Session graph nodes:** Start, PhaseReference (reusable phase with live
  projected exit sockets), SessionDecision (prompt + 1–3 options), End.
- **Phase graph nodes:** Entry, CardExecutor (draw), VariableCheck (progress /
  temperature / stat comparisons), ActionNode (owned ordered action sequence),
  PhaseDecision (options), Return.
- **Action Instances (typed, blocking flags):** PhaseGoto (exit selection),
  SessionGoto (unique socket + label), IncrementProgress, ModifyTemperature,
  StatIncrease, Debug, Choice, Cutscene.
- **Ports:** normal, phase_exit (projected), session_goto, true/false.

## Runtime semantics (Core)

- Session graph VM walks Start → placements/decisions → End; each PhaseRun has
  its own progress + card RNG + draw history.
- Phase graph VM executes nodes until one card is drawn per user-paced step;
  VariableChecks gate branches; GOTO transfers out through an exit;
  RETURN restores the caller from the continuation stack (exact PhaseRun
  resume); EndSession clears the stack.
- Engine facade (`GameSessionEngine`) drives the VM with host services
  (delay/log/prompt/cutscene) and spawn options (temperature overrides).

## Automated results

- `dotnet test Game.Workbench.sln`: **204/204** — `Game.Core.Tests` 102/102,
  `Game.Content.Sqlite.Tests` 102/102. Includes: five deterministic Core
  regression scenarios (normal, recovery-return, recursive, decision,
  one-shot transfer), continuation-stack tests, debugger trace tests, spawn
  seam tests, v1→v2 migration on the preserved v1 fixture, schema constraint
  tests, snapshot round-trips, authoring repositories, undo snapshot
  round-trips, canonical DB integrity (`integrity_check` = ok,
  `foreign_key_check` = 0 rows), user-profile boundary, host-extension safety
  (wpf tables survive EnsureSchema), and the final end-to-end
  DB → snapshot → Core engine playback test.
- **Unity:** thin host compiled against the v2 graph VM (T11) and the portable
  suite passes in `dotnet`; the Unity EditMode (24) / PlayMode smoke (2)
  numbers are from the previous milestone — **the Unity editor was not
  re-run this session** (no editor available); the .NET-side suite is the
  verification for the graph-era changes.

## Human review gates

- **T04 vibe check / layout gate (user):** passed with feedback — layout and
  splitter behavior accepted; per-item fixes (connectors on nodes, phase pane
  population, dark-mode consistency) landed in `b5dae8c` and `74e0907`
  (guaranteed dark mode, readable node/exit naming, exit wiring fixes).
- **T18 / T19 review gates:** the user pre-authorized proceeding past the
  interaction-heavy inspection while away ("build forward as far as you can");
  the undo/redo and live-preview surfaces are implemented and covered by
  automated trace/integration tests. A follow-up human pass is recommended
  using `Docs/GraphWorkbench/WORKBENCH-MANUAL-TEST-GUIDE.md`.
- **T21 FINAL GATE:** report written; **the next Card/Kink authoring milestone
  must NOT start automatically** — it needs a new, human-approved packet.

## Deferred / known gaps

- **Card authoring** (the next milestone) — deck/card creation UI, action
  parameter editing beyond GOTO, instance reordering in the UI.
- **Kink preferences, Equipment inventory, Smart Toy devices/capabilities,
  persistent Happiness** — explicitly deferred to the profile boundary
  (`Docs/GraphWorkbench/USER-PROFILE-DEFERRED.md`); no profile tables exist.
- **Unity host parity** — Unity still uses the thin bridge; the Workbench is
  the authoring host for now.
- **Known UX issues:** Library scale (2000+ cards / 500+ sessions needs tabs /
  search UX beyond the filter boxes); the legacy Run Session window lacks
  debug readouts (the Preview strip is the debugging surface); no UI-level
  reorder for action instances; placement-context phase navigation can blank
  the Inspector in edge re-entrancy cases (guarded but worth a human pass).

## How to run

```bash
dotnet test Game.Workbench.sln           # full portable suite
dotnet run --project DotNet/Game.ReferenceHost.Wpf -- --db path/to/db.db
```
