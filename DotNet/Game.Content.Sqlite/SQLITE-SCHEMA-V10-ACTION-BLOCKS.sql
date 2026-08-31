-- Editor-only reusable Action Block templates. Runtime loaders intentionally
-- ignore this table; insertion materializes ordinary Action Instances.
CREATE TABLE IF NOT EXISTS wpf_action_block (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL UNIQUE COLLATE NOCASE,
  format_version INTEGER NOT NULL,
  template_json TEXT NOT NULL,
  sort_order INTEGER NOT NULL DEFAULT 0
);
