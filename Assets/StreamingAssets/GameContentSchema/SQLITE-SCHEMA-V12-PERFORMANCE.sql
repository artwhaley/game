-- Schema v12: Conversation Performance V1.
--
-- Adds the WPF/SQLite-owned semantic Performance Tag catalog, reusable
-- Conversation Performance Events (ALL/ANY tag query, staging policy, optional
-- anchor/posture restrictions, automatic dialogue refresh), and the Perform
-- action subtype. Additive DDL only; there is no data transform.
--
-- Anchor and posture identifiers are Unity-owned presentation vocabulary and
-- deliberately carry no foreign key here; Core resolves them against the
-- generated PresentationCatalog at run start.

CREATE TABLE IF NOT EXISTS performance_tag_definition (
    id         TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    title      TEXT NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0,
    -- A retired tag keeps identity and stays resolvable for existing content;
    -- it is hidden from new selections.
    is_retired INTEGER NOT NULL DEFAULT 0 CHECK (is_retired IN (0, 1))
);

CREATE TABLE IF NOT EXISTS conversation_performance_event (
    id                        TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    name                      TEXT NOT NULL,
    -- 1 = the ingredient must carry every listed tag; 0 = at least one.
    require_all_tags          INTEGER NOT NULL DEFAULT 1 CHECK (require_all_tags IN (0, 1)),
    -- PerformanceStagingPolicy: 0 Stay, 1 ChooseCompatible, 2 DifferentLocation, 3 NamedLocation.
    staging_policy            INTEGER NOT NULL DEFAULT 0,
    named_anchor_id           TEXT,
    refresh_at_dialogue_start INTEGER NOT NULL DEFAULT 1 CHECK (refresh_at_dialogue_start IN (0, 1)),
    sort_order                INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS performance_event_tag (
    event_id           TEXT NOT NULL,
    performance_tag_id TEXT NOT NULL,
    ordinal            INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (event_id, performance_tag_id),
    FOREIGN KEY (event_id) REFERENCES conversation_performance_event(id) ON DELETE CASCADE,
    FOREIGN KEY (performance_tag_id) REFERENCES performance_tag_definition(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS performance_event_allowed_anchor (
    event_id  TEXT NOT NULL,
    anchor_id TEXT NOT NULL,
    ordinal   INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (event_id, anchor_id),
    FOREIGN KEY (event_id) REFERENCES conversation_performance_event(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS performance_event_allowed_posture (
    event_id   TEXT NOT NULL,
    posture_id TEXT NOT NULL,
    ordinal    INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (event_id, posture_id),
    FOREIGN KEY (event_id) REFERENCES conversation_performance_event(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS action_instance_perform (
    action_instance_id TEXT PRIMARY KEY,
    event_id           TEXT NOT NULL,
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE,
    FOREIGN KEY (event_id) REFERENCES conversation_performance_event(id) ON DELETE RESTRICT
);
