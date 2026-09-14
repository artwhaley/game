# Conversation Performance V1 packet

**Foundation cleanup applied:** [Character presentation foundation patch](../../Docs/UnityPerformance/FOUNDATION-PATCH-SPEC.md) and its [results](../../Docs/UnityPerformance/FOUNDATION-PATCH-RESULTS.md) define the reusable rig/attachments, conversion of the current hair/bra/panties, and animation/face/gaze ownership now in the working tree. The [execution-agent handoff prompt](FOUNDATION-HANDOFF-PROMPT.md) records the verified foundation state before the approved V1 tickets resume. This does not redesign V1.

**September 13 foundation state:** the Lara asset kit, external Humanoid motion source, reusable CharacterRig/attachment foundation and disposable showcase visual pass are verified. Read [Phase 00 — setup and content acquisition](PHASE-00-SETUP-AND-CONTENT.md) only for a concrete missing prerequisite; do not reacquire or destructively rebuild assets that are present. Ticket 00 and the real-rig portion of Ticket 01 are complete. Start at Ticket 01's remaining ingredient registry, factored anchor/operation bindings and generated PresentationCatalog, then continue through Tickets 02–05 while preserving the approved architecture.

Source-audited September 11, 2026 revision. This replaces the preceding eleven-ticket design with six tickets proving an ordinary Card through WPF → SQLite → GameContentDefinition → Core → Unity.

Read the [build plan and source audit](../../Docs/UnityPerformance/BUILD-PLAN.md), [architecture contract](ARCHITECTURE-CONTRACT.md), [tickets](TICKET-STACK.md) and [orchestrator prompt](ORCHESTRATION-PROMPT.md). The V1 packet remains an execution specification; the foundation prerequisite is the implemented portion documented above.

## What gets built

    Perform: Playful Tease
    Dialogue From Tags: tease
    Dialogue From Tags: instruction
    Wait For Continue

Perform references a small reusable Conversation Event. Core plans legal staging and chooses compatible ingredients. Dialogue starts automatically refresh acting. Unity renders and owns animation timing. WPF owns semantic Performance Tags and events; Unity ingredients reference those tags and supply factual compatibility.

## Ticket order

| Ticket | Deliverable |
|---|---|
| 00 | Canonical SQLite integration prerequisite and explicit activity/control classification |
| 01 | Real/representative rig spike, sparse ingredients and generated PresentationCatalog |
| 02 | Minimal Core planner, Perform and async performance host |
| 03 | Typed SQLite persistence, WPF tags/events and simulated planning |
| 04 | First real authored Card playing in Unity |
| 05 | Measured workflow/visual correction and V1 acceptance |

Provider compatibility, canonical SQLite loading, character assets and external Humanoid motion are verified prerequisites for the ticket executor. These are feasibility gates, not permission to replace canonical content or claim a simulated proof.

## Change log

- **Removed:** game-content JSON/duplicate DTOs, dependency-closure exporter and stale publication protocol. The repository already has a canonical SQLite direction and shared snapshot loader.
- **Removed:** Core frame ticking, blend/cadence ownership and speech scheduler. Core plans through async host requests; Unity owns rendering.
- **Reduced:** one public Perform(eventId), a small event form and automatic dialogue refresh. No Refresh Expressions, Set Player State, action overrides or rigid Mood enum.
- **Moved:** Performance Tag definitions to WPF/SQLite; Unity retains ingredient tag membership and factual applicability.
- **Simplified:** sparse gesture metadata with a measured four-field intake target; factored anchor/posture state and reusable operations instead of manually authoring every state/edge combination.
- **Opened to evidence:** Animator/Playables/rigging choice follows the rig spike. Read-only Nodify diagnostics are allowed; future semantic behavior graphs remain possible.
- **Deferred:** driven motion/funscript, voice/lipsync, player posing, broader state, extensive dashboards/arbitration and standalone packaging. No speculative Phase 2 API/schema.
- **Retained:** procedural variety, portable semantic planning, Unity asset authority, WPF authoring, automatic catalog exchange, action classification correction and no takes/per-line clips/persisted combinations.

## Reviewer focus

Can an ordinary card run from the canonical DB in Unity by ticket 04? Can its second line refresh acting without another authored action? Can adding a gesture expand existing content after touching only the necessary fields? Can a new sit-capable anchor reuse existing operations? Does Core remain free of rendering clocks?

V1 ends with an editor-playable Session and demonstrated authoring loop. A separate Phase 2 packet follows acceptance.
