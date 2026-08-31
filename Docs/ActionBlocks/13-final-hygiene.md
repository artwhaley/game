# Action Blocks Final Hygiene

This pass keeps Action Blocks editor-only and leaves the schema version at v10.
Insertion now allocates a full-strength stable namespace and derives every
cloned action, PromptChoice option sequence, nested sequence, and projected
SessionGoto port beneath that namespace. Repeated insertion cannot overlap,
and undo/redo restores the original allocated IDs.

Empty blocks are rejected both by the WPF “Save Entire Sequence as Action
Block” command and by serializer/repository validation. A block row, its JSON
template, and `ActionBlockSerializer.CurrentFormatVersion` must agree; current
and future unsupported versions fail loudly instead of being normalized.

The modal insert chooser supports live case-insensitive search, keyboard
navigation, Enter, double-click, and disabled Insert for zero results. It does
not depend on the persistent browser selection. The persistent Action Blocks
browser also supports searchable slash-delimited folder paths: create a folder
path, filter by it, and move the selected block into or out of that path. The
folder path is stored in the editor-only template JSON, so this adds
organization without changing runtime semantics or the schema version.

Final verification covers the complete Core, SQLite, Profile, and WPF suites,
the WPF build, and canonical SQLite integrity/foreign-key checks. No canonical
content was intentionally authored or migrated by this pass.
