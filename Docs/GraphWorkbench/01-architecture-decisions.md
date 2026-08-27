# Architecture Decisions — Frozen for the Graph Workbench Stack

> Installed from packet `Game_GraphVM_WPF_Authoring_Stack/ARCHITECTURE-DECISIONS.md` (Ticket 01). Treat as authoritative for this stack; local commits only.

## 1. Canonical stores

### Authored game content
`Content/GameContent.db`

Replaced/updated with the game distribution.

### Persistent user data
Separate future `UserProfile.db`, located outside the install folder (Windows implementation should resolve under LocalApplicationData or an equivalent user-data location).

This stack must ensure no user-specific settings are stored in `GameContent.db`.

Do not implement Kink/Equipment/Smart-Toy profile UI yet. Preserve the separate-store boundary and the Session-spawn API described below.

## 2. Portable/runtime boundaries

```text
SQLite
  -> Game.Content.Sqlite
  -> GameContentDefinition snapshot
  -> Game.Core Graph VM
  -> WPF / Unity host services
```

- No SQL in Core.
- No WPF/Unity in Core or Game.Content.
- SQLite mapping remains provider-neutral (`DbConnection`).
- WPF uses Microsoft.Data.Sqlite.
- Unity gets no SQLite provider in this stack.

## 3. Session Type

`SessionType` is a simple authored category/entity (e.g. JOI, Stroker Toy, Butt Stuff).

A Session references exactly one SessionType.

A future launcher will filter eligible Sessions by profile/device requirements and randomly choose one Session of the selected type. Do not implement Equipment/Smart-Toy filtering in this stack; just establish SessionType data and a deterministic/random-select seam that can accept an eligibility predicate later.

## 4. Session graph

Node types:

- `SessionStart` — exactly one; owns Session metadata editing surface; one normal output.
- `PhaseReference` — references exactly one reusable Phase; exposes output sockets projected from that Phase's exported exits.
- `SessionDecision` — prompt + up to 3 options; every option owns an ordered Action sequence; one common normal output; each `SessionGoto` Action Instance creates one additional unique output socket.
- `SessionEnd` — terminal; no output; entering ends run and discards entire continuation stack.

All Session nodes have stable IDs. Session graph edges have stable IDs. A source output may have at most one outgoing edge. Multiple incoming edges to one node are allowed.

There is no PhaseSlot and no arbitrary session-length/slot count limit.

## 5. Phase graph

A Phase is one reusable entity consisting of metadata + exactly one low-level graph.

Node types:

- `PhaseEntry` — exactly one; cannot be deleted; owns Phase metadata editing surface; one normal output.
- `CardExecutor` — card-yield node; executes at most one card per user Advance budget; one normal output. If no eligible card exists, fail loudly in v1 rather than silently advance a Phase.
- `VariableCheck` — True/False outputs.
- `ActionNode` — ordered Action Instance sequence; one normal output.
- `PhaseDecision` — prompt + up to 3 options, each with ordered Action Instances; one common normal output.
- `Return` — terminal convenience node; semantic equivalent of executing Return control action.

A Phase has no implicit completion. It runs until its authored graph executes a Phase GOTO, RETURN, or EndSession. Reaching a dead end without a control transfer is a runtime graph error.

## 6. Phase metadata

Selecting `PhaseEntry` edits Phase-wide metadata, initially:

- stable ID (read-only in normal UI);
- title;
- Card-selection include/required tags;
- Card-selection exclude tags.

Legacy `MinCards` / `MaxCards` are obsolete runtime concepts after this migration.

## 7. Session metadata

Selecting `SessionStart` edits:

- stable ID (read-only);
- Session title;
- SessionType;
- current Session tags if retained.

Do not add speculative settings panels.

## 8. Phase exported exits

A Phase owns an ordered list of stable exported exits:

```text
PhaseExit
    Id      // stable identity
    Name    // editable display label
```

Multiple Phase GOTO Action Instances may reference the same PhaseExit ID. The high-level PhaseReference node shows exactly one output socket for that exit.

### Phase editor UX

At the top of the Phase graph pane show an unobtrusive Exits list/chip strip:

```text
Exits:  Success   Fail   Too Unhappy   [+]
```

- `+` creates a new exit.
- Right-click an exit -> Delete.
- Rename is allowed (stable ID preserves wiring).
- Phase GOTO instances never create exits themselves.
- A Phase GOTO editor is a **ComboBox rendered inline on its Action row inside the graph node**, listing existing Phase exits. No inspector trip is required.

### Shared-interface lock

Count Phase **placements** (`Session PhaseReference nodes`), not merely distinct Sessions.

- 0 or 1 placement: exit topology may be added/deleted.
- >1 placements: adding/deleting exits is blocked.
- Renaming exits remains allowed because identity/wiring is stable.
- Internal graph editing is still allowed while shared.
- Reassigning a GOTO instance among already-existing exits is allowed while shared.

When blocked, offer:

- `Make Unique` — clone Phase for the currently selected Session placement, rewire that placement to the clone, then apply the attempted topology edit.
- `Cancel`.

If Phase is open from Library with no current Session placement, Make Unique has no target; offer `Show Sessions` / Cancel instead.

## 9. SessionGoto

`SessionGoto` is a distinct Action Type from Phase GOTO.

It is valid in SessionDecision option Action sequences.

Every SessionGoto Action Instance owns a **unique output port** on its containing SessionDecision node immediately when created. It is not collapsed by name. Its label is an instance parameter and is rendered inline on the decision node/action row.

Deleting the SessionGoto Action deletes its owned port/edge as one undoable authoring command.

## 10. GOTO / RETURN semantics

There is one safe transfer concept: GOTO always saves continuation state.

### Phase GOTO

- Action references a PhaseExit ID.
- Runtime captures continuation.
- Current Session PhaseReference placement maps that PhaseExit to its projected Session output port.
- The Session graph edge resolves the destination.
- Destination may be PhaseReference, SessionDecision, or SessionEnd.

### SessionGoto

- Action owns a SessionDecision output port.
- Runtime captures continuation.
- Session edge resolves destination.

### RETURN

- Pops the newest continuation and resumes it exactly.
- Empty stack -> clear runtime execution error. No guessed fallback.

### Nested/self calls

A Phase may GOTO another placement of itself. This creates a fresh PhaseRun. The original remains suspended. RETURN restores the original exact PhaseRun.

## 11. Continuation contents

A continuation must be sufficient to resume exactly after the control-transfer Action:

- Session graph execution locus;
- current PhaseReference placement, if any;
- current PhaseRun object, if any;
- current low-level Phase node;
- current Action sequence identity;
- next Action index;
- active Card/action continuation if GOTO occurred inside a Card/choice sequence.

The continuation holds the PhaseRun object rather than copying/reconstructing it.

## 12. PhaseRun state

Every fresh entrance into a Session PhaseReference node creates a fresh PhaseRun:

- `PhaseProgress = 0`;
- fresh Phase-local variable state;
- fresh Card-selection RNG object/state;
- fresh Card draw/history/cooldown/repetition state (empty now; extensible later).

Suspending a PhaseRun freezes these because the object is no longer executing.

RETURN resumes the same object. RNG is not rewound globally; the suspended Phase's RNG simply was not consumed while another PhaseRun executed.

Session-global Temperatures and other future Session-global state are **not** copied/restored on RETURN.

## 13. RNG domains

Keep deterministic domains separate:

- Session selection RNG (outside a run);
- PhaseRun RNG factory/seed source;
- each PhaseRun's Card RNG + future card-history logic.

Do not share one global Card RNG across all phases after this migration.

## 14. Temperatures

Temperatures are Session-global runtime variables with authored definitions.

Initial definition:

```text
Happiness
Min = 0
Max = 100
Default = 50
```

Use a general `TemperatureDefinition`/dictionary model rather than hardcoding a Happiness field in Core.

Session spawn accepts optional temperature overrides:

```text
SessionSpawnOptions.TemperatureOverrides
```

If Happiness is absent, definition default 50 is used. This API allows a future persistent profile/out-of-session feature to inject Happiness without changing the Session architecture.

Temperature mutation clamps to the definition's min/max.

## 15. VariableCheck v1

Sources:

- `PhaseProgress`;
- named `Temperature`;
- named runtime/session stat already exposed by Core.

Operators:

- `<`
- `<=`
- `==`
- `!=`
- `>=`
- `>`

Compare against one literal numeric value.

Two internal outputs: True / False.

No expression language, AND/OR tree, variable-vs-variable expression, scripts, or arbitrary code in v1. Compose checks by wiring nodes.

## 16. Action Types vs Action Instances

Action Types are code-supported capabilities. Action Instances are owned authored occurrences with independent parameter values.

An Action Type registry in code provides:

- stable type key;
- display name;
- allowed owner scopes;
- default values;
- whether nonblocking is configurable;
- explicit WPF editor template/type.

Do not use reflection to synthesize a generic property grid.

Initial/migrated Action Types:

- Debug
- Stat Increase (legacy/general runtime stat)
- Increment Phase Progress
- Modify Temperature
- Play Cutscene
- Prompt Choice (preserve current Card-level choice capability; each option owns an Action sequence)
- Phase GOTO
- SessionGoto
- RETURN
- EndSession

Flow-control Actions are always blocking.

Not every Action Type is legal in every owner scope. In particular:

- Phase GOTO: Phase graph ActionNode / PhaseDecision-owned sequences only.
- SessionGoto: SessionDecision-owned sequences only.
- RETURN: Phase graph and SessionDecision scopes where a continuation may exist.

The toolbox filters by scope.

### Adding a new Action Type is an explicit vertical feature

This is intentionally the point where generic abstraction stops. A genuinely new Action Type normally requires a small, obvious set of code/schema work in one place:

1. portable Action Instance subtype + stable type key;
2. SQLite subtype table/mapping/migration;
3. Action Type registry entry/defaults/scope;
4. Core executor behavior **or** a semantic host-service request;
5. explicit WPF inline/inspector editor;
6. Unity host implementation when the Action performs presentation/device activity there;
7. focused tests.

Do not build a plugin language, EAV parameter bag, or reflection system to avoid this. The explicit vertical slice is a feature: new engine capabilities stay easy to find and reason about.

## 17. Card progress

There is no automatic Core post-card progress increment.

`Increment Phase Progress` is an ordinary Action Type.

When a new Card is authored, its initial Action sequence contains one default instance:

```text
Increment Phase Progress: +10
```

The author may change, move, duplicate, or delete it.

If a Card's Action sequence GOTO-like control is ever legal in a given scope, its continuation resumes the remaining Actions before normal completion. In the current v1 scope, Phase GOTO is not offered directly to reusable Cards because Phase exits belong to the containing reusable Phase, not the Card.

PhaseProgress itself is not clamped; zero/negative mutation is legal.

## 18. Normal node execution

Internal graph nodes normally follow their local output after they complete.

- Entry -> normal output.
- CardExecutor -> normal output after the one Card finishes.
- ActionNode -> normal output after Action sequence completes normally.
- Decision -> common normal output after selected option's Actions complete normally.
- VariableCheck -> True or False.

If GOTO suspends an Action sequence, RETURN resumes at the next Action, then normal node completion continues.

## 19. User-paced Card yield

Preserve the existing useful behavior: one explicit Advance request executes **at most one Card**.

A Core `Advance`/`RunUntilYield` call may execute unlimited finite non-card graph work (Entry, checks, Actions, Decisions, transitions) until:

- it reaches a CardExecutor and has not consumed its one-card budget -> select/execute one Card, continue automatic graph work;
- it later reaches another CardExecutor after a Card was already consumed -> yield before drawing;
- Session ends;
- a runtime error occurs;
- cancellation occurs.

Thus a one-shot/action-only Phase may transition into another Phase and reach its first CardExecutor in the same Advance if no Card was consumed yet.

Add a generous deterministic node-step guard per Advance to detect accidental infinite non-yield loops and fail loudly rather than hang the Workbench.

## 20. CardExecutor no-match behavior

For v1 of the graph VM, no eligible Card is a clear runtime content error at the CardExecutor. Do not retain the old implicit 'advance phase early' behavior because Phase completion is now authored graph control.

A later explicit NoMatch port can be added if real content demonstrates the need.

## 21. Copy/reuse semantics

### Copy Session

Creates a new Session ID and clones all Session-owned graph structure, nodes, decisions, SessionGoto Action Instances, ports, edges, and WPF layout IDs/rows.

PhaseReference nodes continue referencing the **same Phase IDs**.

### Duplicate Phase

Creates new Phase ID and clones:

- metadata/tags;
- Phase graph nodes/ports/edges;
- Phase exits (new exit IDs, same names);
- owned Action Instances (new IDs, copied values);
- WPF Phase layout.

Referenced Resources remain shared references.

### Make Unique

Contextual to the currently selected Session PhaseReference placement:

- clone the referenced Phase exactly as Duplicate Phase;
- change only this Session PhaseReference to the clone;
- remap its projected output-port mappings to cloned PhaseExit IDs while preserving the Session placement port IDs/edges when possible;
- switch Phase editor to clone;
- apply the originally requested interface edit if Make Unique came from a blocked port mutation.

One transaction + one undo command.

## 22. Phase usage UX

Top of Phase editor:

```text
Used in 14 sessions · 17 placements    [Show Sessions] [Make Unique]
```

- Show Sessions filters the Library to Sessions referencing the Phase.
- Make Unique is enabled only with a current Session PhaseReference placement context.

## 23. Undo / redo

First-class authoring feature.

- WPF uses semantic authoring commands, not raw SQL undo.
- Every command executes persistence changes transactionally.
- Undo invokes the semantic inverse transactionally.
- Redo replays.
- History is in-memory for the current Workbench process; not persisted across restarts.
- Node drags coalesce into one command at drag completion.
- Text/numeric changes coalesce to one command per committed edit, not per keystroke.
- Complex operations (`Make Unique`, Copy Session, delete exit + connected edge) are single undo units.

SQLite is continuously updated; no whole-document Save operation is required.

## 24. Deferred Card/Kink/Equipment/Smart-Toy details

Preserve these decisions in docs but do not implement full selection/profile UI in this stack:

- Equipment is persistent user inventory; Card hard requirements.
- Smart Toys are persistent actual devices/capabilities; Session/Card hard requirements should target capabilities rather than hardware model/protocol.
- Kink preference values: Love, Like, Torture, Don't Consent.
- Don't Consent is an absolute eligibility exclusion before weighting.
- If a Card matches any Torture kink, negative preference contribution uses full strength while positive contribution uses 0.5 strength.
- Exact numeric weighting curve is deliberately deferred to Card-selection design.
