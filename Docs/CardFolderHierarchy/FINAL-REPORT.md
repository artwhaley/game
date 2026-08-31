# Cards Folder Hierarchy

## Delivered

- Added schema v9 `card_folder` rows with stable IDs, parent IDs, names,
  persisted paths, and sort order.
- Migrated any existing slash-delimited `card.folder_path` values into a
  complete parent/child hierarchy. Existing card paths remain compatible.
- Replaced the Cards pane's flat folder filter with a tree rooted at **All
  Cards**.
- Added **New Folder**. The selected folder becomes the parent for a new
  folder, and a selected folder remains the destination for **New Card**.
- Cards use extended selection. Dragging one card or a group of selected cards
  onto any folder moves them in one transactional, undoable command.
- Added folder rename and guarded folder deletion through the folder context
  menu. Rename updates descendant folder paths and card paths; deletion is
  rejected while children or cards remain.

## Corrective pass (2026-08-30, evening)

An earlier draft of this report claimed full verification while the WPF
project did not compile (a `DataTrigger` placed directly inside a `Style`).
That claim was wrong. The corrective pass fixed:

- The XAML compile error above.
- A startup crash: `Click` handlers on MenuItems inside the TreeViewItem
  style's ContextMenu (deferred style content) corrupted the generated
  connection ids, leaving `CatalogsLibraryPanel` null — one null causing both
  the "Failed to load content" dialog at startup and a hard crash when
  opening the Cards tab. The menu is now declared on the TreeView itself,
  with per-node visibility tailored in `OnCardTreeContextMenuOpening`.
- Expansion state is now preserved across tree rebuilds (snapshot/restore of
  expanded folder ids; the root expands on first build only).
- Selection is explorer-style: plain-clicking an already-selected card keeps
  the selection so group drags can start from any member; Ctrl+click toggles
  membership; native double-click rename works on cards.
- Tree selection uses the dark accent color, matching the rest of the
  Workbench.
- Test drift: the two layout tests referencing the removed flat `CardList`
  ListBox were updated to the tree (context-menu presence, double-click
  rename through `OnCardTreeDoubleClick`).

## Follow-up pass (same evening): folder delete + row cards

- Folder delete is context-aware. The toolbar Delete button deletes the
  selected folder when a folder (not a card) has focus; the folder context
  menu entry uses the same path. Non-empty folders open a three-way choice
  (`CardFolderDeleteDialog`): delete the folder tree **and its cards**, or
  delete the folder tree **keeping the cards** (relocated to UNASSIGNED).
  Both outcomes run through `DeleteCardFolderTreeCommand` and are fully
  undoable — cascade undo restores the folder rows and every card verbatim,
  including relations and owned action sequences; keep-cards undo restores
  the folders and moves the surviving cards back to their original folders.
- Subtree delete goes deepest-first (the `parent_id` FK is ON DELETE
  RESTRICT, so children must be deleted before parents); restore goes
  shallowest-first for the mirror reason.
- `CardRepository.Create`/`Delete` gained ambient-transaction overloads so
  the subtree operation is one atomic transaction. The default
  wait+progress pair now applies only to cards that arrive with no authored
  instances; authored sequences are persisted verbatim.
- Tree rows are full-width UI cards: a custom `TreeViewItem` template
  stretches each row across the whole pane (the entire surface is clickable
  and a drop target), folders show a gold folder glyph plus a recursive
  card-count badge, cards show a card glyph, and UNASSIGNED/All Cards get
  distinct styling. Vertical growth is ~4px per row (24px MinHeight).

## Follow-up pass (same evening): multi-select batch operations + ranges

- Ctrl+click toggles cards AND real folders into one mixed selection; plain
  click resets to single; the virtual roots (All Cards / UNASSIGNED) never
  join a batch. Batch members highlight through the node-level `IsSelected`
  DataTrigger, which survives tree rebuilds.
- **Shift+click** selects a range of cards in visual tree order from the
  anchor (the last plain- or Ctrl-clicked card). A new Shift+click replaces
  the previous range; Ctrl-toggled members outside the range stay selected.
  The anchor is recomputed on plain and Ctrl clicks and cleared when stale
  (e.g. after deletion). A null-anchor guard on first build fixed a
  startup "Failed to load content: Value cannot be null (key)" regression
  introduced by this feature.
- **Batch drag** (`MoveCardSelectionCommand`): selected cards and entire
  folder subtrees move to one destination in a single undoable transaction.
  Undo restores each subtree's original parent and path and returns cards to
  their original folders. Dropping a folder onto itself or into one of its
  own descendants fails loudly instead of corrupting the tree.
- **Batch delete** (`DeleteCardSelectionCommand`): one confirmation for the
  batch. Explicitly selected cards always delete; the cascade-vs-keep choice
  (single `CardFolderDeleteDialog` in batch mode) applies to every selected
  folder. Undo restores everything verbatim, including authored action
  sequences.
- **Batch duplicate** (`DuplicateCardSelectionCommand`): every selected card
  and folder subtree deep-clones with fresh ids; folders land beside their
  originals as "Name (copy)" (with numeric disambiguation on collision) and
  contained cards follow into the copied folders. After the duplication the
  UI deselects the originals and selects the duplicates, per the acceptance
  criterion. Undo removes every copy.
- Supporting fixes: `CardRepository.Duplicate` gained an ambient-transaction
  overload; batch commands compose subtree operations inside one
  transaction.

## Verification

- SQLite authoring suite: 145 passed (includes batch move/undo, descendant
  drop rejection, batch delete/undo, batch duplicate/undo with subtree and
  title-suffix coverage).
- WPF suite: 41 passed. Core suite: 174 passed. Profile suite: 11 passed.
- The rebuilt Workbench is running for human evaluation of multi-select
  drag, delete, and duplicate flows.
- Canonical `Content/GameContent.db` was already schema v9 and was not
  modified by this pass.
