-- Core schema v1 (core-schema-v1).
--
-- Adapted from the packet's SQLITE-SCHEMA-V1.sql:
--  - PRAGMA foreign_keys/busy_timeout belong to ConnectionInitializer, not a
--    migration (PRAGMA foreign_keys is a no-op inside a transaction anyway).
--  - core_schema_migration is owned by CoreMigrator, not by this script.
--  - card_action.action_id and card_deck_card.card_id are NOT NULL: per the
--    dense-lists decision, reference lists have no null no-op slots; absence
--    of a row means absence of the reference. choice_option.child_action_id
--    stays NULL because a choice option with no child action is legal.

CREATE TABLE tag (
    id    TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    name  TEXT NOT NULL UNIQUE
);

CREATE TABLE session (
    id     TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    title  TEXT NOT NULL
);

CREATE TABLE session_tag (
    session_id  TEXT NOT NULL,
    tag_id      TEXT NOT NULL,
    ordinal     INTEGER NOT NULL CHECK (ordinal >= 0),
    PRIMARY KEY (session_id, tag_id),
    UNIQUE (session_id, ordinal),
    FOREIGN KEY (session_id) REFERENCES session(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES tag(id) ON DELETE RESTRICT
);

CREATE TABLE phase (
    id         TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    title      TEXT NOT NULL,
    min_cards  INTEGER NOT NULL,
    max_cards  INTEGER NOT NULL
);

CREATE TABLE phase_required_tag (
    phase_id  TEXT NOT NULL,
    tag_id    TEXT NOT NULL,
    ordinal   INTEGER NOT NULL CHECK (ordinal >= 0),
    PRIMARY KEY (phase_id, tag_id),
    UNIQUE (phase_id, ordinal),
    FOREIGN KEY (phase_id) REFERENCES phase(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES tag(id) ON DELETE RESTRICT
);

CREATE TABLE phase_excluded_tag (
    phase_id  TEXT NOT NULL,
    tag_id    TEXT NOT NULL,
    ordinal   INTEGER NOT NULL CHECK (ordinal >= 0),
    PRIMARY KEY (phase_id, tag_id),
    UNIQUE (phase_id, ordinal),
    FOREIGN KEY (phase_id) REFERENCES phase(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES tag(id) ON DELETE RESTRICT
);

CREATE TABLE phase_slot (
    id          TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    session_id  TEXT NOT NULL,
    ordinal     INTEGER NOT NULL CHECK (ordinal >= 0),
    title       TEXT NOT NULL,
    UNIQUE (session_id, ordinal),
    FOREIGN KEY (session_id) REFERENCES session(id) ON DELETE CASCADE
);

CREATE TABLE phase_slot_candidate (
    id             TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    phase_slot_id  TEXT NOT NULL,
    ordinal        INTEGER NOT NULL CHECK (ordinal >= 0),
    phase_id       TEXT NOT NULL,
    UNIQUE (phase_slot_id, ordinal),
    FOREIGN KEY (phase_slot_id) REFERENCES phase_slot(id) ON DELETE CASCADE,
    FOREIGN KEY (phase_id) REFERENCES phase(id) ON DELETE RESTRICT
);

CREATE TABLE card (
    id     TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    title  TEXT NOT NULL
);

CREATE TABLE card_tag (
    card_id   TEXT NOT NULL,
    tag_id    TEXT NOT NULL,
    ordinal   INTEGER NOT NULL CHECK (ordinal >= 0),
    PRIMARY KEY (card_id, tag_id),
    UNIQUE (card_id, ordinal),
    FOREIGN KEY (card_id) REFERENCES card(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES tag(id) ON DELETE RESTRICT
);

CREATE TABLE action (
    id           TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    name         TEXT NULL,
    action_type  TEXT NOT NULL,
    is_blocking  INTEGER NOT NULL CHECK (is_blocking IN (0,1))
);

CREATE TABLE action_debug (
    action_id  TEXT PRIMARY KEY,
    message    TEXT NULL,
    FOREIGN KEY (action_id) REFERENCES action(id) ON DELETE CASCADE
);

CREATE TABLE action_stat_increase (
    action_id  TEXT PRIMARY KEY,
    stat_key   TEXT NOT NULL,
    amount     INTEGER NOT NULL,
    FOREIGN KEY (action_id) REFERENCES action(id) ON DELETE CASCADE
);

CREATE TABLE action_choice (
    action_id  TEXT PRIMARY KEY,
    prompt     TEXT NOT NULL,
    FOREIGN KEY (action_id) REFERENCES action(id) ON DELETE CASCADE
);

CREATE TABLE choice_option (
    id               TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    choice_action_id TEXT NOT NULL,
    ordinal          INTEGER NOT NULL CHECK (ordinal >= 0),
    label            TEXT NOT NULL,
    child_action_id  TEXT NULL,
    UNIQUE (choice_action_id, ordinal),
    FOREIGN KEY (choice_action_id) REFERENCES action(id) ON DELETE CASCADE,
    FOREIGN KEY (child_action_id) REFERENCES action(id) ON DELETE RESTRICT
);

CREATE TABLE resource (
    id    TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    kind  TEXT NOT NULL,
    name  TEXT NULL
);

CREATE TABLE action_cutscene (
    action_id   TEXT PRIMARY KEY,
    resource_id TEXT NOT NULL,
    FOREIGN KEY (action_id) REFERENCES action(id) ON DELETE CASCADE,
    FOREIGN KEY (resource_id) REFERENCES resource(id) ON DELETE RESTRICT
);

CREATE TABLE card_action (
    card_id    TEXT NOT NULL,
    ordinal    INTEGER NOT NULL CHECK (ordinal >= 0),
    action_id  TEXT NOT NULL,
    PRIMARY KEY (card_id, ordinal),
    FOREIGN KEY (card_id) REFERENCES card(id) ON DELETE CASCADE,
    FOREIGN KEY (action_id) REFERENCES action(id) ON DELETE RESTRICT
);

CREATE TABLE card_deck (
    id     TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    title  TEXT NULL
);

CREATE TABLE card_deck_card (
    deck_id   TEXT NOT NULL,
    ordinal   INTEGER NOT NULL CHECK (ordinal >= 0),
    card_id   TEXT NOT NULL,
    PRIMARY KEY (deck_id, ordinal),
    FOREIGN KEY (deck_id) REFERENCES card_deck(id) ON DELETE CASCADE,
    FOREIGN KEY (card_id) REFERENCES card(id) ON DELETE RESTRICT
);

-- Host extension tables are intentionally not part of core migration 1.
-- Example future Unity table:
--
-- CREATE TABLE unity_cutscene_binding (
--   resource_id   TEXT PRIMARY KEY,
--   asset_guid    TEXT NOT NULL,
--   local_file_id INTEGER NULL,
--   FOREIGN KEY(resource_id) REFERENCES resource(id) ON DELETE CASCADE
-- );
