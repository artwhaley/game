using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace TruthCardGame.EditorTools
{
    /// <summary>
    /// Ticket 01 spike fixtures for the character rig: stand/sit foundations and an
    /// arm gesture, saved as real clip assets, plus the upper-body AvatarMask.
    ///
    /// The Luna rig ships with no animation at all (see CharacterRigSetup's report), so
    /// these fixtures are what lets the *mechanism* be measured now: whether a pose
    /// can be composed, played back at runtime, and verified, on a rig that owes us
    /// every clip. They are spike inputs, not content — delete them when real
    /// animation lands. The builder is kept as a diagnostic fixture generator and
    /// never participates in prepared Lara assets or authored scenes.
    ///
    /// Four things this builder learned the hard way. All were measured, not
    /// reasoned about, and all are recorded in Docs/UnityPerformance/TICKET-00-BASELINE.md.
    ///
    /// 1. Poses are composed in humanoid muscle space — that is the only rig-agnostic
    ///    way to say "raise the right arm" — but the clips are baked as plain
    ///    transform curves and verified with the Animator's avatar cleared. Unity's
    ///    generated humanoid avatar for this export is not trustworthy: it describes
    ///    a plainly standing rig with a knee at 0.975 and a hip at 0.563, and it
    ///    retargets both hand-authored muscle clips and baked transform clips into a
    ///    pose up to a metre from the one they hold. The identical clip replayed on
    ///    the identical rig with the avatar cleared lands every one of 96 bones
    ///    exactly (deviation 0.00000, see the verify lines). Since these clips belong
    ///    to this rig and there is nothing to retarget from, the avatar is a liability
    ///    rather than a feature — which is the finding ticket 01 needed before any
    ///    work is built on top of it.
    /// 2. Every sign is measured, never assumed: hip flexion, knee flexion and the arm
    ///    raise each pick the direction that produces the intended movement here.
    /// 3. Replay verification must disable the Animator's culling. The default,
    ///    CullUpdateTransforms, skips writing the pose whenever the Animator is not
    ///    visible, and headless nothing ever is — under the default a PlayableGraph
    ///    evaluate writes nothing at all, for any clip, on any rig, including through
    ///    Unity's own AnimationPlayableUtilities.PlayClip.
    /// 4. Ground contact is solved against the foot *bones*, not against the lowest
    ///    point of the skinned mesh. The mesh's lowest point sits 24mm below the toe
    ///    bone at rest, but half a metre below it once the legs fold — it tracks a
    ///    garment, not a sole — so it is reported and never trusted.
    /// </summary>
    public static class RigSpikeBuilder
    {
        public const string SpikeFolder = CharacterRigSetup.CharacterFolder + "/Spike";
        public const string StandClipPath = SpikeFolder + "/Rig_Stand.anim";
        public const string SitClipPath = SpikeFolder + "/Rig_Sit.anim";
        public const string GestureClipPath = SpikeFolder + "/Rig_ArmRaise.anim";
        public const string UpperBodyMaskPath = SpikeFolder + "/UpperBody.mask";

        // Muscle targets, in HumanPose muscle units (-1..1 of each joint's range).
        // The names are Unity's own (HumanTrait.MuscleName, printed by
        // CharacterRigSetup.ReportMuscleNames): a made-up name authors a pose that
        // changes nothing, which is why SetMuscle refuses unknown names. The *signs*
        // are not assumptions — see the Solve* methods.
        private const string ThighMuscleLeft = "Left Upper Leg Front-Back";
        private const string ThighMuscleRight = "Right Upper Leg Front-Back";
        private const string KneeMuscleLeft = "Left Lower Leg Stretch";
        private const string KneeMuscleRight = "Right Lower Leg Stretch";
        private const string ArmMuscle = "Right Arm Down-Up";
        private const string HeadTurnMuscle = "Head Turn Left-Right";

        private const float SitThigh = 0.90f;
        private const float SitKnee = 0.90f;
        private const float ArmRaiseTrial = 0.90f;
        private const float HeadTurn = 0.60f;

        /// <summary>
        /// A baked transform clip should replay exactly, so this covers float storage
        /// only. It is not slack for a bad pose: the failure this must catch — a
        /// hand-authored humanoid clip — is off by 900mm.
        /// </summary>
        private const float ReplayTolerance = 0.005f;

        [MenuItem("TruthCardGame/Diagnostics/Rig Spike/Build Luna Spike Fixtures")]
        public static void BuildFixtures()
        {
            EnsureSpikeFolder();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterRigSetup.ModelPath);
            if (model == null)
            {
                Debug.LogError($"[RIG] {CharacterRigSetup.ModelPath} is not imported; run TruthCardGame → Diagnostics → Rig Spike → Configure Luna Import.");
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("[RIG] === spike fixtures ===");

            // Fixture generation is a diagnostic operation; never replace an
            // authored scene while probing the Luna import.
            var previousScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var instance = Object.Instantiate(model);
            instance.name = "RigSpikeProbe";
            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                {
                    Debug.LogError("[RIG] The rig has no valid humanoid avatar; fixtures need humanoid muscle space.");
                    return;
                }

                var handler = new HumanPoseHandler(animator.avatar, animator.transform);

                // Read the exported pose once — for its root, and to say how far its
                // muscles disagree with its geometry. That disagreement is why the
                // baseline below is zeroed rather than reused.
                var imported = new HumanPose();
                handler.GetHumanPose(ref imported);
                var importedHeights = Measure(animator, report, "imported (as exported)");
                ReportImportedPose(imported, report);
                report.AppendLine($"[RIG]   resting mesh sole {MeshSole(animator):0.####} vs lowest foot bone " +
                                  $"{Feet(animator):0.####}: the mesh hangs {Feet(animator) - MeshSole(animator):0.####} below the " +
                                  $"bones at rest, so contact is solved on bones");

                // ---- the baseline. Every muscle at zero, the root where the export
                // put it: the poses below displace from here and nothing else, so a
                // clip never freezes a skeleton it did not mean to move.
                var rest = RestPose(imported);
                handler.SetHumanPose(ref rest);
                var restHeights = Measure(animator, report, "rest (muscles zero, root kept)");

                // ---- stand: the rest pose with the hips dropped so the feet keep the
                // rest pose's contact. Zero muscles is a slightly crouched stance on
                // this rig, so even the foundation needs the solve.
                var stand = Clone(rest);
                SolveGroundContact(restHeights, animator, ref stand, report, "stand");

                // ---- sit: flex the hips forward, bend the knees whichever way folds
                // them here, then solve the hip height for the same ground contact.
                var sit = Clone(rest);
                SolveThighDirection(handler, ref sit, animator, report);
                SolveKneeDirection(handler, ref sit, animator, report);
                SolveGroundContact(restHeights, animator, ref sit, report, "sit");

                // ---- gesture: raise the right arm and turn the head, with the arm
                // muscle's sign measured rather than assumed.
                var gesture = ComposeGesture(handler, rest, animator, report);
                SolveGroundContact(restHeights, animator, ref gesture, report, "gesture");
                var gestureHeights = Measure(animator, report, "gesture (solved)");

                // ---- bake each pose, then prove it replays.
                var verified = 0;
                if (BakeAndVerify(handler, animator, StandClipPath, "Rig_Stand", stand, report)) verified++;
                if (BakeAndVerify(handler, animator, SitClipPath, "Rig_Sit", sit, report)) verified++;
                if (BakeAndVerify(handler, animator, GestureClipPath, "Rig_ArmRaise", gesture, report)) verified++;

                WriteUpperBodyMask(report);

                report.AppendLine($"[RIG] fixtures verified: {verified}/3 " +
                                  $"(imported pose was hips {importedHeights.HipsY:0.####}, feet {importedHeights.FeetY:0.####})");
                if (verified != 3)
                {
                    Debug.LogError("[RIG] A spike fixture did not replay its authored pose; the mechanism is not proven.");
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
                EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(report.ToString());
        }

        // ---------- pose composition ----------

        private static HumanPose RestPose(HumanPose imported)
        {
            return new HumanPose
            {
                muscles = new float[imported.muscles.Length],
                bodyPosition = imported.bodyPosition,
                bodyRotation = imported.bodyRotation,
            };
        }

        /// <summary>
        /// Says out loud how far the export's muscle values disagree with its
        /// geometry: on this model a plainly standing pose reports a knee near its
        /// stop and a jaw past range, which means the avatar's rest frame differs
        /// from the bind pose. It is why a clip must not freeze the imported pose,
        /// and it is worth knowing before trusting any retargeted clip.
        /// </summary>
        private static void ReportImportedPose(HumanPose imported, StringBuilder report)
        {
            var over = 0;
            var notable = new StringBuilder();
            for (var i = 0; i < imported.muscles.Length; i++)
            {
                var value = imported.muscles[i];
                if (Mathf.Abs(value) < 0.25f) continue;
                over++;
                if (notable.Length < 130) notable.Append($", '{HumanTrait.MuscleName[i]}' {value:+0.###;-0.###}");
            }

            report.AppendLine($"[RIG]   imported muscles: {over} of {imported.muscles.Length} beyond ±0.25 on a standing rig" +
                              $"{notable} — the avatar's rest frame differs from the bind pose, so fixtures displace from zero instead");
        }

        /// <summary>
        /// Hip flexion has a sign that differs per rig, so it is measured: flexing a
        /// hip forward moves the foot toward the direction the toes point, and
        /// whichever sign does that wins.
        /// </summary>
        private static void SolveThighDirection(HumanPoseHandler handler, ref HumanPose pose, Animator animator, StringBuilder report)
        {
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var forward = Facing(animator);
            if (hips == null || !forward.HasValue)
            {
                report.AppendLine($"[RIG]   thigh '{ThighMuscleLeft}' keeps {SitThigh:+0.##;-0.##}: hips or toe bones unavailable, " +
                                  $"so that sign is an assumption");
                SetMuscle(ref pose, ThighMuscleLeft, SitThigh);
                SetMuscle(ref pose, ThighMuscleRight, SitThigh);
                return;
            }

            SetThighs(ref pose, SitThigh);
            handler.SetHumanPose(ref pose);
            var positive = ForwardReach(hips, animator, forward.Value);

            SetThighs(ref pose, -SitThigh);
            handler.SetHumanPose(ref pose);
            var negative = ForwardReach(hips, animator, forward.Value);

            var chosen = positive >= negative ? SitThigh : -SitThigh;
            SetThighs(ref pose, chosen);
            report.AppendLine($"[RIG]   thigh '{ThighMuscleLeft}' keeps {chosen:+0.##;-0.##}: hips->foot forward reach " +
                              $"{positive:0.###} at {SitThigh:+0.##;-0.##} vs {negative:0.###} at {-SitThigh:+0.##;-0.##}");
        }

        /// <summary>
        /// Knee flexion has one degree of freedom with a sign that differs per rig,
        /// so the direction is measured rather than assumed: bending a knee shortens
        /// how far the foot reaches from the hip, and whichever sign does that wins.
        /// </summary>
        private static void SolveKneeDirection(HumanPoseHandler handler, ref HumanPose pose, Animator animator, StringBuilder report)
        {
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (hips == null || foot == null)
            {
                report.AppendLine("[RIG]   knee direction: Hips/LeftFoot bones unavailable; leaving the knee straight");
                return;
            }

            SetKnees(ref pose, SitKnee);
            handler.SetHumanPose(ref pose);
            var positive = HorizontalReach(hips, foot);

            SetKnees(ref pose, -SitKnee);
            handler.SetHumanPose(ref pose);
            var negative = HorizontalReach(hips, foot);

            var chosen = negative < positive ? -SitKnee : SitKnee;
            SetKnees(ref pose, chosen);
            report.AppendLine($"[RIG]   knee '{KneeMuscleLeft}' keeps {chosen:+0.##;-0.##}: hip->foot reach " +
                              $"{positive:0.###} at {SitKnee:+0.##;-0.##} vs {negative:0.###} at {-SitKnee:+0.##;-0.##}");
        }

        /// <summary>
        /// Raise the right arm and turn the head. The arm muscle's sign is measured:
        /// whichever direction lifts the hand is the one the fixture keeps.
        /// </summary>
        private static HumanPose ComposeGesture(HumanPoseHandler handler, HumanPose basis, Animator animator, StringBuilder report)
        {
            var gesture = Clone(basis);
            var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            var handBefore = hand != null ? hand.position.y : 0f;

            SetMuscle(ref gesture, ArmMuscle, ArmRaiseTrial);
            SetMuscle(ref gesture, HeadTurnMuscle, HeadTurn);
            handler.SetHumanPose(ref gesture);
            var raised = hand != null ? hand.position.y - handBefore : 0f;

            if (raised < 0f)
            {
                SetMuscle(ref gesture, ArmMuscle, -ArmRaiseTrial);
                handler.SetHumanPose(ref gesture);
                raised = hand != null ? hand.position.y - handBefore : 0f;
                report.AppendLine($"[RIG]   '{ArmMuscle}' {ArmRaiseTrial:+0.##;-0.##} lowered the hand; fixture keeps {-ArmRaiseTrial:+0.##;-0.##}");
            }
            else
            {
                report.AppendLine($"[RIG]   '{ArmMuscle}' {ArmRaiseTrial:+0.##;-0.##} raised the hand (measured, no sign guess)");
            }

            report.AppendLine($"[RIG]   gesture hand lift: {raised:0.####} units");
            return gesture;
        }

        /// <summary>
        /// Drops (or raises) the hips so the lowest foot bone keeps the rest pose's
        /// contact height, then writes the solved pose. Ground contact as a
        /// measurement of this rig rather than an assumption about it.
        /// </summary>
        private static void SolveGroundContact(Heights ground, Animator animator, ref HumanPose pose, StringBuilder report, string label)
        {
            var handler = new HumanPoseHandler(animator.avatar, animator.transform);

            // Put the pose on the rig first: the correction is measured from where the
            // feet actually are, and the probes above leave an intermediate pose applied.
            handler.SetHumanPose(ref pose);

            var before = pose.bodyPosition.y;
            pose.bodyPosition += new Vector3(0f, ground.FeetY - Feet(animator), 0f);
            handler.SetHumanPose(ref pose);

            var solved = Measure(animator);
            report.AppendLine($"[RIG]   ground contact ({label}): hips {before:0.####} -> {pose.bodyPosition.y:0.####}, " +
                              $"feet {solved.FeetY:0.####} vs rest {ground.FeetY:0.####} " +
                              $"(delta {solved.FeetY - ground.FeetY:+0.####;-0.####;0})");
        }

        private static void SetThighs(ref HumanPose pose, float value)
        {
            SetMuscle(ref pose, ThighMuscleLeft, value);
            SetMuscle(ref pose, ThighMuscleRight, value);
        }

        private static void SetKnees(ref HumanPose pose, float value)
        {
            SetMuscle(ref pose, KneeMuscleLeft, value);
            SetMuscle(ref pose, KneeMuscleRight, value);
        }

        /// <summary>
        /// The direction the rig faces, taken from its own feet: the toe bone sits
        /// ahead of the ankle at rest, and that is the only thing here that says
        /// which way is forward without assuming an axis convention.
        /// </summary>
        private static Vector3? Facing(Animator animator)
        {
            var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var toes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
            if (foot == null || toes == null) return null;

            var forward = toes.position - foot.position;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : (Vector3?)null;
        }

        private static float ForwardReach(Transform hips, Animator animator, Vector3 forward)
        {
            var foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (foot == null) return float.NaN;
            var delta = foot.position - hips.position;
            delta.y = 0f;
            return Vector3.Dot(delta, forward);
        }

        private static float HorizontalReach(Transform a, Transform b)
        {
            var delta = b.position - a.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        private static void SetMuscle(ref HumanPose pose, string muscleName, float value)
        {
            var index = System.Array.IndexOf(HumanTrait.MuscleName, muscleName);
            if (index < 0)
            {
                throw new System.InvalidOperationException(
                    $"[RIG] '{muscleName}' is not a humanoid muscle; fixture authoring would silently do nothing.");
            }
            pose.muscles[index] = value;
        }

        private static HumanPose Clone(HumanPose source)
        {
            return new HumanPose
            {
                bodyPosition = source.bodyPosition,
                bodyRotation = source.bodyRotation,
                muscles = (float[])source.muscles.Clone(),
            };
        }

        // ---------- baking and verification ----------

        /// <summary>
        /// Applies a composed pose, records the resulting skeleton, bakes it as a
        /// clip, then replays that clip on a fresh instance and compares bone for
        /// bone. Recording the skeleton rather than the pose is deliberate: the pose
        /// is the input, the skeleton is what the buyer of the fixture sees.
        /// </summary>
        private static bool BakeAndVerify(HumanPoseHandler handler, Animator animator, string clipPath, string label,
            HumanPose pose, StringBuilder report)
        {
            handler.SetHumanPose(ref pose);
            var expected = Capture(animator);
            var temperatures = Measure(animator);
            report.AppendLine($"[RIG]   {label}: composed hips {temperatures.HipsY:0.####}, feet {temperatures.FeetY:0.####}, " +
                              $"head {temperatures.HeadY:0.####}");

            WriteClip(clipPath, label, animator, report);
            return VerifyClip(clipPath, label, expected, report);
        }

        /// <summary>
        /// Writes the pose currently on the animator as plain transform curves, one
        /// set per bone, over a one-second constant clip. A foundation does not move
        /// on its own — pacing it is the layer mixer's job — and transform curves
        /// replay identically through the editor route and the Animator, which
        /// hand-authored humanoid muscle curves demonstrably do not on this avatar.
        /// </summary>
        private static void WriteClip(string path, string name, Animator animator, StringBuilder report)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { frameRate = 30f };
                AssetDatabase.CreateAsset(clip, path);
            }

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                AnimationUtility.SetEditorCurve(clip, binding, null);
            }

            WriteTransformCurves(clip, string.Empty, animator.transform);
            var bones = 0;
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
            {
                if (bone == animator.transform) continue;
                WriteTransformCurves(clip, RelativePath(animator.transform, bone), bone);
                bones++;
            }

            EditorUtility.SetDirty(clip);
            report.AppendLine($"[RIG]   wrote '{name}': {bones} bones + root, {AnimationUtility.GetCurveBindings(clip).Length} curves " +
                              $"({clip.length:0.###}s)");
        }

        private static void WriteTransformCurves(AnimationClip clip, string path, Transform bone)
        {
            SetConstant(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "localPosition.x"), bone.localPosition.x);
            SetConstant(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "localPosition.y"), bone.localPosition.y);
            SetConstant(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "localPosition.z"), bone.localPosition.z);
            SetConstant(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "localRotation.x"), bone.localRotation.x);
            SetConstant(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "localRotation.y"), bone.localRotation.y);
            SetConstant(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "localRotation.z"), bone.localRotation.z);
            SetConstant(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "localRotation.w"), bone.localRotation.w);
        }

        private static void SetConstant(AnimationClip clip, EditorCurveBinding binding, float value)
        {
            var curve = new AnimationCurve(new Keyframe(0f, value), new Keyframe(1f, value));
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        private static string RelativePath(Transform root, Transform bone)
        {
            var path = bone.name;
            var current = bone.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        /// <summary>
        /// Replays a baked fixture on a fresh instance and compares every bone's world
        /// position against the skeleton it was recorded from — bone for bone, not
        /// joint by joint, because the pose a fixture holds is the whole skeleton and
        /// a spot check on hips and head once passed while both legs were wrong.
        ///
        /// The Animator's culling mode has to be switched off, and that is not a
        /// detail of this tool: the default, CullUpdateTransforms, skips writing the
        /// pose whenever the Animator is not visible, and headless nothing ever is.
        /// Measured (RigClipDiagnostics) — under the default, a bare PlayableGraph
        /// evaluate writes nothing at all, for any clip, on any rig, including through
        /// Unity's own AnimationPlayableUtilities.PlayClip. Anything that poses a rig
        /// without it being on screen — a headless test, an off-screen portrait, an
        /// unattended capture — needs this set.
        /// </summary>
        private static bool VerifyClip(string clipPath, string label, BonePose expected, StringBuilder report)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterRigSetup.ModelPath);
            if (clip == null || model == null)
            {
                report.AppendLine($"[RIG]   verify '{label}': missing clip or model");
                return false;
            }

            var instance = Object.Instantiate(model);
            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    report.AppendLine($"[RIG]   verify '{label}': no animator");
                    return false;
                }

                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                // As the asset imports, with Unity's generated humanoid avatar in
                // place. This is reported, not judged: on this rig the avatar
                // retargets a clip into a pose up to a metre from the one it was
                // recorded from, which is recorded as the defect it is.
                var asImported = Replay(animator, clip, expected, out var importedBone);

                // And as the rig has to be driven — the same clip on the same rig with
                // the avatar cleared, needing no retargeting because the clips belong
                // to this rig. That is the number the fixture is judged on.
                animator.avatar = null;
                var driven = Replay(animator, clip, expected, out var drivenBone);

                var ok = driven <= ReplayTolerance;
                report.AppendLine($"[RIG]   verify '{label}': {expected.Paths.Length} bones; driven (no avatar) worst bone " +
                                  $"'{drivenBone}' off {driven:0.#####} {(ok ? "OK" : "MISMATCH")} " +
                                  $"(tolerance {ReplayTolerance:0.###}); through the imported avatar worst " +
                                  $"'{importedBone}' off {asImported:0.#####}");
                return ok;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Evaluates a clip on an instance through a real PlayableGraph and returns
        /// the largest per-bone deviation from the recorded skeleton.
        /// </summary>
        private static float Replay(Animator animator, AnimationClip clip, BonePose expected, out string worstBone)
        {
            var graph = PlayableGraphHolder.Create(animator, clip);
            try
            {
                graph.Evaluate(0.5f);
                return WorstBone(expected, Capture(animator), out worstBone);
            }
            finally
            {
                graph.Dispose();
            }
        }

        /// <summary>Every bone's world position, keyed by the path a clip addresses it with.</summary>
        private struct BonePose
        {
            public string[] Paths;
            public Vector3[] Positions;
        }

        private static BonePose Capture(Animator animator)
        {
            var paths = new List<string>();
            var positions = new List<Vector3>();
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true))
            {
                paths.Add(bone == animator.transform ? string.Empty : RelativePath(animator.transform, bone));
                positions.Add(bone.position);
            }
            return new BonePose { Paths = paths.ToArray(), Positions = positions.ToArray() };
        }

        private static float WorstBone(BonePose expected, BonePose actual, out string worstBone)
        {
            worstBone = "n/a";
            var worst = 0f;
            for (var i = 0; i < expected.Paths.Length; i++)
            {
                var index = System.Array.IndexOf(actual.Paths, expected.Paths[i]);
                if (index < 0) return float.PositiveInfinity;

                var delta = Vector3.Distance(expected.Positions[i], actual.Positions[index]);
                if (delta > worst)
                {
                    worst = delta;
                    worstBone = expected.Paths[i].Length == 0 ? "<root>" : expected.Paths[i];
                }
            }
            return worst;
        }

        // ---------- mask ----------

        private static void WriteUpperBodyMask(StringBuilder report)
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
            }

            var bodyParts = new[]
            {
                AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head,
                AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers,
            };
            for (var i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, System.Array.IndexOf(bodyParts, (AvatarMaskBodyPart)i) >= 0);
            }
            EditorUtility.SetDirty(mask);
            report.AppendLine("[RIG]   wrote 'UpperBody': legs and feet excluded, body/head/arms/fingers included " +
                              "(humanoid mask for controller layers; the baked transform clips do not exercise it)");
        }

        // ---------- measurement ----------

        private struct Heights
        {
            public float HipsY;
            public float HeadY;
            public float FeetY;
        }

        private static Heights Measure(Animator animator, StringBuilder report, string label)
        {
            var heights = Measure(animator);
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            report.AppendLine($"[RIG]   {label}: hips {heights.HipsY:0.####}, head {heights.HeadY:0.####}, " +
                              $"feet {heights.FeetY:0.####}, Hips bone '{hips?.name}', Head bone '{head?.name}'");
            return heights;
        }

        private static Heights Measure(Animator animator)
        {
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            return new Heights
            {
                HipsY = hips != null ? hips.position.y : float.NaN,
                HeadY = head != null ? head.position.y : float.NaN,
                FeetY = Feet(animator),
            };
        }

        /// <summary>
        /// Lowest foot or toe bone. Ground contact is judged against this rather than
        /// against the skinned mesh, whose lowest point was measured to sit 24mm below
        /// the toe bone at rest and half a metre below it once the legs fold — it
        /// tracks a hanging garment, not a sole.
        /// </summary>
        private static float Feet(Animator animator)
        {
            var lowest = float.MaxValue;
            foreach (var bone in new[]
                     {
                         HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
                         HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
                     })
            {
                var transform = animator.GetBoneTransform(bone);
                if (transform == null) continue;
                if (transform.position.y < lowest) lowest = transform.position.y;
            }
            return lowest == float.MaxValue ? float.NaN : lowest;
        }

        /// <summary>Lowest vertex of the skinned body mesh in world space — reported, never trusted.</summary>
        private static float MeshSole(Animator animator)
        {
            var body = FindBodyRenderer(animator);
            if (body == null || body.sharedMesh == null) return float.NaN;

            var baked = new Mesh();
            try
            {
                body.BakeMesh(baked, true);
                var vertices = baked.vertices;
                var toWorld = body.transform.localToWorldMatrix;
                var lowest = float.MaxValue;
                for (var i = 0; i < vertices.Length; i++)
                {
                    var y = toWorld.MultiplyPoint3x4(vertices[i]).y;
                    if (y < lowest) lowest = y;
                }
                return lowest == float.MaxValue ? float.NaN : lowest;
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
        }

        private static SkinnedMeshRenderer FindBodyRenderer(Animator animator)
        {
            SkinnedMeshRenderer best = null;
            foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                if (best == null || renderer.sharedMesh.vertexCount > best.sharedMesh.vertexCount) best = renderer;
            }
            return best;
        }

        /// <summary>Minimal PlayableGraph wrapper so editor verification and runtime driving share one path.</summary>
        private struct PlayableGraphHolder
        {
            private PlayableGraph _graph;
            public static PlayableGraphHolder Create(Animator animator, AnimationClip clip)
            {
                var holder = new PlayableGraphHolder();
                holder._graph = PlayableGraph.Create("RigSpikeVerify");
                var playable = AnimationClipPlayable.Create(holder._graph, clip);
                var output = AnimationPlayableOutput.Create(holder._graph, "Animation", animator);
                output.SetSourcePlayable(playable);
                holder._graph.Play();
                return holder;
            }

            public void Evaluate(float deltaTime) => _graph.Evaluate(deltaTime);
            public void Dispose() { if (_graph.IsValid()) _graph.Destroy(); }
        }

        private static void EnsureSpikeFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Characters")) AssetDatabase.CreateFolder("Assets", "Characters");
            if (!AssetDatabase.IsValidFolder(CharacterRigSetup.CharacterFolder))
            {
                AssetDatabase.CreateFolder("Assets/Characters", "Luna");
            }
            if (!AssetDatabase.IsValidFolder(SpikeFolder))
            {
                AssetDatabase.CreateFolder(CharacterRigSetup.CharacterFolder, "Spike");
            }
        }
    }
}
