# Procedural performance execution packet

This September 11, 2026 revision replaces the earlier workflow. The unit of authoring is a reusable procedural event and ordinary cards/dialogue. The runtime chooses compatible animation layers. There are no authored takes or saved combinations.

[Build plan](../../Docs/UnityPerformance/BUILD-PLAN.md) explains the product intent. [Architecture contract](ARCHITECTURE-CONTRACT.md) defines ownership and runtime behavior. [Ticket stack](TICKET-STACK.md) gives implementation tasks, context, guardrails and acceptance. Give the [orchestrator prompt](ORCHESTRATION-PROMPT.md) to the executing agent after review.

## The workflow to protect

Unity authors animation ingredients and compatibility metadata once. Its generated catalog is read-only in WPF. WPF authors reusable selection rules, cards and dialogue. Core assembles events at runtime.

    Perform: Conversation variety / Mad / Different location
    Dialogue from tags: challenge
    Refresh expressions
    Dialogue from tags: instruction

Most cards can use automatic expression refresh on dialogue start. Adding compatible animations expands these cards without rewriting them.

Save in WPF; Play/Repeat the card in the already-loaded Unity scene. Automatic committed-content snapshots connect the tools. WPF is not a remote animation sequencer. Unity does not duplicate card logic.

## Execution order

| Ticket | Result |
|---|---|
| 00 | Source baseline, dependency inventory and acceptance fixtures |
| 01 | Real rig ingredient proof and generated Unity catalog |
| 02 | Portable procedural director and action contracts |
| 03 | SQLite event/curve persistence and scoped content transport |
| 04 | WPF event rules, coverage matrix and simulated card execution |
| 05 | Unity loads real content and supports Play/Stop/Repeat Card |
| 06 | Actual travel, poses and compatible layered composition |
| 07 | Dialogue, face, gaze, player state and lifecycle completion |
| 08 | Oscillator and imported scalar-curve motion |
| 09 | Complete and simplify the recurring content workflow |
| 10 | Playable session, standalone build and final acceptance |

Tickets are dependency ordered, not invitations to invent subsystems. Each has its own context and guardrails. Ticket 01 requires actual rig feasibility evidence; ticket 10 requires real visual and authoring evidence. Missing assets may block those results while independent code work proceeds.

## Review checklist

- Can a writer add dialogue/cards with existing events without opening an animation editor?
- Does a new Unity ingredient enter matching events without registration in WPF or card edits?
- Does repeated card playback generate valid variety without saving results?
- Are filters and asset metadata edited by one owner each?
- Does normal Save reach the next Unity Play/Repeat without manual export/import?
- Does the final build use real selection/graph execution and ship its content?
- Have tests avoided reintroducing per-line choreography, takes or an unsaved cross-app protocol?

The packet is a plan, not implementation evidence. Earlier commits on this branch contain superseded designs; execute these current documents.
