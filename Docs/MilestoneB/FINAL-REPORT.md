# Milestone B — Final Report

Date: 2026-08-28. Executor: AI agent (opencode) under the user-approved
execution packet `new tickets/Game_Milestone_B_Cards_Profile_Selection_Execution_Packet/`.

## Starting / final SHA

- Starting: `624b29b` (`graph-workbench-finish`, pushed to origin as backup
  before branching)
- Branch: `milestone-b-cards-profile-selection`
- Final (code complete, pre-human-acceptance): `HEAD` after the docs commit
  in this series — see `git log milestone-b-cards-profile-selection` for the
  exact commit list. Not pushed (per packet rule).

## Migrations

- **Content schema v5** (`milestone-b-cards-profile-selection`): catalogs
  (card tags, kinks, equipment, smart toy capabilities, completed
  session_type with sort_order + required-capability join), card body_text +
  four relation tables, phase card queries (ALL/ANY), session card weighting.
  In-transaction transform copies legacy card tag assignments to new stable
  definitions, seeds default weighting rows, then drops the shared `tag`,
  `session_tag`, `phase_required_tag`, `phase_excluded_tag`, deck, slot, and
  legacy v1 action tables (audit decision, user-approved).
- **Profile schema v1** (`profile-v1`): own ledger, kink_preference,
  equipment_owned, smart_toy_capability_available in a separate
  `UserProfile.db` at `%LocalAppData%/TruthCardGame/`.
- Canonical `Content/GameContent.db` migrated to v5 through the repo
  migrator tool with integrity/foreign-key checks green, committed as a
  separate content checkpoint.

## Projects changed

- `Assets/Scripts/Portable/Game.Content` (catalog definitions, card/phase/
  session model changes; CardDeckDefinition deleted)
- `Assets/Scripts/Portable/Game.Core` (eligibility, weighting, weighted
  selection, session-type eligibility, profile snapshot, engine profile
  plumbing, NextFloat RNG seam)
- `Assets/Scripts/Portable/Game.Profile` (new — portable profile model)
- `DotNet/Game.Profile` + `DotNet/Game.Profile.Sqlite` (new — profile store)
- `DotNet/Game.Profile.Sqlite.Tests` (new)
- `DotNet/Game.Content.Sqlite` (migration 5, transform, repositories,
  card commands, loader v5)
- `DotNet/Game.Content.Sqlite.Tests` (v5 constraint tests, migration tests,
  Milestone B integration gates; v1-era legacy-table suites rewritten for
  their v5 replacements)
- `DotNet/Game.Content.Samples` (v5 sample content)
- `DotNet/Game.Core.Tests` (pipeline + session-type eligibility suites;
  RNG scripting updated for weighted draws)
- `DotNet/Game.ReferenceHost.Wpf` (Catalogs/Cards library modes, Card
  editor, Profile window, weighting editors, phase query editors,
  diagnostics window, Play-by-Type flow)
- `DotNet/Game.ReferenceHost.Wpf.Tests` (Milestone B WPF tests)
- `Game.Workbench.sln` (three new projects)
- `README.md`, `PROJECT-OVERVIEW.md`, `Docs/MilestoneB/*`

## Test totals

- Game.Core.Tests: **135 passed**, 0 failed (22 new pipeline tests, 8 new
  session-type eligibility tests)
- Game.Content.Sqlite.Tests: **123 passed, 1 skipped** (known canonical-DB
  skip), 5 new Milestone B integration tests + rewritten v5 constraint suite
- Game.Profile.Sqlite.Tests: **11 passed** (new)
- Game.ReferenceHost.Wpf.Tests: **10 passed** (5 new)
- **Total: 279 passed, 1 skipped, 0 failed.**

## DB integrity

- `Content/GameContent.db`: `PRAGMA integrity_check` = ok;
  `PRAGMA foreign_key_check` = no rows; ledger at v5; no WAL/SHM leftovers;
  6 card tag definitions carry all 13 card tag assignments (titles
  preserved); both sessions have default weighting rows; `wpf_*` layout
  extensions untouched; no `unity_*` tables existed to preserve beyond the
  tested fake extension path.
- Migration proof: the preserved v1-era fixture upgrades v1→v5 in one run
  with host-extension rows intact (MilestoneBIntegrationTests).

## WPF build

- `dotnet build Game.Workbench.sln` — succeeded, 0 warnings, 0 errors.
- App launch verified twice during development (main window shows, clean
  start, no startup exceptions, clean close with no WAL leftovers).
- Programmatic self-verification through the exact repository/command paths
  the UI uses: 23/23 checks passed (catalog CRUD + usage blocking, card
  create/default-sequence/relations/duplicate/delete, profile persistence at
  the real app-data path).

## Unity status (informational, not gated — per user direction)

- **Not run this milestone.** No Unity editor compile/EditMode/PlayMode was
  executed (the `unity` CLI is not installed on this machine; the user
  explicitly de-prioritized Unity parity for this milestone).
- Compile-compatibility work done: `Game.Profile` source is under
  `Assets/Scripts/Portable/` with a new `Game.Profile.asmdef` (+ generated
  `.meta` files for every new asset, verified no asset lacks a meta);
  `UnityHostAdapters.UnityRandomSource` implements the new `NextFloat` via
  `UnityEngine.Random.Range(float, float)`. The .NET build compiles the same
  physical portable sources Unity sees. Actual editor verification is
  deferred with the user's knowledge.

## Human gates actually completed

- Tickets 11, 12, 13, 14, 15 were converted to **self-verified** gates per
  the user's instruction ("patch in a skip for show-and-tell; keep real
  pauses for what deserves a real look"): automated tests + the
  programmatic authoring round-trip + app launch smoke cover them.
- **Ticket 19 (this gate) is a REAL pause**: the hands-on acceptance list
  below is for the user. No human approval is claimed.

## Remaining known issues

- ~~The PhaseEntry inspector's tag boxes accept tag titles (comma separated);
  unknown titles are dropped silently~~ (still true; a picker-style editor
  remains a Milestone C polish item — FK protects the DB).
- ~~Card relation pickers are multi-select lists of titles; searchable
  add/remove controls are a Milestone C polish item.~~ (unchanged)
- Catalog rename exists at the repository/command layer but the Catalogs list
  has no inline rename control yet.
- SessionType required-capability editing exists in the repositories but
  not yet as a Catalogs-mode editor.
- `SessionTypePickerWindow` shows only type eligibility; missing-capability
  detail per type is in the status lines.
- Unity: not compiled/tested in-editor this milestone (see above).
- ~~Card draw logging in the player window does not yet surface per-candidate
  weights~~ (the `CardSelectionEvaluated` event seam still exists for it;
  surfacing remains deferred).

### Round-2 acceptance fixes (2026-08-28, post first human pass)

Fixed after the first hands-on acceptance: Profile window crash (nonexistent
`ListTextBrush` FindResource on the UI thread — now a static brush plus every
save path degrades to a message); catalog lists not refreshing on create/delete
(handlers now mirror rows into the in-memory snapshot before rebinding); Play
by Type "Session not found" (RunSession loads content itself when constructed
pre-Show); unlabeled weighting fields (row labels + column headers + formula
tooltips); the Phase graph disappearing with the card editor (the Card editor
moved into the Inspector as a fully buffered Save/Revert surface — title, body,
relations, AND Action sequence edit one CardEditBuffer, Save pushes a single
CompositeCommand so one Ctrl+Z reverts the entire card edit, and the shared
ActionSequence handlers route CardSequence scope to the buffer — previously
card action row edits silently vanished); the phase node buttons got a
"Phase nodes:" caption. Test totals after round 2: **283 passed, 1 known
skip** (135 Core / 123 SQLite / 11 profile / 14 WPF, including the composite
save round-trip with single-step undo).

## Deferred Milestone C/D items (per packet)

- Human authoring of ~15–25 real Cards and one complete Session in WPF
  (Milestone C's purpose).
- Anti-repeat/recent-card penalties, decks without replacement, rarity
  multipliers, boolean eligibility expressions, kink intensity scales,
  status-effect-based exclusion, Unity Action bridge/Timeline binding,
  Buttplug/DG-Lab/TCode integration, hardware discovery, content packs,
  cloud/multi profiles, persistent Happiness, rich-media card presentation.

## Preserve contract — intact

Verified against `00-START-HERE.md` / `PRESERVE-WHAT-IS-RIGHT.md`:
SQLite canonical content; Game.Content.Sqlite → portable snapshot → Core;
Session/Phase two-level graphs; PhaseReference; PhaseExit as public seam;
GOTO/RETURN continuation (RNG restore proven); fresh PhaseRun per entrance;
PhaseRun-local progress/RNG/history; session-global Temperatures;
run-until-yield; explicit WaitForContinue; Action Type vs Action Instance;
Nodify; stacked canvases; Make Unique; WPF layout separate from Core
semantics. Not restored: PhaseSlot, query-selected phase permutations,
one-card-per-Advance, implicit progress, reusable configured actions, JSON
canonical content, Unity-owned authoring.

---

## Ticket 19 — Human acceptance steps (the real gate)

Launch: `dotnet run --project DotNet/Game.ReferenceHost.Wpf` from the repo
root (or run the built exe). Then, working only through the UI:

1. **Catalogs** (Library → Catalogs): create 1+ new Session Type, 4+ Card
   Tags, 4+ Kinks, 4+ Equipment (with categories), 1+ Smart Toy capability.
   Try deleting a definition another item uses — expect a blocked message
   with usage counts.
2. **Profile** (Library → Profile): set Kinks to Love/Like/Torture/Don't
   Consent across your definitions; leave at least one deliberately
   unconfigured (top combo slot); check some Equipment and capabilities.
   Close and reopen the window/app — everything must persist
   (`%LocalAppData%\TruthCardGame\UserProfile.db`).
3. **Cards** (Library → Cards): create at least five Cards covering a
   neutral card, a positive-kink card, a Torture card, a DontConsent-rejected
   card, and a card requiring equipment/capabilities you don't have. Edit
   title/body, pick relations, author the ActionSequence (new cards default
   to WaitForContinue + Progress +10). Duplicate one card; verify the copy
   has fresh instance ids; delete the copy.
4. **Phase query** (Session canvas → select a Phase → Entry node): set
   Must-Have-ALL / Must-Have-ANY card tags; click "Preview Eligible Cards"
   and "Selection Diagnostics…" — drag the Happiness slider 0/50/100 and
   watch weights shift; confirm typed rejection reasons.
5. **Session weighting** (Session canvas → Start node): the six
   base/gain fields; set ≥0 values (invalid entries wait silently for
   valid numbers).
6. **Undo**: Ctrl+Z through a catalog create, a card edit, and a weighting
   change; Ctrl+Y forward.
7. **Play by Type** (toolbar): confirm eligibility status per type; Start
   Random Session; play through the player window (Continue) with your
   profile driving selection.
8. **Restart the app**: content and profile both persist; the canonical DB
   stays integrity-clean.

Acceptance: when these all behave, Milestone B is accepted and Milestone C
(hand-authoring ~15–25 real cards and one complete session in the Workbench)
can begin. Do not automatically start Milestone C work.
