# TruthCardGame — Full Project Overview

*Written for an outside reviewer (human or AI agent) who has the source tree in
hand and wants to understand: what this is, how it's built, what's decided,
what's open, and where it should go next.*

> **Addendum (extraction milestone 0.1, 2026-08):** the game rules described in
> §3 now live in portable `Game.Content`/`Game.Core` assemblies under
> `Assets/Scripts/Portable/`, consumed by Unity (thin host) and a WPF reference
> player; the coroutine engine, `CardExecutor`, old prompt/cutscene contracts
> and `SessionDriver` Unity copy were removed. **Sections §3, §3.4, §3.5 and
> the dev-log/ticket history below describe that pre-extraction architecture
> and are kept for background only** — current truth: README "How it works",
> [`Docs/CoreExtraction/FINAL-REPORT.md`](Docs/CoreExtraction/FINAL-REPORT.md),
> and [`Docs/CoreExtraction/02-parity-report.md`](Docs/CoreExtraction/02-parity-report.md).
> Current test posture: 8 EditMode conversion tests + 2 PlayMode smoke tests +
> 87 portable tests via `dotnet test`; human Play-mode passes of both hosts are
> complete. The cutscene timeline assignment (§6.1) remains open. Note on §3.3:
> the scene/content builders remain useful bootstrap/editor automation; they
> are not a standing law that visual Unity work must always be reconstructed
> from C#.

> **Addendum (SQLite content pipeline 0.2, 2026-08):** SQLite is now the
> canonical source of content truth. The relational model
> (Session/Phase/Card/Action/Tag/Resource + PhaseSlot/PhaseSlotCandidate)
> lives at `Content/GameContent.db`, owned by the provider-neutral
> `DotNet/Game.Content.Sqlite` project (migrations, snapshot loader,
> authoring repositories, seed tool). `GameContentDefinition` is the
> in-memory snapshot; `Game.Core` resolves and runs it through
> `ContentCatalog`; the WPF reference player loads the canonical DB and is
> the future primary core-content author; Unity runs a temporary
> ScriptableObject→snapshot bridge (`UnityContentGraphBuilder`) and will
> later read the same SQLite schema, owning `unity_*` extension tables
> (proven safe by the integrity audit). The JSON spike (`Game.Content.Json`)
> was removed; there is no JSON schema v3. Current truth:
> [`Docs/SqliteContentGraph/`](Docs/SqliteContentGraph/) (00-checkpoint,
> 01-schema-v1, 02-integrity-audit) plus README "Status". The extraction-era
> addendum above and §3–§9 below are retained as history.

Companion docs: [`agents.md`](agents.md) (operating rules),
[`README.md`](README.md) (current status + dev log),
[`unity-cli.md`](unity-cli.md) (CLI notes), [`Tickets/`](Tickets/README.md)
(execution tickets).

---

## 1. What the game is

A **single-player Unity 6 card game built around a "truth or dare" concept.**
The game maintains decks of cards; a session draws one card at a time and
*executes* it. A card's execution can be anything we define as an "action":

- play an animation / voiceover mini-cutscene with an NPC character,
- trigger a minigame,
- send output through a Bluetooth toy plugin (wired in later),
- prompt the player with a choice that branches execution,
- adjust player stats, log debug output, etc.

The design constraint is explicit: there will be roughly **8–10 kinds of
actions**, not infinite variety — but the action set must be **extensible and
interchangeable**. Content volume is expected to be large (potentially ~1000
cards), which drives two priorities: data-driven content as ScriptableObjects
from day one, and **authoring tooling** as a first-class upcoming concern.

Editor version: **Unity 6000.5.9f1** (pinned). Platform target is undecided;
everything so far is platform-neutral C# + uGUI.

---

## 2. Development philosophy (read this before proposing anything)

These rules live in `agents.md` and govern every change:

1. **Incremental & intentional construction.** Never fill negative space with
   "app-shaped bullshit." If a feature wasn't asked for, it doesn't exist yet.
2. **Plan before touching files.** List every file to modify first; >5 files →
   split the task.
3. **Three strikes** on a failing fix → stop, propose revert / known-vs-unknown /
   different approach.
4. **No new dependencies without asking** (Timeline + Cinemachine were asked-for
   additions).
5. **Explain before coding** any non-trivial change; wait for a "go."
6. **Keep README current** after each feature.
7. **Fix root causes only** — no workarounds, no band-aids.
8. **Development environment, not production.** Tests/schema may go stale; no
   backwards compatibility for old files. Data isn't sacred.
9. **Fail noisy.** No silent fallbacks that mask problems.
10. **Report verification honestly** — never claim "done" for unverified work.
11. **Commits save the whole project state** every time — no surgical staging,
    no multi-branch cleverness. Single branch (`main`), single developer flow.
    Remote: `https://github.com/artwhaley/game.git`.

A reviewer should judge any proposed plan against rule 1 above all: the failure
mode this project guards against is speculative infrastructure.

---

## 3. Architecture

### 3.1 The core abstraction stack

```
Session            (a "game type": title, metadata tags, ordered Phases)
  └─ Phase         (card filter tags + min/max draw count range)
      └─ Card      (title, filter tags, ordered list of CardActions)
          └─ CardAction  (abstract; blocking-or-continuous; Execute coroutine)
```

The Session→Phase relationship deliberately mirrors Card→Action: both are
"data asset that owns an ordered list of child assets."

- **CardAction** (abstract `ScriptableObject`): has a `Blocking` bool and
  `Execute(GameContext)` returning `IEnumerator`. Blocking actions are awaited
  by the executor; continuous actions are dispatched fire-and-forget so several
  can run concurrently. Implementations today:
  - `DebugAction` — logs, waits configurable delay, completes.
  - `StatIncreaseAction` — bumps a named player stat instantly.
  - `CutsceneAction` — plays a `TimelineAsset` through `ICutscenePlayer`
    (blocking).
  - `ChoiceAction` — shows a prompt with labeled options via `IPromptService`;
    each option carries a child `CardAction`; blocks until chosen, then runs
    exactly one child (inherently blocking).
- **Card**: title + tag list (for include/exclude filtering) + ordered actions.
- **CardDeck**: pool of cards + helper exposing distinct tags (used by UI).
- **CardExecutor**: draws a random card matching must-include/must-exclude tag
  filters; runs its actions honoring the blocking/continuous flags.
- **Session / Phase / SessionLibrary** (ScriptableObjects): Phase defines the
  tag filter for its draws plus min/max card count; SessionDriver picks a
  random target within [min,max] per phase.
- **SessionDriver** (plain C#, fully unit-tested): walks phases. Per phase it
  picks an unscaled base target once; the advance check is
  `drawn >= max(1, Round(base × live LengthModifier))`, evaluated on every card
  completion. The length modifier is **live-read, never baked** — a mid-session
  modifier change takes effect on the very next draw (grow extends the phase;
  shrink can end it immediately). Empty-filter early advance logs a warning and
  moves on. After the final phase the session completes.
- **Endings are authored, not special-cased**: every session ends with an
  authored "ending phase" whose tags match an ending card. There is no
  single-use "game over" mechanism.

### 3.2 Services and seams

- **GameContext** — the per-execution context handed to every action. Carries
  RNG, player/stats, and optional **GameServices**: `ICoroutineRunner`,
  `IPromptService`, `ICutscenePlayer`. Pure interfaces, typed against core
  engine types (e.g. base `PlayableAsset`, not Timeline types) so the core
  assembly stays package-free.
- **GameManager** (MonoBehaviour): implements the runner + prompt service,
  holds the `DirectorPlayer` (cutscene implementation), builds GameServices in
  `Awake`, draws through the SessionDriver, and returns to menu on completion.
- **GamePanel / prompt overlay**: dumb views; buttons and prompt options are
  built at runtime by code.

### 3.3 Scenes are generated code, not hand-wiring

`Assets/Editor/SceneBuilder.cs` + `SampleContentBuilder.cs` are the **single
source of truth** for scene structure and starter content. Menu bar
**TruthCardGame → Build Scenes** regenerates all scenes (MainMenu / GameSetup /
Game), registers build settings, and rebuilds sample content. This runs both in
the editor and headlessly (`-executeMethod …`). Everything visual is therefore
reviewable as C# diff.

Sample content generated: starter deck (~7 cards incl. debug/stat/cutscene/
choice cards), "Relaxing" and "Intense" sessions with multiple phases and
ending phases, ending card ("The End"), `Cutscene_Intro` action asset.

### 3.4 Assembly layout & packages

- `Assets/Scripts` → `TruthCardGame` asmdef (runtime); `Assets/Editor` → its
  own asmdef referencing TruthCardGame + packages; EditMode test asmdef under
  `Assets/Tests/EditMode`.
- Packages added beyond defaults: **com.unity.timeline 1.8.13**,
  **com.unity.cinemachine 3.1.7**, com.unity.test-framework, com.unity.ugui
  (the last two pinned because the fresh-import manifest lacked uGUI and broke
  all UI scripts). Version note: Timeline 1.8.1 / Cinemachine 3.1.3 do NOT
  compile against 6000.5 (obsolete-API-as-error); 1.8.13/3.1.7 do.
- Cinemachine gotcha worth recording: CM3 renamed everything — namespace is
  `Unity.Cinemachine` (not `Cinemachine`) and the component is
  `CinemachineCamera` (not `CinemachineVirtualCamera`).

### 3.5 Testing posture

- **22 EditMode tests**, all green: executor filtering/sequencing, seed
  actions, CutsceneAction yield/completion behavior, ChoiceAction branching +
  failure paths, SessionDriver phase progression/live-modifier math/early
  advance/session completion.
- Known gap, acknowledged twice during playtesting: **scene-glue wiring is
  what EditMode can't cover**, and it produced the only two runtime bugs so far
  (invisible buttons due to a LayoutGroup preferred-height bug; GameManager
  forgetting to pass `prompts: this` into GameServices). A small **PlayMode
  test** booting the Game scene and exercising one choice card would have caught
  both — it's on the shortlist.

---

## 4. History: what's been done, in order

1. **Scaffold**: agents.md, unity-cli notes, git init + remote, folder rules.
   (Unity install itself was painful — Hub installs kept corrupting; solved via
   elevated CLI silent install of 6000.5.9f1.)
2. **Core data + executor**: Card/CardDeck/CardAction, DebugAction,
   StatIncreaseAction, CardExecutor with tag filters and blocking/continuous
   semantics, Player/PlayerStats, GameContext.
3. **Screens v1**: MainMenu, GameSetup with per-tag toggles, Game HUD panel;
   editor builders generating everything.
4. **First successful headless import** on 6000.5.9f1; uGUI/test-framework pin
   fix; 8/8 tests.
5. **Six-ticket plan** written to disk as individual tickets, then executed:
   - Ticket 1: GameContext services seam (`fd39dbc`)
   - Ticket 2: CutsceneAction + DirectorPlayer + Timeline/Cinemachine + scene
     wiring (`8afad52`) — *remains "In progress": the PoC timeline asset itself
     is hand-authored in the Timeline window*
   - Ticket 3: ChoiceAction + prompt overlay (`dd49680`)
   - Ticket 4: Session/Phase/Library + sample sessions (`0f62073`)
   - Ticket 5: SessionDriver with live modifier math (`7621100`)
   - Ticket 6: session picker screen, settings length slider, driver-driven
     loop (`a3be21d`)
6. **Playtest round-trip fixes**: invisible Draw-button row (LayoutGroup
   height bug, `a59ad03`) and missing IPromptService injection (`ca10dd6`).
   Both found by human Play-mode testing, confirming the scene-glue testing gap.
7. Everything pushed to GitHub; `main` == `origin/main`.

---

## 5. Design decisions already made (and why)

| Decision | Rationale |
|---|---|
| Actions = ScriptableObjects with coroutine `Execute` | Extensible set (~8–10 kinds) without executor changes; content-authorable in inspector |
| Blocking vs continuous flag on actions | Lets cards layer concurrent effects (toy buzz + voiceover) while keeping ordering for blocking steps |
| Sessions' tags ≠ phases' tags | Session tags are top-level "game type" selectors the user picks from; phase tags are draw filters. Different vocabularies, no cross-access needed |
| Length modifier is live-read, not baked at session start | Future "choice" cards may change pacing mid-session; clamp math is `max(1, Round(base × modifier))` |
| Endings via authored ending-phase + ending card | Reuses existing infrastructure instead of inventing a one-off game-over mechanism |
| ChoiceAction branches to exactly one child action | Simplest correct model for user-prompted branching; blocking by nature |
| Cutscenes via Unity Timeline + Cinemachine | Native, authorable-in-editor, handles camera+animation+audio sync as one unit rather than fragile stacks of non-blocking sub-actions |
| Scenes generated from Editor code | Scene structure reviewable/diffable as C#; idempotent regeneration; no hand-wired drift |
| Audio decision **deferred** | Recorded as an open question (see §7) |

---

## 6. Open items / immediate next steps

1. **Finish ticket 2**: assign the hand-authored timeline (user created
   `testtime.playable`) to `Cutscene_Intro.asset`'s timeline field; verify the
   cutscene plays in Play mode. Then flip ticket 2 status to Done.
2. **PlayMode test** covering scene-glue: boot Game scene, exercise one choice
   card end-to-end. Would have caught both playtest bugs.
3. **Authoring tools** — explicitly flagged as "very important, coming soon."
   With ~1000 cards planned, manual SO creation won't scale. Needs: create
   actions, duplicate-and-mutate an action, create cards, duplicate-and-mutate
   cards, bulk operations. Format decision pending: stay pure-SO-inspector, or
   move source content to text (JSON/CSV) imported into SOs. This is probably
   the highest-leverage next investment.
4. **Bluetooth toy plugin** — designed for (an action kind among the 8–10), not
   started. Will need a new dependency discussion when wired (rule 4).

---

## 7. Open questions deliberately deferred

- **Audio strategy**: how voiceover/SFX integrate with cutscenes vs standalone
  actions; implications (mixer groups, addressables-size concerns, licensing of
  TTS vs recorded VO) were discussed but no call made yet.
- **Platform/target**: PC-first assumed implicitly; mobile/BT-le constraints may
  matter once the toy plugin lands.
- **Save/persistence**: nothing persists across runs (SessionConfig is a static
  scene-handoff by design). Player stats exist but nothing consumes them long
  term.
- **Content pipeline at scale**: tagging vocabulary governance (tags are free-
  form strings today), deck organization across ~1000 cards.
- **Minigame action kind**: mentioned in the original vision; no design yet.

---

## 8. How to verify a checkout

```bash
# Compile check (headless):
"C:/Program Files/Unity/Hub/Editor/6000.5.9f1/Editor/Unity.exe" \
  -projectPath . -batchmode -quit -logFile -

# Run tests (22 EditMode tests):
unity test . --mode EditMode        # or Test Runner window in-editor

# Regenerate all scenes + sample content (idempotent):
unity run . -batchmode -executeMethod TruthCardGame.EditorTools.SceneBuilder.BuildAllScenes ...
```

In-editor: open `Assets/Scenes/MainMenu.unity`, press Play, pick a session,
draw cards. Expected full loop: session picker → phased draws → choice card
shows overlay buttons → cutscene card plays timeline (once assigned) → ending
phase → "Session complete" → back to menu.

## 9. Honest state summary

- Code: compiles clean, 22/22 unit tests green, all six tickets committed.
- Runtime UX: verified by human playtest through the main loop including the
  choice card; cutscene playback awaits timeline assignment (the one remaining
  ticket item).
- Biggest risks going forward: (a) content authoring at ~1000-card scale
  without tooling, (b) zero coverage on scene glue until PlayMode tests exist,
  (c) tag vocabulary growing ungoverned as content grows.
