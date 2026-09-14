-- Migration 3: Graph Workbench WPF authoring layout tables (tickets 13-15).
--
-- These are authoring-tool persistence, separate from the core game definition
-- graph (phase_graph_node / session_graph_node). The Core VM never reads them;
-- only the WPF workbench does. They live in core migrations so the canonical DB
-- carries them across environments, but they have wpf_ prefixes and are never
-- part of snapshot content.
--
-- node coordinates are authoring-feedback only; moving a node must never change
-- the authored topology, only the layout.

CREATE TABLE IF NOT EXISTS wpf_session_node_layout (
    session_id TEXT NOT NULL,
    node_id    TEXT NOT NULL,
    x          REAL NOT NULL,
    y          REAL NOT NULL,
    PRIMARY KEY (session_id, node_id),
    FOREIGN KEY (node_id) REFERENCES session_graph_node(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS wpf_phase_node_layout (
    phase_id TEXT NOT NULL,
    node_id  TEXT NOT NULL,
    x        REAL NOT NULL,
    y        REAL NOT NULL,
    PRIMARY KEY (phase_id, node_id),
    FOREIGN KEY (node_id) REFERENCES phase_graph_node(id) ON DELETE CASCADE
);

-- Singleton viewport state per canvas (pan + zoom), one row per session/phase.
CREATE TABLE IF NOT EXISTS wpf_viewport_state (
    scope_kind   TEXT NOT NULL,             -- 'session' | 'phase'
    scope_id     TEXT NOT NULL,
    zoom         REAL NOT NULL DEFAULT 1.0,
    x            REAL NOT NULL DEFAULT 0.0,
    y            REAL NOT NULL DEFAULT 0.0,
    PRIMARY KEY (scope_kind, scope_id)
);