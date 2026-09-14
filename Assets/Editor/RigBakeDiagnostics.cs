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
    /// The rig spike measures ground contact from the lowest point of the *skinned*
    /// body mesh — the real sole, not the foot bone origin. That measurement turned
    /// out to disagree with itself across runs: bone positions (hips, head) replayed
    /// from a clip byte-identically, while the baked sole was out by half a unit.
    ///
    /// Since a baked mesh is a pure function of bones, bind poses and vertices, a
    /// stable skeleton cannot produce an unstable bake — unless the bake is reading
    /// stale skinning matrices. Off screen that is exactly what happens: renderer
    /// updates are culled without a camera, the platform sibling of the Animator
    /// culling the spike already tripped over.
    ///
    /// This measures bake repeatability and the two settings that are supposed to
    /// control it, so ground contact rests on a number that means something.
    ///
    /// Editor-only and read-only; it authors nothing.
    /// </summary>
    public static class RigBakeDiagnostics
    {
        private const string ScratchFolder = CharacterRigSetup.CharacterFolder + "/Spike/Scratch";

        private static void EnsureScratchFolder()
        {
            if (!AssetDatabase.IsValidFolder(CharacterRigSetup.CharacterFolder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Characters")) AssetDatabase.CreateFolder("Assets", "Characters");
                AssetDatabase.CreateFolder("Assets/Characters", "Luna");
            }
            if (!AssetDatabase.IsValidFolder(RigSpikeBuilder.SpikeFolder))
            {
                AssetDatabase.CreateFolder(CharacterRigSetup.CharacterFolder, "Spike");
            }
            if (!AssetDatabase.IsValidFolder(ScratchFolder))
            {
                AssetDatabase.CreateFolder(RigSpikeBuilder.SpikeFolder, "Scratch");
            }
        }

        [MenuItem("TruthCardGame/Diagnostics/Rig Spike/Diagnose Skinned Mesh Bake")]
        public static void Diagnose()
        {
            var report = new StringBuilder();
            report.AppendLine("[RIG] === skinned mesh bake diagnosis ===");

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterRigSetup.ModelPath);
            if (model == null)
            {
                Debug.LogError($"[RIG] {CharacterRigSetup.ModelPath} is not imported; run TruthCardGame → Diagnostics → Rig Spike → Setup And Report.");
                return;
            }

            // This probe authors no scene. Keep any authored scene and its
            // unsaved changes intact while the temporary instance is measured.
            var previousScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var instance = Object.Instantiate(model);
                instance.name = "RigBakeProbe";
                try
                {
                    var animator = instance.GetComponentInChildren<Animator>();
                    var body = FindBodyRenderer(animator);
                    if (body == null)
                    {
                        Debug.LogError("[RIG] no skinned mesh renderer on the model");
                        return;
                    }

                report.AppendLine($"[RIG] body renderer '{body.name}': vertices {body.sharedMesh.vertexCount}, " +
                                  $"updateWhenOffscreen={body.updateWhenOffscreen}, " +
                                  $"forceMatrixRecalculationPerRender={body.forceMatrixRecalculationPerRender}, " +
                                  $"quality={(body.quality == SkinQuality.Auto ? "Auto" : body.quality.ToString())}, " +
                                  $"isVisible={body.isVisible}");
                report.AppendLine($"[RIG] bone-derived sole (LeftToes/RightToes/foot min Y): {BoneSoleY(animator):0.####}");

                // Repeatability first: the same state measured twice must agree, or
                // every later comparison is comparing noise.
                report.AppendLine($"[RIG] bind pose, bake #1: {BakeSoleY(body):0.####}");
                report.AppendLine($"[RIG] bind pose, bake #2: {BakeSoleY(body):0.####}");

                body.updateWhenOffscreen = true;
                report.AppendLine($"[RIG] bind pose, updateWhenOffscreen=true: {BakeSoleY(body):0.####}");
                body.updateWhenOffscreen = false;

                body.forceMatrixRecalculationPerRender = true;
                report.AppendLine($"[RIG] bind pose, forceMatrixRecalculationPerRender=true: {BakeSoleY(body):0.####}");

                // Now the pose the spike actually cares about: does a posed skeleton
                // bake to a moved sole, or to the same stale number?
                var handler = new HumanPoseHandler(animator.avatar, animator.transform);
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                SetMuscle(ref pose, "Left Upper Leg Front-Back", 0.9f);
                SetMuscle(ref pose, "Right Upper Leg Front-Back", 0.9f);
                SetMuscle(ref pose, "Left Lower Leg Stretch", -0.9f);
                SetMuscle(ref pose, "Right Lower Leg Stretch", -0.9f);
                handler.SetHumanPose(ref pose);

                report.AppendLine($"[RIG] flexed, bone-derived sole: {BoneSoleY(animator):0.####} (hips {HipsY(animator):0.####})");
                report.AppendLine($"[RIG] flexed, bake with forceMatrixRecalculationPerRender: {BakeSoleY(body):0.####}");
                body.forceMatrixRecalculationPerRender = false;
                report.AppendLine($"[RIG] flexed, bake #1 without it: {BakeSoleY(body):0.####}");
                report.AppendLine($"[RIG] flexed, bake #2 without it: {BakeSoleY(body):0.####}");
                body.updateWhenOffscreen = true;
                    report.AppendLine($"[RIG] flexed, bake with updateWhenOffscreen: {BakeSoleY(body):0.####}");
                    report.AppendLine($"[RIG] flexed, baked mesh bounds min.y with updateWhenOffscreen: {BakeBounds(body):0.####}");
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }

                CompareRoutes(model, report);
                MeasurePoseRoundTrip(model, report);
                CompareAuthoringOrder(model, report);
                CompareRootCurves(model, report);
                CompareMuscleSubsets(model, report);
                Debug.Log(report.ToString());
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(ScratchFolder)) AssetDatabase.DeleteAsset(ScratchFolder);
                EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            }
        }

        /// <summary>
        /// The load-bearing question for the whole spike. Every fixture is composed
        /// by reading the rig's pose into humanoid muscle space, editing it, and
        /// writing it back — so if reading and writing are not a round trip on this
        /// rig, the fixtures are built on sand and so is any clip retargeted onto it
        /// from another character.
        ///
        /// Measured on the way here: hips identical before and after, feet half a
        /// metre away. This says whether that is the rig or the call site.
        /// </summary>
        private static void MeasurePoseRoundTrip(GameObject model, StringBuilder report)
        {
            var instance = Object.Instantiate(model);
            instance.name = "RigPoseProbe";
            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null || animator.avatar == null)
                {
                    report.AppendLine("[RIG] round trip: no avatar");
                    return;
                }

                var handler = new HumanPoseHandler(animator.avatar, animator.transform);

                var captured = new HumanPose();
                handler.GetHumanPose(ref captured);
                var before = Snapshot(animator);
                report.AppendLine($"[RIG] round trip: captured from the imported pose — " +
                                  $"hips {before.HipsY:0.####}, feet {before.FeetY:0.####}, head {before.HeadY:0.####}, " +
                                  $"bodyPosition {Format(captured.bodyPosition)}");
                report.AppendLine($"[RIG] round trip: most displaced muscles — {BiggestMuscles(captured)}");

                handler.SetHumanPose(ref captured);
                var after = Snapshot(animator);
                var reread = new HumanPose();
                handler.GetHumanPose(ref reread);

                report.AppendLine($"[RIG] round trip: set-then-get — max muscle delta {MaxMuscleDelta(captured, reread):0.####} " +
                                  $"at '{MuscleNameAt(MaxMuscleIndex(captured, reread))}', " +
                                  $"bodyPosition delta {Format(reread.bodyPosition - captured.bodyPosition)}");
                report.AppendLine($"[RIG] round trip: skeleton moved hips {System.Math.Abs(after.HipsY - before.HipsY):0.####}, " +
                                  $"feet {System.Math.Abs(after.FeetY - before.FeetY):0.####}, " +
                                  $"head {System.Math.Abs(after.HeadY - before.HeadY):0.####} " +
                                  $"{(System.Math.Abs(after.FeetY - before.FeetY) > 0.02f ? "← NOT A ROUND TRIP" : "(stable)")}");

                // Control: a clean authored pose rather than whatever the FBX left
                // behind. If this one round-trips, the rig is fine and the imported
                // pose is the odd one; if it does not, muscle space is unusable here.
                var zero = new HumanPose
                {
                    muscles = new float[HumanTrait.MuscleCount],
                    bodyPosition = captured.bodyPosition,
                    bodyRotation = captured.bodyRotation,
                };
                handler.SetHumanPose(ref zero);
                var zeroBack = new HumanPose();
                handler.GetHumanPose(ref zeroBack);
                report.AppendLine($"[RIG] round trip control (all muscles zero): max muscle delta {MaxMuscleDelta(zero, zeroBack):0.####}, " +
                                  $"most displaced — {BiggestMuscles(zeroBack)}");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// The clip stores the authored muscles faithfully and replays correctly
        /// through SampleAnimation, yet the Animator's own evaluation of the same
        /// clip lands the feet on the hips and reports muscles far outside their
        /// range — both signatures of the Animator reading cached humanoid muscle
        /// data that was computed before the curves existed, while SampleAnimation
        /// reads the curves directly.
        ///
        /// That cache is invalidated by the asset pipeline, not by SetEditorCurve,
        /// which makes the authoring *order* the suspect: the fixtures create an
        /// empty clip asset and then fill it. This writes the same pose three ways
        /// and plays each back through both routes.
        /// </summary>
        private static void CompareAuthoringOrder(GameObject model, StringBuilder report)
        {
            var captured = new HumanPose();
            var probe = Object.Instantiate(model);
            probe.name = "RigAuthorOrderProbe";
            try
            {
                var animator = probe.GetComponentInChildren<Animator>();
                new HumanPoseHandler(animator.avatar, animator.transform).GetHumanPose(ref captured);
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }

            EnsureScratchFolder();

            var variants = new[]
            {
                ("created-then-filled (what the fixtures do)", false, false),
                ("created-then-filled, then reimported", false, true),
                ("filled-then-created", true, false),
            };

            for (var i = 0; i < variants.Length; i++)
            {
                var (label, curvesFirst, reimport) = variants[i];
                var clip = BuildClip($"Diag_Order{i}", captured, curvesFirst, reimport);
                if (clip == null)
                {
                    report.AppendLine($"[RIG] order '{label}': could not author");
                    continue;
                }

                report.AppendLine($"[RIG] order '{label}': isHumanMotion={clip.isHumanMotion}; " +
                                  $"graph -> {Route(model, clip, EvaluateGraph)}; " +
                                  $"SampleAnimation -> {Route(model, clip, (animator, body, source) => source.SampleAnimation(animator.gameObject, 0.5f))}");
            }
        }

        private enum RootMode
        {
            /// <summary>RootT/RootQ written from the captured pose, as the fixtures do.</summary>
            Full,
            /// <summary>Muscle curves only: no root translation or rotation at all.</summary>
            MusclesOnly,
            /// <summary>RootT written, RootQ forced to an identity quaternion.</summary>
            IdentityRotation,
            /// <summary>No root curves at all.</summary>
            None,
        }

        /// <summary>
        /// The last variable: which muscles the clip is made of. A clip carrying
        /// every muscle of the imported pose also carries that pose's disagreements
        /// with the avatar — measured here as 69 non-zero muscles on a rig whose
        /// bind geometry is plainly standing, several of them outside the [-1,1]
        /// range a muscle covers. A clip carrying only the joints it means to move
        /// carries none of that.
        ///
        /// The single-muscle probe earlier replayed identically through both routes,
        /// which is the hint this follows: the graph was never broken, the *clip
        /// contents* were.
        /// </summary>
        private static void CompareMuscleSubsets(GameObject model, StringBuilder report)
        {
            var captured = new HumanPose();
            var probe = Object.Instantiate(model);
            probe.name = "RigSubsetProbe";
            try
            {
                var animator = probe.GetComponentInChildren<Animator>();
                new HumanPoseHandler(animator.avatar, animator.transform).GetHumanPose(ref captured);
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }

            var outside = new System.Collections.Generic.List<string>();
            for (var i = 0; i < captured.muscles.Length; i++)
            {
                if (Mathf.Abs(captured.muscles[i]) > 1f)
                {
                    outside.Add($"'{HumanTrait.MuscleName[i]}' {captured.muscles[i]:0.###}");
                }
            }
            report.AppendLine($"[RIG] subsets: the imported pose has {outside.Count} muscles outside [-1,1]" +
                              $"{(outside.Count == 0 ? string.Empty : ": " + string.Join(", ", outside))}");

            var variants = new[]
            {
                ("every muscle as captured", AllMuscles(captured)),
                ("thighs and knees only, from rest", new System.Collections.Generic.Dictionary<string, float>
                {
                    { "Left Upper Leg Front-Back", 0.9f },
                    { "Right Upper Leg Front-Back", 0.9f },
                    { "Left Lower Leg Stretch", -0.9f },
                    { "Right Lower Leg Stretch", -0.9f },
                }),
                ("every muscle, clamped to [-1,1]", AllMuscles(captured, clamp: true)),
            };

            EnsureScratchFolder();
            for (var i = 0; i < variants.Length; i++)
            {
                var (label, muscles) = variants[i];
                var clip = BuildMuscleClip($"Diag_Subset{i}", muscles);
                if (clip == null)
                {
                    report.AppendLine($"[RIG] subset '{label}': could not author");
                    continue;
                }

                report.AppendLine($"[RIG] subset '{label}' ({muscles.Count} muscles, humanMotion={clip.isHumanMotion}): " +
                                  $"graph -> {Route(model, clip, EvaluateGraph)}; " +
                                  $"SampleAnimation -> {Route(model, clip, (animator, body, source) => source.SampleAnimation(animator.gameObject, 0.5f))}");
            }
        }

        private static System.Collections.Generic.Dictionary<string, float> AllMuscles(HumanPose pose, bool clamp = false)
        {
            var muscles = new System.Collections.Generic.Dictionary<string, float>();
            for (var i = 0; i < pose.muscles.Length; i++)
            {
                var value = pose.muscles[i];
                if (clamp) value = Mathf.Clamp(value, -1f, 1f);
                muscles[HumanTrait.MuscleName[i]] = value;
            }
            return muscles;
        }

        private static AnimationClip BuildMuscleClip(string name, System.Collections.Generic.Dictionary<string, float> muscles)
        {
            var path = ScratchFolder + $"/{name}.anim";
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) AssetDatabase.DeleteAsset(path);

            var clip = new AnimationClip { frameRate = 30f };
            AssetDatabase.CreateAsset(clip, path);
            foreach (var muscle in muscles)
            {
                var curve = new AnimationCurve(new Keyframe(0f, muscle.Value), new Keyframe(1f, muscle.Value));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), muscle.Key), curve);
            }

            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(path) ?? clip;
        }

        /// <summary>
        /// The one thing left that SampleAnimation and the Animator can treat
        /// differently is the <em>root</em> — the two translation and four rotation
        /// curves written from HumanPose.bodyPosition/bodyRotation, which describe
        /// the character's placement rather than its pose. A zero or non-unit
        /// quaternion there is not a pose error, it is an undefined transform, and
        /// every downstream bone read back through muscle space inherits the
        /// nonsense. This picks the root apart from the muscles.
        /// </summary>
        private static void CompareRootCurves(GameObject model, StringBuilder report)
        {
            var captured = new HumanPose();
            var probe = Object.Instantiate(model);
            probe.name = "RigRootProbe";
            try
            {
                var animator = probe.GetComponentInChildren<Animator>();
                new HumanPoseHandler(animator.avatar, animator.transform).GetHumanPose(ref captured);
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }

            report.AppendLine($"[RIG] captured root: bodyPosition {Format(captured.bodyPosition)}, " +
                              $"bodyRotation {Format(captured.bodyRotation)} " +
                              $"(length {QuaternionLength(captured.bodyRotation):0.####}, a rotation must be 1)");

            EnsureScratchFolder();
            var variants = new[]
            {
                ("root written as captured", RootMode.Full),
                ("muscles only, no root curves", RootMode.MusclesOnly),
                ("root rotation forced to identity", RootMode.IdentityRotation),
            };

            for (var i = 0; i < variants.Length; i++)
            {
                var (label, mode) = variants[i];
                var clip = BuildClip($"Diag_Root{i}", captured, curvesFirst: false, reimport: false, mode: mode);
                if (clip == null)
                {
                    report.AppendLine($"[RIG] root '{label}': could not author");
                    continue;
                }

                report.AppendLine($"[RIG] root '{label}': graph -> {Route(model, clip, EvaluateGraph)}; " +
                                  $"SampleAnimation -> {Route(model, clip, (animator, body, source) => source.SampleAnimation(animator.gameObject, 0.5f))}");
            }
        }

        /// <summary>
        /// Writes the same pose, varying only when the asset is created relative to
        /// when the curves are set, and whether the pipeline is asked to reimport
        /// afterwards.
        /// </summary>
        private static AnimationClip BuildClip(string name, HumanPose pose, bool curvesFirst, bool reimport,
            RootMode mode = RootMode.Full)
        {
            var path = ScratchFolder + $"/{name}.anim";
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) AssetDatabase.DeleteAsset(path);

            var clip = new AnimationClip { frameRate = 30f };
            if (!curvesFirst) AssetDatabase.CreateAsset(clip, path);

            var bindings = new System.Collections.Generic.List<EditorCurveBinding>();
            var values = new System.Collections.Generic.List<float>();
            if (mode == RootMode.Full || mode == RootMode.IdentityRotation)
            {
                AddRootCurves(pose, bindings, values, mode == RootMode.IdentityRotation);
            }
            for (var i = 0; i < pose.muscles.Length; i++)
            {
                var muscleName = HumanTrait.MuscleName[i];
                if (muscleName.StartsWith("RootT") || muscleName.StartsWith("RootQ")) continue;
                bindings.Add(EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), muscleName));
                values.Add(pose.muscles[i]);
            }

            for (var i = 0; i < bindings.Count; i++)
            {
                var curve = new AnimationCurve(new Keyframe(0f, values[i]), new Keyframe(1f, values[i]));
                AnimationUtility.SetEditorCurve(clip, bindings[i], curve);
            }

            if (curvesFirst) AssetDatabase.CreateAsset(clip, path);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            if (reimport) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(path) ?? clip;
        }

        private static void AddRootCurves(HumanPose pose, System.Collections.Generic.List<EditorCurveBinding> bindings,
            System.Collections.Generic.List<float> values, bool identityRotation)
        {
            var rotation = identityRotation ? Quaternion.identity : pose.bodyRotation;

            void Add(string property, float value)
            {
                bindings.Add(EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), property));
                values.Add(value);
            }

            Add("RootT.x", pose.bodyPosition.x);
            Add("RootT.y", pose.bodyPosition.y);
            Add("RootT.z", pose.bodyPosition.z);
            Add("RootQ.x", rotation.x);
            Add("RootQ.y", rotation.y);
            Add("RootQ.z", rotation.z);
            Add("RootQ.w", rotation.w);
        }

        private struct PoseSnapshot
        {
            public float HipsY;
            public float FeetY;
            public float HeadY;
        }

        private static PoseSnapshot Snapshot(Animator animator)
        {
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            return new PoseSnapshot
            {
                HipsY = hips != null ? hips.position.y : float.NaN,
                HeadY = head != null ? head.position.y : float.NaN,
                FeetY = BoneSoleY(animator),
            };
        }

        private static string Format(Vector3 value) => $"({value.x:0.###}, {value.y:0.###}, {value.z:0.###})";

        private static string Format(Quaternion value) =>
            $"({value.x:0.###}, {value.y:0.###}, {value.z:0.###}, {value.w:0.###})";

        private static float QuaternionLength(Quaternion value) =>
            Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);

        private static int MaxMuscleIndex(HumanPose a, HumanPose b)
        {
            var best = -1;
            var worst = 0f;
            for (var i = 0; i < a.muscles.Length && i < b.muscles.Length; i++)
            {
                var delta = Mathf.Abs(b.muscles[i] - a.muscles[i]);
                if (delta > worst)
                {
                    worst = delta;
                    best = i;
                }
            }
            return best;
        }

        private static float MaxMuscleDelta(HumanPose a, HumanPose b)
        {
            var best = 0f;
            for (var i = 0; i < a.muscles.Length && i < b.muscles.Length; i++)
            {
                var delta = Mathf.Abs(b.muscles[i] - a.muscles[i]);
                if (delta > best) best = delta;
            }
            return best;
        }

        private static string MuscleNameAt(int index)
        {
            if (index < 0 || index >= HumanTrait.MuscleCount) return "n/a";
            return HumanTrait.MuscleName[index];
        }

        /// <summary>The muscles the imported pose actually uses, biggest first.</summary>
        private static string BiggestMuscles(HumanPose pose)
        {
            var ranked = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, float>>();
            for (var i = 0; i < pose.muscles.Length; i++)
            {
                if (Mathf.Abs(pose.muscles[i]) > 0.001f)
                {
                    ranked.Add(new System.Collections.Generic.KeyValuePair<string, float>(
                        HumanTrait.MuscleName[i], pose.muscles[i]));
                }
            }
            ranked.Sort((left, right) => Mathf.Abs(right.Value).CompareTo(Mathf.Abs(left.Value)));
            if (ranked.Count == 0) return "none (all muscles at rest)";

            var text = new StringBuilder($"{ranked.Count} non-zero; largest ");
            for (var i = 0; i < ranked.Count && i < 4; i++)
            {
                if (i > 0) text.Append(", ");
                text.Append($"'{ranked[i].Key}' {ranked[i].Value:0.###}");
            }
            return text.ToString();
        }

        /// <summary>
        /// Applies one clip through both routes on a single instance and bakes after
        /// each, so any difference between them is attributable to the route and
        /// nothing else. The fixture verification compared a clip replayed through a
        /// graph against a pose composed through a HumanPoseHandler and found the
        /// bone positions identical while the baked sole disagreed by half a unit;
        /// this is the experiment that says which of the two states is the posed one.
        /// </summary>
        private static void CompareRoutes(GameObject model, StringBuilder report)
        {
            // The authored pose is recoverable here without any bookkeeping: a fresh
            // instance's pose *is* the exported pose, and the clip below is built from
            // it. This authors its own clip on purpose — the mechanism under test is
            // "a clip carrying the whole imported pose", and borrowing whatever the
            // spike fixtures currently are would quietly stop testing that the moment
            // they changed shape. (It did.)
            var authored = new HumanPose();
            var authoredSnapshot = default(PoseSnapshot);
            var measured = Object.Instantiate(model);
            measured.name = "RigAuthorProbe";
            try
            {
                var animator = measured.GetComponentInChildren<Animator>();
                var handler = new HumanPoseHandler(animator.avatar, animator.transform);
                handler.GetHumanPose(ref authored);
                authoredSnapshot = Snapshot(animator);
                report.AppendLine($"[RIG] routes: authored pose — hips {authoredSnapshot.HipsY:0.####}, " +
                                  $"feet {authoredSnapshot.FeetY:0.####}, baked sole {BakeSoleY(FindBodyRenderer(animator)):0.####}");
            }
            finally
            {
                Object.DestroyImmediate(measured);
            }

            EnsureScratchFolder();
            var clip = BuildClip("Diag_RoutesFull", authored, curvesFirst: false, reimport: false);
            if (clip == null)
            {
                report.AppendLine("[RIG] routes: could not author the whole-imported-pose clip");
                return;
            }
            report.AppendLine($"[RIG] routes: testing '{clip.name}' — 7 root curves + 95 muscle curves of the exported pose");

            DescribeClipStorage(clip, authored, report);

            report.AppendLine($"[RIG] routes: via SampleAnimation -> {Route(model, clip, (animator, body, source) => source.SampleAnimation(animator.gameObject, 0.5f))}");
            report.AppendLine($"[RIG] routes: via PlayableGraph -> {Route(model, clip, EvaluateGraph)}");
            DescribePostGraphMuscles(model, clip, authored, authoredSnapshot, report);
        }

        /// <summary>
        /// Does the clip store what was authored? This splits the two candidate
        /// causes apart: a mismatch here means the *write* lost the number, a match
        /// means the clip is faithful and the defect is in evaluation.
        /// </summary>
        private static void DescribeClipStorage(AnimationClip clip, HumanPose authored, StringBuilder report)
        {
            var mismatches = 0;
            var worst = 0f;
            var worstMuscle = "n/a";
            var muscles = 0;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != string.Empty || binding.type != typeof(Animator)) continue;
                if (binding.propertyName.StartsWith("RootT") || binding.propertyName.StartsWith("RootQ")) continue;

                var index = System.Array.IndexOf(HumanTrait.MuscleName, binding.propertyName);
                if (index < 0) continue;

                muscles++;
                var stored = AnimationUtility.GetEditorCurve(clip, binding)?.Evaluate(0f) ?? 0f;
                var delta = Mathf.Abs(stored - authored.muscles[index]);
                if (delta > 0.0005f)
                {
                    mismatches++;
                    if (delta > worst)
                    {
                        worst = delta;
                        worstMuscle = $"{binding.propertyName} authored {authored.muscles[index]:0.####} stored {stored:0.####}";
                    }
                }
            }

            report.AppendLine($"[RIG] clip storage: {muscles} muscle curves, {mismatches} differ from the authored pose" +
                              $"{(mismatches == 0 ? " — the clip is faithful, so any pose error is in evaluation" : $"; worst {worst:0.####} ({worstMuscle})")}");

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (!binding.propertyName.StartsWith("RootT")) continue;
                report.AppendLine($"[RIG]   clip writes '{binding.propertyName}' = " +
                                  $"{AnimationUtility.GetEditorCurve(clip, binding)?.Evaluate(0f) ?? 0f:0.####}");
            }
        }

        private static void EvaluateGraph(Animator animator, SkinnedMeshRenderer body, AnimationClip clip)
        {
            var graph = PlayableGraph.Create("RigRouteProbe");
            try
            {
                var playable = AnimationClipPlayable.Create(graph, clip);
                var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
                output.SetSourcePlayable(playable);
                graph.Play();
                graph.Evaluate(0.5f);
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
            }
        }

        private static string Route(GameObject model, AnimationClip clip,
            System.Action<Animator, SkinnedMeshRenderer, AnimationClip> apply)
        {
            var instance = Object.Instantiate(model);
            instance.name = "RigRouteProbe";
            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                apply(animator, FindBodyRenderer(animator), clip);
                return $"hips {HipsY(animator):0.####}, feet {BoneSoleY(animator):0.####}, " +
                       $"baked sole {BakeSoleY(FindBodyRenderer(animator)):0.####}";
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Reads the skeleton back through humanoid muscle space immediately after a
        /// graph evaluation and names the muscles that no longer hold the authored
        /// value, so the discrepancy stops being a number and becomes a joint.
        /// </summary>
        private static void DescribePostGraphMuscles(GameObject model, AnimationClip clip, HumanPose authored,
            PoseSnapshot authoredSnapshot, StringBuilder report)
        {
            var instance = Object.Instantiate(model);
            instance.name = "RigPostGraphProbe";
            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                EvaluateGraph(animator, FindBodyRenderer(animator), clip);

                var played = Snapshot(animator);
                var handler = new HumanPoseHandler(animator.avatar, animator.transform);
                var reread = new HumanPose();
                handler.GetHumanPose(ref reread);

                report.AppendLine($"[RIG] routes: after the graph, skeleton delta hips " +
                                  $"{System.Math.Abs(played.HipsY - authoredSnapshot.HipsY):0.####}, " +
                                  $"feet {System.Math.Abs(played.FeetY - authoredSnapshot.FeetY):0.####}, " +
                                  $"head {System.Math.Abs(played.HeadY - authoredSnapshot.HeadY):0.####}; " +
                                  $"bodyPosition {Format(authored.bodyPosition)} -> {Format(reread.bodyPosition)}");

                var ranked = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, float>>();
                for (var i = 0; i < authored.muscles.Length && i < reread.muscles.Length; i++)
                {
                    ranked.Add(new System.Collections.Generic.KeyValuePair<string, float>(
                        $"{HumanTrait.MuscleName[i]} {authored.muscles[i]:0.###} -> {reread.muscles[i]:0.###}",
                        Mathf.Abs(reread.muscles[i] - authored.muscles[i])));
                }
                ranked.Sort((left, right) => right.Value.CompareTo(left.Value));

                var text = new StringBuilder("[RIG] routes: muscles the graph changed — ");
                for (var i = 0; i < ranked.Count && i < 6; i++)
                {
                    if (i > 0) text.Append(", ");
                    text.Append($"'{ranked[i].Key}' (delta {ranked[i].Value:0.###})");
                }
                report.AppendLine(text.ToString());
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void SetMuscle(ref HumanPose pose, string muscleName, float value)
        {
            var index = System.Array.IndexOf(HumanTrait.MuscleName, muscleName);
            if (index < 0) throw new System.InvalidOperationException($"[RIG] '{muscleName}' is not a humanoid muscle");
            pose.muscles[index] = value;
        }

        private static float HipsY(Animator animator)
        {
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            return hips != null ? hips.position.y : float.NaN;
        }

        /// <summary>Lowest of the foot and toe bones — cheap, and never culled.</summary>
        private static float BoneSoleY(Animator animator)
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

        /// <summary>Lowest vertex of the baked mesh in world space.</summary>
        private static float BakeSoleY(SkinnedMeshRenderer body) => BakeBounds(body);

        private static float BakeBounds(SkinnedMeshRenderer body)
        {
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
    }
}
