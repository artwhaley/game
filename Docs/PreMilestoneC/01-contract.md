# Pre-Milestone C Reliability — Ticket 01 Scope Contract

This contract freezes the approved work before implementation. It follows the
orchestrator packet and preserves the existing Graph Workbench architecture.

## In scope

1. Enforce the nested `SessionGoto` policy: `SessionGoto` is legal only in the
   direct ActionSequence of a `SessionDecision` option. A PromptChoice nested
   at any depth beneath that option cannot author or run `SessionGoto`; other
   actions inherit the containing context. Malformed existing content is
   detected and reported loudly.
2. Wire/fix the existing Catalogs search box while the Catalogs view is
   visible. Keep the current Library layout and other filters unchanged.
3. Make runner diagnostics reliable: retain 10,000 lines, add Copy/Save/Clear
   Log, log the full card body in the structured Card Started entry, fail
   loudly on an existing-but-unreadable UserProfile database, expose a visible
   integer seed with Randomize, use separate deterministic Session-selection
   and PhaseRun/Card RNG domains, and resolve typed rejection IDs to readable
   names with IDs retained as fallback/detail.
4. Replace destructive ActionSequence rewrite saves with stable-ID diff
   persistence. Surviving Action Instances, PromptChoice options, and nested
   sequences keep their rows/IDs; only genuinely removed objects are deleted.
   Include an extension-table `ON DELETE CASCADE` safety test.
5. Make graph edge Undo/Redo restore the exact captured edge row: edge ID,
   source socket, target node, graph owner, and metadata on every supported
   disconnect/delete/compound path.
6. Run the canonical `Content/GameContent.db` backup, migration/load, SQLite
   integrity/FK, disposable-card canary, and final regression gates as
   specified by Tickets 09 and 10.

## Explicitly deferred / prohibited

- No Resource authoring, temporary catalogs, file pickers, placeholder resource
  workflow, or Resource redesign.
- No Library layout/redesign beyond the existing Catalogs search wiring.
- PromptChoice remains capped at three options.
- No Unity bridge or Unity host authoring work.
- No anti-repeat/recent-card system or unrelated gameplay changes.
- No generic EAV/reflection action-parameter system and no reopening of the
  SQLite/Core/WPF architectural contracts.

## Acceptance gates

- Each ticket gets focused tests, the relevant full .NET tests, WPF build, and
  diff review where applicable.
- SQLite mutations get integrity/FK checks; canonical DB work follows the
  preflight backup and canary record.
- Human gates use the exact review steps from the packet. No human pass is
  claimed until performed by a human.
- After every application code/XAML/content/config change, the WPF main app is
  built and launched and is left running for interactive testing.

The baseline gate in `Docs/PreMilestoneC/00-baseline.md` passed, so this scope
contract authorizes implementation on `pre-milestone-c-reliability`.
