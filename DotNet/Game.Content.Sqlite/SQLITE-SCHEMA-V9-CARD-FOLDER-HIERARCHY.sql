-- Schema v9: persisted Cards library folder hierarchy.
-- card.folder_path remains the portable card-facing path for compatibility;
-- card_folder preserves empty folders and parent/child relationships.
CREATE TABLE IF NOT EXISTS card_folder (
    id TEXT PRIMARY KEY,
    parent_id TEXT NULL,
    name TEXT NOT NULL,
    path TEXT NOT NULL UNIQUE,
    sort_order INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(parent_id) REFERENCES card_folder(id) ON DELETE RESTRICT,
    CHECK(length(trim(name)) > 0),
    CHECK(name NOT LIKE '%/%'),
    CHECK(name NOT LIKE '%\\%')
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_card_folder_path_nocase ON card_folder(path COLLATE NOCASE);
CREATE UNIQUE INDEX IF NOT EXISTS ux_card_folder_parent_name_nocase ON card_folder(COALESCE(parent_id, ''), name COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS idx_card_folder_parent_order ON card_folder(parent_id, sort_order, name);
