# Ticket 06 — Insertion and Clone Semantics

Blocks can be dropped onto a sequence or a specific action row, and can be inserted before or after a row from its context menu. Insertion is atomic: validation completes before the destination changes. Every action and nested PromptChoice option receives a fresh id; the source block is never edited.

Card insertion operates on the dirty in-memory Card buffer. Graph insertion uses the transactional SQLite command path.
