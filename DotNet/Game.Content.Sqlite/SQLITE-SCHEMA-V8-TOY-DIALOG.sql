-- Schema v8: Toy Pattern Resources, Set Toy Pattern, Dialog Tag/Snippet
-- catalogs, and DialogFromTags action subtype.
--
-- Additive DDL only. The legacy intensity-based action_instance_toy_activity
-- rebuild (intensity -> pattern_resource_id) is a data transformation done by
-- Migration8Transform inside the same transaction (same pattern as v5), so
-- the legacy rows can still be read when the transform runs.

-- ---- Persistent toy subtype: Set Toy Pattern ----

CREATE TABLE IF NOT EXISTS action_instance_toy_set_pattern (
    action_instance_id  TEXT PRIMARY KEY,
    capability_id       TEXT NOT NULL,
    pattern_resource_id TEXT NOT NULL,
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE,
    FOREIGN KEY (capability_id) REFERENCES smart_toy_capability_definition(id) ON DELETE RESTRICT,
    FOREIGN KEY (pattern_resource_id) REFERENCES resource(id) ON DELETE RESTRICT
);

-- ---- Dialog Tag catalog ----

CREATE TABLE IF NOT EXISTS dialog_tag_definition (
    id         TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    title      TEXT NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0
);

-- ---- Dialog Snippet catalog ----

CREATE TABLE IF NOT EXISTS dialog_snippet (
    id         TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    name       TEXT NOT NULL,
    text       TEXT NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS dialog_snippet_tag (
    dialog_snippet_id TEXT NOT NULL,
    dialog_tag_id     TEXT NOT NULL,
    ordinal           INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (dialog_snippet_id, dialog_tag_id),
    FOREIGN KEY (dialog_snippet_id) REFERENCES dialog_snippet(id) ON DELETE CASCADE,
    FOREIGN KEY (dialog_tag_id) REFERENCES dialog_tag_definition(id) ON DELETE RESTRICT
);

-- ---- DialogFromTags action subtype ----

CREATE TABLE IF NOT EXISTS action_instance_dialog_from_tags (
    action_instance_id TEXT PRIMARY KEY,
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS action_instance_dialog_from_tag (
    action_instance_id TEXT NOT NULL,
    dialog_tag_id      TEXT NOT NULL,
    ordinal            INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (action_instance_id, dialog_tag_id),
    FOREIGN KEY (action_instance_id) REFERENCES action_instance_dialog_from_tags(action_instance_id) ON DELETE CASCADE,
    FOREIGN KEY (dialog_tag_id) REFERENCES dialog_tag_definition(id) ON DELETE RESTRICT
);
