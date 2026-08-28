# Ticket 02 — Schema Audit and Milestone B Migration Plan

Date: 2026-08-28. Base: schema ledger rows 1–4 on `graph-workbench-finish`
(624b29b). This document inventories every existing concept the Milestone B
packet touches and freezes the exact migration plan (content schema v5 +
profile schema v1).

## Current schema inventory (canonical DB, migration ledger 1–4)

### Retain as-is (untouched by Milestone B)

| Concept | Tables | Decision |
|---|---|---|
| Session/Phase graphs | `session_graph_*`, `phase_graph_*`, `phase_node_*`, `session_node_*`, `phase_exit`, `phase_decision_option`, `session_decision_option` | Retain. Graph VM architecture is the accepted foundation. |
| Action Instances | `action_sequence`, `action_instance` + 11 type tables, `action_instance_choice_option` | Retain. Cards reuse this ownership model via `card.action_sequence_id` (v2). |
| Temperatures | `temperature_definition` (1 row: happiness 0–100 default 50) | Retain. Happiness weighting reads it. |
| Resources | `resource` (1 row) | Retain. Card duplication preserves Resource references. |
| WPF layout host extensions | `wpf_session_node_layout`, `wpf_phase_node_layout`, `wpf_viewport_state` | Retain. Untouched; v5 never rebuilds the DB. |
| Migration ledger | `core_schema_migration` | Retain; v5 appends row 5. |

### Concept: tags (single `tag` table serving three masters)

Current state — one `tag` table (8 rows: intense, relaxing, ending, party,
truth, cutscene, solo, dare) referenced by:

- `card_tag` (13 rows across 8 Cards) — Card classification.
- `phase_required_tag` (2 rows: Warm Up→ending? see below, High
  Intensity→party) — old Phase-selection mechanics, i.e. deck filters.
- `phase_excluded_tag` (1 row: Wind Down→ending) — old NOT semantics.
- `session_tag` (2 rows: Intense→intense, Relaxing→relaxing) — Session
  free-form classification.

**Decision (user-confirmed):** Phases lose classification tags entirely —
Sessions are hand-authored with an exact Phase set, so Phase tags have no
runtime consumer anymore. Cards get their own dedicated catalog
(include-only ALL/ANY; exclusion deferred to a future status-effect system,
not card tags). Session free-form tags are unused by any current code path
(loader reads them into `SessionDefinition.Tags` but nothing consumes them);
they are dropped with the tag system rather than preserved as a zombie.

**Migration v5 plan for tags:**
1. Create `card_tag_definition` (id, title, sort_order) with UNIQUE(title).
2. Create new `card_tag` (card_id, tag_id, ordinal) FK →
   `card_tag_definition(id)` ON DELETE RESTRICT.
3. Copy each distinct `tag` name used by old `card_tag` rows into
   `card_tag_definition` (stable new IDs; old tag IDs are not reused since
   old `tag` rows mixed concerns and IDs like `party`/`truth` are slugs, not
   stable catalog IDs).
4. Re-point each `card_tag` row to the new definition by title match,
   preserving card_id and ordinal order.
5. Drop `session_tag`, `phase_required_tag`, `phase_excluded_tag`, `tag`,
   and the legacy `card_tag` after copying (SQLite `DROP TABLE` in migration
   transaction; per rule 8, pre-production data is not sacred, but the Card
   tag assignments themselves ARE preserved).

### Concept: deck (`card_deck`, `card_deck_card`)

Current state — one "Starter Deck" row referencing all 8 Cards uniformly
(`CardSelector` draws from deck ∩ tag filter). Superseded by Phase Card
queries: eligibility is now Phase-owned ALL/ANY over the whole Card catalog,
weighted by Kinks/Happiness.

**Decision (user-confirmed): replace, not extend.**

**Migration v5 plan:** Drop `card_deck`, `card_deck_card`. Portable model
loses `GameContentDefinition.Deck` / `CardDeckDefinition`; `CardSelector` is
replaced by the eligibility+weighting pipeline (Ticket 07/09).

### Concept: obsolete v1 structures

`phase_slot`, `phase_slot_candidate` (0 rows, already cleared by v2),
top-level `action`, `action_debug`, `action_stat_increase`, `action_choice`,
`choice_option`, `action_cutscene`, `card_action` (0 rows each in canonical
DB; v2 left them physically present "until a cleanup migration").

**Decision:** v5 is that cleanup migration. Drop all of them. `phase.min_cards`
/ `max_cards` columns are obsolete (cadence is authored graph control) —
SQLite cannot drop columns cheaply pre-3.35; since we rebuild nothing, we
rebuild `phase` via the standard 12-step ALTER recipe only if trivial;
otherwise columns stay (harmless, unread). **Choice: leave the two phase
columns in place; they are unread.** (Avoids a risky table rebuild inside a
migration for zero behavior change.)

### Concept: SessionType (`session_type`)

Current state — v2 table with (id, title), 1 row `type-standard|Standard`,
both Sessions referenced. Portable `SessionTypeDefinition` has Id/Title only.

**Decision: complete, not replace (user-confirmed: "Session type will become
real soon — main menu exposes a session TYPE to pick").**

**Migration v5 plan:**
1. `ALTER TABLE session_type ADD COLUMN sort_order INTEGER NOT NULL DEFAULT 0;`
2. Create `session_type_required_smart_toy_capability`
   (session_type_id, capability_id, ordinal) FK → new capability table,
   ON DELETE CASCADE for capability side? No — RESTRICT on capability,
   CASCADE on session_type delete. All listed capabilities are required.
3. Existing `type-standard` row migrates in place (sort_order 0).

### Concept: Card (`card`, `card_tag`)

Current state — card(id, title, action_sequence_id) with owned sequence;
`CardRepository.Create` already seeds WaitForContinue + IncrementProgress(10).

**Migration v5 plan:**
1. `ALTER TABLE card ADD COLUMN body_text TEXT NOT NULL DEFAULT '';`
2. New relation tables (all FK → card ON DELETE CASCADE, definition side
   RESTRICT, ordinal for stable ordering):
   - `card_kink` (card_id, kink_id, ordinal) → `kink_definition`
   - `card_required_equipment` (card_id, equipment_id, ordinal) →
     `equipment_definition`
   - `card_required_smart_toy_capability` (card_id, capability_id, ordinal) →
     `smart_toy_capability_definition`
3. New `card_tag` points at `card_tag_definition` (see tags plan).

### New catalog tables (v5)

- `card_tag_definition` (id, title, sort_order) — as above.
- `kink_definition` (id, title, description NULL, sort_order).
- `equipment_definition` (id, title, category, sort_order).
- `smart_toy_capability_definition` (id, title, category, sort_order).
- No seed rows — authored content only, no fake production content (packet
  rule). `type-standard` SessionType is the only pre-existing catalog row
  (migrated, not seeded).

### Phase Card query + Session weighting (v5)

- `phase_card_all_tag` (phase_id, tag_id, ordinal) FK →
  `card_tag_definition` RESTRICT; `phase_card_any_tag` (phase_id, tag_id,
  ordinal) same shape.
- Existing content migrates to empty queries (Warm Up/High
  Intensity/Wind Down lose their required/excluded tags per the Phase-tag
  removal decision — their old rows are dropped with the tag tables).
- `session_card_weighting` (session_id PK, love_base, love_happiness_gain,
  like_base, like_happiness_gain, torture_base, torture_unhappiness_gain)
  all REAL NOT NULL DEFAULT 1.0 CHECK (>= 0). Existing Sessions migrate to
  defaults.

### Host-extension safety

v5 runs through `CoreMigrator` (one transaction, ledger row 5
`milestone-b-cards-profile-selection`). It never touches `wpf_*` tables and
only drops tables it owns. Stable IDs of all retained content (sessions,
phases, cards, exits, nodes, action instances, resources, session_type) are
preserved. The canonical DB is committed at 624b29b before migration (two-
checkpoint rule per Docs/CONTENT-DATABASE-VERSIONING.md).

### Profile schema (separate DB, v1)

`UserProfile.db` at `UserProfilePaths.ProfileDatabasePath()` (already the
established seam). Tables: `profile_schema_migration` (own ledger),
`kink_preference` (kink_id TEXT PK, preference TEXT CHECK in
love/like/torture/dont_consent)), `equipment_owned` (equipment_id TEXT PK),
`smart_toy_capability_available` (capability_id TEXT PK). No cross-database
FKs. Unknown/removed content IDs tolerated (rows persist; UI hides
unresolvable ones). Own migrator mirroring CoreMigrator shape, initial
version 1.

## Portable model changes (summary)

- `GameContentDefinition`: drop `Deck`; add `CardTagDefinitions`,
  `KinkDefinitions`, `EquipmentDefinitions`, `SmartToyCapabilityDefinitions`.
- `CardDefinition`: add `BodyText`; `Tags` (names) → `CardTagIds`; add
  `KinkIds`, `RequiredEquipmentIds`, `RequiredCapabilityIds`.
- `PhaseDefinition`: `MustIncludeTags`/`MustExcludeTags` →
  `MustHaveAllCardTagIds` / `MustHaveAnyCardTagIds`.
- `SessionTypeDefinition`: add `SortOrder`, `RequiredCapabilityIds`.
- `SessionDefinition`: add weighting six-pack (defaults 1.0); `Tags` dropped.
- New `Game.Profile`-side portable profile model (see Ticket 04).
- `CardSelector`/`CardDeckDefinition` deleted; `PhaseGraphVm.DrawCard` calls
  the new selection service (Ticket 09).

## Audited order of operations for v5 script

All inside one migration transaction (CoreMigrator):
1. CREATE the new catalog/relation/query/weighting tables (IF NOT EXISTS).
2. ALTER session_type add sort_order; ALTER card add body_text.
3. Copy old card tags → new definitions + card_tag rows.
4. DROP legacy: tag, session_tag, phase_required_tag, phase_excluded_tag,
   card_deck, card_deck_card, phase_slot, phase_slot_candidate, action,
   action_debug, action_stat_increase, action_choice, choice_option,
   action_cutscene, card_action. (Old `card_tag` was replaced in step 3.)
5. Ledger row 5.

Drops happen last, after copies, so any failure rolls back atomically.
