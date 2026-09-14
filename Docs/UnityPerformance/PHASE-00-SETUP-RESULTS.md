# Phase 00 setup results

Updated September 13, 2026. Automated setup and content gates are green. The final human visual pass in the prepared Unity scene is the only open Phase 00 acceptance item.

## Current status

| Prerequisite | Status | Evidence / next action |
|---|---|---|
| Pinned Unity editor | Verified | `ProjectSettings/ProjectVersion.txt` pins 6000.5.9f1. Batch setup and both Unity test platforms ran with that exact editor. |
| Portable Core/SQLite baseline | Verified | `dotnet test Game.Workbench.sln --no-restore --verbosity minimal`: 401 passed (Core 178, Content.Sqlite 165, WPF 47, Profile.Sqlite 11), zero failed. |
| SQLite/type identity arrangement | Verified | Portable SQLite mapping has one physical source under `Assets/Scripts/Portable/Game.Content.Sqlite`; the DotNet project links it. No duplicate Game.Content/Core binaries were added. The full Unity EditMode suite includes `SqliteContentIntegrationTests` and passed 37/37. |
| Canonical content | Verified | `Content/GameContent.db` is schema v11 and contains the typed Phase 00 Session, Phase, Card, dialogue and default Session weighting. Loader round trip, `integrity_check`, `foreign_key_check`, and the full .NET suite pass. |
| External motion acquisition | Verified | The official Quaternius Standard no-root-motion Unity FBX is installed with publisher README, CC0 license and provenance manifest. |
| External source Avatar | Verified | `UAL1_StandardAvatar` is valid Humanoid; 52 mapped human bones, 68 skeleton bones, 43 clips. |
| Lara target Avatar, face and materials | Verified automatically | `laraAvatar` is valid Humanoid; 55 mapped human bones, 177 skeleton bones and five meshes. The showcase uses the Daz-generated prefab with Unity 6 supported HDRP/Lit materials and imported texture bindings. Exact exported neutral/smile/frown controls are driven on the real `Genesis8Female.Shape` renderer while the neutral jaw transform is restored after body animation. The remaining check is visual quality in motion. |
| Source/target motion comparison | Awaiting human visual acceptance | Open `Assets/Scenes/Phase00RigShowcase.unity`, press Play and use the upper-left controls. Automated asset/readiness and PlayMode suites pass. |

## Acquired motion source

- Creator: Quaternius.
- Product: Universal Animation Library 1, Standard (free), official archive `Universal Animation Library[Standard].zip` downloaded September 13, 2026.
- Publisher source: https://quaternius.itch.io/universal-animation-library
- License: CC0 1.0, included at `Assets/Animations/Quaternius/Control/LICENSE-CC0-1.0.txt` with the publisher README beside it.
- Selected import: `Unity/UAL1_Standard.fbx`, the publisher's non-root-motion file. The root-motion duplicate was intentionally not imported.
- Repository path: `Assets/Animations/Quaternius/Control/UAL1_Standard.fbx`.
- SHA-256: `21B32D912DA3CB93426D974FB945E86F5B2E86970ACD2CE89905E0FBF9F1DCC2`.
- Independent FBX inspection: Blender 4.5.3 loaded a 65-bone armature, mannequin and 43 animation stacks. Unity reports one valid Humanoid Avatar, 43 clips and one 8,462-vertex mesh.

The seven V1 setup roles are present:

| Role | Source clip |
|---|---|
| Standing foundation | `Idle_Loop` |
| Travel | `Walk_Loop` |
| Stand to sit | `Sitting_Enter` |
| Sitting foundation | `Sitting_Idle_Loop` |
| Sit to stand | `Sitting_Exit` |
| Body alternative 1 | `Idle_Talking_Loop` |
| Body alternative 2 | `Interact` |

`Dance_Loop` and `Sitting_Talking_Loop` remain available as later zero-Card-edit variety candidates. The setup resolves imported role names by their final `|`-delimited segment without renaming or duplicating clips.

This proves an external Humanoid motion library on Unity's retargeting path. A direct Mixamo download/playback check remains optional follow-up; Phase 00 makes no claim that the imported file came from Mixamo.

## Chosen target and actual import inventory

Lara is the selected V1 target. `Logs/Phase00AssetInventory.txt` records the generated inventory:

- Avatar: `laraAvatar`, valid and Humanoid.
- Human mapping: 55; skeleton mapping: 177.
- Required body references include Hips `hip`, Head `head`, both hands, both feet and both eyes.
- Geometry: five meshes, 188,460 imported vertices.
- Facial bindings: 860 shape bindings across the body and fitted meshes. The showcase captures every imported baseline weight as exported, restores that baseline for Neutral, and drives exact morph-name matches after the mesh prefix: `ST Mika 8 Natural Smile` for Smile, `eCTRLFrown_HD` for Frown and the combined `eCTRLvOW` control for an unmistakable OW-phoneme toggle. Exact matching prevents tongue, mouth-open, or similarly named channels from being activated accidentally.
- Materials: the existing Daz bridge converted Lara's DTU records into 39 material assets and copied 40 used texture files. Its bundled 2023 custom Shader Graph shaders render magenta under Unity 6000/HDRP 17, so Phase 00 replaces all 39 with supported `HDRP/Lit` materials while preserving the converted diffuse and normal bindings. The generated Lara prefab has five renderers, 33 material slots, 33 assigned HDRP/Lit materials and 28 texture-bearing slots.
- Hair limitation: Lara's Natty Hair is a Daz dForce generated strand asset. This export contains no hair bones and only a root-to-tip gradient texture, not the opacity/albedo texture set required for normal HDRP hair cards. It therefore cannot gain believable deformation or card rendering through material tuning alone. V1 should use game-ready hair cards with opacity textures and a skinned bone chain; converting this asset would be a separate content-production pipeline.

Luna remains a valid Humanoid comparison model (55 human mappings, 96 skeleton mappings), but its imported meshes expose no blendshapes. It is not the selected V1 character.

## Setup implementation and paths

- `Assets/Editor/Phase00RigSetup.cs` independently configures the Quaternius source and Lara as Humanoid/Create From This Model, retains each model's own Avatar, writes the inventory, creates the upper-body/no-head mask and builds the scene from `Assets/Daz3D/lara/Prefabs/lara_Prefab.prefab` so the converted materials remain attached.
- `Assets/Animations/Quaternius/Control/UpperBodyNoHead.mask` preserves the lower body and excludes the head/face from body overlays.
- `Assets/Scripts/Game/Phase00RigShowcase.cs` plays the same unchanged source Humanoid clip on the source mannequin and Lara through separate valid Avatars. It provides stand, travel, sit/stand, two masked overlays, exact neutral/smile/frown presets and gaze controls. Face capture starts from the Lara prefab root, not the incidental Animator subtree, and its direct prefab test proves the visible `Genesis8Female.Shape` Natural Smile channel reaches 100%. Gaze derives the visible face-forward direction from the mapped eye bones' forward axes, with an orthogonal rig-up frame, so it does not assume the Daz head bone uses Unity's positive-Z axis or mistake the eyes' height offset for pitch. It is an asset viewer, not a Card interpreter or editable performance database.
- `Assets/Scenes/Phase00RigShowcase.unity` is the runnable setup scene. Its global HDRP volume uses fixed EV100 12 exposure, so Play Mode does not begin overexposed and then visibly adapt. The scene uses a 12,000-lux neutral daylight/studio key, restrained flat ambient light and HDRP/Lit floor/marker materials.
- `Assets/Settings/Phase00RigShowcaseVolumeProfile.asset` persists that exposure rule; the profile owns its HDRP Exposure sub-asset so reloading the project cannot silently drop it.
- `Assets/Tests/EditMode/Phase00AssetReadinessTests.cs` enforces one valid Humanoid Avatar per selected rig, all seven motion roles, the exact expression controls used by the showcase, an active HDRP asset in Linear color space, a persistent fixed-exposure profile, a materialized Lara prefab, non-null material slots, supported HDRP shaders and real texture bindings. It also instantiates the actual Lara prefab and proves the showcase code drives the visible body/face renderer's exact Smile morph.
- `Packages/manifest.json` pins High Definition RP 17.5.0, the version shipped for Unity 6000.5.9f1. `Assets/Settings/TruthCardGameHDRP.asset` is the project's default render pipeline asset, and project color space is Linear so HDRP texture and skin evaluation is not performed in the legacy Gamma path.
- `Assets/Daz3D/Scripts/Editor/Phase00HdrpMaterialSetup.cs` runs the repository's existing Daz conversion path without dialogs, rebuilds the showcase and fails batch setup unless the resulting prefab has renderers, assigned materials, HDRP shaders and texture bindings.
- `Assets/Daz3D/Scripts/Editor/DTUConverter.cs` now persists or updates each generated material asset before retaining it. `Daz3DDTUImporter.cs` reloads those materials after texture imports and asset refreshes, preventing invalid temporary material references from breaking prefab generation. The converter continues to reuse the bridge's DTU mapping and shader implementation; no parallel material mapper was added.

## Canonical test fixture

The fixture was authored through existing typed repositories after the Unity project lock was released. No SQL or provisional action discriminator was added.

- Session: `phase00-session` — Conversation Performance Setup.
- Default Session card weighting: all six values `1.0`.
- Phase: `phase00-phase` — Conversation Performance Setup.
- Card: `phase00-card` — Playful Tease.
- Dialogue tag: `phase00-playful`.
- Snippets: `phase00-line-hello`, `phase00-line-tease`.
- Card actions: blocking `DialogFromTags`, blocking `WaitForContinue`, then nonblocking `IncrementProgress(10)`.

The Session graph starts, references the Phase and ends. The Phase graph enters, executes the one Card and reaches its `Complete` exit through the existing `PhaseGoto` action. The first full test run exposed a missing `session_card_weighting` row; the seeder was corrected to call `SessionRepository.ReplaceCardWeighting` with the model defaults. The canonical migration-copy test then passed with the other 400 .NET tests.

Ticket 03 will add the approved `Perform` action when that real discriminator/schema/UI exists. Phase 00 does not invent it early.

## Verification

| Check | Result | Artifact |
|---|---|---|
| Phase 00 configuration/import | Passed, no compile errors | `Logs/Phase00-Configure-5.log` |
| Targeted asset readiness | 4/4 passed | `Logs/Phase00-Readiness-EditMode.xml` |
| Unity 6 HDRP material fallback | Passed; 5 renderers, 33/33 material slots assigned, 28 textured HDRP/Lit slots | `Logs/Phase00-HDRP-FallbackMaterials.log` |
| Full Unity EditMode under HDRP | 37/37 passed | `Logs/Phase00-HDRP-EditMode.xml` |
| Full Unity PlayMode under HDRP | 11/11 passed | `Logs/Phase00-HDRP-PlayMode.xml` |
| Full .NET solution | 401/401 passed | Console run September 13, 2026 |
| Canonical SQLite | `integrity_check=ok`; `foreign_key_check` returned no rows | Typed seeder and canonical migration-copy test |

The PlayMode log contains cancellation stack traces from the existing `UnityGameDelay` teardown path. They did not fail a test; the Unity runner completed with exit code 0 and 11/11 passing.

## Final visual acceptance

Open `Assets/Scenes/Phase00RigShowcase.unity`, press Play, and use the controls in the upper-left corner. The left figure is the Quaternius source and the right figure is Lara. Each body control sends the same unchanged Humanoid clip to both.

Check these concrete points:

1. `Stand`, `Walk`, `Sit` and `Stand Up` keep Lara's feet, knees, hips and shoulders coherent enough for the vertical slice.
2. Apply each gesture while standing, then while sitting. The lower body should stay on its foundation and the face/head should remain available.
3. Confirm the opening brightness is immediately stable; it should not fade down after Play begins. Confirm Lara renders with skin, hair, eye and clothing textures rather than flat grey materials.
4. Switch Exported Neutral, Smile and Frown while a body motion continues. Use Toggle OW Phoneme for the unmistakable wiring check: ON drives the combined `eCTRLvOW` morph to 100%, and OFF restores the exported neutral face. The on-screen status reports the exact preset and number of matched meshes.
5. Toggle gaze and confirm Lara faces the green target directly without destroying the body motion. Move the target during Play Mode to test several directions.
6. Record any visibly incompatible posture/gesture pair by its two button names. Compatibility tags belong in the later Unity ingredient catalog; no Card should enumerate these combinations.

After that visual pass, Phase 00 is complete and execution resumes at the first unmet acceptance criterion in ticket 00/01, reusing these exact assets and canonical fixture.
