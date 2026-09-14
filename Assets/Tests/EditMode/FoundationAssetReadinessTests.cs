using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace TruthCardGame.Tests.EditMode
{
    /// <summary>
    /// Guards the reusable prefab contract that authored scenes depend on. This
    /// intentionally checks serialized asset shape rather than opening the
    /// disposable showcase, so an asset update cannot silently fall back to the
    /// retired one-off binders.
    /// </summary>
    public sealed class FoundationAssetReadinessTests
    {
        private const string LaraPrefabPath = "Assets/Daz3D/lara/Prefabs/lara_Prefab.prefab";
        private static readonly string[] AttachmentPaths =
        {
            "Assets/Characters/LaraAttachments/2021-02Hair_189597.prefab",
            "Assets/Characters/LaraAttachments/Bra_20266.prefab",
            "Assets/Characters/LaraAttachments/Panties_8559.prefab",
        };

        [Test]
        public void LaraPrefabHasTheReusableFoundationControllers()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LaraPrefabPath);
            Assert.That(prefab, Is.Not.Null, LaraPrefabPath + " must be imported");

            var rig = prefab.GetComponent<CharacterRig>();
            Assert.That(rig, Is.Not.Null, "Lara must own the CharacterRig contract");
            Assert.That(rig.Animator, Is.Not.Null, "CharacterRig must serialize the body Animator");
            Assert.That(rig.BodySkeletonRoot, Is.Not.Null, "CharacterRig must serialize the body skeleton root");
            Assert.That(rig.AttachmentsRoot, Is.Not.Null, "CharacterRig must serialize the attachment container");
            Assert.That(rig.RigFamilyId, Is.EqualTo("Genesis8Female"));
            var face = prefab.GetComponent<CharacterFaceController>();
            Assert.That(face, Is.Not.Null);
            var faceSerialized = new SerializedObject(face);
            var facialRenderers = faceSerialized.FindProperty("facialRenderers");
            Assert.That(facialRenderers, Is.Not.Null);
            Assert.That(facialRenderers.arraySize, Is.GreaterThan(0),
                "Lara must serialize explicit facial renderers instead of discovering attachment meshes at runtime");
            Assert.That(prefab.GetComponent<CharacterGazeController>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<CharacterPresentation>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<CharacterAnimationPlayer>(), Is.Not.Null);
        }

        [Test]
        public void PreparedAttachmentsUseTheReusableBinderAndNoRetiredBinder()
        {
            foreach (var path in AttachmentPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path + " must be imported");
                var attachment = prefab.GetComponent<RiggedAttachment>();
                Assert.That(attachment, Is.Not.Null,
                    path + " must use RiggedAttachment");
                Assert.That(attachment.SupportedRigFamilyId, Is.EqualTo("Genesis8Female"),
                    path + " must declare the rig family it was prepared against");
                var retired = prefab.GetComponentsInChildren<MonoBehaviour>(true);
                Assert.That(retired.Any(component => component != null && component.GetType().Name == "DazHairAttachment"), Is.False,
                    path + " still contains the retired hair binder");
                Assert.That(retired.Any(component => component != null && component.GetType().Name == "DazHairRigFollower"), Is.False,
                    path + " still contains the retired hair follower");
            }
        }
    }
}
