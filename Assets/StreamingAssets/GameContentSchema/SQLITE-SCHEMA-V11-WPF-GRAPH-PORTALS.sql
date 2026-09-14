-- Migration 11: WPF-only bridge/portal pairs for Session and Phase graph edges.
-- These rows are presentation metadata. They are deliberately not loaded into
-- GameContentDefinition and cannot become runtime graph nodes.

CREATE TABLE IF NOT EXISTS wpf_session_edge_portal_pair (
    id         TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    session_id TEXT NOT NULL,
    edge_id    TEXT NOT NULL UNIQUE,
    label      TEXT NOT NULL CHECK (length(trim(label)) > 0),
    color_slot INTEGER NOT NULL CHECK (color_slot BETWEEN 0 AND 7),
    source_x   REAL NOT NULL,
    source_y   REAL NOT NULL,
    target_x   REAL NOT NULL,
    target_y   REAL NOT NULL,
    UNIQUE (session_id, label),
    FOREIGN KEY (session_id) REFERENCES session(id) ON DELETE CASCADE,
    FOREIGN KEY (edge_id) REFERENCES session_graph_edge(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_wpf_session_portal_session
    ON wpf_session_edge_portal_pair(session_id);

CREATE TRIGGER IF NOT EXISTS trg_wpf_session_portal_owner_insert
BEFORE INSERT ON wpf_session_edge_portal_pair
BEGIN
    SELECT CASE WHEN NOT EXISTS (
        SELECT 1 FROM session_graph_edge
        WHERE id = NEW.edge_id AND session_id = NEW.session_id
    ) THEN RAISE(ABORT, 'Session portal edge does not belong to its session') END;
END;

CREATE TRIGGER IF NOT EXISTS trg_wpf_session_portal_owner_update
BEFORE UPDATE OF session_id, edge_id ON wpf_session_edge_portal_pair
BEGIN
    SELECT CASE WHEN NOT EXISTS (
        SELECT 1 FROM session_graph_edge
        WHERE id = NEW.edge_id AND session_id = NEW.session_id
    ) THEN RAISE(ABORT, 'Session portal edge does not belong to its session') END;
END;

CREATE TABLE IF NOT EXISTS wpf_phase_edge_portal_pair (
    id         TEXT PRIMARY KEY CHECK (length(trim(id)) > 0),
    phase_id   TEXT NOT NULL,
    edge_id    TEXT NOT NULL UNIQUE,
    label      TEXT NOT NULL CHECK (length(trim(label)) > 0),
    color_slot INTEGER NOT NULL CHECK (color_slot BETWEEN 0 AND 7),
    source_x   REAL NOT NULL,
    source_y   REAL NOT NULL,
    target_x   REAL NOT NULL,
    target_y   REAL NOT NULL,
    UNIQUE (phase_id, label),
    FOREIGN KEY (phase_id) REFERENCES phase(id) ON DELETE CASCADE,
    FOREIGN KEY (edge_id) REFERENCES phase_graph_edge(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_wpf_phase_portal_phase
    ON wpf_phase_edge_portal_pair(phase_id);

CREATE TRIGGER IF NOT EXISTS trg_wpf_phase_portal_owner_insert
BEFORE INSERT ON wpf_phase_edge_portal_pair
BEGIN
    SELECT CASE WHEN NOT EXISTS (
        SELECT 1 FROM phase_graph_edge
        WHERE id = NEW.edge_id AND phase_id = NEW.phase_id
    ) THEN RAISE(ABORT, 'Phase portal edge does not belong to its phase') END;
END;

CREATE TRIGGER IF NOT EXISTS trg_wpf_phase_portal_owner_update
BEFORE UPDATE OF phase_id, edge_id ON wpf_phase_edge_portal_pair
BEGIN
    SELECT CASE WHEN NOT EXISTS (
        SELECT 1 FROM phase_graph_edge
        WHERE id = NEW.edge_id AND phase_id = NEW.phase_id
    ) THEN RAISE(ABORT, 'Phase portal edge does not belong to its phase') END;
END;
