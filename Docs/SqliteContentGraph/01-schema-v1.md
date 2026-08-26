# 01 — Core Schema v1 Decisions (Ticket 04)

Implements `SQLITE-SCHEMA-V1.sql` from the packet as the embedded
`SQLITE-SCHEMA-V1.sql` in `Game.Content.Sqlite`, applied by `CoreMigrator`
as migration 1 (`core-schema-v1`).

## Adjustments vs. the packet script

Semantics are preserved; the following were adapted deliberately:

1. **`PRAGMA foreign_keys = ON;` removed from the script.** Connection-level
   pragmas are owned by `ConnectionInitializer` (applied before any
   transactional work, with hard verification). A `PRAGMA foreign_keys`
   inside a transaction is a no-op anyway.
2. **`core_schema_migration` creation removed from the script.** The migrator
   owns the migration table.
3. **`card_action.action_id` and `card_deck_card.card_id` are `NOT NULL`.**
   Per the dense-lists decision: ordered reference lists have no null no-op
   slots; the absence of a row is the absence of the reference. (The old
   embedded model allowed null entries as skipped no-ops; the reshaped
   portable model drops that, and real sample content had no nulls.)
4. **`choice_option.child_action_id` remains nullable.** A choice option with
   no child action is legal (baseline "Cautious" option), so a null child
   reference is not a null slot.

## Contract properties proven by tests (`SchemaConstraintTests`)

- Real PK/FK constraints enforced (missing candidate Phase, missing Card
  Action, missing Choice child Action, missing Tag → rejected).
- Unique `(parent, ordinal)` enforced (duplicate slot ordinal → rejected).
- Reusable references `ON DELETE RESTRICT` (referenced Phase/Action delete →
  rejected).
- Owned children `ON DELETE CASCADE` (Session → slots/candidates; Choice
  Action → options; Card → card_action/card_tag), while the referenced
  reusable entities survive.
- The same Action may appear multiple times on one Card (distinct ordinals);
  the same Card may appear multiple times in a Deck (distinct ordinals).

## Action subtypes

Base `action` row + explicit subtype rows (`action_debug`,
`action_stat_increase`, `action_choice`, `action_cutscene`). No EAV. The
loader (Ticket 06) must verify type/subtype agreement and fail loudly on
unknown types or missing subtype rows.

## Resources

Cutscene Action `ResourceId`s become `resource(kind = 'cutscene')` rows
(seeded in Ticket 06).
