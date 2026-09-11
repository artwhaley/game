# Unity Performance Playable Slice — Execution Packet

This packet turns [`Docs/UnityPerformance/BUILD-PLAN.md`](../../Docs/UnityPerformance/BUILD-PLAN.md) into an ordered implementation stack. Its finish line is a real Windows Unity build and a WPF authoring workflow, not an animation API demonstration.

## Review and execution status

This packet is ready for review. It does **not** authorize implementation merely by existing in the repository. After review, the user must explicitly approve execution of the packet (or an amended commit). Once approved, the packet is standing authorization for its coherent multi-file tickets and its named gates; the executor need not pause for the repository's normal five-file or per-change approval defaults.

Purchasing assets, adding packages/dependencies, pushing implementation branches, or changing the pinned Unity version remain outside that standing authorization unless the user separately approves them.

## Start here

Give the executor this repository and tell it to read, in order:

1. repository `agents.md`;
2. [`ORCHESTRATION-PROMPT.md`](ORCHESTRATION-PROMPT.md);
3. [`ARCHITECTURE-CONTRACT.md`](ARCHITECTURE-CONTRACT.md);
4. [`Docs/UnityPerformance/BUILD-PLAN.md`](../../Docs/UnityPerformance/BUILD-PLAN.md);
5. [`TICKET-STACK.md`](TICKET-STACK.md), completely, before editing.

The architecture contract resolves ambiguity. The build plan explains intent and detailed behavior. The ticket stack defines execution order and acceptance.

## Ticket index

| # | Ticket | Principal result | Gate |
|---:|---|---|---|
| 00 | Baseline, protection, and implementation map | Reproducible base, DB safety, asset inventory, exact edit map | HARD |
| 01 | Real rig and animation proof | Visually approved layering, transition, speech-face and scrub proof | HARD + HUMAN |
| 02 | Portable performance domain and action semantics | Core director, routing, selection, clocks, lifecycle and actions | HARD |
| 03 | SQLite persistence and authoring commands | Scoped loader, undo/import, separate canonical migration checkpoint | HARD |
| 04 | Basic WPF authoring and simulated play | Inherited defaults, shared buffers, starter conversation and local audition | HARD + HANDS-ON |
| 05 | Runtime export, Unity bootstrap and first visual audition | Edit unsaved text in WPF and watch the real proof rig with one command | HARD + HANDS-ON |
| 06 | Stage movement and layered actor renderer | Six locations/poses and the first integrated writer/visual user gate | HARD + HUMAN |
| 07 | Dialogue, face and player presentation | Subtitles, voice/mouth, line acting and four player postures | HARD + HANDS-ON |
| 08 | Procedural motion and funscript | Oscillator/script motion through the existing edit/audition loop | HARD + HANDS-ON |
| 09 | Computed coverage, asset intake and authoring polish | Register vocabulary once, inspect shared effects and fix iteration friction | HARD + HANDS-ON |
| 10 | Acceptance content, built-player canary and handoff | Playable 3–5 minute session and authoring acceptance | FINAL HUMAN |

Tickets are ordered milestones, not single-commit size limits. Split implementation into coherent tested commits; keep migration code, canonical binary migration and meaningful content batches in separate checkpoints. HANDS-ON means executor GUI/visual verification with evidence; HUMAN means explicit user acceptance. Only 01, 06 and 10 require a user gate.

## Workflow review — September 11, 2026

The previous packet left avoidable daily work for the author. These findings are resolved in the architecture contract, build plan and tickets:

| Friction found | Required revision | Delivered by |
|---|---|---|
| Profile/mood re-entry and accidental resets | Independent Keep current updates; show inherited values/source | 02–04 |
| Save/export/switch apps/press Unity Run to hear one edit | Buffered one-command Audition, scene kept loaded, preview data outside Assets | 04–05 |
| Full-session replay to test one line; uncontrolled rerolls | Explicit local test context, optional Test arrival, Replay take/New take and pin cue | 02, 04–06 |
| Shared profile slider experiments alter every consumer | Apply/Revert buffers, usage/coverage diff, Make unique from edited values | 03–04 |
| Incomplete unrelated content blocks all tests | Scoped load/validation; disabled cues stay out of rotation; direct error repair links | 03–05, 09 |
| Register the same clip/IDs in two apps; manually refresh manifests | Asset intake and acknowledged registration; independent content/asset freshness | 05, 09 |
| Writers must assemble technical stage/graph scaffolding | Reviewed starter setup plus one New conversation command using existing types | 03–04 |
| Close DB before every write; retype the demo into canonical at the end | Checkpoint-only closure; early canonical migration; one evolving conversation | 03–10 |
| First useful preview arrives late; repeated approval/giant-commit gates | Real visual audition by 05, writer gate at 06, coherent commits and three user gates | Orchestration |

This review changes the planned workflow, not the application. The warm-loop target is an edit followed by one WPF command and a preview frame within two seconds for loaded/cached assets, excluding intentional travel/blends/import. Measure it on the user's machine during execution. Preserve explicit full-session gameplay testing and committed-data build export alongside the faster local audition.

## Reviewer checklist

Ask the reviewing agent to report findings by severity and cite this packet's file/section. In particular, check:

- whether a fresh executor could implement each ticket without inventing an unstated ownership, ordering, persistence or failure rule;
- whether dependencies and human gates prevent expensive WPF/schema work from landing before the actual rig proves the animation model;
- whether the WPF workflow stays centered on writing dialogue and reusing performance vocabulary;
- whether any ticket accidentally reintroduces per-conversation Timeline authoring, duplicate compatibility logic, direct SQLite access from Unity, or a second editable content store;
- whether action lifetime, region conflicts, movement readiness, dialogue ordering, procedural timing, failure propagation and replay cleanup have testable outcomes;
- whether the stack ends in a standalone playable session and hands-on authoring test, rather than stopping at APIs or a sandbox;
- whether any proposed package, asset purchase, licensing assumption or hidden external dependency lacks an explicit gate.

The reviewer should propose concrete edits for every finding. A broad “too large” finding should identify a safe dependency-preserving ticket split rather than deleting an acceptance obligation.

## Definition of done

The stack is complete only when the user can conveniently author a new conversation in WPF, export it, and play it in a standalone Unity build where the real character travels, settles, speaks and performs varied compatible animation. The final report must distinguish automated results, executor-observed visual results, and user acceptance.
