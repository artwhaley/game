# Ticket 09 — Undo/Redo

Save, rename, delete, and graph insertion are undoable authoring commands. SessionDecision option insertion removes/recreates only the inserted projected sockets on undo/redo and never copies edges.

Card insertion is an in-memory compound edit; undo/redo refreshes the Card editor without reloading from SQLite, preserving unsaved Card changes. Graph-owned edits continue to use transactional database reloads.
