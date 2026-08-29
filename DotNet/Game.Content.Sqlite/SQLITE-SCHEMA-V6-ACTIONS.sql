-- Schema v6: Dialog, Delay, and ToyActivity action subtypes.

CREATE TABLE IF NOT EXISTS action_instance_dialog (
    action_instance_id TEXT PRIMARY KEY,
    dialog_text        TEXT NOT NULL DEFAULT '',
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS action_instance_delay (
    action_instance_id TEXT PRIMARY KEY,
    duration_seconds   REAL NOT NULL DEFAULT 0 CHECK (duration_seconds >= 0),
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS action_instance_toy_activity (
    action_instance_id TEXT PRIMARY KEY,
    capability_id      TEXT NOT NULL,
    intensity          REAL NOT NULL DEFAULT 1,
    duration_seconds   REAL NOT NULL DEFAULT 0 CHECK (duration_seconds >= 0),
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE,
    FOREIGN KEY (capability_id) REFERENCES smart_toy_capability_definition(id) ON DELETE RESTRICT
);
