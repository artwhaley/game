using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace TruthCardGame.Tests.EditMode
{
    public sealed class Phase00AssetReadinessTests
    {
        private const string SourcePath = "Assets/Animations/Quaternius/Control/UAL1_Standard.fbx";
        private const string LaraPath = "Assets/Daz3D/lara/lara.fbx";
        private const string LaraMaterialPrefabPath = "Assets/Daz3D/lara/Prefabs/lara_Prefab.prefab";
        private const string ShowcaseVolumeProfilePath = "Assets/Settings/Phase00RigShowcaseVolumeProfile.asset";

        private static readonly string[] RequiredClips =
        {
            "Idle_Loop", "Walk_Loop", "Sitting_Enter", "Sitting_Idle_Loop",
            "Sitting_Exit", "Idle_Talking_Loop", "Interact"
        };

        [TestCase(SourcePath)]
        [TestCase(LaraPath)]
        public void SelectedRigHasOneValidHumanoidAvatar(string path)
        {
            var avatars = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().ToArray();
            Assert.That(avatars, Has.Length.EqualTo(1), path + " must expose exactly one Avatar identity");
            Assert.That(avatars[0].isValid, Is.True, path + " Avatar is invalid");
            Assert.That(avatars[0].isHuman, Is.True, path + " must use Unity Humanoid retargeting");
        }

        [Test]
        public void ExternalSourceCoversEveryPhase00MotionRole()
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(SourcePath)
                .OfType<AnimationClip>()
                .Select(clip => CanonicalClipName(clip.name))
                .ToArray();
            foreach (var name in RequiredClips)
                Assert.That(clips, Does.Contain(name), "missing external motion role clip " + name);
        }

        [Test]
        public void LaraImportsTheExactFacePresetControlsOnTheBodyMesh()
        {
            var body = AssetDatabase.LoadAllAssetsAtPath(LaraPath)
                .OfType<Mesh>()
                .SingleOrDefault(mesh => string.Equals(mesh.name, "Genesis8Female.Shape", StringComparison.Ordinal));
            Assert.That(body, Is.Not.Null, "facial presets must bind against Lara's body/face mesh, not merely clothing autofollow channels");
            var shapes = Enumerable.Range(0, body.blendShapeCount)
                .Select(index => CanonicalMorphName(body.GetBlendShapeName(index)))
                .ToArray();
            Assert.That(shapes, Does.Contain("ST Mika 8 Natural Smile"),
                "the showcase's exact natural-smile preset is missing from Lara's face mesh");
            Assert.That(shapes, Does.Contain("eCTRLFrown_HD"),
                "the showcase's exact frown preset is missing from Lara's face mesh");
            Assert.That(shapes, Does.Contain("eCTRLvOW"),
                "the showcase's unmistakable combined OW phoneme is missing from Lara's face mesh");
        }

        [Test]
        public void LaraMaterialPrefabUsesHdrpAndRealTextures()
        {
            var pipeline = GraphicsSettings.defaultRenderPipeline;
            Assert.That(pipeline, Is.Not.Null, "Phase 00 must have a project render-pipeline asset");
            Assert.That(pipeline.GetType().Name, Is.EqualTo("HDRenderPipelineAsset"));
            Assert.That(PlayerSettings.colorSpace, Is.EqualTo(ColorSpace.Linear),
                "HDRP material evaluation must use the project's Linear color space");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LaraMaterialPrefabPath);
            Assert.That(prefab, Is.Not.Null, "the showcase must use the Daz-generated material prefab");
            var materials = prefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .ToArray();
            Assert.That(materials, Is.Not.Empty);
            Assert.That(materials, Has.None.Null, "every Lara renderer slot needs a material");
            Assert.That(materials.Any(material => material.shader.name.IndexOf("HDRP", StringComparison.OrdinalIgnoreCase) >= 0), Is.True,
                "Lara must use the bundled Daz HDRP shaders");
            Assert.That(materials.Any(material => material.GetTexturePropertyNames().Any(name => material.GetTexture(name) != null)), Is.True,
                "Lara's generated materials must bind imported texture assets");
        }

        [Test]
        public void ShowcaseUsesFixedHdrpExposure()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ShowcaseVolumeProfilePath);
            Assert.That(profile, Is.Not.Null, "the showcase needs a persistent HDRP volume profile");
            Assert.That(profile.TryGet(out Exposure exposure), Is.True, "the showcase profile must override HDRP exposure");
            Assert.That(exposure.active, Is.True);
            Assert.That(exposure.mode.overrideState, Is.True);
            Assert.That(exposure.mode.value, Is.EqualTo(ExposureMode.Fixed));
            Assert.That(exposure.fixedExposure.overrideState, Is.True);
            Assert.That(exposure.fixedExposure.value, Is.EqualTo(12f));
        }

        [Test]
        public void ShowcaseOwPhonemeDrivesGenesis8FemaleShapeOnTheActualPrefabHierarchy()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LaraMaterialPrefabPath);
            var instance = UnityEngine.Object.Instantiate(prefab);
            var host = new GameObject("Phase00FaceDriverTest");
            try
            {
                var showcase = host.AddComponent<Phase00RigShowcase>();
                showcase.targetActor = instance.transform;
                showcase.targetAnimator = instance.GetComponentInChildren<Animator>(true);
                Assert.That(showcase.targetAnimator, Is.Not.Null);

                InvokePrivate(showcase, "CaptureNeutralFace");
                InvokePrivate(showcase, "SetFacePreset", "eCTRLvOW", 100f);

                var body = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Single(renderer => renderer.sharedMesh != null &&
                        string.Equals(renderer.sharedMesh.name, "Genesis8Female.Shape", StringComparison.Ordinal));
                var index = Enumerable.Range(0, body.sharedMesh.blendShapeCount)
                    .Single(i => string.Equals(CanonicalMorphName(body.sharedMesh.GetBlendShapeName(i)),
                        "eCTRLvOW", StringComparison.OrdinalIgnoreCase));
                Assert.That(body.GetBlendShapeWeight(index), Is.EqualTo(100f),
                    "the showcase must drive Lara's visible combined OW phoneme, not a sibling or clothing mesh");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static string CanonicalClipName(string importedName)
        {
            var separator = importedName.LastIndexOf('|');
            return separator < 0 ? importedName : importedName.Substring(separator + 1);
        }

        private static string CanonicalMorphName(string importedName)
        {
            var separator = importedName.LastIndexOf("__", StringComparison.Ordinal);
            return separator < 0 ? importedName : importedName.Substring(separator + 2);
        }

        private static void InvokePrivate(object target, string methodName, params object[] arguments)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "missing showcase method " + methodName);
            method.Invoke(target, arguments);
        }
    }
}
