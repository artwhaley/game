# Ticket 04 — Multi-Select

The shared action editor supports plain click, Ctrl/Cmd-style toggle, and Shift range selection in visual row order. Selection is exposed by stable action ids, so saving a selected subset is deterministic and preserves nested action data.

Inserted rows are selected after the host refreshes. Added rows use the blue selected/ready visual treatment; invalid authored rows retain the red validation treatment.
