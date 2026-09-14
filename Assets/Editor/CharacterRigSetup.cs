using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TruthCardGame.EditorTools
{
    /// <summary>
    /// Ticket 01 rig inputs. Pins the import settings for the character model and
    /// reports, by measurement, what actually imported: skeleton size, humanoid
    /// mapping, skinned meshes, morph targets and animation clips. The spike's
    /// question ("is this rig usable, and what is missing?") is answered from
    /// this output rather than by opening the editor and eyeballing it.
    ///
    /// Editor-only. Nothing here ships, and the report is read-only apart from
    /// the import settings it deliberately pins. These menus are intentionally
    /// under Diagnostics/Rig Spike: they answer the original Luna import
    /// question and are not the production character-preparation workflow.
    /// </summary>
    public static class CharacterRigSetup
    {
        public const string CharacterFolder = "Assets/Characters/Luna";
        public const string ModelPath = CharacterFolder + "/luna.fbx";

        [MenuItem("TruthCardGame/Diagnostics/Rig Spike/Configure Luna Import")]
        public static void ConfigureImport()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[RIG] No model importer at {ModelPath}. Copy the FBX under Assets/ first.");
                return;
            }

            // Humanoid: the portable model talks about anchors/postures, and
            // humanoid muscle space is the only rig-agnostic way to phrase
            // "raise the right arm" across characters from different toolchains.
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

            // Bones must stay addressable: hand/head/foot lookups, gaze targets and
            // masked layers all reach specific transforms at runtime.
            importer.optimizeGameObjects = false;

            importer.importAnimation = true;
            importer.importBlendShapes = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            // Materials live inside the model asset: no extra generated .mat files
            // to track for a spike, and textures beside the model are wired up.
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            // The report measures the rig; keep meshes CPU-readable so tests can
            // bake the posed mesh instead of inferring it from transforms.
            importer.isReadable = true;
            importer.globalScale = 1f;

            importer.SaveAndReimport();
            Debug.Log($"[RIG] Import settings pinned for {ModelPath} (Humanoid, bones kept, readable).");
        }

        [MenuItem("TruthCardGame/Diagnostics/Rig Spike/Report Luna Rig")]
        public static void ReportRig()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[RIG] {ModelPath} does not load. Import it first.");
                return;
            }

            var report = new StringBuilder();
            report.AppendLine($"[RIG] === {ModelPath} ===");
            report.AppendLine($"[RIG] importer: {DescribeImporter(model)}");

            var assets = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
            var avatars = new List<Avatar>();
            var clips = new List<AnimationClip>();
            var meshes = new List<Mesh>();
            foreach (var asset in assets)
            {
                if (asset is Avatar avatar) avatars.Add(avatar);
                else if (asset is AnimationClip clip)
                {
                    // The importer always synthesises a preview clip; it is not content.
                    if (!clip.name.StartsWith("__preview__")) clips.Add(clip);
                }
                else if (asset is Mesh mesh) meshes.Add(mesh);
            }

            report.AppendLine($"[RIG] sub-assets: {assets.Length} (avatars {avatars.Count}, clips {clips.Count}, meshes {meshes.Count})");
            foreach (var clip in clips) report.AppendLine($"[RIG]   clip: {clip.name} ({clip.length:0.###}s, {clip.frameRate}fps)");
            if (clips.Count == 0) report.AppendLine("[RIG]   clip: none — the FBX carries no animation");

            var morphTotal = 0;
            var vertices = 0;
            foreach (var mesh in meshes)
            {
                morphTotal += mesh.blendShapeCount;
                vertices += mesh.vertexCount;
            }
            report.AppendLine($"[RIG] meshes: {meshes.Count} ({vertices} vertices), morph targets: {morphTotal}");
            if (morphTotal == 0)
            {
                report.AppendLine("[RIG]   morph targets: none — a facial preset cannot be driven from this export");
            }

            foreach (var avatar in avatars)
            {
                report.AppendLine($"[RIG] avatar '{avatar.name}': valid={avatar.isValid} human={avatar.isHuman}");
                var description = avatar.humanDescription;
                report.AppendLine($"[RIG]   mapped human bones: {description.human.Length}, skeleton bones: {description.skeleton.Length}");
                foreach (var bone in description.human)
                {
                    if (bone.humanName == "Hips" || bone.humanName == "Head" || bone.humanName == "LeftFoot"
                        || bone.humanName == "RightFoot" || bone.humanName == "LeftHand" || bone.humanName == "RightHand")
                    {
                        report.AppendLine($"[RIG]   {bone.humanName} <- '{bone.boneName}'");
                    }
                }
            }

            // Measure an instance: bounds, skinned renderers, bones, and the
            // feet-to-head span, which is what "is this human sized?" means.
            // Keep the user's authored scene open. This probe is disposable and
            // must not replace unsaved authoring work just to measure an import.
            var previousScene = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var instance = Object.Instantiate(model);
            instance.name = "LunaRigProbe";
            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                report.AppendLine($"[RIG] animator: {(animator == null ? "none" : "present")}, avatar assigned: {(animator != null && animator.avatar != null)}");

                var skinned = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var totalBones = 0;
                foreach (var renderer in skinned)
                {
                    var bones = renderer.bones != null ? renderer.bones.Length : 0;
                    totalBones = Mathf.Max(totalBones, bones);
                    report.AppendLine($"[RIG]   skinned '{renderer.name}': bones {bones}, morphs {(renderer.sharedMesh == null ? 0 : renderer.sharedMesh.blendShapeCount)}, material '{(renderer.sharedMaterial == null ? "none" : renderer.sharedMaterial.name)}'");
                }
                report.AppendLine($"[RIG] skinned renderers: {skinned.Length}, deepest bone list: {totalBones}");

                var bounds = DescribeBounds(instance);
                report.AppendLine($"[RIG] rendered bounds: {bounds}");

                if (animator != null && animator.avatar != null && animator.avatar.isHuman)
                {
                    // Which humanoid bones this avatar cannot reach. It is the
                    // question an asset decision turns on: an unmapped eye rules out
                    // bone-driven gaze and forces a morph or a second renderer, and an
                    // unmapped finger rules out hand posing, whatever the clip set
                    // contains. Reported by asking for each bone rather than by
                    // reading the avatar description, because reachability at runtime
                    // is what the code will actually depend on.
                    var unreachable = new List<string>();
                    foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
                    {
                        if (bone == HumanBodyBones.LastBone) continue;
                        if (animator.GetBoneTransform(bone) == null) unreachable.Add(bone.ToString());
                    }
                    var eyes = animator.GetBoneTransform(HumanBodyBones.LeftEye) != null &&
                               animator.GetBoneTransform(HumanBodyBones.RightEye) != null;
                    report.AppendLine($"[RIG] humanoid bones without a transform: {unreachable.Count}" +
                                      $"{(unreachable.Count == 0 ? " (every bone reachable)" : " — " + string.Join(", ", unreachable))}");
                    report.AppendLine($"[RIG] gaze: eye bones {(eyes ? "present — bone-driven gaze is available without new assets" : "MISSING — gaze needs morphs or a different mechanism")}");

                    var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                    var head = animator.GetBoneTransform(HumanBodyBones.Head);
                    var leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    if (hips != null && head != null && leftFoot != null)
                    {
                        report.AppendLine($"[RIG] heights (unity units): feet->head {head.position.y - leftFoot.position.y:0.###}, feet->hips {hips.position.y - leftFoot.position.y:0.###}, hips {hips.position.y:0.###}");
                    }
                    else
                    {
                        report.AppendLine("[RIG] heights: could not resolve Hips/Head/LeftFoot transforms");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
                EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            }

            Debug.Log(report.ToString());
        }

        [MenuItem("TruthCardGame/Diagnostics/Rig Spike/Setup And Report")]
        public static void SetupAndReport()
        {
            ConfigureImport();
            ReportRig();
        }

        /// <summary>
        /// Dumps Unity's humanoid muscle and bone vocabulary. Fixture clips must be
        /// authored against exactly these names; guessing them authors a clip that
        /// animates nothing, so the vocabulary is printed rather than recalled.
        /// </summary>
        [MenuItem("TruthCardGame/Diagnostics/Rig Spike/Report Humanoid Vocabulary")]
        public static void ReportMuscleNames()
        {
            var report = new StringBuilder();
            report.AppendLine($"[RIG] humanoid bones: {HumanTrait.BoneCount}, muscles: {HumanTrait.MuscleCount}");
            report.AppendLine("[RIG] muscle names: " + string.Join(" | ", HumanTrait.MuscleName));
            report.AppendLine("[RIG] bone names: " + string.Join(" | ", HumanTrait.BoneName));
            Debug.Log(report.ToString());
        }

        private static string DescribeImporter(GameObject model)
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) return "missing";
            return $"animationType={importer.animationType} avatarSetup={importer.avatarSetup} " +
                   $"optimizeGameObjects={importer.optimizeGameObjects} importBlendShapes={importer.importBlendShapes} " +
                   $"globalScale={importer.globalScale} readable={importer.isReadable}";
        }

        private static string DescribeBounds(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return "no renderers";

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return $"size {bounds.size.x:0.###} x {bounds.size.y:0.###} x {bounds.size.z:0.###}, " +
                   $"min.y {bounds.min.y:0.###}, max.y {bounds.max.y:0.###} (from {renderers.Length} renderers)";
        }
    }
}
