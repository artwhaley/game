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
    /// Answers a question fixtures cannot answer about themselves: when a clip
    /// authored from script through <see cref="AnimationUtility.SetEditorCurve"/>
    /// appears to play back as nothing, is the *clip* empty or is the *playback
    /// route* wrong?
    ///
    /// The first version of this diagnosed the clip: one humanoid muscle clip and
    /// one generic transform clip, each sampled through <see cref="AnimationClip.SampleAnimation"/>
    /// (the editor route) and through a bare <see cref="AnimationPlayableOutput"/>
    /// graph (the runtime route). Result: both clips are real — the humanoid one
    /// reports <c>isHumanMotion=True</c> and moves the rig when sampled — but the
    /// bare graph wrote nothing for either. So the clip was never the problem.
    ///
    /// This version therefore holds the clip fixed (the humanoid muscle clip, the
    /// shape the spike fixtures need) and varies the *route*: culling mode, an
    /// explicit <see cref="Animator.Update"/>, <see cref="Animator.Rebind"/>, and
    /// Unity's own <see cref="AnimationPlayableUtilities.PlayClip"/>. Whichever
    /// route moves the rig is the one the fixtures must use.
    ///
    /// Editor-only, read-only apart from the scratch clip it deletes.
    /// </summary>
    public static class RigClipDiagnostics
    {
        private const string ScratchFolder = CharacterRigSetup.CharacterFolder + "/Spike/Scratch";
        private const string HumanoidClipPath = ScratchFolder + "/Diag_HumanoidMuscle.anim";

        [MenuItem("TruthCardGame/Diagnostics/Rig Spike/Diagnose Clip Sampling")]
        public static void Diagnose()
        {
            var report = new StringBuilder();
            report.AppendLine("[RIG] === clip sampling diagnosis ===");

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterRigSetup.ModelPath);
            if (model == null)
            {
                Debug.LogError($"[RIG] {CharacterRigSetup.ModelPath} is not imported; run TruthCardGame → Diagnostics → Rig Spike → Setup And Report.");
                return;
            }

            EnsureScratchFolder();
            // This probe authors no scene. Keep any authored scene and its
            // unsaved changes intact while the temporary instance is measured.
            var previousScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                WriteHumanoidClip();
                DescribeAnimator(model, report);

                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(HumanoidClipPath);
                if (clip == null)
                {
                    report.AppendLine("[RIG] humanoid scratch clip missing");
                    return;
                }

                // The control: the editor route, which the previous run proved works.
                Try(model, "SampleAnimation (known good)", report,
                    animator => clip.SampleAnimation(animator.gameObject, 0.5f));

                Try(model, "graph.Evaluate(0.5) — bare, as fixtures did it", report,
                    animator => WithGraph(animator, clip, graph => graph.Evaluate(0.5f)));

                Try(model, "AlwaysAnimate + graph.Evaluate(0.5)", report,
                    animator =>
                    {
                        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        WithGraph(animator, clip, graph => graph.Evaluate(0.5f));
                    });

                Try(model, "graph.Evaluate(0.5) + animator.Update(0)", report,
                    animator =>
                    {
                        WithGraph(animator, clip, graph => graph.Evaluate(0.5f));
                        animator.Update(0f);
                    });

                Try(model, "AlwaysAnimate + graph.Evaluate(0.5) + animator.Update(0)", report,
                    animator =>
                    {
                        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                        WithGraph(animator, clip, graph => graph.Evaluate(0.5f));
                        animator.Update(0f);
                    });

                Try(model, "animator.Rebind() + graph.Evaluate(0.5) + animator.Update(0)", report,
                    animator =>
                    {
                        animator.Rebind();
                        WithGraph(animator, clip, graph => graph.Evaluate(0.5f));
                        animator.Update(0f);
                    });

                Try(model, "PlayClip (Unity's own) + graph.Evaluate(0.5)", report,
                    animator => WithGraph(animator, clip, graph => graph.Evaluate(0.5f), useUtility: true));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            }

            // The scratch clip exists only to be sampled; leaving it behind would
            // put a nonsense one-curve clip in the character folder as if it were
            // content. Deleting the folder also drops its .meta.
            if (AssetDatabase.IsValidFolder(ScratchFolder)) AssetDatabase.DeleteAsset(ScratchFolder);

            Debug.Log(report.ToString());
        }

        // ---------- the routes ----------

        /// <summary>
        /// Runs <paramref name="apply"/> on a fresh instance and reports the rig's
        /// own bone before and after, so a route that "worked" but changed nothing
        /// cannot be mistaken for a working route.
        /// </summary>
        private static void Try(GameObject model, string label, StringBuilder report, System.Action<Animator> apply)
        {
            var instance = Object.Instantiate(model);
            instance.name = "RigClipProbe";
            var graph = default(PlayableGraph);
            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    report.AppendLine($"[RIG] route '{label}': no animator");
                    return;
                }

                var before = Probe(animator);
                apply(animator);
                var after = Probe(animator);
                var moved = Mathf.Abs(after - before);
                report.AppendLine($"[RIG] route '{label}': {before:0.####} -> {after:0.####} " +
                                  $"(moved {moved:0.####}) {(moved > 0.001f ? "← APPLIES" : "wrote nothing")}");
            }
            catch (System.Exception exception)
            {
                report.AppendLine($"[RIG] route '{label}': THREW {exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                Object.DestroyImmediate(instance);
            }
        }

        private static void WithGraph(Animator animator, AnimationClip clip, System.Action<PlayableGraph> evaluate,
            bool useUtility = false)
        {
            if (useUtility)
            {
                AnimationPlayableUtilities.PlayClip(animator, clip, out var graph);
                try
                {
                    evaluate(graph);
                }
                finally
                {
                    if (graph.IsValid()) graph.Destroy();
                }
                return;
            }

            var own = PlayableGraph.Create("RigClipDiagnostics");
            try
            {
                var playable = AnimationClipPlayable.Create(own, clip);
                var output = AnimationPlayableOutput.Create(own, "Animation", animator);
                output.SetSourcePlayable(playable);
                own.Play();
                evaluate(own);
            }
            finally
            {
                if (own.IsValid()) own.Destroy();
            }
        }

        /// <summary>
        /// Reports the Animator state that could suppress application, so a route
        /// failing for a reason other than its own wiring is visible in the log.
        /// </summary>
        private static void DescribeAnimator(GameObject model, StringBuilder report)
        {
            var instance = Object.Instantiate(model);
            instance.name = "RigStateProbe";
            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    report.AppendLine("[RIG] animator state: none on the model");
                    return;
                }

                var renderers = instance.GetComponentsInChildren<Renderer>();
                report.AppendLine($"[RIG] animator state: isInitialized={animator.isInitialized}, enabled={animator.enabled}, " +
                                  $"cullingMode={animator.cullingMode}, updateMode={animator.updateMode}, " +
                                  $"applyRootMotion={animator.applyRootMotion}, avatar={(animator.avatar != null ? animator.avatar.name : "none")}, " +
                                  $"renderers={renderers.Length}, firstVisible={(renderers.Length > 0 ? renderers[0].isVisible.ToString() : "n/a")}, " +
                                  $"activeInHierarchy={animator.gameObject.activeInHierarchy}");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        // ---------- clip authoring ----------

        private static void WriteHumanoidClip()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(HumanoidClipPath);
            if (clip == null)
            {
                clip = new AnimationClip { frameRate = 30f };
                AssetDatabase.CreateAsset(clip, HumanoidClipPath);
            }

            var muscle = "Right Arm Down-Up";
            if (System.Array.IndexOf(HumanTrait.MuscleName, muscle) < 0)
            {
                throw new System.InvalidOperationException($"[RIG] '{muscle}' is not a humanoid muscle");
            }

            SetConstant(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Animator), muscle), 0.9f);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
        }

        private static void SetConstant(AnimationClip clip, EditorCurveBinding binding, float value)
        {
            var curve = new AnimationCurve(new Keyframe(0f, value), new Keyframe(1f, value));
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        /// <summary>Height of the rig's own forearm bone: the proxy for "did the pose change".</summary>
        private static float Probe(Animator animator)
        {
            if (animator == null) return float.NaN;
            var bone = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            return bone != null ? bone.position.y : float.NaN;
        }

        private static void EnsureScratchFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Characters")) AssetDatabase.CreateFolder("Assets", "Characters");
            if (!AssetDatabase.IsValidFolder(CharacterRigSetup.CharacterFolder))
            {
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
    }
}
