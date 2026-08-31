# Ticket 02 — Persistence

Implemented editor-only `wpf_action_block` persistence in schema v10. Definitions store id, unique name, format version, serialized template JSON, and sort order. CRUD, search, rename, delete, and undoable delete/rename commands are provided by `ActionBlockRepository` and the WPF Blocks browser.

The table is deliberately outside Core/runtime action models. Startup reads invalid templates as browser errors; insertion rejects corrupt or unsupported templates without mutating content.
