# Graph Workbench Remediation — Final Report

Date: 2026-08-28  
Branch: `graph-workbench-remediation`  
Push: not performed

## Scope and commit record

The orchestration prompt was followed in ticket order, `00` through `15`, on top of the existing `sqlite-content-graph` work. The repository was branched before implementation.

- Starting repository SHA: `42d834f`
- Baseline documentation commits already present: `fe138d8`, `c7a1e3c`
- Final implementation SHA: `8892684`
- Follow-up layout-binding fix SHA: `bab0376`
- Final report commit: documentation-only commit after the implementation commit

At the remediation handoff, the pre-existing dirty `Content/GameContent.db` was deliberately not staged or committed because it predated the ticket run. During validation it was restored to its pre-task semantic/version state: core migrations through v3, zero WPF layout rows, `PRAGMA integrity_check` reported `ok`, and `PRAGMA foreign_key_check` returned no rows. Migration v4 and the new behavior were tested on temporary database copies. Because SQLite rewrote pages during the validation/restore cycle, byte-for-byte identity is not claimed.

After that handoff, the user explicitly authorized protecting the authored database through version control. Commit `a5627eb` records the exact pre-v4 content checkpoint. The repository migrator then upgraded the canonical file to v4; integrity, foreign keys, nullable PhaseGoto schema, content counts, and the full automated suite were revalidated before the migrated database checkpoint. The standing procedure is documented in `Docs/CONTENT-DATABASE-VERSIONING.md`.

## Delivered changes

### Runtime and portable model

- Added explicit `WaitForContinue` and `IncrementProgress` action types.
- Replaced the one-card advance contract with `RunUntilYieldAsync` / `ContinueAsync` across the portable engine and hosts.
- A run continues through cards and phases until an authored wait, GOTO/RETURN transfer, session end, or graph completion.
- Wait suspension is resumable at the exact action/card/graph locus. Continue resumes the remaining action sequence and emits each lifecycle event once.
- Card GOTO/RETURN preserves the interrupted card continuation; nested and recursive same-Phase calls are guarded by a shared contextual execution budget.
- Progress changes only through authored Action Instances. Phase-local progress, RNG, history/cooldown state, and session-global temperatures persist in their specified scopes.
- Unity action shells and sample content now author explicit pacing/progress; there is no implicit card progress or min/max card-count runtime behavior.

### SQLite and authoring persistence

- Added schema migration v4 making an unassigned Phase GOTO target a real `NULL`, removing sentinel-ID ambiguity.
- Added explicit typed Action Instance append, edit, reorder, delete, and undo operations with owner-scope validation.
- Added atomic session-with-Start and phase-with-Entry creation, structural Start/Entry invariants, atomic phase tag replacement, and transaction-scoped graph edits.
- PhaseExit deletion now clears GOTO references and projected session edges in one transaction; undo restores exact IDs, assignments, and edge identity.
- Normal authoring uses granular row updates and preserves stable IDs; whole-graph replacement is restricted to import/restore-style operations.
- The read-only snapshot loader can inspect the preserved canonical database without attempting migrations or writes.

### WPF Graph Workbench

- Implemented the requested outer layout: 18% Library, 64% stacked Session/Phase center, 18% Inspector, with bounded panes and persistent splitters.
- Added connection-aware deterministic graph auto-layout and durable node positions for new/missing nodes.
- Added explicit typed Action Instance editing, reorder/delete controls, Phase GOTO Unassigned state, and the red warning indicator.
- Added structural New Session/New Phase creation, drag/drop phase placement, Show Sessions filtering, Make Unique flow, and command-based undo/redo across graph selection changes.
- Preserved shared Phase topology and blocks deletion of structural Start/Entry nodes.
- Follow-up fix binds Nodify's draggable `ItemContainer.Location` and selection
  state two-way to the graph view models in both editors. This closes the gap
  where visual dragging did not update `NodeMoved` or SQLite, and adds two WPF
  regression tests covering container/view-model synchronization and both editor
  style assignments.

### Documentation

The current README, project overview, agent rules, Graph Workbench design notes, migration notes, and manual test guide were updated to describe the graph model, schema v4, explicit action pacing, current runtime contract, and the separation between automated, human, and unavailable gates. Older milestone documents remain historical and are labeled as such where needed.

## Automated verification

All automated .NET tests were run with:

```text
dotnet test Game.Workbench.sln --no-restore --nologo --verbosity minimal
```

Result:

- `Game.Core.Tests`: 105 passed, 0 failed.
- `Game.Content.Sqlite.Tests`: 109 passed, 1 skipped, 0 failed.
- `Game.ReferenceHost.Wpf.Tests`: 2 passed, 0 failed.
- Total: 216 passed, 1 skipped, 0 failed.
- The one skip is the canonical-database playback guard, because the preserved user database contains a pre-existing unconnected projected PhaseExit; the test documents and ignores that exact preserved condition.

The WPF host was built with `dotnet build` using `--no-restore`: 0 errors and 12 pre-existing CS0067 unused-event warnings in legacy view-model plumbing. After the layout-binding fix, the host was rebuilt and launched visibly; process PID `25488` remained alive after startup with the expected `TruthCard Game — Graph Workbench` title. The first earlier smoke launch was against the canonical database and caused normal migration/layout writes; those writes were intentionally checkpointed according to the content versioning policy. Interactive drag/switch/restart acceptance remains a human gate.

Unity 6000.5.9f1 was invoked in batch mode for both EditMode and PlayMode using `-runTests`, `-testResults`, and `-logFile`. Unity imported and compiled the project and exited with code 0, but did not start the Test Runner or emit either requested XML result file in this environment. Therefore Unity EditMode and PlayMode are **not reported as passed**. Exact unavailable reason: the headless batch invocation completed asset refresh/script compilation and exited without producing Test Runner output. Logs:

- `Logs/GraphWorkbenchRemediation-Ticket14-EditMode-20260827235354.log`
- `Logs/GraphWorkbenchRemediation-Ticket14-PlayMode-20260827235427.log`
- `Logs/GraphWorkbenchRemediation-Ticket14-EditMode-abs-20260827235450.log`

The logs also contain Unity license-handshake warnings and unrelated Cinemachine asmref/native-extension warnings; no Unity test summary was available to classify those separately.

## Human acceptance gates

No human acceptance gate was performed, waived, or represented as passed. The following Ticket 15 checks remain **not run** and require an interactive reviewer:

1. Launch the WPF host and inspect the Library-left / Inspector-right / Session-top-center / Phase-bottom-center layout, splitter bounds, graph-local toolbars, and horizontal connector flow.
2. Arrange nodes, navigate away and back, restart, add/delete nodes and edges, and verify positions persist without corrupting graph state.
3. Add a PhaseExit, verify unassigned GOTO warning, assign and connect it, delete the used exit with Delete Anyway, verify cleared references and edge, undo exact restoration, verify shared-Phase protection, Make Unique, rename, and reassignment.
4. Verify New Session has exactly one Start, New Phase exactly one Entry, drag/drop placement, Show Sessions filtering, and Make Unique preserves the original shared Phase.
5. Play through auto-flow, Wait/Continue, no-Wait flow, card GOTO/RETURN continuation, and SessionEnd.
6. Edit/reorder/delete actions and switch between Session and Phase panes while verifying undo/redo remains available.

## Remaining issues and follow-up

- Unity EditMode/PlayMode results remain unavailable until the project is run in an environment where Unity Test Runner emits result XML, or reviewed interactively in the Unity Editor.
- The WPF project retains 12 pre-existing unused-event warnings; they do not block the build but can be cleaned up separately.
- The canonical database is now intentionally tracked at schema v4. Future authoring and migration batches must follow the documented closed-writer, integrity-check, and checkpoint workflow.
- Interactive Ticket 15 acceptance is still required before calling the remediation human-verified.

## Preserve What Is Right

### Persistence/runtime
`GameContent.db -> Game.Content.Sqlite -> portable snapshot -> Game.Core`.

Core does not issue SQL.

### Two graph levels
Session graph: SessionStart, PhaseReference, SessionDecision, SessionEnd.

Phase graph: PhaseEntry, CardExecutor, VariableCheck, ActionNode, PhaseDecision, Return/equivalent.

Do not merge them.

### Reusable Phase
A Phase is its low-level graph plus metadata. Session PhaseReference nodes reference it. Copy Session keeps Phase references shared. Make Unique clones only the selected placement's Phase.

### PhaseExit
Stable public interface member of a reusable Phase. Wiring uses stable ID, not name. Rename does not break Session wiring. Multiple internal GOTOs may use one PhaseExit.

### Action Type / Action Instance
Types are code-defined. Instances are owner-scoped and store their own parameter values. No EAV.

New Action Types may require Core, persistence, WPF and Unity/host code. That explicit vertical work is acceptable.

### GOTO / RETURN
One safe GOTO semantics: always preserve resumable continuation. No Jump/Call split. RETURN restores exact suspended state. Nested and recursive same-Phase calls are legal.

### State scopes
Session-global Temperatures persist across GOTO/RETURN.

PhaseRun-local state is restored:

- PhaseProgress;
- current graph locus;
- current Card/action continuation;
- Card-selection RNG state;
- Card history/cooldowns/anti-repeat state;
- future Phase-local variables.

### SessionEnd
Absolute; terminates and discards suspended continuations.

### Progress
No hidden Card progress. Progress only changes through Action Instances.

### VariableCheck
Keep structured numeric v1: PhaseProgress / named Temperature / existing numeric runtime stat, operators `< <= == != >= >`, literal numeric compare, True/False outputs.

### Stable-ID persistence
Normal authoring updates rows transactionally. Do not truncate/rebuild the whole DB or delete/reinsert all entities.
