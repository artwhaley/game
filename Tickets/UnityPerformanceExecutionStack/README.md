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
| 03 | SQLite persistence and authoring commands | Schema, loader, repositories, cloning, undo and script import | HARD |
| 04 | Basic WPF authoring and simulated play | Writers can author and execute the first nonvisual conversation | HARD + HUMAN |
| 05 | Runtime export and current Unity bootstrap | WPF-authored current content launches in Editor and player | HARD |
| 06 | Stage movement and layered actor renderer | Six locations, poses, routes and coherent layer arbitration | HARD + HUMAN |
| 07 | Dialogue, face and player presentation | Subtitles, voice/mouth, line acting and four player postures | HARD + HUMAN |
| 08 | Procedural motion and funscript | Oscillator/script-driven arm/prop and pelvis motion | HARD + HUMAN |
| 09 | Computed matrix and Unity audition | Pleasant high-volume authoring and visual audition loop | HARD + HUMAN |
| 10 | Acceptance content, built-player canary and handoff | Playable 3–5 minute session and authoring acceptance | FINAL HUMAN |

Every numbered ticket is one coherent commit unless its own gate requires an asset-content checkpoint or canonical database migration checkpoint. Ticket 00 records any justified split before implementation begins.

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
