# Testing Methodology — "House Party Shakedown" (WPF) and Unity Parity Assessment

Status: **living plan for the hand-authored shakedown session**. Branch: `nodify-graph-live-follow`.
Scope: (A) author and execute one representative ~20-card test session that touches every
feature of the Graph Workbench / Core engine, ending in a deterministic seeded run whose
saved trace becomes the Unity parity target; (B) the current Unity status report and the
path to playback parity.

---

## Part A — The WPF shakedown session

### A.0 Preflight — canonical DB

The canonical content store is `Content/GameContent.db` (current schema: v10 Action Blocks,
v11 portal pairs). Per `Docs/CONTENT-DATABASE-VERSIONING.md`:

1. Close all writers (quit the WPF host). Confirm no `GameContent.db-wal` / `-shm` companions.
2. Run `PRAGMA integrity_check;` (must be `ok`) and `PRAGMA foreign_key_check;` (must return 0 rows).
3. Take a backup snapshot.
4. **Delete the inherited starter fixture** (1 Session, 1 Phase, 3 Cards) — decision made.
   Delete via the Library UI, in dependency order: **session first, then phases, then cards**
   (phase deletion is protected while a phase is placed in a session). All deletions are
   normal Library operations with cascade + undo.
5. **One test must be relaxed first**: `CanonicalDatabase_LoadsThroughV2Loader`
   (`DotNet/Game.Content.Sqlite.Tests/CanonicalDatabaseTests.cs`) hard-asserts the canonical
   DB has >0 sessions/phases/cards. Change it to assert load invariants (well-formed rows
   when present, graphs/sequences non-null) instead of non-emptiness, so an empty-but-valid
   DB is a legal state (rule 8: keep the harness fitted). This is the only canonical-content
   shape assertion in the suite — `PassesIntegrityChecks`, `MigratesCopy`,
   `PlaysThroughTheSessionVm`, and `UserProfileBoundary` are all shape-agnostic; the
   MilestoneB tests use temp/legacy fixtures, not the canonical DB.
6. After emptying the DB, restart the workbench once to confirm it loads an empty-but-valid
   DB cleanly, then run `dotnet test Game.Workbench.sln` (expected green; current baseline 384 tests).
7. Launch the WPF host and leave it running for the authoring session (rule 12).

Note on `CanonicalDatabase_PlaysThroughTheSessionVm`: once the new session exists, this test
will run it to completion in CI. It self-skips only on unwired projected exit sockets — so
keep every GOTO exit wired (the design below does).

### A.1 Authoring order (dependency-first)

Everything below is UI work in the running WPF workbench — **no code changes required**.

1. **Catalogs** (Library → Catalogs)
   - Session Types: `party` (+ a second `tech-demo` type that requires a capability — proves
     Play-by-Type ineligibility filtering).
   - Card Tags: `truth`, `dare`, `physical`, `talkative`, `cozy`, `spicy`.
   - Kinks: `romance`, `playful`, `intense`, `humiliation`.
   - Equipment: `blindfold` (category `gear`).
   - Capabilities: `vibrate`, `rotate`.
   - Resources: cutscene `arrival`; toy patterns `pulse`, `wave`, `steady`.
   - Dialog Tags: `tease`, `praise`, with 2–3 snippets each.
2. **Action Blocks** (reusable blocks, deep-cloned on insert):
   - `Pacing Beat` = Dialog → WaitForContinue → IncrementProgress +10.
   - `Encourage` = StatIncrease `boldness` +1 (nonblocking) → Dialog.
   Proves block creation, scope validation, and drag/drop insertion.
3. **Profile** (Profile window → `UserProfile.db`, separate from content):
   - romance = **Love**, playful = **Like**, intense = **Torture**, humiliation = **DontConsent**.
   - Equipment/capability inventories stay empty by design (inventory UI is deferred) — that is
     exactly what makes the equipment/capability exclusion tests bite.
4. **Card folders** (Cards pane): create `Cozy/`, `Truth/`, `Dare/`, `Physical/`.
5. **Cards** — the 20 cards below, authored in batches; save + restart between batches to
   verify persistence each time.
6. **Phases** (4) with graphs and exits.
7. **Session graph** with decisions, SessionGoto sockets, portal pairs.
8. **Runs** (Passes 1–4 in A.6).

### A.2 The 20 cards

Sequence column = the card's owned Action sequence; blocking notes shown only where
non-default (nb = nonblocking).

| # | Card (folder) | Tags | Kinks | Req. | Owned sequence → coverage |
|---|---|---|---|---|---|
| 1 | Soft Landing (Cozy) | cozy, truth | romance | — | Dialog (welcome) → ModifyTemperature happiness +5 (nb) → WaitForContinue — *dialog, temperature mutation, wait pacing, Love-weighted draw* |
| 2 | Fireside Flatter (Cozy) | cozy, talkative | romance | — | DialogFromTags (tease) → StatIncrease boldness +1 (nb) → WaitForContinue — *DialogFromTags + snippet RNG, stat* |
| 3 | Two Truths or a Lie (Cozy) | cozy, truth | playful | — | PromptChoice, 3 options: A Dialog → IncrementProgress 10 → Wait; B Dialog → WaitForAll → Dialog; C StatIncrease boldness +1 (nb) → Debug (nb) → Wait — *PromptChoice 3-option max, nested option sequences, WaitForAll barrier, Debug* |
| 4 | Butterfly Round (Cozy) | cozy | playful | — | ToySetPattern (vibrate, wave) (nb) → Delay 2s (blocking) → Dialog → Wait — *SetPattern persistent toy state, blocking Delay* |
| 5 | Midnight Murmur (Cozy) | cozy, spicy | romance | — | ToyActivity (vibrate, pulse, 5s, nb) → WaitForContinue — *timed nonblocking toy on the background tracker* |
| 6 | Roast Battle (Truth) | dare, physical | playful | — | Dialog → IncrementProgress 15 → Wait |
| 7 | Seven Minutes (Dare) | dare, spicy, physical | intense | — | ToyActivity (rotate, steady, 8s, **blocking**) → Dialog → Wait — *blocking timed toy* |
| 8 | Blind Draw (Dare) | dare, physical | intense | **blindfold** | Dialog → Wait — *exclusion: MissingEquipment (typed reason in diagnostics)* |
| 9 | The Spin (Dare) | dare, physical | playful | **vibrate** | ToySetPattern (vibrate, steady) (nb) → Wait — *exclusion: MissingCapability* |
| 10 | Truth Cannon (Dare) | dare, spicy | humiliation | — | Dialog → StatIncrease (nb) → Wait — *exclusion: KinkDontConsent* |
| 11 | Confessional (Dare) | dare, spicy | intense | — | ModifyTemperature happiness −20 → Dialog → Wait — *happiness drop swings Torture weight up* |
| 12 | Echo Chamber (Dare) | dare, spicy | playful | — | Cutscene (`arrival`, blocking) → IncrementProgress 10 → Wait — *cutscene host + resource id* |
| 13 | Trial by Laughter (Dare) | dare, physical | intense | — | ToyActivity (vibrate, pulse, 6s, nb) → **WaitForAll** → IncrementProgress 5 → Wait — *barrier drains a real background toy* |
| 14 | Slow Burn (Dare) | dare, spicy | — | — | Delay 4s (blocking) → Debug (nb) → Wait |
| 15 | The Gauntlet (Dare) | dare | intense | — | ToySetPattern (vibrate, steady) (nb) → ToyActivity (rotate, pulse, 10s, nb) → Wait — *two capabilities coexist; set + timed supersede rule* |
| 16 | Truth or Dare Spin (Dare) | dare, spicy | playful | — | PromptChoice, 2 options: A Dialog → StatIncrease (nb); B ToyActivity (vibrate, wave, 4s, nb) → Dialog → Wait — *choice leading into a toy* |
| 17 | Mercy Card (Cozy) | cozy, truth | romance | — | Delay 2s **(nb)** → Dialog → Wait — *nonblocking Delay* |
| 18 | The Anchor (Dare) | dare, spicy | intense | — | Dialog → IncrementProgress 10 → Wait — *heavy Torture base; shows weighted draw when happiness is low* |
| 19 | Silent Witness (Truth) | truth, talkative | *(none)* | — | Dialog → StatIncrease boldness **−1** (nb) → Wait — *zero-kink neutral weight path, negative stat* |
| 20 | Final Word (Dare) | dare, spicy | intense | — | Dialog → IncrementProgress **20** → Wait — *finale draw; progress 20 triggers the encore recursion via the phase check* |

**Coverage summary — all 17 action types appear at least once:**

| Type | Where |
|---|---|
| debug | 3C, 14 |
| statIncrease | 2, 3C, 10, 16A, 19 (+ phase ActionNode bump in P2) |
| increment_progress | 3A, 6, 12, 13, 18, 20 (+ phases) |
| modify_temperature | 1, 11 |
| cutscene | 12 |
| dialog | most cards |
| dialog_from_tags | 2 |
| delay | 4 (blocking), 14 (blocking), 17 (nb) |
| toy_activity | 5, 7, 13, 15, 16B |
| toy_set_pattern | 4, 9, 15 |
| wait_for_all | 3B, 13 |
| prompt_choice | 3, 16 |
| wait_for_continue | ubiquitous pacing |
| phase_goto | P2/P4 graphs, PhaseDecision options, nested PromptChoice in a P2 ActionNode (legal ChoiceOption inheritance) — *not* in card sequences (see A.5 negative tests) |
| session_goto | SessionDecision "Intermission" option |
| return | P3 Return node, P4 Return node |
| end_session | SessionDecision "Call it a night" option |

Also exercised across the cards: blocking vs nonblocking forms of Delay and timed ToyActivity;
SetPattern always-nonblocking; three distinct kink-preference weight paths (Love/Like/Torture)
plus a zero-kink neutral card; all three exclusion kinds with typed rejection reasons.

### A.3 Phases and graphs

- **P1 Warm-Up** — tags ALL `cozy`; exit `open-door`.
  Entry → Draw → VariableCheck *Happiness ≥ 55* → True: ActionNode (Dialog + IncrementProgress 20)
  → PhaseGoto `open-door`; False: ActionNode (ModifyTemperature +5, WaitForContinue) → loop back to Draw.
  *Covers: temperature check, temperature raise, loop-back edge (revisit Draw), exit GOTO.*
- **P2 Main Event** — tags ALL `dare`, ANY `physical`/`talkative`/`spicy`; exits `finish-strong`, `early-out`.
  Entry → Draw → ActionNode (StatIncrease boldness +1 nb, WaitForAll, and a **nested PromptChoice
  whose one option contains PhaseGoto `early-out`** — the legal ChoiceOption-inheritance case) →
  VariableCheck *progress ≥ 60* → True: ActionNode (finale Dialog + IncrementProgress 10) →
  PhaseGoto `finish-strong`; False: PhaseDecision "Another round?" → [One more → loop back to Draw],
  [Wrap it up → PhaseGoto `early-out`].
  *Covers: progress check, phase-level ActionNode, WaitForAll across cards, nested-choice GOTO
  inheritance, PhaseDecision, repeated draws at one location.*
- **P3 Spin-Off** — tags ALL `truth`; no exits.
  Entry → Draw → ActionNode (Dialog + IncrementProgress 10) → **Return node**.
  *Covers: SessionGoto target; RETURN restores the suspended SessionDecision continuation; its own
  fresh PhaseRun (independent progress + card RNG).*
- **P4 Grand Finale** — tags ALL `spicy`; exit `encore`.
  Entry → Draw → VariableCheck *progress ≥ 20* → True: ActionNode (farewell Dialog) → PhaseGoto
  `encore` (**wired back to the same placement = recursive PhaseRun #2**) ; False: **Return node** →
  session resumes → End.
  *Covers: recursion, per-PhaseRun independent progress (run #2 starts at 0), return-to-session.*

### A.4 Session graph — "House Party Shakedown" (type `party`)

```
Start → P1 (Warm-Up)
        open-door → P2 (Main Event)
        finish-strong → SessionDecision "Intermission"
        early-out → P4 (skip intermission)

SessionDecision "Intermission" options:
  • "Take a breather" — Dialog → SessionGoto → P3 (Spin-Off); P3 Return resumes the option
    sequence → common normal output → P4
  • "Power through"   — StatIncrease → normal → P4
  • "Call it a night" — EndSession (absolute terminal, discards the whole continuation stack)

P4 encore → P4 (self-recursion); P4 post-Return → End
```

- Add **portal pairs** on the long P2 `early-out` → P4 edge and on one session edge; verify the
  pair persists across restart and that undo/redo restores the same pair + endpoint identities
  (schema v11).
- Verify projected exit sockets on the P1/P2 placements update live when exits are edited.

### A.5 Negative tests (throwaway `--db <tempfile>` copy — never the canonical file)

Run the workbench against a temp DB for these, or author-and-delete inside a scratch copy:

1. **PhaseGoto added to a card's sequence** → loud scope rejection (PhaseGoto is phase-owned;
   a card's nested PromptChoice inherits Card scope — this is the illegal case, distinct from
   the legal P2 case).
2. **SessionGoto added anywhere outside a SessionDecision option** → loud rejection.
3. **Temp phase with ALL `[nonexistent-tag]`** → `NoEligibleCard` loud error with the full
   per-card rejection summary (typed reasons).
4. **Temp phase Entry → Return** (empty continuation stack) → loud runtime error.
5. **Unwire a GOTO exit** (temporarily delete the edge) → "unwired export" error naming
   Session / Phase placement / Phase / exit.
6. **Dead-end ActionNode** (no outgoing edge) → graph error.
7. *(Conditional)* delete one profile kink row → `KinkUnconfigured` exclusion in diagnostics.

Each fixture is cleaned up afterward; nothing broken ever lands in the canonical DB.

### A.6 Execution passes

1. **Reference Player, pinned seed** — enter an explicit integer seed; follow the scripted
   choice path. Verify in the log: seed line; candidate eligible/rejected lines with typed
   reasons and weights; check evaluations; edge traversals; phase enter/leave; CARD START with
   the full card body; dialog lines presented; cutscene start/finished; toy timing and
   supersede behavior; pause/resume mid-run. **Restart with the same seed → identical replay**
   (same card order). Save the full log.
2. **Live Preview (Preview ▸ → Start ▶)** — watch amber node rings, BringIntoView follow, and
   edge highlight/trail on both canvases; exercise the **dirty-card pause gate**: edit a card
   body mid-run → execution pauses with an explanation → Save or Revert → resume is guarded
   until the buffer is clean.
3. **Play by Type** — consumer flow: the `tech-demo` session type (requires an unavailable
   capability) is filtered out; uniform pick among eligible sessions; profile-driven selection.
4. **Authoring regressions** — undo/redo chains across both graph panes (Ctrl+Z / Ctrl+Y);
   restart persistence of everything; Copy Session / Duplicate Phase / Make Unique + the port
   lock popup; Action Block insert with deep clone; folder batch operations (drag, cascade
   delete, duplicate with "(copy)" names); exit rename preserves wiring; portal pair undo.

### A.7 Artifacts

- Canonical DB checkpointed and committed **in batches** per `Docs/CONTENT-DATABASE-VERSIONING.md`
  (close writers, integrity + FK checks before and after, no WAL/SHM companions).
- Saved seed + full Reference Player log committed under `Docs/TestSessionShakedown/` with a
  short report — this is the **Unity parity target trace**.
- The canonical DB after this milestone contains only the hand-authored session (the starter
  fixture is gone).

---

## Part B — Unity status and the path to playback parity

### B.1 Where Unity actually stands (facts, not hopes)

- **"We kept compiling against Unity in our build tests" is half true.** `dotnet test
  Game.Workbench.sln` compiles the *same physical portable sources* Unity uses
  (`Assets/Scripts/Portable/**` — Game.Content, Game.Core, Game.Profile), so the engine layer is
  continuously compile-tested (current baseline 384 tests green). But Unity's **host code** —
  GameManager, SceneBuilder, the ScriptableObject bridge, adapters, UI — compiles only inside
  the Unity editor. The last verified headless editor compile + EditMode run (18/18) was at the
  GraphWorkbench baseline. Since then `Docs/ToyPatternDialog/17-unity-compile-drift.md`
  (2026-08-30) explicitly records *"editor compile remains unverified"* — only a static audit
  plus one stale `CardDeck` reference fix was done. **Step zero of any Unity work is a headless
  editor compile on 6000.5.9f1 to find the real drift.**
- **It is not in parity.** Unity currently:
  - Reads content **only via the ScriptableObject bridge** (`UnityContentGraphBuilder`) — there
    is no SQLite provider anywhere in `Assets`, and `Packages/manifest.json` has none, so Unity
    cannot load `Content/GameContent.db` (or `UserProfile.db`) at all.
  - Synthesizes a fixed linear graph from legacy `Session`/`Phase`/`CardDeck` ScriptableObjects —
    it cannot run authored decisions, VariableChecks, or GOTO/RETURN exits, so it cannot run the
    session built in Part A.
  - Has no adapters/wrappers for Dialog, Delay, Toy Activity, ModifyTemperature, PromptChoice,
    DialogFromTags, or WaitForAll — the post-Milestone-B action vocabulary.
  - Has no selection-pipeline wiring (weighting/kinks/equipment/capability/profile) and uses
    `UnityEngine.Random` (unseeded, unreplayable) instead of the existing
    `SessionSpawnOptions.Seed` + per-domain RNG.
  - Shows the card **title only** — no body text; no dialog presentation.
- **But the shape is already right.** The portable `CoreServices` seam (delay / log / prompt /
  cutscene / dialog / toy / pause) is exactly the finished-product seam, and Unity implements 4
  of 7 (`UnityGameDelay`, `UnityGameLog`, `UnityPromptService`, and `DirectorPlayer` +
  `CutsceneBindingRegistry` for Timeline). The engine emits the same trace events WPF consumes
  (`GraphEdgeTraversal`, node/phase change, selection evaluation). Dialog is a small UI adapter
  (reuse the prompt-overlay pattern); toy is a logging no-op for v1 that hardware slots into
  later; seeded spawn + per-domain RNG (`PhaseRunRngFactory`) already exist — Unity just needs
  to use them.

### B.2 The one infrastructure decision (rule 4 — new dependency, must ask before adding)

How Unity gets SQLite:

- **(Recommended) SQLitePCLRaw + Microsoft.Data.Sqlite packaged into `Assets/Plugins`** — the
  provider-neutral `Game.Content.Sqlite` / `Game.Profile.Sqlite` loaders then run as-is in
  Unity; same DB, same loader, same engine in every host. Feature-forward: content packs,
  runtime profile, shipping builds. This is the honest "finished product" direction.
- JSON snapshot export from the workbench → Unity reads the snapshot. Zero new dependency, but
  a sync step and drift risk — wrong direction for shipping.
- Editor-time DB→ScriptableObject bridge. Duplicated state, cannot represent the graph model —
  worst fit.

### B.3 Work items (medium, well-scoped)

1. Headless editor compile (`6000.5.9f1 -batchmode`) and repair drift in host code.
2. Unity packaging of `Game.Content.Sqlite` + `Game.Profile.Sqlite` + SQLite provider (asmdefs
   + platform native libs).
3. Rework `GameManager` (DB loader instead of SO bridge, seeded spawn), `GameSetup` (session
   list from the DB instead of `SessionLibrary` SO), `GamePanel` (card body text, dialog
   presentation).
4. New Unity adapters: `IDialogService` (UI), `IToyActivityService` (logging no-op for v1),
   seeded per-domain RNG (replace `UnityRandomSource`), pause gate optional for v1.
5. `SceneBuilder` updates for the new GamePanel surface.
6. **Parity harness**: run the Part A pinned seed through Unity and diff the engine event
   traces against the committed expected trace; a PlayMode test replays it.

The WPF shakedown in Part A produces that expected trace (A.7) — the two halves of this
methodology are deliberately sequenced: **author + shakedown in WPF first, then wire Unity to
reproduce the same run.**

---

## Rules that govern this work (from agents.md, abbreviated)

- No new dependencies without asking (rule 4) — the SQLite provider choice in B.2 is a
  decision point.
- Keep README current after each feature (rule 6).
- Fix root causes, never band-aids (rule 7).
- Data and tests are not sacred; keep the harness fitted to the current app state (rule 8).
- Fail noisy — no silent recovery (rule 10).
- Report verification honestly; a clean build is not a substitute for running the app (rules
  11–12): launch the WPF host and leave it running after every app change.
