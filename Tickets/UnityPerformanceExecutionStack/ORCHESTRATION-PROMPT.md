# Orchestrator prompt

Implement this repository's procedural performance ticket stack after the user authorizes execution. The requested deliverable is a playable Unity card game slice and an easy recurring content-authoring workflow.

## Read first

Read agents.md and README.md, then current Docs/GraphWorkbench documentation and relevant database versioning instructions. Read this packet's README, ARCHITECTURE-CONTRACT and TICKET-STACK, plus Docs/UnityPerformance/BUILD-PLAN.md. Current packet text supersedes earlier packet commits and older timeline-oriented proposals.

Inspect actual source before proposing exact file changes. Portable code physically lives under Assets/Scripts/Portable and is linked into DotNet projects. Do not create a second implementation under DotNet.

## Product intent you must preserve

The game draws a Card. A Perform action establishes procedural rules and may move the character. Dialogue selects/delivers lines. Core chooses compatible body, face and gaze combinations at runtime. Expression refresh may occur automatically at dialogue boundaries. No author saves a chosen combination.

Unity owns animation ingredients, spatial/rig bindings and their factual compatibility metadata. WPF reads the generated Unity catalog and owns reusable event queries/policies, cards, dialogue and scalar curves. Core owns selection, compatibility and event lifetime. A new compatible enabled ingredient must improve existing cards with no edits to those cards.

Two event flavors: ongoing conversational variety and finite scalar-driven motion. Support pelvis/body ownership as well as prop/arm movement. Keep actual movement and clip/mask execution in Unity.

## Execute

Work through tickets 00–10, reading the context, guardrails and acceptance for each before editing. Announce the concrete files and result for the next coherent batch. When the user explicitly authorizes this packet as an execution run, that authorization replaces repeated “go” requests under agents.md rules 2 and 5 and permits coherent batches over five files. It does not override dependency approval, database safety, fail-noisy behavior or honest verification.

Use source control checkpoints for completed coherent milestones. Preserve unrelated local backup files. Keep root README current as features land. Push only when authorized. No editor/package upgrades or added dependencies without approval.

Prove the actual rig in ticket 01 and get a real Card running in Unity by ticket 05. Do not spend the whole stack building editors before exposing visual integration. Missing assets/tools must be named precisely; continue independent work without fabricating passing visual evidence.

Make the existing action pipeline work: separate blocking activity from control transfer; integrate all new actions with Card buffers, clone/equality, undo, persistence, export and both hosts. Preserve graph VM, card/dialogue selection and gameplay constraints.

Implement scoped validation at data loading/export, not by catching global validation exceptions. Include conservative dependencies for all random candidates and branches. Enabled broken included ingredients fail visibly. Optional body rest is explicit policy.

Each runtime milestone must cover readiness, finite versus persistent work, cancellation and error propagation. Tests use the same production director/actions, never a second scripted demonstration runner. Use bounded deterministic seeds for verification; a seed is diagnostic data, not authored performance content.

## Do not build

No takes, audition/pin controls, per-line clip/facial selectors, saved sampled combinations, timeline/cue tracks, per-card Animator assets, New Conversation graph scaffolding, SQLite animation-registration tables, shared unsaved editor overlays, remote WPF actor commands or graph seek. Do not bring these back under new names.

Do not author every matrix combination. The matrix derives coverage from event rules and Unity ingredient descriptors. Keep forms/pickers within the existing WPF workflow; no generic authoring framework or inheritance architecture.

Do not silently substitute assets, teleport on route failure, ignore unknown actions, skip contextual graph actions in Test Card, swallow background presentation failures or report desired state as arrived.

## Workflow acceptance is architecture acceptance

After a valid WPF Save, the next Play/Repeat in the loaded Unity scene uses the latest committed snapshot automatically. Export failure/staleness is visible. No manual transport per edit and no per-keystroke reimport.

Repeat a Card with fresh presentation randomness. Tune one event once and affect its users. Add one compatible ingredient in Unity and expand existing matching events with zero Card/event changes. Preserve dirty Card edits during catalog refresh.

Test Card uses disposable run state with normal constraints and production execution. A Card requiring enclosing graph context is tested in its Session with a clear message, not partially executed. Final acceptance uses the normal Session selection/VM and standalone packaged content.

## Verification and handoff

Run appropriate portable, persistence and editor tests; verify Unity behavior in the pinned editor. Launch and leave the relevant main app running after application changes as required by agents.md. A build success does not establish visual correctness.

At each milestone report what works, evidence, outstanding dependencies and the next ticket. Record reproducible failures and fix root causes. Honor the repository three-strikes rule. Do not repeatedly request permission for already authorized routine work.

Finish with the playable scene/build location, concise authoring instructions, test results, visual evidence and any unmet acceptance. Do not call the stack complete until ticket 10 passes. If a required dependency prevents that, state the exact remaining work without substituting a simulated demo.
