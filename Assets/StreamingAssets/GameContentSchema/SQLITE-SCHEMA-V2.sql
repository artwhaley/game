-- Core schema v2 (core-graph-schema-v2).
--
-- Implements the Graph Workbench schema contract (Docs/GraphWorkbench/
-- 03-schema-v2-design.md). Companion C# (Migration2Transform) runs in the SAME
-- transaction: it seeds session types / temperature definitions, transforms the
-- legacy sample content into graph rows, and clears stale PhaseSlot rows so
-- phase deletion is no longer blocked by RESTRICT candidates of dead sessions.
--
-- Posture: pre-production content; obsolete v1 structures stay physically
-- present but unused (phase.min_cards/max_columns columns, phase_slot*,
-- top-level action*, card_action, choice_option). They are never read or
-- written by v2 code and can be dropped by a later cleanup migration once no
-- unknown host extension references them. Unknown host tables are never touched.

CREATE TABLE IF NOT EXISTS session_type (
    id     TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    title  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS temperature_definition (
    id             TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    title          TEXT NOT NULL,
    min_value      REAL NOT NULL,
    max_value      REAL NOT NULL,
    default_value  REAL NOT NULL
);

-- Session gains its type reference. Nullable during migration; Migration2Transform
-- assigns every existing session to a seeded type, after which v2 writers always
-- supply one. (SQLite ALTER TABLE cannot add a NOT NULL/FK column in place.)
-- Reference integrity for this column and card.action_sequence_id below is
-- enforced by v2 repositories/loader plus constraint tests, not by physical FK;
-- a later cleanup migration may rebuild these two tables for physical FKs.
ALTER TABLE session ADD COLUMN session_type_id TEXT NULL;
CREATE INDEX IF NOT EXISTS idx_session_session_type ON session(session_type_id);

CREATE TABLE IF NOT EXISTS action_sequence (
    id  TEXT PRIMARY KEY CHECK (length(trim(id)) > 0)
);

CREATE TABLE IF NOT EXISTS action_instance (
    id                  TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    action_sequence_id  TEXT NOT NULL,
    ordinal             INTEGER NOT NULL CHECK (ordinal >= 0),
    action_type         TEXT NOT NULL,
    is_blocking         INTEGER NOT NULL CHECK (is_blocking IN (0,1)),
    UNIQUE (action_sequence_id, ordinal),
    FOREIGN KEY (action_sequence_id) REFERENCES action_sequence(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS action_instance_debug (
    action_instance_id  TEXT PRIMARY KEY,
    message             TEXT NULL,
    delay_seconds       REAL NULL,
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS action_instance_stat_increase (
    action_instance_id  TEXT PRIMARY KEY,
    stat_key            TEXT NOT NULL,
    amount              REAL NOT NULL,
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS action_instance_increment_progress (
    action_instance_id  TEXT PRIMARY KEY,
    amount              REAL NOT NULL,
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS action_instance_modify_temperature (
    action_instance_id  TEXT PRIMARY KEY,
    temperature_id      TEXT NOT NULL,
    amount              REAL NOT NULL,
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE,
    FOREIGN KEY (temperature_id) REFERENCES temperature_definition(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS action_instance_cutscene (
    action_instance_id  TEXT PRIMARY KEY,
    resource_id         TEXT NOT NULL,
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE,
    FOREIGN KEY (resource_id) REFERENCES resource(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS action_instance_prompt_choice (
    action_instance_id  TEXT PRIMARY KEY,
    prompt              TEXT NOT NULL,
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS action_instance_choice_option (
    id                  TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    action_instance_id  TEXT NOT NULL,
    ordinal             INTEGER NOT NULL CHECK (ordinal >= 0),
    label               TEXT NOT NULL,
    action_sequence_id  TEXT NOT NULL,
    UNIQUE (action_instance_id, ordinal),
    FOREIGN KEY (action_instance_id) REFERENCES action_instance_prompt_choice(action_instance_id) ON DELETE CASCADE,
    FOREIGN KEY (action_sequence_id) REFERENCES action_sequence(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS action_instance_phase_goto (
    action_instance_id  TEXT PRIMARY KEY,
    phase_exit_id       TEXT NOT NULL,
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE,
    FOREIGN KEY (phase_exit_id) REFERENCES phase_exit(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS action_instance_session_goto (
    action_instance_id  TEXT PRIMARY KEY,
    label               TEXT NOT NULL DEFAULT '',
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS action_instance_return (
    action_instance_id  TEXT PRIMARY KEY,
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS action_instance_end_session (
    action_instance_id  TEXT PRIMARY KEY,
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE
);

-- Cards own exactly one sequence (their ordered Action Instances).
ALTER TABLE card ADD COLUMN action_sequence_id TEXT NULL;

CREATE TABLE IF NOT EXISTS phase_exit (
    id        TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    phase_id  TEXT NOT NULL,
    ordinal   INTEGER NOT NULL CHECK (ordinal >= 0),
    name      TEXT NOT NULL,
    UNIQUE (phase_id, ordinal),
    FOREIGN KEY (phase_id) REFERENCES phase(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS phase_graph_node (
    id        TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    phase_id  TEXT NOT NULL,
    node_type TEXT NOT NULL,
    FOREIGN KEY (phase_id) REFERENCES phase(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS phase_node_variable_check (
    node_id          TEXT PRIMARY KEY,
    source_kind      TEXT NOT NULL,
    variable_key     TEXT NULL,
    compare_operator TEXT NOT NULL,
    compare_value    REAL NOT NULL,
    FOREIGN KEY (node_id) REFERENCES phase_graph_node(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS phase_node_action (
    node_id             TEXT PRIMARY KEY,
    action_sequence_id  TEXT NOT NULL,
    FOREIGN KEY (node_id) REFERENCES phase_graph_node(id) ON DELETE CASCADE,
    FOREIGN KEY (action_sequence_id) REFERENCES action_sequence(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS phase_node_decision (
    node_id  TEXT PRIMARY KEY,
    prompt   TEXT NOT NULL,
    FOREIGN KEY (node_id) REFERENCES phase_graph_node(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS phase_decision_option (
    id                  TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    node_id             TEXT NOT NULL,
    ordinal             INTEGER NOT NULL CHECK (ordinal >= 0),
    label               TEXT NOT NULL,
    action_sequence_id  TEXT NOT NULL,
    UNIQUE (node_id, ordinal),
    FOREIGN KEY (node_id) REFERENCES phase_node_decision(node_id) ON DELETE CASCADE,
    FOREIGN KEY (action_sequence_id) REFERENCES action_sequence(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS phase_node_output (
    id       TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    node_id  TEXT NOT NULL,
    port_kind TEXT NOT NULL,
    ordinal   INTEGER NOT NULL CHECK (ordinal >= 0),
    label     TEXT NULL,
    FOREIGN KEY (node_id) REFERENCES phase_graph_node(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_phase_node_output_node ON phase_node_output(node_id);

CREATE TABLE IF NOT EXISTS phase_graph_edge (
    id              TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    phase_id        TEXT NOT NULL,
    source_port_id  TEXT NOT NULL,
    target_node_id  TEXT NOT NULL,
    UNIQUE (source_port_id),
    FOREIGN KEY (phase_id) REFERENCES phase(id) ON DELETE CASCADE,
    FOREIGN KEY (source_port_id) REFERENCES phase_node_output(id) ON DELETE CASCADE,
    FOREIGN KEY (target_node_id) REFERENCES phase_graph_node(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_phase_graph_edge_phase ON phase_graph_edge(phase_id);

CREATE TABLE IF NOT EXISTS session_graph_node (
    id          TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    session_id  TEXT NOT NULL,
    node_type   TEXT NOT NULL,
    FOREIGN KEY (session_id) REFERENCES session(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS session_node_phase (
    node_id   TEXT PRIMARY KEY,
    phase_id  TEXT NOT NULL,
    FOREIGN KEY (node_id) REFERENCES session_graph_node(id) ON DELETE CASCADE,
    FOREIGN KEY (phase_id) REFERENCES phase(id) ON DELETE RESTRICT
);

CREATE TABLE IF NOT EXISTS session_node_decision (
    node_id  TEXT PRIMARY KEY,
    prompt   TEXT NOT NULL,
    FOREIGN KEY (node_id) REFERENCES session_graph_node(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS session_decision_option (
    id                  TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    node_id             TEXT NOT NULL,
    ordinal             INTEGER NOT NULL CHECK (ordinal >= 0),
    label               TEXT NOT NULL,
    action_sequence_id  TEXT NOT NULL,
    UNIQUE (node_id, ordinal),
    FOREIGN KEY (node_id) REFERENCES session_node_decision(node_id) ON DELETE CASCADE,
    FOREIGN KEY (action_sequence_id) REFERENCES action_sequence(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS session_node_output (
    id                              TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    node_id                         TEXT NOT NULL,
    port_kind                       TEXT NOT NULL,
    ordinal                         INTEGER NOT NULL CHECK (ordinal >= 0),
    label                           TEXT NULL,
    phase_exit_id                   TEXT NULL,
    session_goto_action_instance_id TEXT NULL,
    FOREIGN KEY (node_id) REFERENCES session_graph_node(id) ON DELETE CASCADE,
    FOREIGN KEY (phase_exit_id) REFERENCES phase_exit(id) ON DELETE CASCADE,
    FOREIGN KEY (session_goto_action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_session_node_output_node ON session_node_output(node_id);

CREATE TABLE IF NOT EXISTS session_graph_edge (
    id              TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    session_id      TEXT NOT NULL,
    source_port_id  TEXT NOT NULL,
    target_node_id  TEXT NOT NULL,
    UNIQUE (source_port_id),
    FOREIGN KEY (session_id) REFERENCES session(id) ON DELETE CASCADE,
    FOREIGN KEY (source_port_id) REFERENCES session_node_output(id) ON DELETE CASCADE,
    FOREIGN KEY (target_node_id) REFERENCES session_graph_node(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_session_graph_edge_session ON session_graph_edge(session_id);
