# Foundation patch results

Updated September 13, 2026. This records the foundation work applied before the Conversation Performance V1 stack resumes.

## What is now in the working tree

- `CharacterRig` owns the explicit Lara Animator, `Genesis8Female` body root, sibling `Attachments` container and exact body-bone map.
- `RiggedAttachment` is the one runtime attachment contract. Shared-skeleton mode is used by the bra and panties; preserved-skeleton mode is used by the 2021-02 hair. It binds on enable, rebinds after reparenting, suppresses unbound renderers and applies private body-bone synchronization after the final presentation pose.
- Each prepared attachment now declares `supportedRigFamilyId: Genesis8Female`; a drag-and-drop onto a different rig family fails with a named diagnostic and stays hidden instead of silently deforming.
- `CharacterPresentation` owns the frame order: Animator/Playables, facial jaw baseline, face controls, gaze, then preserved attachment synchronization.
- `CharacterAnimationPlayer` owns one persistent Playables graph with a foundation mixer and one masked overlay. Changing clips does not rebuild both graphs or reset the other channel.
- `CharacterFaceController` owns the explicit body/eyelash facial renderer list, captured neutral weights, exact exported control matching, and the Lara jaw baseline. `CharacterGazeController` owns persistent smoothed head aim with the expanded yaw/pitch limits. Gaze calibration uses the eye bones' forward axes and an orthogonal rig-up frame; it does not use the eye-height offset from the head pivot as a facing vector.
- `Phase00RigShowcase` is now a thin fixture UI. It does not own a second animation graph, face implementation or gaze implementation. The direct camera input remains WASD/Q/E plus RMB drag.
- Hair, bra and panties no longer serialize the retired binder components. The old `DazHairAttachment` and `DazHairRigFollower` classes remain only as obsolete compatibility types so an old external serialized asset can be diagnosed instead of becoming a missing-script component.
- `Phase00RigSetup` separates one-time preparation from the disposable showcase rebuild. Preparation is idempotent: it only regenerates the bridge hair when the asset is absent, still uses the retired binder, or is still a bridge prefab variant. The normal showcase path validates prepared assets and then assembles the test scene.
- The Luna-only import/pose probes remain available under `TruthCardGame > Diagnostics > Rig Spike`. They now run in an additive temporary scene and restore the user's active scene. They are measurement tools, not the Lara content workflow. The two explicit attachment rebuild menus are labeled destructive and require confirmation; ordinary preparation calls their internal rebuild path only when an asset is actually missing or retired.

## Intentional current-foundation differences from the original patch expectation

The material-ready Daz prefab at `Assets/Daz3D/lara/Prefabs/lara_Prefab.prefab` remains the canonical reusable Lara base. It now carries the production-shaped rig/presentation components and the sibling `Attachments` container, but a second game-owned wrapper prefab is deferred until the import pipeline can own that conversion without duplicating the Daz material/import source.

The checked-in hair asset may still be the previously generated bridge variant until the explicit editor preparation command is run once after this code change. The builder now unpacks the bridge instance before saving, and `Ensure202102Hair` detects the old variant and rebuilds it. This is the only remaining asset migration action; it is deliberately an explicit Unity-editor operation rather than a batch launch that could touch licensing.

The three current attachment assets are prepared with `RiggedAttachment` metadata in place. The source export, HDRP materials, invisible scalp-cap material and existing bridge import assets remain the source of truth. There is no general import validator, wardrobe UI, hair physics system, or bulk authored-scene updater in this patch.

If a wardrobe attachment is ever missing, the explicit rebuild command recovers its mesh from `Assets/Daz3D/lara/lara.fbx`; it does not depend on the already-stripped material prefab. Normal preparation still leaves an existing compatible attachment untouched.

## Verification completed without launching Unity

- The six reusable runtime classes compile against the installed Unity 6000.5.9f1 managed assemblies with zero errors.
- `Phase00RigShowcase`, `Phase00DebugFlyCamera`, `DazHairAttachmentBuilder` and `LaraWardrobeAttachmentBuilder` compile in isolated Unity facade/editor checks with zero errors.
- The diagnostic editor scripts (`CharacterRigSetup`, `RigSpikeBuilder`, `RigBakeDiagnostics`, `RigClipDiagnostics`) compile in the same isolated Unity editor check with zero errors.
- The new `FoundationAssetReadinessTests` guards the serialized Lara controller/attachment contract and rejects retired binder components. Its source compiles against the installed Unity editor/NUnit facades with zero errors; it still requires the normal Unity EditMode runner for execution.
- Prepared prefab YAML has no duplicate local file IDs. The base prefab contains the explicit CharacterRig, face, gaze, presentation, animation-player and Attachments records; each attachment prefab contains a RiggedAttachment record and no retired binder record.
- `git diff --check` reports no whitespace errors in the changed foundation files. Existing unrelated line-ending warnings are preserved and are not foundation failures.

Unity Play Mode still owns the evidence that cannot be established by these checks: actual attachment deformation after animation, hair binding after the unpacked rebuild, face/gaze appearance, and the fixed-exposure/camera pass.

## Required hands-on Unity check

With the project open in Unity 6000.5.9f1, run `TruthCardGame > Phase 00 > Prepare Current Character Foundation` once. Then run `TruthCardGame > Phase 00 > Validate Prepared Character Foundation`, open `Assets/Scenes/Phase00RigShowcase.unity` by running `TruthCardGame > Phase 00 > Rebuild Disposable Rig Test Scene`, and press Play. If the preparation or validation menu reports a named missing bone/renderer, stop and record that concrete asset path; do not add a new fallback binder. Do not use the `Rebuild ... (destructive)` attachment menus unless the preparation command has identified a missing or retired asset.

After that pass, the next agent starts at the first unmet approved Conversation Performance V1 ticket. It must preserve the prepared prefab references, treat the showcase scene as disposable, and never rebuild or overwrite authored scenes as part of ordinary performance/content work.
