# Ticket 00 — Baseline Record

Date: 2026-08-28

## Base branch/SHA

- Branch: `graph-workbench-finish`
- SHA: `624b29bc0b48c85afc03a474f54626482096c072`
- `milestone-b-cards-profile-selection` was created from this SHA.
- `graph-workbench-finish` was pushed to `origin` (backup) before branching.
- Worktree clean at branch creation.

## Milestone A markers verified in source

- Stacked Session/Phase Workbench — `MainWindow.xaml.cs` GridSplitters with
  persisted ratios; `wpf_viewport_state` present in canonical DB.
- Full ActionSequence editor — `ActionSequenceEditorViewModel`
  (`DotNet/Game.ReferenceHost.Wpf/ActionEditorRegistry.cs`), reuse across
  node/Decision-option scopes.
- Decision options with normal ActionSequences — `ActionOwnerScope`
  `SessionDecisionOptionSequence` used by both Phase and Session decision
  options (`GraphViewModels.cs` `PopulateDecisionEditing`).
- Run-until-yield — `RunUntilYieldRemediationTests.cs` green.
- WaitForContinue — `WaitForContinueInstanceDefinition` present through Core,
  Content.Sqlite, WPF.
- PhaseExit nullable/unassigned behavior — schema v4
  (`action_instance_phase_goto_v4` migration row 4 in ledger).
- Make Unique — `MakeUniqueRepository` + `MakeUniqueCommand` with undo tests.
- Persistent WPF node layout — `wpf_session_node_layout` / `wpf_phase_node_layout`.
- Semantic Undo/Redo — `AuthoringUndo.cs` with identity-stable undo tests.

All markers present. No branch mismatch.

## Verification results at baseline

- Full .NET suite: **222 passed, 0 failed, 1 skipped** (Game.Core 105,
  Game.Content.Sqlite 112, WPF 5; skip is a known SQLite tooling skip).
- WPF build: **succeeded, 0 warnings, 0 errors**.
- Canonical DB `Content/GameContent.db`:
  - `PRAGMA integrity_check` → `ok`
  - `PRAGMA foreign_key_check` → no rows
  - No `-wal` / `-shm` companions.
- Migration ledger: rows 1–4 (v1 core, v2 graph, v3 wpf layout, v4 nullable
  phase-goto exit).
- Unity: not run for baseline (treated as informational per user direction;
  pinned editor 6000.5.9f1).

## Known baseline facts relevant to Milestone B

- `session_type` table exists (v2) with one row `type-standard|Standard`;
  both Sessions reference it; no SortOrder, no capability join yet.
- Single `tag` table currently serves both Cards (`card_tag`, 13 rows) and
  Phases (`phase_required_tag` 2 rows, `phase_excluded_tag` 1 row) — tag-kind
  split is in scope for Ticket 02.
- `card` table has `action_sequence_id`; `CardRepository.Create` already seeds
  the required default sequence (WaitForContinue + IncrementProgress +10).
- Legacy `card_deck` / `card_deck_card` / `phase_slot` / `phase_slot_candidate`
  tables exist; supersedure decided in Ticket 02.
- `temperature_definition` contains `happiness|Happiness|0..100|default 50`.
- `UserProfilePaths.cs` exists as path seam; `UserProfile.db` does not exist.
- `PhaseRunRngFactory` gives per-PhaseRun RNG; `CardSelector` draws uniformly
  from a deck — replaced by Milestone B selection pipeline.
