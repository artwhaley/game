# Next WPF High-Level Authoring — Handoff

The next UI milestone begins at the top of this document. This is a **handoff
doc, not a spec**: it states what exists, what the next milestone should build
in order, and which design decisions must be resolved **with the user** before
implementation.

Current state of the stack (all committed on `sqlite-content-graph`):

- `Content/GameContent.db` — canonical DB, seeded from the Unity sample
  content, guarded by tests.
- `DotNet/Game.Content.Sqlite` — provider-neutral (any `DbConnection`):
  `ConnectionInitializer` (FK enforcement), `CoreMigrator.EnsureSchema`,
  `GameContentSnapshotLoader.Load` (DB → `GameContentDefinition` snapshot),
  `DatabaseInitializer` (empty-DB creation), `StableIds`.
- Granular authoring repositories (namespace `TruthCardGame.Content.Sqlite`):
  - `SessionRepository` — `Create`, `UpdateTitle`, `ReplaceTags`, `Delete`,
    `List`, `Get`, `ListPhaseSlots`.
  - `PhaseSlotRepository` — `Create`, `UpdateTitle`, `Delete`, `Reorder`,
    `AddCandidate`, `RemoveCandidate`, `ReorderCandidates`, `ListCandidates`.
  - `PhaseRepository` — `Create`, `Update`, `ReplaceMustIncludeTags`,
    `ReplaceMustExcludeTags`, `Delete`, `List`, `Get`.
  - **No Card / Action / Deck / Tag repositories yet** — those belong to the
    "descend" phase (§9). Session/phase tags are created on demand by the
    existing `ReplaceTags`/`ReplaceMust*Tags` helpers.
- `DotNet/Game.ReferenceHost.Wpf` — thin reference player (canonical DB →
  snapshot → `GameSessionEngine`). This is the seed of the workstation.
- Tests to copy patterns from: `AuthoringRepositoryTests`,
  `EngineIntegrationTests` (DB → snapshot → engine), `SchemaConstraintTests`,
  `HostExtensionSafetyTests` (host tables must survive all core operations).

---

## 1. Workspace

Minimal open/create of `Content/GameContent.db` with clear DB/schema errors.
No dashboard filler.

- Reuse `ConnectionInitializer.Initialize` + `CoreMigrator.EnsureSchema` on
  every connection (they are idempotent and fail loudly on FK-off or schema
  trouble).
- For a missing DB: `DatabaseInitializer` (or the seed tool) creates the
  schema; **never** create a DB and silently continue with empty content —
  surface the path and the decision.
- Show the resolved DB path (the reference player already resolves
  `--db <path>` or the repo-relative `Content/GameContent.db`).
- `PRAGMA integrity_check`/`foreign_key_check` are cheap opening guards
  (see `CanonicalDatabaseTests`).

## 2. Session Library

List / search / select / create / rename / delete Sessions.

- `SessionRepository.List` / `Create` / `UpdateTitle` / `Delete`. A session
  delete cascades its slots and candidates but **not** shared Phases —
  the UI must say that out loud.
- Session tags are ordered strings (`ReplaceTags`), displayed as a reorderable
  chip list.

## 3. Session Flow Surface

Central visual ordered PhaseSlot flow:

```text
[Warmup]
   ↓
[Tease]
   ↓
[Resolution]
   ↓
[End]
```

The UI should make overall shape legible. Slots come from
`SessionRepository.ListPhaseSlots` (ordered by `ordinal`); each slot shows its
candidate(s) (§5).

## 4. Phase Library

Independent reusable Phases such as:

- Gentle Warmup
- Fast Warmup
- Mean Warmup
- Release
- Frustrate

Reuse must be obvious. `PhaseRepository.List` for the library; a Phase is
independent — the same Phase referenced by many slots across many sessions.
Consequences to design for:

- Editing a Phase updates every session that references it (that's the point).
- `PhaseRepository.Delete` **refuses** a Phase referenced by any candidate
  (RESTRICT + explicit guard) — the UI should explain and offer to remove the
  references first.
- Phase min/max cards and tags live here, not on the slot (§7).

## 5. PhaseSlot Candidates

Selecting a slot shows its ordered candidate Phase FKs. A candidate is an
**occurrence referencing a Phase, not a Phase copy.**

- `PhaseSlotRepository.AddCandidate` / `ReorderCandidates` / `RemoveCandidate`.
- Today's schema/model supports one-or-more candidates per slot; the engine
  currently requires **exactly one** (zero or multiple fail loudly). Until §6
  lands selection rules, the authoring UI should keep exactly one candidate
  per slot (or surface the restriction).

## 6. Design progression/branching WITH the user before implementation

Known example:

```text
Resolution Slot
  Release
  Frustrate
```

may depend on stats/conditions/dice. Explicitly decide, in a design session
with the user, **before** writing code:

- condition gates (stat predicates? what vocabulary?);
- priority vs weighted choice;
- fallback when no candidate qualifies;
- how chance combines with stat checks;
- whether selection can fail (and what happens then);
- whether branch targets can skip slots;
- replay/determinism requirements.

Random slot selection gets a **dedicated flow RNG**, separate from the
phase-length RNG and the card RNG (the engine already keeps those domains
separate; the snapshot's `ContentCatalog` resolution is the seam where a
slot-selection rule would plug in). Today, with one candidate per slot, the
"selection" is deterministic — the flow RNG must be introduced together with
the selection rule, not before.

## 7. Phase completion/pass contract

Current Phase min/max Cards remains until deliberately redesigned. Completion/
pass behavior belongs logically to the reusable Phase definitions, not to
session-specific slots. Keep `MinCards`/`MaxCards` on `Phase` and preserve the
engine's current semantics (scaled target, live length modifier) until the
user redesigns them.

## 8. High-level live flow simulator

Before any Card/Action authoring UI, run the **real Core** and display:

- current Session;
- current PhaseSlot;
- candidate Phases;
- why a candidate was selected (deterministic today: the single candidate);
- selected Phase;
- completion/pass state;
- why the transition happened;
- next Slot.

Use the current SFW sample cards for card draws (they're in the canonical DB).

- Construct `GameSessionEngine` from a freshly loaded snapshot, exactly like
  the reference player (`MainWindow.OnStartSession`) and
  `EngineIntegrationTests` do.
- The engine already emits `CardStarted`/`CardFinished`/`PhaseChanged`/
  `SessionCompleted` and exposes `PhaseTitle`, `CurrentTarget()`, `Remaining()`,
  `IsComplete` — wire the simulator display to those so the UI can never
  disagree with Core.
- **Never** mutate a running snapshot from live DB edits.

## 9. Only then descend

Later milestones, in this order:

- Card authoring (needs new `CardRepository` + `CardActionRepository` +
  deck repository);
- Action library (per-type action tables + `ActionRepository`);
- Action attachment to cards;
- Unity/host binding tooling (Unity later owns `unity_*` tables in the same
  DB; host tables are safe under all core operations — see
  `02-integrity-audit.md`).

---

## DB/runtime rule (standing)

- WPF edits SQLite **via the granular repositories** (parameterized, scoped,
  transactional — never raw wholesale deletes/rebuilds).
- Preview reloads a **fresh snapshot** from the DB.
- Do not mutate an already-running snapshot from live DB edits.
