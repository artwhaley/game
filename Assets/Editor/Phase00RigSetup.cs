using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace TruthCardGame.EditorTools
{
    /// <summary>
    /// Phase 00 preparation and disposable fixture assembly. Preparation updates
    /// only the current reusable prefab/attachment assets; validation is
    /// read-only; the showcase command is the sole command allowed to replace a
    /// scene, and it targets only the disposable Phase 00 scene.
    /// </summary>
    public static class Phase00RigSetup
    {
        public const string LaraPath = "Assets/Daz3D/lara/lara.fbx";
        public const string LaraMaterialPrefabPath = "Assets/Daz3D/lara/Prefabs/lara_Prefab.prefab";
        public const string LaraHairAttachmentPrefabPath = "Assets/Characters/LaraAttachments/2021-02Hair_189597.prefab";
        public const string LaraBraAttachmentPrefabPath = "Assets/Characters/LaraAttachments/Bra_20266.prefab";
        public const string LaraPantiesAttachmentPrefabPath = "Assets/Characters/LaraAttachments/Panties_8559.prefab";
        public const string LunaPath = "Assets/Characters/Luna/luna.fbx";
        public const string SourcePath = "Assets/Animations/Quaternius/Control/UAL1_Standard.fbx";
        public const string ScenePath = "Assets/Scenes/Phase00RigShowcase.unity";
        public const string MaskPath = "Assets/Animations/Quaternius/Control/UpperBodyNoHead.mask";
        public const string ShowcaseVolumeProfilePath = "Assets/Settings/Phase00RigShowcaseVolumeProfile.asset";
        public const string ReportPath = "Logs/Phase00AssetInventory.txt";

        private static readonly string[] RequiredClips =
        {
            "Idle_Loop", "Walk_Loop", "Sitting_Enter", "Sitting_Idle_Loop",
            "Sitting_Exit", "Idle_Talking_Loop", "Interact"
        };

        [MenuItem("TruthCardGame/Phase 00/One-time Setup: Configure Imports, Report, and Rebuild Disposable Showcase")]
        public static void ConfigureReportAndBuild()
        {
            ConfigureImports();
            ReportInputs();
            BuildShowcase();
        }

        [MenuItem("TruthCardGame/Phase 00/Configure Humanoid Imports")]
        public static void ConfigureImports()
        {
            ConfigureModel(SourcePath, importAnimation: true, importBlendShapes: false);
            ConfigureModel(LaraPath, importAnimation: false, importBlendShapes: true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("TruthCardGame/Phase 00/Report Lara, Luna, and Motion Source")]
        public static void ReportInputs()
        {
            var report = new StringBuilder();
            report.AppendLine("PHASE 00 ASSET INVENTORY");
            report.AppendLine(DateTime.UtcNow.ToString("O"));
            AppendModel(report, SourcePath);
            AppendModel(report, LaraPath);
            AppendModel(report, LunaPath);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath) ?? "Logs");
            File.WriteAllText(ReportPath, report.ToString());
            Debug.Log(report.ToString());
        }

        [MenuItem("TruthCardGame/Phase 00/Prepare Current Character Foundation")]
        public static void PrepareCharacterFoundation()
        {
            LaraWardrobeAttachmentBuilder.EnsureExtractedAndBaseStripped();
            DazHairAttachmentBuilder.Ensure202102Hair();

            var root = PrefabUtility.LoadPrefabContents(LaraMaterialPrefabPath);
            try
            {
                var animator = root.GetComponentInChildren<Animator>(true);
                if (animator == null) throw new InvalidOperationException("Lara prefab has no Animator.");
                var bodyRoot = root.transform.Find("Genesis8Female");
                if (bodyRoot == null)
                {
                    var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                    bodyRoot = hips == null ? null : hips.parent;
                }
                if (bodyRoot == null) throw new InvalidOperationException("Lara prefab has no Genesis body skeleton root.");

                var attachmentsRoot = root.transform.Find("Attachments");
                if (attachmentsRoot == null)
                {
                    var attachmentsObject = new GameObject("Attachments");
                    attachmentsRoot = attachmentsObject.transform;
                    attachmentsRoot.SetParent(root.transform, worldPositionStays: false);
                }

                var rig = root.GetComponent<CharacterRig>() ?? root.AddComponent<CharacterRig>();
                rig.Configure(animator, bodyRoot, attachmentsRoot, "Genesis8Female");
                var face = root.GetComponent<CharacterFaceController>() ?? root.AddComponent<CharacterFaceController>();
                face.Configure(rig, bodyRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true), "lowerJaw");
                var gaze = root.GetComponent<CharacterGazeController>() ?? root.AddComponent<CharacterGazeController>();
                gaze.Configure(rig, null);
                var presentation = root.GetComponent<CharacterPresentation>() ?? root.AddComponent<CharacterPresentation>();
                presentation.Configure(rig, face, gaze);
                var animationPlayer = root.GetComponent<CharacterAnimationPlayer>() ?? root.AddComponent<CharacterAnimationPlayer>();
                animationPlayer.Configure(animator, null);

                PrefabUtility.SaveAsPrefabAsset(root, LaraMaterialPrefabPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[PHASE00] Prepared reusable Lara CharacterRig foundation and attachment container.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("TruthCardGame/Phase 00/Validate Prepared Character Foundation")]
        public static void ValidatePreparedCharacterFoundation()
        {
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(LaraMaterialPrefabPath);
            if (character == null) throw new InvalidOperationException("Missing prepared Lara prefab: " + LaraMaterialPrefabPath);
            var rig = character.GetComponent<CharacterRig>();
            if (rig == null || rig.Animator == null || rig.BodySkeletonRoot == null || rig.AttachmentsRoot == null)
                throw new InvalidOperationException("Lara prefab is missing its CharacterRig references. Run Prepare Current Character Foundation.");
            if (character.GetComponent<CharacterFaceController>() == null ||
                character.GetComponent<CharacterGazeController>() == null ||
                character.GetComponent<CharacterPresentation>() == null ||
                character.GetComponent<CharacterAnimationPlayer>() == null)
                throw new InvalidOperationException("Lara prefab is missing one or more presentation controllers. Run Prepare Current Character Foundation.");
            RequirePreparedAttachment(LaraHairAttachmentPrefabPath, "2021-02 hair");
            RequirePreparedAttachment(LaraBraAttachmentPrefabPath, "bra");
            RequirePreparedAttachment(LaraPantiesAttachmentPrefabPath, "panties");
            Debug.Log("[PHASE00] Prepared character foundation is present: CharacterRig, presentation controllers, and three RiggedAttachment prefabs.");
        }

        [MenuItem("TruthCardGame/Phase 00/Rebuild Disposable Rig Test Scene")]
        public static void BuildShowcase()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            var target = AssetDatabase.LoadAssetAtPath<GameObject>(LaraMaterialPrefabPath);
            if (source == null || target == null)
                throw new InvalidOperationException(
                    "Phase 00 source or Lara's material prefab is missing. Run the HDRP/DTU material setup before rebuilding the showcase.");
            ValidatePreparedCharacterFoundation();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[PHASE00] Showcase rebuild cancelled so unsaved authored scenes remain untouched.");
                return;
            }

            var clips = AssetDatabase.LoadAllAssetsAtPath(SourcePath)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                .GroupBy(c => CanonicalClipName(c.name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var missing = RequiredClips.Where(name => !clips.ContainsKey(name)).ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException("Phase 00 source is missing clips: " + string.Join(", ", missing));

            var mask = EnsureUpperBodyMask();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ConfigureShowcaseEnvironment();
            CreateFixedExposureVolume();
            CreateGround();
            CreateChair();
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "PlayerGazeTarget";
            // Offset the player marker from Lara so the gaze test requires an
            // obvious horizontal and vertical correction rather than a tiny nod.
            marker.transform.position = new Vector3(0f, 1.55f, -2.6f);
            marker.transform.localScale = Vector3.one * 0.12f;
            marker.GetComponent<Renderer>().sharedMaterial = MakeMaterial("GazeTargetGreen", new Color(0.2f, 1f, 0.35f));

            var sourceInstance = PrefabUtility.InstantiatePrefab(source) as GameObject;
            var targetInstance = PrefabUtility.InstantiatePrefab(target) as GameObject;
            if (sourceInstance == null || targetInstance == null) throw new InvalidOperationException("Could not instantiate Phase 00 models.");
            sourceInstance.name = "SOURCE_Quaternius";
            targetInstance.name = "TARGET_Lara";

            Attach202102Hair(targetInstance.transform);
            AttachWardrobe(targetInstance.transform);

            var sourceAnimator = RequireHumanAnimator(sourceInstance, "Quaternius source");
            var targetAnimator = RequireHumanAnimator(targetInstance, "Lara target");
            var sourcePlayer = sourceInstance.GetComponent<CharacterAnimationPlayer>() ?? sourceInstance.AddComponent<CharacterAnimationPlayer>();
            sourcePlayer.Configure(sourceAnimator, mask);
            var targetPlayer = targetInstance.GetComponent<CharacterAnimationPlayer>();
            if (targetPlayer == null) throw new InvalidOperationException("Prepared Lara prefab has no CharacterAnimationPlayer.");
            targetPlayer.Configure(targetAnimator, mask);
            var targetGaze = targetInstance.GetComponent<CharacterGazeController>();
            if (targetGaze == null) throw new InvalidOperationException("Prepared Lara prefab has no CharacterGazeController.");
            targetGaze.SetTarget(marker.transform);
            var host = new GameObject("Phase00RigShowcase");
            var showcase = host.AddComponent<Phase00RigShowcase>();
            showcase.sourceAnimator = sourceAnimator;
            showcase.targetAnimator = targetAnimator;
            showcase.sourcePlayer = sourcePlayer;
            showcase.targetPlayer = targetPlayer;
            showcase.faceController = targetInstance.GetComponent<CharacterFaceController>();
            showcase.gazeController = targetGaze;
            showcase.sourceActor = sourceInstance.transform;
            showcase.targetActor = targetInstance.transform;
            showcase.gazeTarget = marker.transform;
            showcase.upperBodyMask = mask;
            showcase.idle = clips["Idle_Loop"];
            showcase.walk = clips["Walk_Loop"];
            showcase.sitEnter = clips["Sitting_Enter"];
            showcase.sitIdle = clips["Sitting_Idle_Loop"];
            showcase.sitExit = clips["Sitting_Exit"];
            showcase.talking = clips["Idle_Talking_Loop"];
            showcase.interact = clips["Interact"];

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            // Lara begins at (1.35, 0, -1.25) facing +Z. Put the default view
            // in front of her, centered on that spawn, rather than behind her.
            cameraObject.transform.position = new Vector3(1.35f, 1.65f, 4.8f);
            cameraObject.transform.LookAt(new Vector3(1.35f, 1.35f, -1.25f));
            cameraObject.AddComponent<Phase00DebugFlyCamera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            var lightObject = new GameObject("Key Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            // HDRP directional lights use Lux. This is a deliberately modest
            // daylight/studio key which pairs with the fixed EV100 exposure.
            light.intensity = 12000f;
            light.colorTemperature = 5600f;
            light.useColorTemperature = true;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PHASE00] Showcase written to {ScenePath}");
        }

        private static void RequirePreparedAttachment(string path, string label)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var attachment = prefab == null ? null : prefab.GetComponent<RiggedAttachment>();
            if (attachment == null)
                throw new InvalidOperationException("Prepared " + label + " attachment is missing RiggedAttachment: " + path);
            if (!string.Equals(attachment.SupportedRigFamilyId, "Genesis8Female", StringComparison.Ordinal))
                throw new InvalidOperationException("Prepared " + label + " attachment targets rig family '" +
                                                    attachment.SupportedRigFamilyId + "' instead of Genesis8Female: " + path);
        }

        private static void Attach202102Hair(Transform targetActor)
        {
            var hairPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LaraHairAttachmentPrefabPath);
            if (hairPrefab == null) throw new InvalidOperationException("Could not create the 2021-02 hair attachment.");
            var hair = PrefabUtility.InstantiatePrefab(hairPrefab) as GameObject;
            if (hair == null) throw new InvalidOperationException("Could not instantiate the 2021-02 hair attachment.");
            hair.name = "Lara Hair - 2021-02";
            var rig = targetActor.GetComponent<CharacterRig>();
            if (rig == null || rig.AttachmentsRoot == null) throw new InvalidOperationException("Lara has no prepared attachment root.");
            hair.transform.SetParent(rig.AttachmentsRoot, worldPositionStays: false);
            hair.transform.localPosition = Vector3.zero;
            hair.transform.localRotation = Quaternion.identity;
            hair.transform.localScale = Vector3.one;
            if (hair.GetComponent<RiggedAttachment>() == null) throw new InvalidOperationException("The 2021-02 hair prefab has no RiggedAttachment.");
        }

        private static void AttachWardrobe(Transform targetActor)
        {
            var bra = AssetDatabase.LoadAssetAtPath<GameObject>(LaraBraAttachmentPrefabPath);
            var panties = AssetDatabase.LoadAssetAtPath<GameObject>(LaraPantiesAttachmentPrefabPath);
            if (bra == null || panties == null) throw new InvalidOperationException("Lara wardrobe attachment prefabs are missing.");
            AttachDazAttachment(targetActor, bra, "Lara Wardrobe - Bra");
            AttachDazAttachment(targetActor, panties, "Lara Wardrobe - Panties");
        }

        private static void AttachDazAttachment(Transform targetActor, GameObject prefab, string instanceName)
        {
            var attachment = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (attachment == null) throw new InvalidOperationException("Could not instantiate " + instanceName + ".");
            attachment.name = instanceName;
            var rig = targetActor.GetComponent<CharacterRig>();
            if (rig == null || rig.AttachmentsRoot == null) throw new InvalidOperationException("Lara has no prepared attachment root.");
            attachment.transform.SetParent(rig.AttachmentsRoot, worldPositionStays: false);
            attachment.transform.localPosition = Vector3.zero;
            attachment.transform.localRotation = Quaternion.identity;
            attachment.transform.localScale = Vector3.one;
            if (attachment.GetComponent<RiggedAttachment>() == null) throw new InvalidOperationException(instanceName + " has no RiggedAttachment.");
        }

        private static void ConfigureModel(string path, bool importAnimation, bool importBlendShapes)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("No ModelImporter at " + path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeGameObjects = false;
            importer.importAnimation = importAnimation;
            importer.importBlendShapes = importBlendShapes;
            importer.isReadable = true;
            importer.SaveAndReimport();

            if (!importAnimation) return;
            var defaults = importer.defaultClipAnimations;
            foreach (var clip in defaults)
            {
                clip.loopTime = clip.name.EndsWith("_Loop", StringComparison.OrdinalIgnoreCase);
                clip.loopPose = clip.loopTime;
            }
            importer.clipAnimations = defaults;
            importer.SaveAndReimport();
        }

        private static void AppendModel(StringBuilder report, string path)
        {
            report.AppendLine();
            report.AppendLine("=== " + path + " ===");
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            report.AppendLine(importer == null
                ? "importer: missing"
                : $"importer: animationType={importer.animationType}; avatarSetup={importer.avatarSetup}; scale={importer.globalScale}; blendShapes={importer.importBlendShapes}; animations={importer.importAnimation}");
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            var avatar = assets.OfType<Avatar>().FirstOrDefault();
            report.AppendLine(avatar == null
                ? "avatar: missing"
                : $"avatar: {avatar.name}; valid={avatar.isValid}; human={avatar.isHuman}; mapped={avatar.humanDescription.human.Length}; skeleton={avatar.humanDescription.skeleton.Length}");
            if (avatar != null)
            {
                foreach (var bone in avatar.humanDescription.human)
                    report.AppendLine($"bone: {bone.humanName} <- {bone.boneName}");
            }
            var clips = assets.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            report.AppendLine("clips: " + clips.Length + (clips.Length == 0 ? "" : "; " + string.Join(" | ", clips.Select(c => $"{c.name} ({c.length:0.###}s/{c.frameRate:0.#}fps)"))));
            var meshes = assets.OfType<Mesh>().ToArray();
            report.AppendLine($"meshes: {meshes.Length}; vertices={meshes.Sum(m => m.vertexCount)}; blendShapes={meshes.Sum(m => m.blendShapeCount)}");
            foreach (var mesh in meshes)
                for (var i = 0; i < mesh.blendShapeCount; i++)
                    report.AppendLine($"blendShape: {mesh.name} :: {mesh.GetBlendShapeName(i)}");
        }

        public static string CanonicalClipName(string importedName)
        {
            var separator = importedName.LastIndexOf('|');
            return separator < 0 ? importedName : importedName.Substring(separator + 1);
        }

        private static AvatarMask EnsureUpperBodyMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, MaskPath);
            }
            foreach (AvatarMaskBodyPart part in Enum.GetValues(typeof(AvatarMaskBodyPart)))
            {
                if (part != AvatarMaskBodyPart.LastBodyPart)
                    mask.SetHumanoidBodyPartActive(part, false);
            }
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            EditorUtility.SetDirty(mask);
            return mask;
        }

        private static Animator RequireHumanAnimator(GameObject instance, string label)
        {
            var animator = instance.GetComponentInChildren<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException(label + " does not have a valid Humanoid Avatar.");
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            return animator;
        }

        private static void CreateGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(1.1f, 1f, 1.1f);
            ground.GetComponent<Renderer>().sharedMaterial = MakeMaterial("GroundGray", new Color(0.22f, 0.23f, 0.25f));
        }

        private static void CreateChair()
        {
            var seat = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seat.name = "ChairSeat";
            seat.transform.position = new Vector3(1.35f, 0.47f, 2.85f);
            seat.transform.localScale = new Vector3(0.75f, 0.12f, 0.75f);
            var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
            back.name = "ChairBack";
            back.transform.position = new Vector3(1.35f, 0.93f, 3.18f);
            back.transform.localScale = new Vector3(0.75f, 0.9f, 0.12f);
        }

        private static Material MakeMaterial(string name, Color color)
        {
            var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No HDRP/Lit shader is available for the Phase 00 scene.");
            var material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.color = color;
            return material;
        }

        private static void ConfigureShowcaseEnvironment()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.055f, 0.065f, 0.085f);
            RenderSettings.reflectionIntensity = 0.25f;
        }

        private static void CreateFixedExposureVolume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ShowcaseVolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "Phase00RigShowcaseVolumeProfile";
                AssetDatabase.CreateAsset(profile, ShowcaseVolumeProfilePath);
            }

            // Volume components are separate ScriptableObjects. Persist them as
            // sub-assets or Unity reloads the profile with a null component.
            profile.components.RemoveAll(component => component == null);
            if (!profile.TryGet(out Exposure exposure))
            {
                exposure = profile.Add<Exposure>(true);
                AssetDatabase.AddObjectToAsset(exposure, profile);
            }
            exposure.active = true;
            exposure.mode.Override(ExposureMode.Fixed);
            // EV100 12 with the 12,000 Lux key gives the viewer a stable,
            // daylight-like base without HDRP adapting after Play starts.
            exposure.fixedExposure.Override(12f);
            EditorUtility.SetDirty(profile);

            var volumeObject = new GameObject("Fixed Exposure (EV100 12)");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.sharedProfile = profile;
        }
    }
}
