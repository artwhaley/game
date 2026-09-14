# Phase 00 — acquire and prove the V1 character/animation test kit

**Current status:** the acquisition/setup work described here is already present in the working tree. Read [foundation results](../../Docs/UnityPerformance/FOUNDATION-PATCH-RESULTS.md) and the [execution-agent handoff](FOUNDATION-HANDOFF-PROMPT.md) before doing anything. Do not reacquire the motion library, rerun destructive imports, or rebuild the character foundation unless current Unity evidence proves a missing asset. The only expected hands-on prerequisite is the one-time preparation/validation menu pass in the open editor.

September 13, 2026. This setup assignment supplemented tickets 00/01; it did not renumber them, restart completed work, or expand V1. The deliverable is an installed, inspectable test kit and a visual retargeting proof, not another list of assets someone else must find.

## Agent assignment and success condition

You have web/browser, filesystem, package-management and Unity editor access. Acquire missing reusable animation content, configure a supplied character to consume it through Unity Humanoid retargeting, verify facial capability, and leave a working test scene and exact handoff for tickets 01–04. Do the research, downloads, imports, binding and troubleshooting yourself where access permits. Ask the user only for a specific login, purchase decision, missing source export or visual action you cannot perform.

The user does not want Daz-authored body animations. Do not ask them to create or buy those. Daz supplies the character and, if needed, facial morphs; a reusable external humanoid motion library supplies body animation.

Use Unity **Humanoid**, with a configured Avatar, for the requested Mixamo-compatible workflow. Unity's **Generic** import type is a different mechanism. Do not replace/re-skin a working Daz skeleton merely to rename its bones to Mixamo names. A valid mapping and a successful external-clip playback test are the objective. [Unity retargeting](https://docs.unity3d.com/6000.0/Documentation/Manual/Retargeting.html)

## Read this current-state audit before doing anything

Audit performed against the working tree on September 13. Source/files and existing test XML were inspected; the following is not a new Unity test run.

| Area | Evidence and current status | Required response |
|---|---|---|
| SQLite/CLR identity | Mapping source now lives in Assets/Scripts/Portable/Game.Content.Sqlite; DotNet/Game.Content.Sqlite links it. Schema scripts have one physical home in Assets/StreamingAssets/GameContentSchema. Provider DLLs exist under Assets/Plugins. SqliteContentIntegrationTests covers loader/engine type equality. | Preserve and verify this work. Do not reinstall a parallel SQLite stack or restore the moved files to duplicate locations. |
| Action classification | ActionExecutor dispatches using ActionExecutionKind.ControlOrYield. | Preserve; rerun relevant tests, do not redo the architecture. |
| Existing test evidence | Logs/Ticket01Rig-EditMode-EditMode-20260912195807.xml reports 30 passed; Ticket01Rig-PlayMode-PlayMode-20260912195812.xml reports 11 passed. Ticket-00-BASELINE reports 401 DotNet passes at its recorded checkpoint. | These are previous-run results, predating this setup. Verify the now-modified project before claiming current green status. |
| Main Unity game | GameManager still uses UnityContentGraphBuilder and ScriptableObject content. Recent work also touches legacy scenes/cutscene tests. | Loader feasibility is not completion of the real SQLite-authored V1 game path. Stop spending setup effort completing legacy cutscenes. |
| Luna | Assets/Characters/Luna/luna.fbx; original also at ../source/luna.fbx. Prior report measured no clips/no blendshapes and a valid/human Avatar with serious pose discrepancies in generated fixtures. | Keep as a comparison candidate. Reproduce with known-good external humanoid motion before attributing every symptom to its skeleton. |
| Luna spike | Rig_Stand, Rig_Sit, Rig_ArmRaise are generated local transform clips. RigSpikeBuilder verifies playback with Avatar cleared; its UpperBody mask was not exercised in that report. | This proves a limited local playback route, not cross-character retargeting or layered V1 acceptance. Do not use avatar=null as the solution for the chosen character. |
| New Daz export | Assets/Daz3D/lara/lara.fbx and lara.dtu. DTU identifies Genesis8Female and contains 124 Morphs/124 MorphNames. Current FBX meta has no human mapping and serialized animationType=2. | Prioritize inspecting Lara: configure Humanoid and verify actual imported blendshapes. DTU entries are promising export metadata, not proof of usable Unity shape channels. |
| Daz bridge | Assets/Daz3D/Scripts/Editor/Daz3DDTUImporter.cs contains mapping support. Its visible Genesis 8 T-pose correction branch is disabled with an additional false condition. | Inspect the actual importer behavior; do not assume bridge presence means pose normalization happened. Do not patch vendor code blindly. |
| Performance runtime | No Performance Director/Event/PresentationCatalog implementation found in the inspected Assets source. | Those are later implementation tickets. Setup must not depend on them already existing. |
| Game content | The previous baseline reports canonical DB schema v11 with zero Sessions/Cards. | Query current counts; if still empty, create a small fixture through existing authoring facilities. Missing authored test content is work assigned to the agent. |

Read Docs/UnityPerformance/TICKET-00-BASELINE.md, TICKET-01-RIG-SPIKE.md and SQLITE-PROVIDER-VENDORING.md as historical evidence. Their “fix or bypass Avatar” and “bring actual animation” open decisions are resolved here: obtain external motion and prove Humanoid retargeting. Keep useful diagnostics; revise those conclusions in the setup completion report.

The working tree contains substantial uncommitted implementation and imported assets. Preserve all of it. Do not reset, overwrite the DB, delete spike files or bulk-stage/push third-party binaries without first inspecting the exact project state and asset terms. Follow agents.md and database versioning rules. Do not confuse moved SQLite files with missing source.

## A. Establish a usable environment, then start acquisition immediately

1. Confirm Unity 6000.5.9f1 and project path. Inventory actual running editor instances and their project paths before batch runs; do not kill all Unity processes or run two editors against the same Library. Use the existing editor when possible. Coordinate saving/closing that project only if needed for a batch run, then reopen it afterward.
2. Read existing scripts and unity-cli.md. The existing run-rig-spike.sh calls Luna-specific import/fixture generation; do not rerun it as an inspection-only tool or let it overwrite repaired settings. Make new reports accept an explicit model path. Provide PowerShell-compatible commands or a verified Git Bash invocation on Windows.
3. Check current Console/import errors and render pipeline. Repair only errors blocking this setup. The Daz bridge includes Standard/URP/HDRP packages; use the project's actual pipeline, not all three or a pipeline migration. Plain working materials are sufficient for motion tests.
4. Reuse installed tools/packages first. Agent execution includes acquiring free animation assets and using available package managers. If repository dependency rules require approval for a genuinely new tool/plugin, name the exact package/version, cost and purpose in one concise request; continue independent acquisition/inspection. Do not use “third party” as a reason to stop inventory or research. No paid purchase or subscription without an explicit user decision.
5. Recheck baseline compilation and targeted SQLite/type-identity tests after the bridge import. A provider test failure gets an actual reproduction; a missing rig never blocks portable Core work.

## B. Obtain a small reusable motion library

Start with these actual sources; inspect the downloaded edition and its license instead of assuming every advertised clip is free:

- [Quaternius Universal Animation Library](https://quaternius.com/packs/universalanimationlibrary.html), [official download page](https://quaternius.itch.io/universal-animation-library). The publisher provides a free Standard edition and describes humanoid retargeting, FBX and root-motion/no-root-motion variants. Acquire the free edition first for an immediate external-motion control. Confirm which sitting/gesture clips are actually included; paid/source editions are not assumed available.
- [Mixamo](https://www.mixamo.com/) for missing conversational/sitting clips and a direct Mixamo smoke test. Use a stock character as the source rig; no upload of Lara is needed for Unity retargeting. Adobe says access requires an Adobe ID but no paid subscription. [Official FAQ](https://helpx.adobe.com/creative-cloud/faq/mixamo-faq.html)

Minimum role coverage, not a demand for exact clip titles:

| Role | Search candidates / selection requirements |
|---|---|
| Standing foundation | Idle / Breathing Idle; calm, loopable |
| Travel | Walking; prefer an in-place option with coherent footsteps |
| Stand → sit | Sitting Down; ordinary chair, not floor/crouch |
| Sitting foundation | Sitting Idle; compatible with chosen chair transition |
| Sit → stand | Standing Up; an actual tested operation, not assumed reversed playback |
| Two body alternatives | Talking, Waving, Pointing or similar distinct gestures; select for useful torso/arm movement and test under a mask |

Download enough to cover those seven roles; a library file may contain multiple usable clips. Identify one additional gesture to add later for the zero-Card-edit variety test. Do not bulk-download thousands of assets. Body source packs are not expected to supply facial animation.

For Mixamo, choose a single source character, retain a reference download with its skeleton, and obtain the selected motion files using the current FBX/Unity option. Prefer Without Skin for additional clips, 30 fps and no key reduction initially where those controls are offered. Preserve source skeleton information. Record actual UI settings and exact downloaded names—these search candidates are not a claim that today's catalog has uniquely named matches.

Put imports under a clearly named Assets/Animations/<Source>/ folder, with source URL, edition/license, original clip name, import settings and role mapping recorded in a short asset manifest. Use .meta files and existing LFS rules. Do not expose raw assets publicly merely because use inside a game is allowed.

If Mixamo login requires the user, send the precise handoff in section G and continue using the Quaternius control clips. If a provider is unreachable, try the other official source, diagnose network/auth access, and report the exact missing roles. A log saying “no animation in character FBX” is not a blocker after this assignment—it is normal for character and motion to be separate assets.

## C. Configure and prove the supplied character

1. Inspect Lara first, then Luna. For each report model path, imported meshes, Avatar mapping/validity, real animation clips, actual blendshape names per renderer, facial bones, unit scale and rest pose. Use editor APIs to inspect imported assets; do not infer blendshape success solely from DTU or importBlendShapes=true.
2. Configure the target model's Rig as Humanoid / Create From This Model, check required bones and correct the reference pose in Avatar Configure. Inspect hips/spine/head, limbs and twist/helper mappings. A green validity indicator is necessary but not sufficient. Save repeatable importer settings; do not edit the source mesh/skeleton unnecessarily. [Unity Avatar configuration](https://docs.unity3d.com/6000.0/Documentation/Manual/ConfiguringtheAvatar.html)
3. Import external motion as Humanoid using its **source** skeleton/Avatar. Copy From Other Avatar is valid only when that file uses the same source skeleton. Do not assign Lara's Avatar as the import source for a different Mixamo skeleton. The target Animator retains Lara's own Avatar; retargeting bridges them.
4. Prove the exact same idle/walk clip on its source character and on Lara. Compare scale, orientation, feet, knees, hips, arms and root drift. If source playback is also broken, fix clip/import/test setup first. Test normal Animator playback as a control for custom Playables/bake diagnostics. Use AlwaysAnimate during off-camera automated tests; this is not a replacement for watching the result.
5. Use the source library's no-root-motion travel variant where available and one parent mover for the in-place room test. Inspect sitting hip height/root settings separately; do not bake away required pose change merely to stop travel drift. Calibrate a simple chair to the rig, not a whole contact solver.
6. Fix actual target mapping/reference-pose/import issues based on that comparison. Do not require 96-bone equality across different rigs/proportions. Do require coherent movement on the target. Never pass setup by clearing its Avatar and replaying Luna-specific transform curves.

Default target is Lara if its imported facial capability and external retargeting pass. Luna remains an alternative if it is quicker to repair and has a usable facial mechanism. If neither supplied rig passes after bounded diagnosis, obtain a representative humanoid from an official source to keep body/planner work moving; report the supplied-rig defect and the exact repair needed. A substitute proves the pipeline but does not silently complete the supplied-character/facial acceptance gate. Keep both tracks explicit, with a concrete user step if the source export needs repair. Observe the repository three-strikes rule by changing approach after a failed series, not retrying the same bake or abandoning unrelated work.

Do not upload/re-rig the Daz model online as the default. That can complicate facial morphs, extra bones and fitted meshes. Investigate local Avatar repair first; if re-rigging becomes necessary, present the observed failure and proposed preservation/conversion step for review.

## D. Supply only the facial capability V1 needs

Lara's DTU includes expression-related entries such as ST Mika 8 Natural Smile, MCM_Bette_Frown and Mouth Left-Right; these names must be checked against actual imported meshes and visually tested before binding. Choose neutral plus two clearly different usable expressions. Keep all affected facial meshes coherent, including eyelashes/eyes where necessary. Exclude head/face transforms from the body gesture mask when they would overwrite this test.

If usable blendshapes already exist, make simple Unity presets from them. No Daz animation clips, SALSA, phoneme export or lipsync package is required for V1. If morphs are missing, inspect bridge/export settings first. Request only a character re-export with the specific missing expression morphs, not animated takes.

The workspace's ../DAZ-GENESIS-8.1-UNITY-SALSA-MORPH-CHECKLIST.md is not a prerequisite: it describes a larger 8.1/SALSA workflow, while this DTU identifies Genesis8Female. Verify the actual source figure version before prescribing morph IDs. Do not send the user to export seventy speech controls to obtain two facial expressions. Bridge behavior/reference: [DazToUnity source](https://github.com/daz3d/DazToUnity).

## E. Leave a visual asset test and concrete content handoff

Create one small setup scene with ground, two anchors, a simple chair, the selected target character, player gaze target and basic camera/light. Provide Play/Stop/Repeat or equivalent simple controls for stand, in-place travel, sit, stand up, two masked gestures, face presets and gaze. Reuse the simplest technique that passes the existing rig spike. This scene tests assets; it must not become a second game interpreter or production performance timeline.

At least one setup demonstration must use an unchanged external humanoid motion clip on the supplied target with its Avatar assigned. Demonstrate body overlay over sitting and standing, stable lower body and a face change while body motion continues. Record which combinations are actually valid; do not claim all source gestures work in every posture.

Before handoff, leave exact prefab/clip/Avatar/mask/preset/scene paths and the role manifest. Reuse them in ticket 01's registry and catalog work; do not make that agent import and diagnose everything again. If the registry is not yet implemented, the manifest is an acquisition record, not a second editable performance database.

Query the canonical DB's current content using the existing provider. If it is empty, author a minimal named Session, Phase, Card and two tagged dialogue snippets through WPF or existing typed authoring commands, after required checkpoints. Use ordinary blocking dialogue, Continue and progress/end behavior that actually terminates. Avoid equipment/consent requirements irrelevant to the fixture while preserving the real selector. Do not use SampleContentBuilder/legacy ScriptableObjects as canonical content.

Perform does not exist yet: create the valid ordinary dialogue fixture now and identify exactly where ticket 03 will add Playful Tease and Perform once its schema/UI exist. Do not invent placeholder action discriminators or demand that the user author unavailable fields. Completing real Core performance planning and ticket 04 playback remains the approved stack's work; asset acquisition must not depend on it.

## F. Evidence, completion and resumption

Create Docs/UnityPerformance/PHASE-00-SETUP-RESULTS.md during execution with:

- Current baseline results and exact commands, chosen target/source rig and asset provenance/settings.
- Actual target Avatar validity, bone mapping and imported facial channel inventory.
- External-clip control comparison on source and target; visible idle, walk, sit/stand, two gestures, two face presets and player gaze, with screenshots/video and scene path.
- Mask/contact/root observations, known unusable combinations and targeted tests. Old generated-pose tests alone do not pass this gate.
- Canonical fixture IDs, or an exact migration/authoring error being repaired; source acquisition is not a substitute for game content.
- File paths and one short “open this scene, press this control” instruction for the user.
- Clear status per prerequisite: verified, being repaired, or awaiting a named user action. Do not report the entire stack blocked by one asset issue.

Setup is complete when the chosen supplied character can visibly use reusable external Humanoid motion and simple expressions, the seven motion roles are usable, the test fixture is prepared to the current schema, and the next V1 agent can resume without another acquisition round. A Mixamo clip retargeted successfully supports the explicit Mixamo claim; a Quaternius-only pass must be called external Humanoid proof with the direct Mixamo check still pending.

Resume at the first unmet acceptance of existing ticket 00/01, retaining proven SQLite/classification work. Build the minimal registry/catalog and Core/WPF V1 path next. Keep the approved one Perform action, blocking-dialogue refresh and deferred Phase 2 unchanged. Leave the relevant app running for inspection.

## G. Human handoffs: short, specific and prepared

Never ask “please provide animations” or “fix the rig.” First do all discovery/import/setup work possible. Then give the user one bounded task, its reason, exact application/page, selected file/control names, destination and a visible completion check. Do not ask for passwords. While waiting, continue unrelated steps and list what moved forward.

**If Mixamo login is needed:**

“Open https://www.mixamo.com/ in the browser tab I prepared and sign in with your Adobe account. Tell me when the animation browser is visible; I will select/download the motion set. No Daz upload is required.”

If browser automation cannot download, the agent must first select the exact current results and then supply a short download checklist with source character, clip titles/links or unambiguous preview identifiers, settings and the absolute download folder. Typical controls are FBX/FBX for Unity, Without Skin for extra motion clips, 30 fps, no reduction and In Place for walking where available; verify today's labels before issuing instructions. Import/rename/configure/verify everything after the files arrive.

**If the character needs a morph re-export:**

“Open the source figure for lara in Daz Studio. The imported Unity mesh is missing [actual tested channels]. Select the figure and use its Daz-to-Unity Bridge export; enable Morphs and select [exact labels verified for this figure]. Export the skeletal character with those morphs; do not export body animations. Save to [agent-prepared staging destination]. Tell me when the FBX and DTU are present.”

Before sending that request, identify the installed bridge version and actual dialog controls, prepare the destination, preserve the previous export and explain the two expressions the new export should enable. The agent validates the resulting Unity mesh and finishes the import. Do not ask the user to diagnose a missing face after export.

**If Avatar configuration requires hands-on work:**

Open the target FBX's Rig → Humanoid → Create From This Model → Apply → Configure. The agent must identify the exact wrong bone slots/reference-pose issue first, give the required assignments and pose step, then request Apply/Done and the specific preview to inspect. Do not hand off an unexplained “make it T-pose.” Return immediately to the source/target clip test after the user completes it.

**If an account/tool/purchase decision really blocks a role:**

Provide one preferred option, exact cost/package/version, the specific role it fills and the free alternative already attempted. Ask once; continue other work. An empty source FBX, absent paid plugin, optional facial polish or missing standalone build is not itself permission to stop the V1 setup.

## Copy/paste execution prompt

Execute Tickets/UnityPerformanceExecutionStack/PHASE-00-SETUP-AND-CONTENT.md against the current working tree. Acquire and install the missing reusable humanoid motion, prioritize the supplied Lara export, preserve existing SQLite/classification work, and prove external Humanoid retargeting with the target Avatar assigned. Use web/package access to solve acquisition; give me precise short hands-on steps only for login, source export or other actions you cannot do. Do not require Daz body animations, redo the approved architecture, or claim locally baked avatar-null playback proves Mixamo compatibility. Keep independent work moving while a human step is pending. Finish the documented setup gates, leave the test scene runnable, and hand off the first unmet approved V1 ticket with exact asset/content paths.
