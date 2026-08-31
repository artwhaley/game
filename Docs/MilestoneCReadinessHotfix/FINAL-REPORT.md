# Final Milestone C Readiness Hotfix — Final Report

Date: 2026-08-30  
Branch: `milestone-c-readiness-hotfix`  
Base: `602bd205c8dd08625b41243a3e97bfbed7b5d9ac`  
Remote push: not performed

## Result

The corrective specification is implemented across portable content/Core,
SQLite authoring and loading, the WPF Workbench, and the existing Unity
ScriptableObject compatibility bridge. No schema migration was added; the
canonical schema remains v8.

- Timed Toy Pattern and Set Toy Pattern both require a configured Smart Toy
  Capability at creation and validation boundaries.
- Capability usage and safe deletion now account for Card relations, Session
  Type relations, Timed Toy Pattern actions, and Set Toy Pattern actions.
- Resource, Dialog Tag, and Capability deletion inspect the live unsaved Card
  buffer recursively, including nested PromptChoice sequences. Catalog
  undo/redo uses the same preflight.
- Resource, Toy Pattern, Capability, and Dialog Tag choices hot-refresh in every
  already-open root and nested ActionSequence editor without reloading or
  replacing a dirty Card buffer.
- PromptChoice is always blocking in registry metadata and validation.
  Wait For All is legal in nested PromptChoice sequences and executes as a
  snapshot barrier. SessionGoto remains illegal in descendants and the
  three-option maximum remains.
- `ContentReferenceValidator` recursively validates host-facing Resource,
  Capability, Dialog Tag, and Dialog Snippet references without SQL knowledge.
  It runs after SQLite reconstruction and again in `GameSessionEngine` before
  any host service can be dispatched.
- The SQLite loader accepts pre-kind legacy Resource rows only by inferring an
  empty kind from a typed action reference in the in-memory snapshot. The DB is
  not rewritten; missing resources and explicitly wrong nonempty kinds still
  fail loudly. This preserves the existing canonical cutscene row.
- Rehydrating a Set Toy Pattern editor now snapshots both persisted fields
  before bound setters synchronize, so selecting its Capability cannot clear
  its Pattern.
- Engine shutdown now fulfills its documented idempotence contract: normal
  completion and an explicit host cleanup share one cached toy-stop operation.
- Viewport hydration suppresses transient Nodify default values and reapplies
  the persisted viewport at dispatcher idle, preventing startup from replacing
  a saved pan position with `(0,0)`.
- Direct Dialog uses `IDialogService`; Dialog From Tags selects through the
  dialog RNG and then uses the same `IDialogService`. Neither uses the Cutscene
  host.

## True authoring-to-execution canary

`MilestoneC_TwentyCardTwoPhaseAuthoringCanary_ReloadsAndExecutesEveryCard`
creates a disposable schema-v8 SQLite database through production repositories
and authors:

- 20 Cards in searchable folders, each with a distinct owned ActionSequence;
- two Card Tags and two include-only Phases;
- a complete Start → Phase One → Phase Two → End Session graph;
- a Cutscene Resource, Toy Pattern Resource, Capability, Temperature, Dialog
  Tag, and tagged Dialog Snippet;
- PromptChoice with nonblocking Timed Toy Pattern, Dialog, nested Wait For All,
  and post-wait Dialog; Set Toy Pattern; Dialog From Tags; Cutscene; Delay;
  Modify Temperature; Stat Increase; direct Dialog; Debug; Continue; and
  Progress actions.

The test reloads through `GameContentSnapshotLoader`, constructs the real
`GameSessionEngine`, executes all 20 Cards exactly once in deterministic order,
crosses both Phase exits, verifies all recording host boundaries and state
mutations, and completes with exactly one toy shutdown.

## Verification

| Gate | Result |
|---|---:|
| `Game.Core.Tests` | 174 passed |
| `Game.Content.Sqlite.Tests` | 137 passed |
| `Game.Profile.Sqlite.Tests` | 11 passed |
| `Game.ReferenceHost.Wpf.Tests` | 41 passed |
| **Total** | **363 passed, 0 failed** |
| `dotnet build Game.Workbench.sln` | 0 warnings, 0 errors |
| Unity `6000.5.9f1` batch compile | exit 0, successful |
| Canonical `integrity_check` | `ok` |
| Canonical `foreign_key_check` | zero rows |
| Profile `integrity_check` | `ok` |

The successful Unity compile log is:

`C:\Users\artwh\AppData\Local\Temp\milestone-c-unity-compile-3.log`

The compile also corrected stale Unity adapter/test references to the removed
portable Deck, Tags, and exclusion-query fields. Deck cards now populate the
portable Card catalog, tags map to CardTag IDs/include-only queries, and legacy
Unity exclusion tags fail loudly rather than being silently discarded. This is
compatibility drift repair, not a redesign of Unity resource/toy bindings.

## Canonical database safety

The canonical database was user-authored before this hotfix and was preserved.
A byte-for-byte pre-hotfix backup exists at:

`C:\Users\artwh\AppData\Local\Temp\GameContent.milestone-c-readiness.20260830-164151.bak`

Final canonical facts before launch:

- size: 708,608 bytes;
- SHA-256: `42722C1CC46976D40AB520138CE66377BFE19771AFC37A5A33214A2DF5C00211`;
- schema ledger maximum: 8;
- Sessions: 1; Phases: 1; Cards: 3;
- Resources: 1; Dialog Tags: 0; Dialog Snippets: 0; Capabilities: 1;
- no WAL/SHM companion files.

The byte hash differs from the backup because SQLite rewrote pages while an
exact viewport value was restored after a startup-smoke diagnostic. Full
logical dumps compare with zero differing lines. A second logical comparison
while the final app was running also reported zero differing lines.

## Human handoff

The rebuilt WPF Workbench is running and responding as PID `36360`. Automated
WPF coverage and the disposable full execution canary are green. A human UI
authoring/playback canary is intentionally not claimed as completed; the
running app is left available for that acceptance pass.

