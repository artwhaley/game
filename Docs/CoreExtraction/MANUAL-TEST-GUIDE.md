# Manual Test Guide — Extraction Milestone 0.1

Human verification pass for what automation could not cover (headless
environment): the real Unity scene flow and interactive WPF play. Work top to
bottom; everything here maps to columns marked BLOCKED/PARTIAL in
`02-parity-report.md`.

---

## 0. Prerequisites

| Check | Expected |
|---|---|
| `dotnet --list-sdks` | .NET 10 SDK present |
| Unity Hub | 6000.5.9f1 installed |
| Repo | `repo/Game.Workbench.sln`, `repo/Assets/Scenes/*.unity` exist |

## A. Automated suites (fast sanity before manual passes)

```bash
cd repo
dotnet test Game.Workbench.sln          # expect: 85/85 passed
```

Unity: open the project (Hub → Add → this folder), then **Window → General →
Test Runner**:
- EditMode tab → Run All → **8/8**
- PlayMode tab → Run All → **2/2**

Headless equivalent (no editor needed):

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.5.9f1\Editor\Unity.exe" -batchmode `
  -projectPath <repo> -runTests -testPlatform EditMode `
  -testResults out.xml -logFile out.log
# repeat with -testPlatform PlayMode
```

---

## B. Unity scene pass (~10 minutes)

Open `Assets/Scenes/MainMenu.unity` and press **Play**. Keep the Console
visible.

### B1. Main menu
1. Title, **Start Game**, **Settings** render and respond.
2. Settings opens a dialog with a slider labeled **0.5x–3.0x**, current value
   shown (default 1.0x); dragging updates the label live; Close works.

### B2. Session pick
3. Start Game → session picker shows **Relaxing** and **Intense** (from
   SessionLibrary asset).

### B3. Automatic first draw + pacing
4. Pick **Relaxing** → Start Game.
5. **First card starts automatically** (status "Executing…", Draw Next
   disabled) — matches baseline `Start()` behavior.
6. When it finishes, status becomes **Done.** and Draw Next re-enables.
7. Each subsequent click draws **exactly one** card; rapid double-clicking
   must never start two cards (Core busy guard).

### B4. Phases (Relaxing = solo/truth warmup)
8. Warm Up draws 2–3 cards (target = random 2–3 scaled by length setting),
   then advances to Teasing, then Wind Down. Phase titles aren't displayed in
   the Unity UI — infer from card tag changes (solo → solo+truth → ending).

### B5. Choice card (play **Intense** to guarantee it)
9. Start an **Intense** session. Within Build/High Intensity phases the
   **Face the Crowd** card can draw: a full-screen prompt appears with two
   buttons (**Own it** / **Shrug it off**).
10. While the prompt is open, Draw Next stays disabled.
11. Click **Own it** → overlay closes once, Console shows
    `courage +1 (now …)`; the card completes normally.
12. Redraw until you get it again and pick **Shrug it off** → returns
    instantly (continuous child dispatched in background; card still completes).

### B6. Cutscene card (known pre-existing gap — verify it degrades cleanly)
13. Drawing **A Familiar Face** logs
    `[TruthCardGame] CutsceneAction has no resource assigned.` — **expected**
    (baseline left the Timeline unassigned; repo Ticket 2 open). The card
    no-ops and completes; the session continues. No hang, no exception spam.

### B7. Live length modifier (mid-phase!)
14. During a phase (after ≥1 completed card), open Settings and drag to
    **3.0x** → the phase now requires noticeably more cards.
15. Drag to **0.5x** → the phase can complete on the very next card.
    (Target = max(1, Round(base × modifier)) evaluated live.)

### B8. Completion beat
16. Final phase draws **The End** → panel shows **Session complete /
    Returning to menu…**, waits ~1.6 seconds (scaled time), returns to
    MainMenu automatically.

### B9. Console integrity sweep
17. Across both sessions: **no Missing Script**, no broken-reference errors,
    no unhandled Task exceptions, no unexpected red errors other than the
    documented cutscene message in B6.

---

## C. WPF standalone pass (~10 minutes)

Launch (or let the assistant start it):

```powershell
repo\DotNet\Game.ReferenceHost.Wpf\bin\Debug\net10.0-windows\Game.ReferenceHost.Wpf.exe
```

1. Window opens; execution log shows the fixture path loaded successfully;
   session dropdown lists **Parity Session**.
2. **Start / Restart Session** → first card (**Warm Two**) begins
   automatically — same auto-first-draw contract as Unity.
3. Status cycles Idle → Executing → Done; **Draw Next** enabled only between
   cards.
4. Second click draws **Warm One**: log shows courage +2 stat line and a
   background `warming up` entry that does NOT block the card completing.
5. Third click draws **Crossroads**: two buttons (**Brave** / **Cautious**)
   appear under Prompt/cutscene; Draw Next is disabled while waiting.
   - **Brave** → log shows brave +5.
   - Restart and choose **Cautious** on a later run → nothing happens (null
     child no-op), card completes.
6. Fourth click draws **The End** → **CUTSCENE / cs:finale** placeholder with
   **Complete Cutscene** button; Draw Next stays disabled until clicked;
   clicking finishes the card.
7. Then **SESSION COMPLETE** appears in log/status; Draw Next disables.
8. **Live length control:** restart, set slider to 3.0x before the Warm Up
   phase finishes → phase needs 6 draws; set 0.5x → ends on next card.
   Label always shows the multiplier.
9. **Restart safety:** start a session, get to a pending choice or cutscene,
   hit **Start / Restart** → old wait cancels silently, fresh session begins,
   no crash, log notes the cancellation.
10. **Close during pending cutscene** → window closes cleanly, process exits 0.
11. Log stays bounded (oldest entries trimmed past ~400 lines).

---

## D. Recording results

After each section, mark PASS/FAIL and anything surprising, then append the
outcome to `Docs/CoreExtraction/02-parity-report.md`'s manual columns (Unity
manual / WPF manual) so the report goes fully green — or documents the miss.

Expected total time: ~20–25 minutes.
