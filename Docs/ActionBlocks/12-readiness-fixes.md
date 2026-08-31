# Action Blocks Readiness Fixes

This corrective pass keeps Action Blocks editor-only and makes schema v10 authoritative. Repository CRUD now requires the migration-created table instead of issuing DDL. The WPF load boundary runs connection initialization and CoreMigrator.EnsureSchema before content/repository access.

Block insertion now uses the portable content-reference validator for detached fragments, adds the missing DialogFromTags cardinality check, and enforces Set Toy Pattern as always nonblocking in the shared registry, serializer, SQLite writer, and loader.

The chooser no longer returns the hidden Blocks-browser selection for row context-menu commands. Multiple blocks always reach the chooser, zero blocks report "Create an Action Block first.", and every shared sequence has a + Block append path. Post-insert selection recursively finds root, decision-option, and PromptChoice descendant editors, including dirty Card buffers.

Automated regression coverage includes same-Phase and cross-Phase GOTO validation, stale reference/cardinality/invariant rejection, unmigrated repository failure, SessionGoto socket identity and edge isolation, undo/redo, recursive editor selection, and dirty nested Card-buffer rebuilds. Human acceptance remains a manual canary.
