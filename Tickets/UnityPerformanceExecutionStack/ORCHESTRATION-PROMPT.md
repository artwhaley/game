# Orchestration Prompt — Unity Performance Playable Slice

Repository: `https://github.com/artwhaley/game`

You are the implementation orchestrator for the reviewed Unity Performance Playable Slice packet. Your job is to execute the complete stack without losing the authoring-first intent, while stopping at genuine human/authority gates.

## Authorization precondition

Do not implement until the user approves execution of the reviewed packet. If they say to execute the current reviewed branch, resolve and record that commit yourself; do not require them to type a SHA that Git already supplies. Ask only when multiple reviewed revisions make the approved target ambiguous. This revision request is documentation work, not approval to execute the application stack.

After approval, this packet authorizes the coherent multi-file work described in Tickets 00–10 and overrides the repository's normal five-file planning threshold and request-for-`go` between those tickets. It does not authorize:

- paid asset acquisition;
- a new package, framework, service, model, cloud API, or Unity version;
- pushing an implementation branch unless the user says to push;
- destructive cleanup of user work or backup files;
- weakening a HARD or HUMAN gate.

If one of those is necessary, finish all safe preparatory work, present the exact item/cost or dependency/reason, and wait.

## Base and branch

At packet creation the planning branch is `unity-performance-ticket-stack`, based on `e3995cc0daeaf26c7fe387f69df5b10110484e2e`. This SHA is context, not permission to ignore later review edits.

1. Fetch and resolve the exact user-approved packet commit.
2. Require a clean tracked working tree at that commit. Preserve untracked user backups and unrelated work; never run `git clean`, reset, restore, or checkout over them.
3. Create implementation branch `unity-performance-playable-slice` from the approved commit unless the user names another branch.
4. Record base SHA, branch, remote status, Unity version, SDK versions, and canonical DB status.
5. Never absorb later unrelated commits silently. If the base moved, report the difference and establish an explicit reviewed base.

## Required reading

Read completely before editing:

1. repository `agents.md` and `README.md` status;
2. this prompt;
3. `ARCHITECTURE-CONTRACT.md`;
4. `Docs/UnityPerformance/BUILD-PLAN.md`;
5. all of `TICKET-STACK.md`;
6. `Docs/GraphWorkbench/01-architecture-decisions.md` and `02-execution-semantics.md`;
7. `Docs/CONTENT-DATABASE-VERSIONING.md`;
8. files named in the current ticket's starting map and the tests for that area.

Historical ticket stacks are precedent, not architecture truth. When documentation conflicts, use this order: user correction > reviewed packet architecture contract > reviewed build plan > current authoritative repository docs > current code behavior > historical docs.

## How to orchestrate

- Execute tickets in numeric order. Do not start a dependent ticket before its gate passes.
- Keep one shared implementation report under `Docs/UnityPerformance/Execution/`, with Ticket 00 baseline and one concise record per ticket. Update it before each commit.
- Before each ticket, list its planned file edits. The approved packet authorizes a larger coherent set, but it does not excuse scope drift.
- Prefer one worker at a time for code that touches the shared branch, Unity project, SQLite database, WPF process, generated content, or common contracts. Parallelize read-only research/tests only when outputs cannot race.
- If delegating, give the worker the full per-ticket `Context`, `Contract`, `Guardrails`, `Acceptance`, and `Handoff` sections. Never ask a worker to infer requirements from the ticket title.
- The orchestrator owns integration, architecture consistency, canonical DB safety, commits, final application launches, and the truthfulness of reports. A worker saying “done” is not a gate.
- Inspect the diff and run focused tests for changed behavior. At each HARD gate, run its named checks; repeat a broader suite only after relevant changes/failures, rather than repeatedly running identical checks for paperwork.
- Tickets are dependency milestones, not single-commit size targets. Split large tickets into coherent commits with a recorded file map; do not bundle the whole Core or WPF feature into one opaque change. Save all intended project state, including `.meta` files. Exclude local backups, logs, temp preview messages, builds, `Library`, and machine-specific settings.
- Update `README.md` after implemented features. Add focused docs where future maintainers need operational details; do not turn reports into the only specification.
- If three consecutive attempts fail for the same cause, stop, preserve evidence, and follow repository rule 3.

## Visual verification and human gates

A HUMAN gate requires the real WPF app and/or pinned Unity Editor/player. Complete implementation, checks, content, launch and exact test steps before asking the user to judge the running result. Only Tickets 01, 06 and 10 require explicit human acceptance: actual rig feasibility, the first integrated write-and-watch loop, and final game/authoring acceptance. An existing explicit user acceptance of the same unchanged result satisfies the gate.

Tickets 04, 05, 07, 08 and 09 require hands-on executor verification and evidence, with optional user feedback; they do not add another approval pause. Continue when their checks pass. If actual UI/visual verification is unavailable, report it as pending and do not claim it passed. Human acceptance cannot be inferred from tests or executor opinion. Fix user-reported material friction in the affected ticket unless explicitly deferred.

Keep the applicable application running when handing a HUMAN gate to the user. Record process/editor state and exact scene/card/session to inspect.

## Canonical database protection

`Content/GameContent.db` is authored binary content.

- Use disposable databases for destructive tests and migration experiments. After Ticket 03's tested migration-code commit, checkpoint/migrate canonical content separately, so real authored examples from Ticket 04 can be preserved and extended through the stack.
- Before canonical migration, filesystem backup or Git database checkpoint: close writers normally, verify no WAL/SHM companions, run integrity/FK checks, hash the backup and record ledger/semantic counts. These are checkpoint requirements, not requirements before every authoring command.
- During ordinary authoring, keep WPF open and use its transactional commands and Save/Apply buffers. Scoped read snapshots/exports use SQLite transactions while WPF is open; never copy a live DB file. Checkpoint meaningful authoring batches, not individual lines. Preview requests write generated files only.
- Migration code/tests are committed before migrating the canonical DB. Canonical migration is a separate checkpoint commit if it changes the tracked DB.
- Do not hand-edit migration rows, line-merge DB files or silently renumber a migration collision. Install starter semantic content once via explicit commands, preserve its IDs, and evolve one canonical acceptance conversation from Ticket 04 onward. Never require the user to retype a successful disposable demo into canonical content at the end.
- Generated Unity JSON and manifests are build transports. SQLite remains the only editable content authority.

## Unity and asset protocol

- Keep Unity at 6000.5.9f1 and preserve every `.meta` file.
- Ticket 01 must use the actual intended humanoid test rig, face controls, animation clips and voice audio. If they are missing, produce the exact asset manifest and block the visual gate. Do not substitute a capsule and proceed as if the rig risk is closed.
- Do not buy assets or install a package without authorization. Built-in Playables, animation, audio, UI and existing Timeline/Cinemachine are sufficient for the planned slice.
- Do not manually sprawl scene YAML. Use the Editor and focused builders/binding assets where appropriate.
- After Unity app changes, compile and run in the pinned Editor. At the build gates, test a Windows development player with WPF closed.

## Architectural invariants

Enforce `ARCHITECTURE-CONTRACT.md`. In particular:

- Core chooses semantic performance, compatibility, routes and deterministic variation.
- Unity owns transforms, clips, masks, bones, blendshapes, audio, props and cameras.
- WPF is the pleasant authoring surface; SQLite is canonical.
- Persistent ambience is director state, not an endless background action.
- Blocking activity is not graph flow control.
- Tags do not replace typed physical constraints.
- No per-conversation Timeline or Cartesian combination table.
- No silent fallback for missing routes, required coverage, bindings or capabilities.
- Existing gameplay/consent/card/dialog selection semantics remain intact.

## Protect the authoring loop

Use the architecture contract's WPF workflow as a deliverable, not end-stage polish. Ticket 04 establishes inherited defaults, dialogue entry, buffers and scoped simulated audition. Ticket 05 must connect one-command WPF audition to the real proof renderer. Ticket 06 is the integrated user gate. Ticket 09 completes bulk coverage/intake tools; it must not introduce the first usable preview.

Treat repeated Save/export/Unity Run, repeated profile selection, copying asset IDs, retyping acceptance content, global validation of unrelated unfinished work, and replaying a whole scene for a single line as defects. Audit every authoring step for them. Add no service/framework to remove a few clicks: use existing commands, a focused local preview host and explicit ownership.

For each ticket, record one brief user task from entry to observed result, including clicks/commands, lost context/focus and warm audition latency where applicable. Fix avoidable friction at its source. Keep hashes, resource IDs, regions and traces in details/technical editors unless needed to explain an actionable error.

## Test and failure protocol

- Use manual clocks and fake hosts for deterministic Core tests.
- Separate card, dialogue and every performance RNG domain; sort candidates by stable ID.
- Test cancellation, stale acknowledgements, teardown, replay and app pause, not just happy paths.
- Latch fatal presentation failures into visible session failure, including failures while waiting on Continue.
- Normalize semantic traces only when comparing WPF and Unity. Never claim pixel or timing identity across hosts.
- Report exact test commands, totals, skips, failures and unverified areas. A build is not a run.

## Per-ticket worker prompt template

When assigning one ticket, send:

```text
Implement Ticket NN from Tickets/UnityPerformanceExecutionStack/TICKET-STACK.md on the current approved implementation branch.

Read repository agents.md, ARCHITECTURE-CONTRACT.md, the entire Ticket NN section, and the build-plan sections it cites. Treat the ticket's Context, Contract, Guardrails, Acceptance, and Handoff as binding. Inspect current code before proposing files. Preserve unrelated work and canonical DB safety. Make no dependency/package/version/purchase changes. Do not commit or push unless the orchestrator explicitly delegates that operation.

Return: files changed, behavioral result, test commands and exact outcomes, manual verification performed, known limitations, DB/assets touched, and any contract question. Do not claim a HUMAN gate passed.
```

## Final gate and report

Ticket 10 must leave the standalone Windows development player runnable and the WPF Workbench/Unity Editor open as appropriate for user testing. The report must include:

- approved packet/base SHA, branch, and every ticket commit;
- files and schema changes grouped by architecture area;
- canonical DB backup hashes, migration ledger, integrity/FK results and content checkpoints;
- asset provenance/license notes and binding manifest revision;
- automated commands/totals and manual canary results item by item;
- exact WPF authoring task timing/friction observations;
- Editor and built-player results;
- confirmation of each architecture invariant and exclusion;
- known visual/content gaps and deliberately deferred features;
- running process/editor/build paths;
- a clear statement that final user acceptance is pending until the user says otherwise.
