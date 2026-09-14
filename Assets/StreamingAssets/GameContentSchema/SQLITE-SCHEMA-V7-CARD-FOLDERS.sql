-- Schema v7: optional searchable authoring folder path on Cards only.
ALTER TABLE card ADD COLUMN folder_path TEXT NOT NULL DEFAULT '';
CREATE INDEX IF NOT EXISTS idx_card_folder_path ON card(folder_path);
