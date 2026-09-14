-- Migration 4: an unassigned PhaseGoto is represented by NULL, never a sentinel.
-- Rebuild only the core-owned child table; host tables are not touched.

CREATE TABLE action_instance_phase_goto_v4 (
    action_instance_id  TEXT PRIMARY KEY,
    phase_exit_id       TEXT NULL,
    FOREIGN KEY (action_instance_id) REFERENCES action_instance(id) ON DELETE CASCADE,
    FOREIGN KEY (phase_exit_id) REFERENCES phase_exit(id) ON DELETE RESTRICT
);

INSERT INTO action_instance_phase_goto_v4 (action_instance_id, phase_exit_id)
SELECT action_instance_id, phase_exit_id
FROM action_instance_phase_goto;

DROP TABLE action_instance_phase_goto;
ALTER TABLE action_instance_phase_goto_v4 RENAME TO action_instance_phase_goto;
