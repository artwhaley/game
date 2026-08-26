# 01 — Domain Map: CURRENT → TARGET

Freeze of the corrected domain contract (corrective stack Ticket 01), reviewed against
actual source on `content-graph-phase-slots` @ `1a8f3ee` + dirty stable-ID/cutscene work.

**Reading guide.** "Entity" = independently authored, stable-ID, reusable, serialized once,
top-level in the content library. "Owned record" = structural data belonging to exactly one
parent, serialized inside it. The correction moves Card/Action/Phase/CardDeck from
embedded-owned records to top-level entities and expresses all relationships as opaque
string IDs.

---

## 1. Portable library root

### `GameContentDefinition` (NEW — `Assets/Scripts/Portable/Game.Content/`)

| | CURRENT | TARGET |
|---|---|---|
| Role | none — document roots at `ContentDocument` | portable in-memory root for Core: the full content library |
| Shape | — | `Deck` (CardDeckDefinition), `Sessions[]`, `Phases[]`, `Cards[]`, `Actions[]` (polymorphic) |
| Notes | — | Plain mutable authoring data. No lookup dictionaries serialized. Single deck preserved (matches current game). |

`GameContentDefinition` replaces the `(SessionDefinition, CardDeckDefinition)` pair as the
engine's content input; `ContentDocument` (JSON layer) becomes its serialization envelope.

---

## 2. Entities — independent, top-level, referenced by ID

### `SessionDefinition` (`Assets/Scripts/Portable/Game.Content/SessionDefinition.cs`)

| Field | CURRENT | TARGET |
|---|---|---|
| `Id` | string (present) | unchanged |
| `Title` | string | unchanged |
| `Tags` | `List<string>` | unchanged (metadata/identity only; never a draw filter) |
| `Phases` | **`List<PhaseDefinition>` (EMBEDDED — the distortion)** | **`List<PhaseSlotDefinition> PhaseSlots` (owned records, ordered)** |

### `PhaseDefinition` (`PhaseDefinition.cs`)

| Field | CURRENT | TARGET |
|---|---|---|
| `Id` | string (present) | unchanged |
| `Title` | string | unchanged |
| `MustIncludeTags` | `List<string>` | unchanged |
| `MustExcludeTags` | `List<string>` | unchanged |
| `MinCards` / `MaxCards` | int / int | unchanged (semantics untouched) |
| Ownership | embedded in Session | **top-level entity in `GameContentDefinition.Phases`; sessions reference it** |

### `CardDefinition` (`CardDefinition.cs`)

| Field | CURRENT | TARGET |
|---|---|---|
| `Id` | string (present) | unchanged |
| `Title` | string | unchanged |
| `Tags` | `List<string>` | unchanged |
| `Actions` | **`List<GameActionDefinition>` (EMBEDDED — the distortion)** | **`List<string> ActionIds` (ordered; null/empty entries = skipped no-op, matching baseline)** |
| Ownership | embedded in Deck | **top-level entity in `GameContentDefinition.Cards`; deck references it** |

### `GameActionDefinition` (abstract, `GameActionDefinition.cs`)

| Field | CURRENT | TARGET |
|---|---|---|
| `Id` | string (present) | unchanged |
| `IsBlocking` | bool | unchanged |
| Ownership | embedded in Card / Choice option | **top-level polymorphic entity in `GameContentDefinition.Actions`; referenced by Card and Choice option** |

No `ActionInstance` wrapper. No cloning when referenced more than once — canonical instance is shared.

### `CardDeckDefinition` (`CardDeckDefinition.cs`)

| Field | CURRENT | TARGET |
|---|---|---|
| `Id` | string (present) | unchanged |
| `Cards` | **`List<CardDefinition>` (EMBEDDED — the distortion)** | **`List<string> CardIds` (ordered; null entries may remain as baseline no-op entries)** |
| Ownership | document root | **top-level entity in `GameContentDefinition.Deck` (single deck preserved)** |

---

## 3. Owned records (serialized inside their parent)

### `PhaseSlotDefinition` (NEW — the composition seam)

| Field | TARGET |
|---|---|
| `Id` | stable opaque string; session-owned |
| `Title` | slot label (e.g. "Warmup") |
| `CandidatePhaseIds` | `List<string>`; **exactly one** valid candidate for runtime in this stack |

Semantics for THIS stack: one slot per current linear phase; zero candidates = invalid for
runtime (fail loudly); multi-candidate = deliberately unsupported (fail loudly, never pick
silently, never consume RNG). Transitional Unity slots use deterministic derived ids
`legacy-slot:<sessionId>:<phaseId>:<occurrence>` — never a fresh GUID per conversion.

### `ChoiceOptionDefinition` (`ChoiceOptionDefinition.cs`)

| Field | CURRENT | TARGET |
|---|---|---|
| `Id` | string (present) | unchanged |
| `Label` | string | unchanged |
| `Child` | **`GameActionDefinition` (EMBEDDED — the distortion)** | **`string ChildActionId`; null/empty = legal no-op (baseline)** |

### `ChoiceActionDefinition` (`ChoiceActionDefinition.cs`)

| Field | CURRENT | TARGET |
|---|---|---|
| `Prompt` | string | unchanged |
| `Options` | `List<ChoiceOptionDefinition>` | unchanged (owned option records; child actions referenced, never embedded) |

---

## 4. Core resolution layer (NEW — `Assets/Scripts/Portable/Game.Core/`)

### `ContentCatalog` (NEW)

| | TARGET |
|---|---|
| Input | `GameContentDefinition` |
| Behavior | index all independent entities by ID (Session / Phase / Card / Action); reject duplicate non-empty IDs; reject missing/empty IDs on construction; explicit typed lookups (`SessionById`, `PhaseById`, `CardById`, `ActionById`) with clear errors naming the missing ID and entity type |
| Identity | two references to the same ID resolve to the same canonical instance |
| Boundaries | NOT a database/repository/service-locator/mutable editor model; no reflection over collections; no mutation/save logic |
| Timing note | fail-loud on construction is correct now (no authoring UI); the WPF authoring milestone will need validation separated from runtime resolution — recorded for handoff |

### Engine construction (TARGET)

```text
GameSessionEngine(GameContentDefinition content, string sessionId,
                  Func<float> lengthModifier,
                  IRandomSource phaseLengthRng,   // renamed from phaseRng
                  IRandomSource cardRng,
                  CoreServices services)
```

(or equivalent via prebuilt `ContentCatalog`). Hosts stop passing materialized
`(SessionDefinition, CardDeckDefinition)` pairs.

---

## 5. Unity side (TARGET — Ticket 07)

| Current | Target |
|---|---|
| `Session.ToDefinition()` embeds `phase.ToDefinition()` | shallow convert; one `PhaseSlotDefinition` per Phase reference; collect each referenced Phase once by ID |
| `CardDeck.ToDefinition()` embeds `card.ToDefinition()` | ordered `CardIds`; collect each referenced Card once |
| `Card.ToDefinition()` embeds `action.ToDefinition()` | ordered `ActionIds`; collect each referenced Action once |
| `ChoiceOption` embeds child `action.ToDefinition()` | `ChildActionId`; recursively **collect** child (not embed) |
| recursive `ToDefinition(registry)` on every SO | `UnityContentGraphBuilder` (non-generic): selected Session + Deck → collect referenced SOs once → `GameContentDefinition` |
| — | duplicate ID across distinct SOs → loud failure; same SO referenced twice → converted once |
| `CutsceneBindingRegistry` registers per conversion | collector registers Timeline bindings once per Action asset; repeated references idempotent; two distinct resources with the same `ResourceId` still fail |

GameManager builds the graph once at session start; `Game.Core` rules unchanged.

---

## 6. JSON (TARGET — Ticket 06)

- `ContentSchemaVersion` → **3**.
- `ContentDocument.SchemaVersion` default → the **current** version (fixes today's bug:
  defaults to `1` while `CurrentSchemaVersion` is `2`).
- Shape: top-level `deck` + `sessions[]` + `phases[]` + `cards[]` + `actions[]`;
  relationships as IDs (`cardIds`, `actionIds`, `candidatePhaseIds`, `childActionId`).
- Polymorphic actions keep the explicit `type` discriminator; serialized once in the top-level
  Action library.
- Fixture: `parity-content-v2.json` → `parity-content-v3.json`, demonstrating real sharing
  (one Phase referenced by two Sessions — already true of the Unity sample content; one
  Action referenced from more than one Card/Choice path).
- No v1/v2 migration subsystem (no production content); unsupported versions keep failing loudly.

---

## 7. Runtime semantics explicitly NOT changing

- `SessionDriver`: linear session walk, per-phase unscaled base target, live length-modifier
  math (`max(1, Round(base × modifier))`), no-match early advance with warning, completion
  after the final phase — preserved, driven through one-candidate slots + catalog resolution.
- `CardSelector`: tag matching (all-include / none-exclude), null-entry skip, uniform
  random-with-replacement draw, dedicated card RNG — preserved.
- `ActionExecutor`: ordered blocking execution, nonblocking background tracking, cancellation
  commit boundary, missing-service logged no-op, choice executes at most one selected child
  (by `ChildActionId`), cutscene by `ResourceId` — preserved.
- RNG domains stay separate: phase-length RNG and card RNG. **No slot-selection RNG consumed.**
  Future PhaseSlot selection (next milestone) gets its own flow RNG domain — recorded now so
  progression logic never perturbs existing target/card sequences.
- Events (`CardStarted`/`CardFinished`/`PhaseChanged`/`SessionCompleted`) unchanged in shape.
- Action reference cycles (Choice → child → … → itself) get a minimal execution-time cycle
  guard: clear failure, no uncontrolled stack overflow.

---

## 8. Explicit decisions (Ticket 01 checklist)

1. Independent entities serialized once, relationships by opaque string ID — **yes**.
2. Reference lists preserve order — **yes**.
3. IDs are opaque strings; nothing parses them as GUIDs — **yes** (existing GUID ids remain valid).
4. PhaseSlot is session-owned; Phases are independent — **yes**.
5. This stack supports exactly one candidate Phase per slot — **yes**; zero/multi fail loudly.
6. No PhaseSlot-selection RNG consumed now — **yes**.
7. Future PhaseSlot selection gets its own RNG domain — **recorded** (§7).
8. Phase `MinCards`/`MaxCards` semantics unchanged — **yes**.
9. Actions remain polymorphic independent entities, no `ActionInstance` wrapper — **yes**.
10. Choice option remains exactly one child Action reference — **yes**.
11. Current single deck remains single deck — **yes**.
12. Schema target v3 — **yes**.
13. No production migration layer for v1/v2 — **yes**.
14. Null/empty entries in reference lists = skipped no-op (baseline parity); a **non-empty**
    unresolved ID = loud runtime error — **yes** (execution judgment, closes Ticket 03's seam).
15. No branching DSL, no weights/conditions/dice/stat predicates, no slot jumps — **out of scope**.

## 9. Scope boundary (what this map deliberately does NOT decide)

PhaseSlot multi-candidate selection rules, weighted randomness, stat predicates, dice syntax,
arbitrary branch graphs, slot jumps, pass-condition DSL, editor UX, Action binding UX.
These belong to the WPF high-level authoring milestone; `NEXT-WPF-HANDOFF.md` (Ticket 10)
will open that discussion.

## 10. SQLite persistence override (Ticket 12)

Short override, recorded after the fact rather than erasing the map above: the
**persistence layer** this map anticipated as "JSON schema v3" (item 12) was
instead delivered as **SQLite**. The graph decisions in §§1–9 stand unchanged
— this override only replaces the storage backend:

- Canonical content lives in the relational DB at `Content/GameContent.db`
  (schema v1 in `DotNet/Game.Content.Sqlite/SQLITE-SCHEMA-V1.sql`):
  `session`, `phase_slot`, `phase_slot_candidate`, `phase`, `card`,
  `card_action`, `tag`, `resource`, `action` + per-type tables.
- `Game.Content.Json` was removed; **no JSON schema v3 will be created**.
- `GameContentDefinition` remains the in-memory snapshot the engine runs;
  `Game.Content.Sqlite` loads/stores it, `Game.Core` resolves it.
- Host extensions (Unity later, WPF now) may add `unity_*`/`wpf_*` tables;
  the core schema, migrator, loader, and all authoring operations preserve
  unknown tables (see `Docs/SqliteContentGraph/02-integrity-audit.md`).
