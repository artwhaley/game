# Character presentation foundation patch

Status: implemented in the working tree, September 13, 2026. The code and prepared-asset migration below are the bounded prerequisite before resuming the Conversation Performance V1 tickets at their first unmet acceptance. Preserve their approved Core/WPF/SQLite architecture and ticket numbers. Final Unity Play-mode visual verification remains an explicit handoff step.

## Outcome and scope

Lara is a reusable character prefab. The existing 2021-02 hair, bra and panties are reusable attachment prefabs: put one beneath the character's Attachments container and it binds itself when enabled. A scene does not call a Lara-specific binder. The same character presentation components drive the showcase and later the Unity performance host.

Keep the supplied assets, Humanoid retargeting, HDRP material corrections, face presets, OW diagnostic, camera controls and visible stand/walk/sit/gesture/gaze proof. Finish the current capabilities; do not add wardrobe selection UI, inventory, equipment rules, hair physics, cloth simulation, arbitrary-body fitting, import wizard, bulk validation framework or content-pack tooling. Runtime contract checks and targeted regression tests are necessary to make these components reliable; full import validation remains deferred as requested.

No Core, database or WPF changes. No new packages. No new semantic actions, timeline authoring, saved combinations, random idle scheduler, lipsync or driven-motion implementation. The OW control remains a diagnostic face preset.

## Source findings that motivate the patch

- `DazHairAttachment` is used for clothing despite its name. It relies on an editor call to populate bones and retains failed rigid-head/fallback logic. A fresh runtime instance is not a proven drop-in attachment.
- `DazHairRigFollower` retains the correct source skeleton, but searches the actor subtree by first matching name. Excluding itself still allows another attachment's duplicate skeleton to win. Its serialized mapping runs head-to-hip; moving a parent after setting a child changes that child's final world pose.
- Extraction hardcodes Lara and renderer names, and showcase building re-extracts hair and strips the base prefab. The generated hair is a variant of the whole bridge export. Source exports and runtime assets need distinct ownership.
- `Phase00RigShowcase` owns animation graphs, face discovery, jaw protection, gaze, movement and debug UI. Gesture changes destroy/recreate both graphs and restart foundation time. Pending sit/stand completion is not comprehensively canceled by reset/replacement.
- Face discovery includes every attachment renderer and resets every captured blendshape. It should own explicit facial channels only.
- Gaze smoothing now persists, but face-forward is reconstructed from eye positions and flattened against world/actor up every frame. This loses pitch information. Its limit is relative to animated facing rather than a calibrated anatomical frame, and smoothing can temporarily escape a new limit after actor movement.
- A DLL timestamp or successful compile is not Play-mode evidence. Prior claims of validation must not substitute for the lifecycle checks below.

## 1. Character and attachment contract

### CharacterRig

Add `CharacterRig` to a game-owned Lara prefab under `Assets/Characters`, separate from the Daz bridge source. Serialize the explicit Animator, body skeleton root, Attachments container and rig-family identifier (`Genesis8Female` for this fixture). Organize Attachments as a sibling of the body skeleton, never inside it. Cosmetic subcontainers are permitted; they do not implement equipment slots or automatic replacement rules.

Build a cached bone map from the explicit body skeleton only. Exclude any nested attachment boundary. Use exact names within this rig family; ambiguous names and missing required bones are errors, never first-match or fuzzy fallback. The API can be small: `EnsureInitialized`, required bone lookup, attachment registration/unregistration and an initialized-state query. Initialization must be idempotent and callable by a child regardless of Unity Awake ordering.

CharacterRig owns the character's calibrated rig references; it does not select clips, performances, outfits or tags. Cached references are rebuilt after scene/domain reload. Do not serialize references from an attachment asset to a particular scene actor.

### RiggedAttachment

Replace both old binders with one `RiggedAttachment` component. It owns an explicit list of renderer bindings and, when needed, a private source skeleton. On enable, find the nearest ancestor CharacterRig, initialize it and bind. On disable/destroy, unregister and release actor references. On reparent, unbind the previous owner and bind the new ancestor; support enable-after-parenting and instantiate-then-parent. Until parented to a rig it is unattached, with renderers suppressed and a clear status. Once an owner exists, incompatible data is a named error and the attachment remains hidden rather than deforming at the origin.

Maintain source binding data independently of current runtime renderer references. Rebinding to a second character must not derive names or bind data from the first. Do not move attachment-owned bones outside the attachment hierarchy; removing the attachment must remove all its private objects. Do not change shared meshes, materials or source assets at runtime.

Use an explicit serialized binding mode, chosen once when preparing the asset. Do not choose a mode dynamically merely because names happen to match:

| Mode | Use now | Contract |
|---|---|---|
| SharedSkeleton | Bra, panties | Ordered bone keys per renderer resolve to body bones; preserve mesh bind poses and authored renderer-to-character space. Requires the prepared compatible bind space. |
| PreservedSkeleton | 2021-02 hair | Keep original mesh/bone references, hierarchy and rest transforms. Map shared body bones to CharacterRig; retain product-specific strand bones in their original hierarchy. |

Name matching alone does not prove bind-pose compatibility. This patch establishes it for the three current assets by comparing source bind transforms and testing deformation. Broader compatibility detection belongs to import tooling. Declare the supported rig family and bind-space assumption on prepared assets; do not advertise arbitrary Genesis morphs or other skeletons as automatically supported.

For PreservedSkeleton, cache mappings in ancestor-before-descendant order. Synchronize them once after the final character pose, including gaze and facial bone adjustments. Preserve source local scales for the current assets, prove uniform actor scaling and reject unsupported nonuniform scale; do not blindly overwrite scales with lossyScale. Retain every ancestor needed by the renderer or mapped bones. No per-frame tree searches, LINQ, or allocations.

No hardcoded Genesis bone whitelist in the runtime component. The prepared attachment stores its shared mappings explicitly. Product bones remain private even if another attachment has the same name.

## 2. Convert the current assets and builders

Create a game-owned `Lara` prefab and an assembled default/test prefab containing its three attachments. Preserve current source FBX, bridge prefabs and materials as import inputs. The reusable base has no Natty hair, bra or panties renderer baked into it; the assembled prefab composes the separate items.

Migrate the existing attachment prefabs in place where practical to retain GUIDs. Hair retains the proven source bind hierarchy, but make its game-owned output independent of a variant relationship to the entire bridge prefab. Keep only the selected renderer and required transform ancestry; avoid deleting an ancestor that contains a bone. Bra and panties retain their meshes/materials, ordered bones and compatible mesh space. If their renderers have already been stripped from the bridge prefab, recover source geometry from the original Lara FBX and existing extracted assets, not a new Daz export.

Persist the known material results in prepared assets: transparent eye shells, lash opacity, hair surfaces, invisible scalp cap. Never use a null material to hide a submesh. Verify the invisible material writes neither visible color nor unwanted depth/shadow/specular contribution with the installed HDRP shader configuration. Keep material repair an explicit editor operation, not character initialization.

Replace the destructive recurring extraction/stripping calls in `Phase00RigSetup`. Normal Build Showcase only instantiates prepared assets. Provide a clearly named, idempotent preparation/rebuild command for these current assets, with explicit source/output paths confined to editor code. This is a fixture preparation utility, not the deferred general import pipeline. Rebuilding a test scene must not overwrite curated character/material assets.

Remove obsolete rigid-head fallback code, null-cap branches and old binder components after references are migrated. Preserve source and unrelated working-tree changes. Update old menu names/callers so there is one unambiguous normal scene-build operation and a separate explicit asset-preparation operation.

## 3. Animation presentation components

Extract `CharacterAnimationPlayer` from the showcase. Use the proven Playables approach with one graph per character. The player owns its graph, finite operation completion, blends and shutdown. It accepts clips/masks already resolved by a caller; it does not know Cards, semantic tags or the catalog.

Keep only the current channels: foundation and one body gesture. Use a small crossfade mixer where required. Do not reserve future layers. Foundation playback continues when a gesture starts, changes or clears. Transition foundation clips into their requested resting loop without rebuilding the graph. Ordinary gestures play once and fade back to foundation; explicitly requested looping demo gestures may loop until cleared, but must not redefine the V1 finite-gesture default.

A replacement/reset/disable cancels the previous pending operation. Old completion must never start a queued sitting clip after Reset or after a new request. Operation handles/tasks must settle as completed, canceled or faulted; graph disposal cannot leave a waiter pending. Keep this local to the player, using a small operation identity/cancellation mechanism rather than a general scheduler.

Expose the small operations needed now: set foundation, play transition then foundation, play/clear gesture, reset/stop. Exact signatures may follow repository async conventions, but readiness means a requested transition/blend has settled, not merely that a playable was allocated. The later performance host wraps these operations and reports semantic arrival; this patch does not implement that host.

Use the existing tested gesture mask. Ordinary gestures exclude head/face. A concrete head-owning gesture can carry the approved simple claim that suspends gaze during its influence and releases it on clear/completion/cancel. No generalized body-channel arbitration. Root motion stays disabled; showcase travel still moves the actor root. Navigation and anchor operations remain in the performance stack.

## 4. Facial ownership and gaze

### CharacterFaceController

Move exact face-control bindings and neutral data into character-owned configuration. Cache indices on explicitly assigned facial renderers, not every renderer beneath the character. Retain current neutral/smile/frown/OW behavior. Exact exported control names may occur on multiple relevant renderers; the prepared binding can list each occurrence without fuzzy matching.

Store authored neutral weights rather than recapturing an already animated expression on re-enable. Change/reset only owned channels, leaving unrelated shaping and clothing morphs untouched. Keep the current jaw correction as an explicit Lara facial configuration, applied before facial posing, rather than an unconditional showcase hack. The face controller is the sole owner of its jaw adjustment. Do not build lipsync now.

### CharacterGazeController

Extract the working persistent-smoothing principle into a reusable controller. Serialize a calibrated face-forward/up basis relative to the head and a neutral head frame relative to its parent. Establish those values from this rig's rest pose once; do not infer them from eye positions each frame or assume negative actor-forward. No package replacement is needed for this patch.

Use head-only aim for the current foundation. Torso/neck distribution, eye tracking and secondary motion are future additions, not prerequisites. Configure separate yaw and pitch limits in the neutral parent frame; start the fixture at yaw ±70 degrees, pitch up 30/down 40, and record any changes needed for visible mesh quality. These are authored presentation defaults, not universal anatomical claims.

Compute desired aim in that calibrated frame, clamp it, smooth persistent aim state using elapsed time, then clamp the final result again. Apply the complete correction after animation. Preserve authored pose when gaze influence is zero. Blend gaze influence on enable/disable and head-claim changes; reset smoothing on explicit character reset/teleport. Targets behind the character clamp consistently; no instant 180-degree turn. A null/lost target blends back to animation. Test pitched and rotated actor poses as well as upright standing.

One `CharacterPresentation` coordinator gives a deterministic final-pose order: Animator/Playable evaluation → configured jaw/face adjustment → gaze → registered private-skeleton attachments. Controllers expose internal apply methods rather than competing independent LateUpdates. Facial/gaze references resolve only against the body rig. SharedSkeleton clothing follows the final body transforms directly. Prove this order in the actual Unity frame loop; do not infer it from compilation.

## 5. Showcase and developer workflow

Reduce `Phase00RigShowcase` to buttons, fixture positions, source/target comparison and calls to these components. It must not retain a second animation/gaze implementation. The production character works in a blank scene without this MonoBehaviour or its builder. Keep its simple travel interpolation as test orchestration until the approved anchor work replaces it.

Preserve the front-facing spawn-aligned camera, direct WASD/Q/E input, RMB look, fixed exposure and current materials. Camera capture must release on RMB-up, disable and focus loss; movement must stop on focus loss. No controller-axis input. Do not silently rebuild the camera behind Lara.

The artist's workflow after this patch: instantiate the assembled Lara prefab, delete/disable one attachment, drag a prepared attachment beneath Attachments, enter Play. Binding needs no scene-specific call or inspector bone assignment. Runtime spawn/reparent follows the same path. This guarantees prepared compatible prefabs, not raw Daz exports. No custom wardrobe UI is required.

## 6. Implementation sequence and acceptance

Use five cohesive steps. Do not advance past a broken visual fixture to implement more abstraction.

1. **Capture baseline and introduce rig/binding contract.** Record current working asset references and materials. Implement CharacterRig/RiggedAttachment and ordered private-rig synchronization. Prove a fresh runtime instance and disable/re-enable, not only an editor-built scene.
2. **Convert assets and stop destructive rebuilding.** Convert hair, bra and panties; prepare the base and assembled Lara prefabs; remove old binder paths. Prove blank-scene use, removal and replacement. Keep current appearance.
3. **Extract animation ownership.** Preserve foundation time during gesture changes, add bounded blends and cancel stale transitions. Retain standing, walking, sit/stand and both current gesture demonstrations.
4. **Extract facial/gaze ownership and final-pose order.** Preserve visible face presets and calibrated gaze; synchronize hair after the resulting head pose. Keep reset/disable/re-enable deterministic.
5. **Slim showcase, verify and document.** Exercise the combined fixture, write short artist/runtime caller instructions, update setup results and README, and leave a clear handoff to Ticket 01/02/04 as applicable. Do not claim those tickets completed merely by this cleanup.

Focused regression evidence (not an import-validator project):

- Fresh scene reload and Play, with normal domain reload and with the editor's configured fast-enter-play options: body and all three items bind without builder calls.
- Two actors with identically named skeletons: each attachment binds to its nearest owner. Reparent an item between actors; no references remain to the previous actor and removal leaves no orphan bones.
- Verify mapped proxy world transforms after a full animated frame, especially head and hip: parent-order errors must be detected. Sample standing/sitting/head-turn poses and visually check deformation and cap rendering. Bounds alone do not prove correct skinning.
- Change/clear a gesture while walking: foundation phase does not reset. Reset or replace a sit transition midway: its old completion never fires. Repeated enable/disable leaves one live graph per enabled player and no dangling operations.
- Neutral, smile, frown and OW visibly work; a nonfacial shaping channel is unchanged by face reset. Hair follows the final gaze pose in the same frame.
- Gaze tracks reachable left/right/up/down targets, clamps unreachable ones, behaves with actor rotation/pitch, and gives comparable settled results at representative frame rates. Toggle gaze/head ownership without a snap or permanent offset.
- No pink surfaces, restored white eyes, opaque lashes, scalp z-fighting, runaway camera or exposure ramp. Check in HDRP Play mode, not only serialized properties.

Add targeted automated lifecycle/playback tests where they expose these regressions and perform the combined visual check in Unity. Report separately what compiled, what tests ran, and what was visually confirmed. Do not treat a stand-alone .NET compile against Unity DLLs as equivalent to Unity execution. No licensing-client changes or license-management commands. If the editor requires a user-run menu/test action, prepare one precise instruction and state the outstanding evidence.

## Expected file surface and execution guardrails

Runtime additions under the existing `Assets/Scripts/Game` tree: CharacterRig, RiggedAttachment, CharacterAnimationPlayer, CharacterFaceController, CharacterGazeController and CharacterPresentation; use small serializable records alongside owners rather than an extensibility framework. Unity-facing assets stay Unity-owned. Modify Phase00RigShowcase, Phase00RigSetup and current material/preparation callers; keep DazHairAttachment/DazHairRigFollower only as obsolete compatibility types until all external serialized assets are migrated. Prepared attachment prefabs remain under Assets/Characters with Unity-generated metadata; the current canonical Lara base remains the Daz-generated material prefab until the import pipeline provides a game-owned wrapper. Add focused tests under the existing Unity test assemblies and update setup documentation.

The patch has been applied without licensing-client changes, package installation, pushing, or overwriting unrelated work. The current reusable character still uses the Daz-generated material prefab as its canonical base; a separate game-owned wrapper prefab is deferred until the import pipeline exists. The first prepared hair asset is regenerated as an unpacked independent prefab only when the explicit preparation command sees the old bridge variant. Those are deliberate current-foundation facts for the next execution agent, not permission to redesign V1.

Completion boundary: current content works through reusable Unity components and prepared prefabs. Resume the approved Performance Director stack immediately. The later host supplies selections to CharacterPresentation; Core remains unaware of skeletons, materials, graph topology and attachment lifecycle.
