# Graph Workbench Stack — Ticket 00: Preflight Baseline

Packet: `new tickets/Game_GraphVM_WPF_Authoring_Stack` (Graph VM + WPF Authoring).
Recorded at execution time, from the actual local checkout (packet's supplied-snapshot notes treated as advisory only).

## Starting point

- **Branch:** `sqlite-content-graph`
- **HEAD:** `35578ae` — `docs: record stack completion and per-ticket commit index`
- **Working tree:** clean (`git status --short` empty; `git diff --stat` and `--ignore-space-at-eol --stat` both empty)
- **Remote:** branch already pushed to `origin` before stack work began (user-directed one-time push). Per packet orchestration rules, **no further pushes** during this stack; all gates get local commits only.
- Prior corrective stack (`Docs/SqliteContentGraph/03-stack-complete.md`) is fully committed and verified.

## Baseline results

| Gate | Result |
|---|---|
| `dotnet test Game.Workbench.sln` | **140/140 passed** (94 Game.Core.Tests + 46 Game.Content.Sqlite.Tests) |
| Unity EditMode (6000.5.9f1, batchmode) | **18/18 passed** (all `TruthCardGame.Tests.ContentAdapterTests`; prior session's "24" figure was an over-count — re-measured precisely here) |
| `PRAGMA integrity_check` on `Content/GameContent.db` | `ok` |
| `PRAGMA foreign_key_check` on `Content/GameContent.db` | no rows (clean) |

## Obsolete surfaces this stack replaces

Portable model (`Assets/Scripts/Portable/Game.Content/`):

- `PhaseSlotDefinition`, `PhaseSlotCandidateDefinition` — deleted by v2 model.
- `SessionDefinition.PhaseSlots` — replaced by Session graph nodes (`PhaseReference` etc.).
- `PhaseDefinition.MinCards` / `MaxCards` runtime progression — replaced by authored Phase graph control flow.
- `GameContentDefinition.Actions` reusable configured Action entities — replaced by Action Instances owned by sequences.
- `CardDefinition.ActionIds` references — replaced by a per-Card owned `ActionSequence`.
- `ChoiceOptionDefinition.ChildActionId` single-child reference — replaced by PromptChoice instances with per-option sequences.
- `Game.Core.SessionDriver` linear slot progression — replaced by Session graph VM.
- `Game.Core.CardSelector` remains conceptually but selection state moves into per-PhaseRun RNG/state.

SQLite (schema v1 → v2):

- `phase_slot`, `phase_slot_candidate` tables — deprecated (left physically present, ignored; later cleanup migration may remove when safe).
- top-level `action*` configured-Action tables and `card_action` — deprecated same way.
- New: session_type, temperature_definition, phase_exit, phase/session graph node+port+edge tables, action_sequence + action_instance (+subtypes), wpf_* extension schema (separate ledger).

## Protection posture

- No user changes existed to preserve at start; nothing checkpointed.
- Safety artifacts from the previous stack remain external to the repo (`../pre-sqlite-working-tree.patch`, `../pre-sqlite-backup/`) if ever needed for archaeology.
- All new work continues directly on `sqlite-content-graph`; commits made per gate.
