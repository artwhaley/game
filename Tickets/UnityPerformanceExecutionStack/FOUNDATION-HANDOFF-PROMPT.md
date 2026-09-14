# Execution-agent handoff: Conversation Performance V1 after foundation

Copy this prompt into the execution agent that will finish the approved Conversation Performance V1 ticket stack.

You are continuing an approved implementation. Do not redesign the packet, replace the procedural-card model with timelines, or reopen the WPF/Core/SQLite boundary. Read `agents.md`, `Tickets/UnityPerformanceExecutionStack/README.md`, `ARCHITECTURE-CONTRACT.md`, `TICKET-STACK.md`, `ORCHESTRATION-PROMPT.md`, `Docs/UnityPerformance/FOUNDATION-PATCH-SPEC.md` and `Docs/UnityPerformance/FOUNDATION-PATCH-RESULTS.md` before editing. Inventory the existing worktree and preserve unrelated user changes.

## Current foundation state

The foundation prerequisite is already implemented in the working tree. The main runtime pieces are:

- `Assets/Scripts/Game/CharacterRig.cs`
- `Assets/Scripts/Game/RiggedAttachment.cs`
- `Assets/Scripts/Game/CharacterAnimationPlayer.cs`
- `Assets/Scripts/Game/CharacterFaceController.cs`
- `Assets/Scripts/Game/CharacterGazeController.cs`
- `Assets/Scripts/Game/CharacterPresentation.cs`
- the slimmed `Assets/Scripts/Game/Phase00RigShowcase.cs`
- the direct-input `Assets/Scripts/Game/Phase00DebugFlyCamera.cs`

The prepared assets are the current Lara material prefab at `Assets/Daz3D/lara/Prefabs/lara_Prefab.prefab` and the three attachment prefabs under `Assets/Characters/LaraAttachments`. Lara's prefab now owns the explicit body rig, explicit facial renderer list, gaze, presentation and persistent animation-player components, with a sibling `Attachments` container. Bra and panties use shared-skeleton `RiggedAttachment`; 2021-02 hair uses preserved-skeleton `RiggedAttachment`. Each attachment declares the `Genesis8Female` rig family it was prepared against. The old binder classes are retained only as obsolete compatibility types.

The canonical base is still the Daz-generated material prefab. Do not invent a second wrapper prefab or duplicate the material/import tree during V1. A general Daz import pipeline and wardrobe system are later work.

The hair builder now unpacks the bridge instance before saving a prepared hair asset. The current asset preparation and showcase visual pass have been run and verified. Only repeat the preparation sequence if a later asset change makes the attachment validation report a bridge variant or missing asset:

1. `TruthCardGame > Phase 00 > Prepare Current Character Foundation`
2. `TruthCardGame > Phase 00 > Validate Prepared Character Foundation`
3. `TruthCardGame > Phase 00 > Rebuild Disposable Rig Test Scene`

The Play-mode deformation/material/face/gaze/camera pass has been verified in `Assets/Scenes/Phase00RigShowcase.unity`. Do not launch Unity in batch mode, change the Unity licensing client, clear licenses, or run license-management commands. If a later change regresses this fixture, give the user the exact three-menu repair sequence above; do not invent a replacement asset path.

The old Luna import/pose probes are still useful measurements, but they are diagnostics only: find them under `TruthCardGame > Diagnostics > Rig Spike`. They run in a temporary additive scene and must not be used to prepare Lara or rebuild authored scenes. The explicit hair and wardrobe rebuild menus are labeled `(destructive)` and require confirmation; use them only when the idempotent preparation command reports an absent or retired asset.

`Rebuild Disposable Rig Test Scene` is allowed to replace only the disposable Phase 00 showcase scene. It must never be used as a content-authoring workflow. Prepared prefab changes should flow through prefab instances in authored scenes; do not rewrite curated scene YAML or rebuild the whole project when an asset changes.

## V1 execution intent

Execute only the first unmet acceptance in tickets 00 through 05, in their approved order. The game experience is a pull-card flow: Core selects a card/action, the performance director chooses legal expressive ingredients, Unity moves/adopts/plays them, and a dialogue line is presented. There are no takes, audition timelines, per-line clips, persisted combinations, or author-facing “experiment with a cue” workflow.

- WPF owns semantic Performance Tags and reusable Performance Events.
- SQLite is canonical content storage and uses the existing portable mapping source. Unity must have one CLR/type identity for `TruthCardGame.Content` and `TruthCardGame.Core`; never copy portable sources into a second assembly or import duplicate `Game.Content.dll`/`Game.Core.dll` binaries.
- Core owns portable legal planning and async host requests, not rendering clocks, skeletons, materials, graph topology or attachment lifecycles.
- Unity owns ingredient compatibility, the generated PresentationCatalog, animation/face/gaze execution, anchors/postures and actual visual timing.
- The first real proof is the approved ordinary blocking-dialogue Card. Keep existing dialogue RNG: `DialogFromTags` chooses its snippet synchronously, Core asks the active Performance Director for the next compatible expressive acting immediately before presenting a blocking line when refresh is enabled, then the existing `IDialogService.ShowAsync(...)` presents it.

## Explicit V1 guardrails

Do not add a generalized Unity-to-Core visual-start callback, stale-dialogue generations, a speech scheduler, queue coordinator, or nonblocking-dialogue synchronization protocol solely for V1. Preserve existing nonblocking behavior and document a concrete case before expanding that contract.

Keep the V1 Performance Event query as the small shared semantic ALL/ANY model. During Ticket 05, inspect real content for metadata duplication between body gestures and facial expressions (for example, a face Smirk needing every body behavior verb). Report the evidence and make the smallest correction only if the real V1 content demonstrates the problem. Do not add speculative separate face/body dimensions.

Do not expand V1 with driven motion/funscript, lipsync, player posing, wardrobe UI, hair physics, generalized import validation, a random idle scheduler, a second semantic action family, or a timeline authoring layer.

## Authoring/workflow acceptance

The authoring loop must remain pleasant: add or edit a reusable WPF Performance Tag/Event and a Unity ingredient, then use it in a Card through the canonical database. Adding a compatible gesture or facial ingredient must not require a new timeline, a new scene, a new Unity graph, or a Core rendering concept. A new anchor/posture should reuse the factored operations and update compatibility tags, not multiply state-edge boilerplate.

Keep editor tooling split by lifetime:

- explicit preparation/import commands may mutate prepared assets and are idempotent;
- validation/report commands are read-only and fail with the exact asset/bone/renderer path;
- the Phase 00 showcase rebuild is a disposable fixture only;
- ordinary performance/content work never extracts Daz assets or reconstructs authored scenes.

## Required verification and handoff

Run the repository’s focused .NET tests and the relevant Unity EditMode/PlayMode tests. Treat Ticket 00 and the foundation visual pass as prior evidence; do not claim them again without a regression. In Unity, prove the ordinary blocking-dialogue Card end to end: select the Card, select/accept a compatible performance, move to the requested place, adopt the pose/expression, present the selected line, select the next acting on the next blocking line, and move to the next place. Exercise reset/disable/re-enable and prefab attachment reparenting. Report separately what compiled, what automated tests passed, what was visually checked, and any user action still required.

Stop and report a concrete blocker if the foundation menu or Play-mode fixture reveals a missing serialized renderer/bone, duplicate type identity, or a material/animation failure. Do not paper over it with a new fallback binder or by changing the approved V1 architecture.
