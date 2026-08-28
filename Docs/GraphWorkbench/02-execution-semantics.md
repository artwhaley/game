# Graph VM Execution Semantics

> Installed from packet `Game_GraphVM_WPF_Authoring_Stack/EXECUTION-SEMANTICS.md` (Ticket 01). Runtime contract for Game.Core; deterministic, fail-loud.

## Runtime objects

### SessionRun

Conceptual state:

```text
SessionDefinition
Current Session graph node
Temperatures
Session/runtime stats
ContinuationStack
Active PhaseRun? 
Completed
```

### PhaseRun

```text
Session PhaseReference placement ID
Phase ID
Current Phase graph node
PhaseProgress (starts 0)
Phase-local variables
CardSelectionState
    RNG object/state
    draw history (empty now)
    future cooldown/repetition state
Current action/card continuation as needed by VM
```

Every entrance to a PhaseReference creates a new PhaseRun even if the same Phase is already suspended elsewhere in the stack.

## Session startup

`StartSession(content, sessionId, SessionSpawnOptions, randomFactory/services)`:

1. load Session;
2. initialize Temperatures from definitions;
3. apply provided overrides (Happiness default remains 50 when absent);
4. locate exactly one SessionStart node;
5. enter it;
6. host may invoke the first `RunUntilYieldAsync` automatically.

## Run-until-yield

`RunUntilYieldAsync` has one shared graph/action safety budget per call. It does
not impose a card budget: ordinary Cards and non-card graph nodes continue until
an authored yield, transfer, session end, cancellation, or error.

`ContinueAsync` calls the same operation after an authored
`WaitForContinue` instance and resumes the saved action/card locus.

Pseudo-flow:

```text
while not complete:
    step current graph node
    if CardExecutor wants card:
        choose and execute the next eligible card
        if an authored WaitForContinue is reached: YIELD
    continue automatic nodes
```

Decision prompts/cutscene waits are asynchronous operations inside the same run.

Use cancellation tokens throughout.

Use a large node-transition guard to detect non-yield cycles.

## Session graph

### SessionStart
Follow normal edge.

### PhaseReference
Create fresh PhaseRun and enter PhaseEntry.

### SessionDecision
Prompt user; run selected option's Action sequence.

- if sequence completes normally: follow common normal output;
- SessionGoto suspends continuation and transfers;
- RETURN resumes caller if valid;
- EndSession terminates.

### SessionEnd

Clear stack, clear active execution, set complete, emit completion event.

## Phase graph

### PhaseEntry
Follow normal edge.

### CardExecutor

1. choose eligible Card using current Phase metadata/tags and PhaseRun CardSelectionState;
2. if none -> runtime content error;
3. emit CardStarted;
4. execute Card Action sequence;
5. if sequence returns normally, emit CardFinished;
6. follow CardExecutor normal edge.

No implicit Progress increment.

### VariableCheck
Evaluate current live state and follow True/False internal edge.

### ActionNode
Run Action sequence. If it completes normally, follow normal internal edge.

### PhaseDecision
Prompt; execute selected option sequence. If normal, follow common normal internal edge.

### Return node
Execute RETURN semantics.

## Action sequence execution

Action sequences are indexed ordered lists of Action Instances.

Normal:

```text
for i from currentIndex:
   execute action[i]
```

A flow transfer does not discard later Actions.

### GOTO example

```text
1 Modify Happiness +10
2 Phase GOTO Fail
3 Increment Progress +20
```

At #2 continuation stores `nextActionIndex = 3` and the current PhaseRun. If target eventually RETURNs, #3 executes, then CardFinished is emitted exactly once.

If target never returns and Session ends, #3 never executes.

## Blocking/nonblocking

Retain background-action tracking for appropriate Activity Actions.

Flow-control Action Types (`PhaseGoto`, `SessionGoto`, `Return`, `EndSession`) are always blocking/control-synchronous and cannot be configured nonblocking.

If an asynchronous nonblocking Action faults, preserve the existing explicit background fault logging/observation behavior.

## Phase GOTO resolution

1. validate referenced PhaseExit belongs to active Phase;
2. capture current continuation;
3. locate current Session PhaseReference placement;
4. locate placement's projected Session output port mapped to PhaseExit;
5. require one wired Session edge;
6. transfer to target Session node.

Unwired export -> clear runtime content error naming Session, Phase placement, Phase, exit.

## SessionGoto resolution

1. validate Action Instance belongs to currently executing SessionDecision option;
2. capture current continuation;
3. locate Action-owned Session output port;
4. require wired Session edge;
5. transfer to target Session node.

## RETURN

If stack empty: runtime error.

Else pop and restore exactly:

- prior graph locus;
- prior active PhaseRun object if one existed;
- Action sequence + next index;
- current card continuation.

Session-global Temperatures remain at current values.

## Recursive Phase call

Legal:

```text
A run #1 -> GOTO placement of A -> A run #2 -> RETURN -> A run #1
```

Each PhaseRun owns independent PhaseProgress and Card RNG state.

## EndSession

Absolute terminal:

- cancel/finish according to existing host-action rules;
- discard continuation stack;
- mark complete;
- no later RETURN.

## Errors, not guessed behavior

Fail loudly for:

- missing singular Start/Entry;
- invalid node subtype data;
- dead-end normal output where continuation is required;
- unwired GOTO output;
- RETURN with empty stack;
- missing Card at CardExecutor;
- step-guard exhaustion;
- Action Type unsupported in its owner scope;
- missing Resource required by Activity Action.

Do not silently repair authored flow at runtime.
