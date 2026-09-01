# Milestone C Testing Methodology

## 1. Purpose and acceptance standard

This procedure validates Milestone C in two layers:

1. **Part A - WPF authoring and runtime:** author a realistic card library, play it through genuinely random multi-card phases, exercise graph control flow and all action families, test profile eligibility, and preserve reproducible evidence.
2. **Part B - Unity integration:** run the same database, profiles, seeds, and scripted choices in Unity and compare normalized engine behavior.

The primary acceptance scenario must resemble actual use of the tool. It uses four substantial phases with overlapping semantic card pools and weighted random selection. A small deterministic diagnostic session exists only for control-flow paths that ordinary random play cannot reliably force. It is not presented as normal gameplay or as the card-coverage test.

Milestone C is complete only when all automated tests pass, the realistic seeded runs collectively cover all cards and required branches, the random-distribution audit passes, all negative and cancellation scenarios have evidence, WPF and Unity traces agree, and the final database passes SQLite integrity checks.

The timestamped WPF log is supplemental diagnostic evidence. The parity oracle is the normalized structured trace in section 10.

## 2. Safety rules and preflight

### 2.1 Files and processes

- Work on a backup or an explicitly designated test copy before changing content.
- Close the WPF application and every SQLite client before copying, checking, or committing a database.
- Never edit the canonical database with ad hoc SQL except for a planned schema migration. Use a disposable copy for malformed-data fixtures.
- Do not delete or overwrite the user's local profile database. The content `--db` argument does not redirect the profile database.
- After every authoring batch, close all writers and confirm that no `-wal` or `-shm` sidecar remains.

### 2.2 Starting state

The repository database is expected to begin at schema version 10 with no authored sessions, phases, or cards. Starter-content deletion and the loader-test relaxation are already complete; do not repeat those changes.

Record the branch, commit, database checksum, and automated-test result. Then run:

```sql
PRAGMA integrity_check;
PRAGMA foreign_key_check;
SELECT MAX(version) FROM core_schema_migration;
```

Expected before migration:

- `integrity_check` returns `ok`.
- `foreign_key_check` returns no rows.
- `MAX(version)` in `core_schema_migration` is 10.

Launch the WPF application once against the designated content database, allow the Milestone C migration to complete, close it, and rerun the checks. Commit the schema migration separately before authoring content. Expected `MAX(version)` after migration: 11. `PRAGMA user_version` is not the authoritative schema indicator for this repository and must not be used for this gate.

### 2.3 Test profiles

Use the profile UI; do not directly alter or delete rows in the real profile database.

| State | Kinks | Equipment | Capabilities | Use |
|---|---|---|---|---|
| Permissive coverage | romance = Love; playful = Like; intense = Torture; humiliation = Like | blindfold owned | vibrate and rotate available | Realistic positive-path, seed-discovery, and canonical playback runs |
| Restrictive diagnostic | romance = Love; playful = Like; intense = Torture; humiliation = Don't Consent | blindfold not owned | vibrate and rotate unavailable | Eligibility and filtering tests |

Restore the permissive state at the end. Automated tests, not the user's profile database, own malformed, missing, and wholly unconfigured profile cases.

The canonical playback harness must build a deterministic permissive `CardSelectionProfile` from canonical content: configure every kink and enable every required equipment item and capability. It must never load the user's profile. An empty profile is not a valid permissive profile because kinked cards are correctly ineligible.

## 3. Canonical catalog

Author these items before the cards. Resources are managed under **Inspector -> Resources**.

### 3.1 Session types and tags

- Session types: `party`, `diagnostic`, `tech-demo`.
- Gameplay card tags: `truth`, `dare`, `physical`, `talkative`, `cozy`, `spicy`.
- Diagnostic addressing tags: `fixture-warmup`, `fixture-return-node`, `fixture-tech`, `fixture-loop`, `fixture-return-action`, and `fixture-encore`.
- Negative-test tag: `no-card`; create it but assign it to no card.

Gameplay phases query only gameplay tags. The six `fixture-*` tags are assigned to Cards 1, 2, 9, 13, 19, and 20 respectively and are used only by the diagnostic and tech-demo sessions. Do not create one fixture tag or one phase per card.

### 3.2 Eligibility and resources

- Kinks: `romance`, `playful`, `intense`, `humiliation`.
- Equipment: `blindfold`.
- Toy capabilities: `vibrate`, `rotate`.
- Cutscene resource: `arrival`.
- Toy patterns: `pulse`, `wave`, `steady`.
- Dialog tags: `tease`, `praise`.
- Create exactly three distinct, non-empty snippets for each dialog tag.

`happiness` is the seeded reference definition used by the runtime. Verify it exists; do not recreate it through the catalog UI.

### 3.3 Sessions

Create exactly four sessions:

| Session | Type | Purpose | Requirements |
|---|---|---|---|
| House Party Standard | party | primary realistic random-play acceptance session | none beyond card-level requirements |
| House Party After Dark | party | same realistic phase graph with a different weighting profile | none beyond card-level requirements |
| Control Flow Lab | diagnostic | forced transfers, returns, recursion, and stack cleanup | none beyond card-level requirements |
| Tech Demo Fixture | tech-demo | session-type filtering and a minimal toy run | capability `vibrate` |

Use distinct weighting configurations:

| Session | Love base/gain | Like base/gain | Torture base/unhappiness gain |
|---|---:|---:|---:|
| House Party Standard | 3.0 / 2.0 | 1.0 / 0.5 | 0.5 / 4.0 |
| House Party After Dark | 1.5 / 1.0 | 2.5 / 1.0 | 1.0 / 4.0 |

Record the saved values in the evidence. The diagnostic and tech-demo sessions may use defaults because they do not test card-weight distribution.

Both party sessions use the four realistic phases in section 5. This gives Play-by-Type at least two real party candidates and tests that the session's weighting configuration affects card selection without changing the authored phase pools.

## 4. Canonical cards

### 4.1 Authoring rules

- Give every card a unique, non-empty body that identifies its purpose.
- Assign the gameplay tags listed below. Assign diagnostic tags only to the six cards identified in section 3.1.
- New cards begin with `WaitForContinue -> IncrementProgress +10`. Delete both generated actions before creating the listed sequence.
- Every card ends with exactly one `IncrementProgress +1`. The small common increment makes a realistic phase draw several cards before advancing.
- Use short durations of 0.5 to 2.0 seconds so asynchronous behavior remains observable without making sessions tedious.
- Add `WaitForContinue` and `WaitForAll` only where listed.
- Every action that addresses a toy capability must have a matching card capability requirement.

### 4.2 Card matrix

| # | Name and body intent | Gameplay tags / eligibility | Required action sequence |
|---:|---|---|---|
| 1 | Soft Landing - introductory romance prompt | `truth`, `cozy`; kink romance; diagnostic `fixture-warmup` | blocking Dialog; Modify `happiness` +5 nonblocking; WaitForContinue; IncrementProgress +1 |
| 2 | Fireside Voices - tagged-dialog prompt | `talkative`, `cozy`; kink playful; diagnostic `fixture-return-node` | DialogFromTags `tease` blocking; DialogFromTags `praise` nonblocking; StatIncrease `encouragement` +1 nonblocking; WaitForAll; WaitForContinue; IncrementProgress +1 |
| 3 | Pick a Spark - three-way choice | `dare`, `talkative`; kink playful | PromptChoice: A = Dialog; B = short nonblocking Debug, WaitForAll, Dialog; C = StatIncrease `boldness` +1, Dialog; common WaitForContinue; IncrementProgress +1 |
| 4 | Wave Check - direct pattern | `physical`; kink playful; capability vibrate | SetPattern vibrate/wave; Delay 1.0 blocking; Dialog; WaitForContinue; IncrementProgress +1 |
| 5 | Pulse Timer - nonblocking timed pattern | `physical`; kink playful; capability vibrate | TimedPattern vibrate/pulse 1.5 nonblocking; WaitForContinue; IncrementProgress +1 |
| 6 | Pacing Beat - reusable sequence source | `talkative`, `cozy`; kink romance | Dialog; WaitForContinue; IncrementProgress +1 |
| 7 | Turning Point - blocking timed pattern | `physical`, `spicy`; kink intense; capability rotate | TimedPattern rotate/steady 1.5 blocking; Dialog; WaitForContinue; IncrementProgress +1 |
| 8 | Eyes Closed - equipment gate | `truth`, `cozy`; kink romance; equipment blindfold | Dialog; WaitForContinue; IncrementProgress +1 |
| 9 | Steady Signal - capability gate | `physical`; kink playful; capability vibrate; diagnostic `fixture-tech` | SetPattern vibrate/steady; WaitForContinue; IncrementProgress +1 |
| 10 | Hard Boundary - non-consent gate | `talkative`, `spicy`; kink humiliation | Dialog; Debug with 0.25-second blocking delay; StatIncrease `boundary_checks` +1 nonblocking; WaitForContinue; IncrementProgress +1 |
| 11 | Mood Dip - negative reference change | `truth`, `spicy`; kink intense | Modify `happiness` -20; Dialog; WaitForContinue; IncrementProgress +1 |
| 12 | Arrival Replay - blocking and nonblocking cutscene | `talkative`; kink playful | Cutscene `arrival` blocking; Cutscene `arrival` nonblocking; WaitForAll; WaitForContinue; IncrementProgress +1 |
| 13 | Synchronized Pulse - explicit join | `physical`; kink playful; capability vibrate; diagnostic `fixture-loop` | TimedPattern vibrate/pulse 1.5 nonblocking; WaitForAll; WaitForContinue; IncrementProgress +1 |
| 14 | Quiet Delay - delay/debug join | `talkative`, `cozy`; kink romance | Delay 1.0 blocking; short Debug nonblocking; WaitForAll; WaitForContinue; IncrementProgress +1 |
| 15 | Command Supersession - replacement and coexistence | `dare`, `physical`, `spicy`; kink intense; capabilities vibrate and rotate | SetPattern vibrate/steady; TimedPattern rotate/pulse 1.5 nonblocking; TimedPattern vibrate/wave 1.5 nonblocking; SetPattern vibrate/pulse; WaitForAll; WaitForContinue; IncrementProgress +1 |
| 16 | Mixed Signals - multi-kink weighted choice | `dare`, `talkative`, `spicy`; kinks playful and intense; capability vibrate | PromptChoice: A = Dialog nonblocking, WaitForAll, StatIncrease `mixed_choice_a` +1; B = TimedPattern vibrate/pulse 1.0 nonblocking, Dialog blocking, WaitForAll; common WaitForContinue; IncrementProgress +1 |
| 17 | Deferred Word - nonblocking delay | `talkative`; kink playful | Delay 1.0 nonblocking; WaitForAll; Dialog; WaitForContinue; IncrementProgress +1 |
| 18 | Joined Conversation - nonblocking dialog | `talkative`, `cozy`; kink romance | Dialog nonblocking; WaitForAll; WaitForContinue; IncrementProgress +1 |
| 19 | Honest Return - return-lab payload | `truth`, `talkative`; kink romance; diagnostic `fixture-return-action` | Dialog; StatIncrease `boldness` -1 nonblocking; WaitForContinue; IncrementProgress +1 |
| 20 | Encore - recursion-lab payload | `dare`, `spicy`; kink intense; diagnostic `fixture-encore` | Dialog; WaitForContinue; IncrementProgress +1 |

Card 15 is reachable by a challenge query using ANY `dare`, `physical`, or `spicy`, and it deliberately tests replacement on vibrate while rotate continues independently. Card 16 is the mixed-kink weighting case.

### 4.3 Action Blocks

Action Blocks cannot be created before a source sequence exists. After Cards 2 and 6 exist:

1. Save Card 6's complete sequence as `Pacing Beat`.
2. Save a StatIncrease-plus-Dialog subsequence as `Encourage`.
3. Insert each block twice into disposable authoring fixtures.
4. Confirm every insertion receives fresh action and nested-option identifiers.
5. Edit one clone and confirm the stored block and the other clone do not change.
6. Confirm incompatible-scope actions are rejected.
7. Delete the disposable fixtures before the final checkpoint.

### 4.4 Action coverage check

Before running sessions, verify the authored content collectively contains every runtime action definition: Dialog, DialogFromTags, Delay, Cutscene, SetPattern, TimedPattern, Debug, ModifyReference, IncrementProgress, StatIncrease, PromptChoice, WaitForContinue, WaitForAll, Return, PhaseGoto, SessionGoto, and EndSession. The Return action is exercised in F3A. The separate graph Return node is exercised in F3B and F4; it does not count as the Return action definition.

## 5. Primary realistic random-play session

### 5.1 Phase design

Create four shared gameplay phases. Each phase draws repeatedly from a semantic pool. Selection is random and weighted by the active session and profile. Repeats are allowed because the current selector has no repeat-suppression rule.

| Phase | Query | Cards per normal visit | Cumulative progress threshold | Intended candidate pool under permissive profile |
|---|---|---:|---:|---|
| P1 Arrival | ALL `cozy` | 5 | 5 | 1, 2, 6, 8, 14, 18 |
| P2 Connection | ANY `truth`, `talkative` | 7 | 12 | 1, 2, 3, 6, 8, 10, 11, 12, 14, 16, 17, 18, 19 |
| P3 Challenge | ANY `dare`, `physical`, `spicy` | 8 | 20 | 3, 4, 5, 7, 9, 10, 11, 13, 15, 16, 20 |
| P4 Aftercare | ANY `cozy`, `talkative` | 5 | 25 | 1, 2, 3, 6, 8, 10, 12, 14, 16, 17, 18, 19 |

For each phase, wire:

`Entry -> DrawCard -> progress threshold check`

- False loops to `DrawCard`.
- True executes `PhaseGoto next`.
- P4 uses `PhaseGoto done`.

For both party sessions, wire `Start -> P1`, each projected `next` exit to the following phase placement, and `P4.done -> SessionEnd`.

The expected normal run is 25 randomly selected cards across four meaningful pools. It must visibly tolerate repeats. Do not add fixture tags to these queries, force a particular card order, or claim that one session is guaranteed to draw every card.

Before seed discovery, use the selector's eligibility diagnostics to confirm that each listed candidate pool is correct under the permissive profile and that every canonical card is reachable from at least one gameplay phase.

### 5.2 Real-selector seed discovery

Exact useful seeds cannot be known honestly until the final database IDs, insertion order, profile, weights, and snippets exist. Generate and freeze them from the completed database as follows:

1. Target `House Party Standard` directly with the permissive generated profile.
2. Sweep seeds **61001 through 61999** using the real runtime selector, automatic Continue input, and choice policy A.
3. Choice policy A selects option A for every PromptChoice. Policy B selects B. Policy C selects C when present and otherwise A.
4. For every run, record phase, complete candidate set, effective weight for each candidate, selected card, dialog snippet, progress, and choice.
5. Apply deterministic greedy set cover: select the seed covering the most still-uncovered card IDs; break ties by choosing the lower seed. Continue until all 20 cards have appeared.
6. The covering set must contain no more than eight sessions. If eight sessions do not cover all cards, fix an unreachable or excessively rare card, rebalance the pool/weights, or increase draws per phase. Do not paper over the problem with hundreds of seeds.
7. From seeds where Card 3 appears, replay one with policy B and one with policy C. From a seed where Card 16 appears, replay with policy B. These branch replays are in addition to the card-covering set only when the selected coverage seeds cannot carry the required policy.
8. If the selected runs do not cover all three snippets for both dialog tags, select the lowest additional seed or seeds from the same sweep that complete snippet coverage.
9. Replay every selected seed and policy twice. The card sequence, candidate/weight records, snippets, actions, and final state must match exactly.

Save the result as the **Realistic Seed Manifest**:

| Seed | Choice policy | Cards newly covered | Choice branches | Snippets newly covered | Trace artifact |
|---:|---|---|---|---|---|
| _filled from the final database_ | | | | | |

The seed manifest is a release artifact. After any content, stable-ID, ordering, profile, or weighting change, regenerate it rather than assuming old seeds still mean the same thing.

### 5.3 Randomness and weighting audit

Card coverage proves reachability, not correct randomness. Run a separate deterministic sweep of seeds **62001 through 63000** for each party session.

For every draw, calculate the expected probability from the trace's eligible candidate set and effective weights. For each card within each phase, accumulate:

- observed selections `O`;
- expected selections `E = sum(p)`;
- variance `V = sum(p * (1 - p))`.

Flag a card/phase result when `abs(O - E) > max(5, 4 * sqrt(V))`. Because the seed range is fixed, this audit is reproducible; do not repeatedly rerun until it passes. Investigate candidate construction, weighting, or PRNG use when it fails.

Also verify:

- every eligible card appears at least once in the 1,000-session sweep;
- no ineligible card is selected;
- at least one repeated card occurs within a phase and completes normally;
- the same seed is deterministic;
- different seeds produce more than one sequence;
- Standard and After Dark produce distributions consistent with their different coefficients;
- Card 16's mixed playful/intense weight uses the documented combination rule at multiple happiness values.

## 6. Deterministic control-flow lab

This session tests graph mechanics that random gameplay cannot reliably force. It addresses only six existing cards with fixture tags; it is not the gameplay or card-coverage example.

Every exact fixture query below means ALL = the named `fixture-*` tag with no ANY tags. Confirm it resolves to exactly one card before running the lab.

### 6.1 Phase graphs

Create five phases:

- **F1 Warm-up:** check `happiness >= 55` before drawing `fixture-warmup`. False modifies happiness +5, waits, and loops to the check. True draws the exact fixture and uses `PhaseGoto open-door`.
- **F2 Main:** draw `fixture-loop`, then execute an ActionNode containing StatIncrease `loop_count` +1 nonblocking, WaitForAll, and a nested PromptChoice. `Continue` exits normally; `Early` executes `PhaseGoto early-out`. After normal completion, check `loop_count >= 3`. True executes `PhaseGoto finish`; false enters a PhaseDecision where `One More` returns normally to the draw and `Wrap` executes `PhaseGoto early-out`.
- **F3A Return Action:** draw `fixture-return-action`; an ActionNode performs Dialog followed by the Return action definition. This tests `ReturnInstanceDefinition`.
- **F3B Return Node:** draw `fixture-return-node`; then execute the graph Return node. This separately tests graph-node return behavior.
- **F4 Encore:** check session-global `encore_once >= 1`. On first entry, draw `fixture-encore`, then execute one sequence in this order: Dialog; StatIncrease `encore_once` +1; `PhaseGoto encore`; EndSession. On recursive entry, the true branch executes a graph Return node. That Return pops the recursive continuation and resumes the first F4 sequence after `PhaseGoto`, where EndSession clears the remaining stack.

The F4 order is essential. `Return -> End` is not a valid graph because Return resumes a captured continuation and has no normal output to wire to EndSession.

### 6.2 Session graph and scripted runs

Wire `Start -> F1 -> F2`. Wire `F2.finish` to a SessionDecision with:

- `Return action detour`: Dialog, then SessionGoto F3A;
- `Return node detour`: Dialog, then SessionGoto F3B;
- `Call it a night`: EndSession.

The decision's normal continuation goes to F4 after either returning detour. Wire `F2.early-out -> F4` and `F4.encore -> F4`.

| Seed | Scripted path | Required observation |
|---:|---|---|
| 42001 | Continue and One More until threshold; Return action detour | three F2 iterations, Return action resumes, one recursion, clean EndSession |
| 42002 | Continue and One More until threshold; Return node detour | graph Return resumes, one recursion, clean EndSession |
| 42003 | choose Early on first F2 iteration | nested PhaseGoto bypasses remaining F2 flow and reaches F4 |
| 42004 | normal F2; choose Call it a night | EndSession terminates directly and clears stack/background work |
| 42005 | choose Wrap at the first PhaseDecision | decision-side PhaseGoto reaches F4 without another draw |

These are scripted-choice scenarios. A seed does not select UI choices and is not evidence that mutually exclusive branches ran.

## 7. Tech-demo and profile eligibility

Create T1 with an exact `fixture-tech` query followed by `PhaseGoto done`. Wire `Tech Demo Fixture` as `Start -> T1 -> SessionEnd`.

Run and trace these transitions:

1. Restrictive state: tech-demo is filtered because vibrate is unavailable.
2. Enable vibrate: tech-demo becomes selectable and completes.
3. Disable vibrate: it becomes filtered again.
4. In restrictive state, Card 8 is excluded for missing blindfold; enable blindfold and verify it becomes eligible in its normal phase pool.
5. Card 10 is excluded for Don't Consent to humiliation; change humiliation to Like and verify it becomes eligible.
6. Restore the permissive state and rerun one seed-manifest party session.

When playing by type `party`, verify both party sessions participate in uniform session selection before their separate card-weight rules apply. Filtering by `diagnostic` must return only Control Flow Lab. Filtering by `tech-demo` must return only Tech Demo Fixture when vibrate is available.

## 8. Negative, resilience, and teardown tests

Run malformed-data cases only against a disposable content database passed with `--db`. Do not mutate canonical content or the user's profile database.

Required cases:

- Phase query ALL = `no-card`, a valid tag assigned to no card: expect `NoEligibleCard` with a useful reason.
- Return action and Return node with an empty continuation stack: controlled failure, not a crash or hang.
- Unwired projected PhaseGoto and SessionGoto exits: validation or controlled runtime error.
- Dead-end ActionNode: graph validation identifies it.
- Non-yielding graph cycle: execution budget stops it deterministically.
- Invalid PhaseGoto/SessionGoto scope: controlled fixture or incompatible Action Block drop is rejected.
- Missing or invalid resource reference: isolated automated fixture or disposable-database change produces a named error.
- Unconfigured and malformed profiles: isolated automated tests only.

For nonblocking Delay, TimedPattern, Dialog, and Cutscene operations, separately test Halt, window close, and EndSession while work is active. Verify toy output stops, pending work is canceled or joined, no continuation resumes after termination, no unobserved exception appears, and a clean subsequent session can start.

With Card 15, verify that each later vibrate command replaces the earlier vibrate command while rotate continues independently. `WaitForAll` must wait for rotate and must not hang on superseded vibrate work.

## 9. Authoring UI regression pass

Exercise and record:

- create, rename, duplicate, copy/paste, folder move, and delete for sessions, phases, and cards;
- graph node creation, connection, deletion, projected sockets, and live socket rename propagation;
- portal-pair creation on several long realistic-session edges and persistence after reload;
- Action Block save, insert, clone independence, and invalid-scope rejection;
- Inspector validation for missing names, bodies, tags, resources, and graph wiring;
- resource creation and selection under Inspector -> Resources;
- restart and reload after each major authoring batch.

Delete all duplicate, copy/paste, folder, and invalid-content fixtures before the final checkpoint.

## 10. Evidence and normalized traces

### 10.1 Automated gates

Run the complete automated suite before migration, after migration, after each five-card batch, after session/phase graphs, after every defect fix, and at the final checkpoint.

The canonical playback harness must load the final database with its generated permissive profile and scripted inputs. It must complete both realistic party sessions, all five flow-lab scripts, and tech-demo without reading the user's profile.

### 10.2 Trace schema

Capture a machine-readable ordered trace containing stable data only:

- schema/version, seed, and choice policy;
- session, phase, card, edge, action, resource, and dialog-snippet stable IDs;
- candidates, exclusion reasons, effective weights, expected probabilities, and selection;
- scripted choices;
- action start, completion, cancellation, and transfer type;
- reference, stat, and progress values before and after mutation;
- continuation push, pop, resume, and clear events;
- final completion or controlled-error code.

Exclude wall-clock timestamps, host paths, UI prose, thread IDs, and semantically irrelevant concurrent ordering. Apply one documented canonical ordering rule where concurrent completions are genuinely unordered.

Capture at minimum:

- every entry in the Realistic Seed Manifest and its branch replays;
- flow seeds 42001 through 42005;
- one positive and one negative profile transition;
- every controlled error class;
- each cancellation mode;
- aggregate output from the fixed distribution sweep.

The WPF log, screenshots, and manual notes are supplemental evidence linked to the normalized trace run ID.

## 11. Batch verification and final checkpoint

After every authoring batch:

1. Save and close the app.
2. Run the automated suite.
3. Run `integrity_check`, `foreign_key_check`, and inspect `MAX(version)` from `core_schema_migration`.
4. Confirm there is no WAL/SHM sidecar.
5. Record database checksum, test output, and trace artifacts.
6. Reopen and verify content and graph wiring persist.

Expected final canonical authored counts:

- 4 sessions;
- 10 phases: 4 realistic gameplay, 5 control-flow, and 1 tech-demo;
- 20 cards;
- 2 Action Blocks;
- schema version 11;
- zero foreign-key violations;
- `integrity_check = ok`;
- no temporary duplicates, folders, malformed fixtures, WAL, or SHM files.

Verify every card has a unique non-empty body, the listed gameplay tags, requirements, and sequence; only six cards have diagnostic addressing tags.

## 12. Part B - Unity integration

Part B begins only after Part A's final database, Realistic Seed Manifest, and normalized traces are frozen.

### 12.1 Dependency and build proof

Do not treat SQLite packaging as complete merely because Editor play mode works. Before adopting a provider, prove:

- headless Unity compilation;
- Editor execution;
- at least one supported standalone player build;
- an IL2CPP build when IL2CPP is supported;
- native library placement for every target architecture;
- assembly-definition references;
- managed-code stripping/linker preservation;
- read-only packaged content-database access and writable profile-database placement;
- clean failure reporting for a missing, locked, or incompatible database.

Record the provider/version decision. Do not install a dependency until its platform proof and license are reviewed.

### 12.2 Unity work items

- Compile engine and adapters in Unity-compatible assemblies without WPF dependencies.
- Implement game setup/session selection, game manager, and game panel.
- Implement Unity adapters for dialog, cutscene, delay, toy, prompt choice, continue, logging, and cancellation.
- Show full card body and dialog text.
- Apply the same profile, session-type, equipment, capability, kink, weighting, and random-selection rules as WPF.
- Add EditMode tests for loading, selection, graphs, actions, transfers, and trace serialization.
- Add PlayMode tests for UI binding, input, asynchronous completion, Halt, scene unload, and quit.
- Run the canonical database in Editor and a standalone build.

### 12.3 Parity gate

Feed Unity the same frozen database, generated profiles, seeds, and scripted choices used for WPF. Compare normalized traces for:

- every Realistic Seed Manifest entry and branch replay;
- flow seeds 42001 through 42005;
- representative eligibility transitions;
- cancellation during delay, toy, dialog, and cutscene work;
- controlled error classes supported by the Unity host.

Run at least a reduced fixed-seed distribution audit in Unity and compare candidate sets, effective probabilities, and aggregate selections with WPF. Parity passes only when stable engine decisions, transfers, mutations, selected IDs, cancellation, and completion status match. Host presentation and timestamps may differ.

## 13. Exit report

The report must include:

- branch and commit;
- final database checksum and schema version;
- automated-test commands and results;
- completed Realistic Seed Manifest with trace links;
- distribution-audit results for both party sessions;
- control-flow, profile, negative, and cancellation scenario results;
- final catalog/content counts;
- SQLite integrity and foreign-key results;
- WPF and Unity build targets tested;
- SQLite provider/platform proof;
- every deviation, limitation, or waived scenario with an owner and follow-up issue.

Do not sign off from a single lucky playthrough or from the deterministic diagnostic lab. Sign off from reproducible random gameplay through realistic phase pools, compact seed-based card and branch coverage, fixed-seed distribution checks, control-flow diagnostics, resilience tests, authoring cleanup, and WPF/Unity trace parity.
