# SQLite Core Schema v2 — Design Contract

> Installed from packet `Game_GraphVM_WPF_Authoring_Stack/SQLITE-SCHEMA-V2-DESIGN.md` (Ticket 01). Design contract, not copy/paste SQL; implement migration 2 against current v1 schema and tests.

## Migration posture

This is still pre-production authored content. Preserve existing sample content enough to keep the project runnable, but prefer the correct v2 shape over heroic parity with obsolete min/max-card semantics.

Because v1 host-extension tables are not shipped user data yet, migration 2 may deprecate v1 Action/PhaseSlot structures. However:

- never drop unknown host tables;
- preserve the `resource` table and stable Resource IDs;
- if an unknown host table blocks a destructive core-table migration, fail loudly rather than deleting it;
- safest default: leave obsolete v1 core tables present but unused, empty PhaseSlot rows after transformation, and have v2 loader/repositories ignore legacy tables. A later cleanup migration can remove them when safe.

## New/changed core entities

### Session types

```text
session_type
    id PK
    title UNIQUE-ish/user-visible

session.session_type_id FK -> session_type
```

Seed a simple default/legacy type for migrated sample Sessions.

### Temperature definitions

```text
temperature_definition
    id PK
    title
    min_value REAL
    max_value REAL
    default_value REAL
```

Seed `happiness` = 0/100/default 50.

### Phase exits

```text
phase_exit
    id PK
    phase_id FK phase CASCADE
    ordinal
    name
    UNIQUE(phase_id, ordinal)
```

Do not enforce unique exit names; identity is ID. UI can discourage duplicate display names if confusing, but runtime never string-matches.

### Phase graph nodes

```text
phase_graph_node
    id PK
    phase_id FK phase CASCADE
    node_type
```

Subtype/config tables as needed:

```text
phase_node_variable_check
    node_id PK/FK
    source_kind
    variable_key NULL
    compare_operator
    compare_value REAL

phase_node_action
    node_id PK/FK
    action_sequence_id FK

phase_node_decision
    node_id PK/FK
    prompt

phase_decision_option
    id PK
    node_id FK decision CASCADE
    ordinal
    label
    action_sequence_id FK
```

Entry/CardExecutor/Return need no parameter rows unless implementation benefits.

### Phase internal output ports + edges

```text
phase_node_output
    id PK
    node_id FK phase_graph_node CASCADE
    port_kind          // normal, true, false
    ordinal
    label

phase_graph_edge
    id PK
    phase_id FK phase CASCADE
    source_port_id FK phase_node_output CASCADE
    target_node_id FK phase_graph_node CASCADE
    UNIQUE(source_port_id)
```

Multiple incoming edges are legal.

Repositories guarantee source/target belong to same Phase.

### Session graph nodes

```text
session_graph_node
    id PK
    session_id FK session CASCADE
    node_type

session_node_phase
    node_id PK/FK
    phase_id FK phase RESTRICT

session_node_decision
    node_id PK/FK
    prompt

session_decision_option
    id PK
    node_id FK decision CASCADE
    ordinal
    label
    action_sequence_id FK
```

SessionStart/End need no subtype row unless useful.

### Session output ports

Use placement-specific stable ports so Session edges survive PhaseExit rename and MakeUnique remapping:

```text
session_node_output
    id PK
    node_id FK session_graph_node CASCADE
    port_kind          // normal, phase_exit, session_goto
    ordinal
    phase_exit_id FK phase_exit NULL
    session_goto_action_instance_id FK action_instance NULL
```

Rules:

- SessionStart: one normal port.
- PhaseReference: one `phase_exit` port per referenced PhaseExit.
- SessionDecision: one normal port + one `session_goto` port per SessionGoto instance.
- SessionEnd: none.

Repository/service code enforces discriminator consistency.

### Session edges

```text
session_graph_edge
    id PK
    session_id FK session CASCADE
    source_port_id FK session_node_output CASCADE
    target_node_id FK session_graph_node CASCADE
    UNIQUE(source_port_id)
```

Repositories verify both ends belong to same Session.

## Action sequences + Action Instances

### Sequence

```text
action_sequence
    id PK
```

Owners reference a sequence ID:

- Card;
- Phase ActionNode;
- PhaseDecision option;
- SessionDecision option;
- PromptChoice option.

Deletion repositories must delete owned sequences transactionally. Do not make normal Save rebuild all sequences.

### Action Instance

```text
action_instance
    id PK
    action_sequence_id FK action_sequence CASCADE
    ordinal
    action_type
    is_blocking
    UNIQUE(action_sequence_id, ordinal)
```

Configured occurrence is never shared between sequences.

### Explicit subtype tables

At minimum:

```text
action_instance_debug
    action_instance_id PK/FK
    message
    delay_seconds

action_instance_stat_increase
    action_instance_id
    stat_key
    amount REAL

action_instance_increment_progress
    action_instance_id
    amount REAL

action_instance_modify_temperature
    action_instance_id
    temperature_id FK temperature_definition
    amount REAL

action_instance_cutscene
    action_instance_id
    resource_id FK resource RESTRICT

action_instance_phase_goto
    action_instance_id
    phase_exit_id FK phase_exit RESTRICT

action_instance_session_goto
    action_instance_id
    label
    // corresponding session_node_output links back to this instance

action_instance_return
    action_instance_id

action_instance_end_session
    action_instance_id

action_instance_prompt_choice
    action_instance_id
    prompt

action_instance_choice_option
    id
    action_instance_id FK prompt_choice CASCADE
    ordinal
    label
    action_sequence_id FK
```

No EAV table.

## Card

Add/replace with:

```text
card.action_sequence_id FK action_sequence
```

New Card creation creates sequence + default `increment_phase_progress(amount=10)` Action Instance.

Legacy `card_action` and top-level `action*` become ignored/deprecated after migration.

## Phase legacy fields

`phase.min_cards` / `phase.max_cards` and PhaseSlot tables may remain physically in v1-era DB for migration safety, but v2 loader/Core must not use them.

## WPF extension schema

Separate WPF migration/version ledger; tables prefixed `wpf_`:

```text
wpf_session_node_layout(node_id PK, x REAL, y REAL)
wpf_phase_node_layout(node_id PK, x REAL, y REAL)
wpf_graph_view_state(graph_kind, graph_id, zoom, pan_x, pan_y, PK(...))
wpf_workspace_state(id/singleton, library_width, session_width, phase_width, inspector_width)
```

Core migrator ignores them.

## Sample-content transformation

Transform current sample Session/Phase/Card content to runnable v2 data:

- each current Session -> Session graph Start -> exact PhaseReference nodes -> End;
- each current Phase -> reusable standard Phase graph;
- standard migrated Phase exports at least `Complete` and optionally `Fail`;
- standard low graph may be `Entry -> CardExecutor -> Progress>=100? -> GOTO Complete / loop` (and a Happiness<10 fail check if useful to exercise Temperature/exit wiring);
- current Card configured Actions are cloned into per-Card Action Instances;
- append default `Increment Phase Progress +10` to each migrated Card so sample flow progresses;
- convert existing Choice Action to per-instance PromptChoice with option Action sequences;
- preserve cutscene Resource IDs.

Exact old MinCards/MaxCards parity is intentionally not required because the mechanic has been replaced.
