-- Profile schema v1 (Milestone B, Ticket 04).
--
-- User state only — never authored content. Lives in its own database
-- (UserProfile.db) at the per-user app-data location; no cross-database
-- foreign keys by design: content ids are opaque strings here, so replacing
-- GameContent.db never breaks or erases profile rows, and unknown ids are
-- tolerated (rows persist; the UI hides unresolvable ones).
--
-- Missing row semantics:
--   kink_preference        -> Unconfigured
--   equipment_owned        -> not owned
--   capability_available   -> unavailable

CREATE TABLE IF NOT EXISTS profile_schema_migration (
    version     INTEGER PRIMARY KEY,
    name        TEXT NOT NULL,
    applied_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS kink_preference (
    kink_id     TEXT PRIMARY KEY CHECK (length(trim(kink_id)) > 0),
    preference  TEXT NOT NULL CHECK (preference IN ('love', 'like', 'torture', 'dont_consent'))
);

CREATE TABLE IF NOT EXISTS equipment_owned (
    equipment_id TEXT PRIMARY KEY CHECK (length(trim(equipment_id)) > 0)
);

CREATE TABLE IF NOT EXISTS smart_toy_capability_available (
    capability_id TEXT PRIMARY KEY CHECK (length(trim(capability_id)) > 0)
);
