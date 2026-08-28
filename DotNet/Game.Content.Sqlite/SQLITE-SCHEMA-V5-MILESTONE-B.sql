-- Core schema v5 (milestone-b-cards-profile-selection).
--
-- Implements Docs/MilestoneB/02-schema-audit.md:
--  - Card/Kink/Equipment/SmartToyCapability catalogs + CardTagDefinition;
--  - Card relations (kinks, required equipment, required capabilities) and
--    card.body_text; Card tags re-point at card_tag_definition;
--  - SessionType sort_order + all-required capability join;
--  - Phase Card query tables (ALL/ANY, include-only);
--  - Session weighting six-pack (defaults 1.0, nonnegative);
--  - drops the superseded deck/tag/legacy-action structures AFTER the
--    in-transaction copy (see Migration5Transform) inside the same
--    transaction, so a failure rolls back atomically.
--
-- Posture: pre-production content; the drops are deliberate (user-approved
-- 2026-08-28): phases have no classification tags anymore, decks are
-- replaced by phase card queries, v1 action tables were already unread.
-- Unknown host tables (unity_*, wpf_*) are never touched.

-- ---- catalogs ----

CREATE TABLE IF NOT EXISTS card_tag_definition (
    id         TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    title      TEXT NOT NULL UNIQUE,
    sort_order INTEGER NOT NULL DEFAULT 0 CHECK (sort_order >= 0)
);

CREATE TABLE IF NOT EXISTS kink_definition (
    id          TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    title       TEXT NOT NULL UNIQUE,
    description TEXT NULL,
    sort_order  INTEGER NOT NULL DEFAULT 0 CHECK (sort_order >= 0)
);

CREATE TABLE IF NOT EXISTS equipment_definition (
    id         TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    title      TEXT NOT NULL UNIQUE,
    category   TEXT NOT NULL DEFAULT '',
    sort_order INTEGER NOT NULL DEFAULT 0 CHECK (sort_order >= 0)
);

CREATE TABLE IF NOT EXISTS smart_toy_capability_definition (
    id         TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    title      TEXT NOT NULL UNIQUE,
    category   TEXT NOT NULL DEFAULT '',
    sort_order INTEGER NOT NULL DEFAULT 0 CHECK (sort_order >= 0)
);

-- ---- SessionType completion ----

ALTER TABLE session_type ADD COLUMN sort_order INTEGER NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS session_type_required_smart_toy_capability (
    session_type_id  TEXT NOT NULL,
    capability_id    TEXT NOT NULL,
    ordinal          INTEGER NOT NULL CHECK (ordinal >= 0),
    PRIMARY KEY (session_type_id, capability_id),
    UNIQUE (session_type_id, ordinal),
    FOREIGN KEY (session_type_id) REFERENCES session_type(id) ON DELETE CASCADE,
    FOREIGN KEY (capability_id) REFERENCES smart_toy_capability_definition(id) ON DELETE RESTRICT
);

-- ---- Card body + relations ----

ALTER TABLE card ADD COLUMN body_text TEXT NOT NULL DEFAULT '';

-- card_tag is rebuilt against card_tag_definition by Migration5Transform
-- (copy old assignments, then drop the legacy table). This CREATE only
-- produces rows on fresh databases without v1 history.
CREATE TABLE IF NOT EXISTS card_tag_v5 (
    card_id   TEXT NOT NULL,
    tag_id    TEXT NOT NULL,
    ordinal   INTEGER NOT NULL CHECK (ordinal >= 0),
    PRIMARY KEY (card_id, tag_id),
    UNIQUE (card_id, ordinal),
    FOREIGN KEY (card_id) REFERENCES card(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES card_tag_definition(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS card_kink (
    card_id   TEXT NOT NULL,
    kink_id   TEXT NOT NULL,
    ordinal   INTEGER NOT NULL CHECK (ordinal >= 0),
    PRIMARY KEY (card_id, kink_id),
    UNIQUE (card_id, ordinal),
    FOREIGN KEY (card_id) REFERENCES card(id) ON DELETE CASCADE,
    FOREIGN KEY (kink_id) REFERENCES kink_definition(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS card_required_equipment (
    card_id       TEXT NOT NULL,
    equipment_id  TEXT NOT NULL,
    ordinal       INTEGER NOT NULL CHECK (ordinal >= 0),
    PRIMARY KEY (card_id, equipment_id),
    UNIQUE (card_id, ordinal),
    FOREIGN KEY (card_id) REFERENCES card(id) ON DELETE CASCADE,
    FOREIGN KEY (equipment_id) REFERENCES equipment_definition(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS card_required_smart_toy_capability (
    card_id         TEXT NOT NULL,
    capability_id   TEXT NOT NULL,
    ordinal         INTEGER NOT NULL CHECK (ordinal >= 0),
    PRIMARY KEY (card_id, capability_id),
    UNIQUE (card_id, ordinal),
    FOREIGN KEY (card_id) REFERENCES card(id) ON DELETE CASCADE,
    FOREIGN KEY (capability_id) REFERENCES smart_toy_capability_definition(id) ON DELETE RESTRICT
);

-- ---- Phase Card query (include-only) ----

CREATE TABLE IF NOT EXISTS phase_card_all_tag (
    phase_id  TEXT NOT NULL,
    tag_id    TEXT NOT NULL,
    ordinal   INTEGER NOT NULL CHECK (ordinal >= 0),
    PRIMARY KEY (phase_id, tag_id),
    UNIQUE (phase_id, ordinal),
    FOREIGN KEY (phase_id) REFERENCES phase(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES card_tag_definition(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS phase_card_any_tag (
    phase_id  TEXT NOT NULL,
    tag_id    TEXT NOT NULL,
    ordinal   INTEGER NOT NULL CHECK (ordinal >= 0),
    PRIMARY KEY (phase_id, tag_id),
    UNIQUE (phase_id, ordinal),
    FOREIGN KEY (phase_id) REFERENCES phase(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES card_tag_definition(id) ON DELETE RESTRICT
);

-- ---- Session weighting ----

CREATE TABLE IF NOT EXISTS session_card_weighting (
    session_id               TEXT PRIMARY KEY,
    love_base                REAL NOT NULL DEFAULT 1.0 CHECK (love_base >= 0),
    love_happiness_gain      REAL NOT NULL DEFAULT 1.0 CHECK (love_happiness_gain >= 0),
    like_base                REAL NOT NULL DEFAULT 1.0 CHECK (like_base >= 0),
    like_happiness_gain      REAL NOT NULL DEFAULT 1.0 CHECK (like_happiness_gain >= 0),
    torture_base             REAL NOT NULL DEFAULT 1.0 CHECK (torture_base >= 0),
    torture_unhappiness_gain REAL NOT NULL DEFAULT 1.0 CHECK (torture_unhappiness_gain >= 0),
    FOREIGN KEY (session_id) REFERENCES session(id) ON DELETE CASCADE
);
