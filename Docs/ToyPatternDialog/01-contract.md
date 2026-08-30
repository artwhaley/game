# Toy / Resource / Dialog Contract (frozen)

Scope of this contract: the `toy-pattern-dialog-catalog` patch stack. Nothing
below may drift during implementation; deviations require updating this file
first and saying why.

## Resource

`Resource` remains a portable identity only:

```text
Resource
  Id      stable opaque string
  Kind    centralized constant
  Name    friendly author-facing label
```

Known kinds (centralized constants in `ResourceKinds`):

- `cutscene`   — existing kind, unchanged
- `toy_pattern`— new in this stack

The pattern's actual representation (waveform/keyframes/curve) is **deferred**.
A `toy_pattern` Resource is identity + name only in this stack.

Core never contains: Timeline assets, Unity object references, funscript paths,
waveform keyframes, toy device addresses/protocols. Unknown future kinds must
still load safely (loader keeps them; kind validation only fires where a
consumer demands a specific kind).

## Dialog content

Dialog Snippets are portable authored text content, NOT Resources. They need no
Unity asset binding and are stored in their own catalog tables, not the
`resource` table.

## Action model

- Action Types remain code-defined (`ActionTypeKeys` + `ActionTypeRegistry`).
- Action Instances remain configured owner-scoped occurrences with stable IDs.
- ActionSequence remains an ordered list of Action Instances.

## Toy actions

### Timed Toy Pattern (refactor of `toy_activity`)

```text
CapabilityId        Smart Toy capability stable ID
PatternResourceId   Resource of kind toy_pattern
DurationSeconds     >= 0
IsBlocking          configurable
```

Authored `Intensity` is removed. Display label: `Timed Toy Pattern`.

### Set Toy Pattern (new, `toy_set_pattern`)

```text
CapabilityId
PatternResourceId
```

No duration. Always nonblocking (registry-guaranteed; not user-configurable).

### Toy command semantics

- Toy output state is keyed by Capability ID.
- A new command for capability X supersedes the previous command for X.
- Commands for different capabilities are independent and coexist.
- Timed: active until duration expires, supersession, or teardown. On natural
  expiry it clears the capability ONLY if it still owns the current generation.
- Stale timer rule: a superseded timed command must never stop a newer command
  when its original timer expires (per-capability generation ownership).
- Set Toy Pattern: applies/replaces state and returns promptly. Persistent
  state survives Cards, Phase transitions, GOTO/RETURN, Delay, WaitForContinue.
  Persistent state is NOT a background Task; WaitForAll does not wait on it.
- WaitForAll keeps its snapshot barrier over nonblocking timed Tasks only.
- Session teardown (SessionEnd, Halt, cancellation, runtime error, host
  close/restart) runs StopAll even when the gameplay token is already canceled;
  cleanup exceptions must not hide the original error.

## Dialog

### Direct Dialog (kept, unchanged model)

```text
Dialog
  Text
```

Execution moves from the temporary `ICutsceneService.PlayAsync` hack to
`IDialogService.ShowAsync(text, ct)`. Missing service: log loudly (with the
text), complete safely, never touch the Cutscene service.

### Dialog From Tags (new, `dialog_from_tags`)

```text
RequiredDialogTagIds[]  one or more; all-match semantics
IsBlocking              default true
```

- All configured required tags must be present on a snippet.
- Zero required tags at runtime: content error (loud).
- No matching snippets: content error with readable tag names (loud).
- No silent fallback to arbitrary dialog or inline text.
- Selection: uniform random among all-match candidates in stable deterministic
  ordering. No weighting, no anti-repeat in this stack.
- Direct Dialog stays; DialogFromTags does not replace it.
- Both call the same `IDialogService`.

## Dialog RNG domain

New deterministic domain `DialogSelection` with a fixed salt in
`SeededRandomDomains`, independent from SessionSelection and PhaseRun/Card
streams. Session-scoped, persists across Phase transitions; nested Action
contexts preserve the same Dialog RNG. Adding/removing DialogFromTags actions
must not perturb Card draw randomness (and vice versa).

## PromptChoice

Maximum of 3 options remains. Not touched.

## Library / WPF

No Library redesign. Toy Patterns are authored as Resources in the Inspector
browser (`Actions | Resources`); Dialog Tags and Dialog Snippets are authored
in Catalogs. These are deliberately separate concepts.

## Persistence invariants

All new subtypes/relations participate in the existing stable-ID
ActionSequence Sync, cloning, Undo snapshots, and extension-table safety:
fake `wpf_*`/`unity_*` rows attached to surviving Action IDs survive pattern
changes, duration changes, tag changes, reorders, and unrelated Card edits.
