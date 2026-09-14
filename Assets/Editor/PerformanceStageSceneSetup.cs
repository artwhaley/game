using System;
using System.IO;
using TruthCardGame.Content;
using TruthCardGame.Performance;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TruthCardGame.EditorTools
{
    /// <summary>
    /// Ticket 04 scene wiring: places the catalog anchors inside the open
    /// disposable showcase, binds the character's presentation controllers and
    /// the performance registry into a <see cref="UnityPerformanceHost"/>, and
    /// attaches the smoke panel that drives the real Core planning stack.
    ///
    /// Only the disposable showcase is ever touched, matching the Phase 00
    /// convention; authored scenes are never modified by this command. Re-running
    /// on an already-wired scene refreshes the bindings in place instead of
    /// stacking a second rig.
    /// </summary>
    public static class PerformanceStageSceneSetup
    {
        public const string RigObjectName = "PerformanceStage";

        [MenuItem("TruthCardGame/Performance/Wire Current Scene for Performance")]
        public static void WireCurrentScene()
        {
            var registry = AssetDatabase.LoadAssetAtPath<PerformanceRegistry>(PerformanceCatalogSetup.RegistryPath);
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "No performance registry at " + PerformanceCatalogSetup.RegistryPath +
                    ". Run TruthCardGame/Performance/Ensure V1 Performance Fixture first.");
            }

            // The character instance is the prepared Lara prefab in the scene; its
            // presentation controllers live on the prefab root.
            var character = GameObject.Find("TARGET_Lara");
            if (character == null)
            {
                throw new InvalidOperationException(
                    "No 'TARGET_Lara' instance in the open scene. Open the disposable showcase " +
                    "(Assets/Scenes/Phase00RigShowcase.unity) or rebuild it with the Phase 00 command.");
            }

            var rig = character.GetComponent<CharacterRig>();
            var animator = rig == null ? null : rig.Animator;
            if (animator == null) animator = character.GetComponentInChildren<Animator>();
            if (animator == null)
                throw new InvalidOperationException("TARGET_Lara has no Animator; run the Phase 00 preparation first.");
            var animationPlayer = character.GetComponent<CharacterAnimationPlayer>();
            if (animationPlayer == null)
                animationPlayer = character.AddComponent<CharacterAnimationPlayer>();
            var face = character.GetComponent<CharacterFaceController>();
            if (face == null)
                throw new InvalidOperationException("TARGET_Lara has no CharacterFaceController; run Phase 00 Prepare Current Character Foundation.");
            var gaze = character.GetComponent<CharacterGazeController>();
            if (gaze == null)
                throw new InvalidOperationException("TARGET_Lara has no CharacterGazeController; run Phase 00 Prepare Current Character Foundation.");

            // The player gaze target must exist for gaze to have a direction.
            var gazeTarget = GameObject.Find("PlayerGazeTarget");
            if (gazeTarget == null)
            {
                throw new InvalidOperationException(
                    "No 'PlayerGazeTarget' in the open scene; rebuild the showcase with the Phase 00 command.");
            }

            // One rig object owns the anchors, the host and the smoke panel.
            var rigObject = GameObject.Find(RigObjectName);
            if (rigObject == null)
            {
                rigObject = new GameObject(RigObjectName);
                Undo.RegisterCreatedObjectUndo(rigObject, "Wire Performance Stage");
            }
            var anchors = rigObject.GetComponent<PerformanceStageAnchors>() ?? rigObject.AddComponent<PerformanceStageAnchors>();
            var host = rigObject.GetComponent<UnityPerformanceHost>() ?? rigObject.AddComponent<UnityPerformanceHost>();
            var panel = rigObject.GetComponent<PerformanceSmokePanel>() ?? rigObject.AddComponent<PerformanceSmokePanel>();

            // Anchor placements follow the fixture: Lara spawns at the room-center
            // anchor and the chair sits between her and the gaze target. Y stays 0
            // because the actor root is floor-level, exactly like the Phase 00 walk.
            var roomCenter = EnsureAnchorPoint(rigObject.transform, "Anchor_RoomCenter",
                new Vector3(1.35f, 0f, -1.25f), Quaternion.Euler(0f, 180f, 0f));
            var chair = EnsureAnchorPoint(rigObject.transform, "Anchor_Chair",
                new Vector3(1.35f, 0f, 2.75f), Quaternion.Euler(0f, 180f, 0f));

            anchors.Configure(
                character.transform,
                new[]
                {
                    new PerformanceStageAnchors.AnchorBinding { anchorId = "anchor-room-center", anchor = roomCenter },
                    new PerformanceStageAnchors.AnchorBinding { anchorId = "anchor-chair", anchor = chair },
                },
                gazeTarget.transform,
                "anchor-room-center",
                PresentationPostures.Standing);

            host.Configure(registry, anchors, animationPlayer, face, gaze);
            panel.Bind(host);

            Undo.RecordObject(anchors, "Wire Performance Stage");
            EditorUtility.SetDirty(anchors);
            EditorUtility.SetDirty(host);

            // The smoke panel loads Content/PresentationCatalog.json from disk, so
            // a stale catalog would silently misrepresent the registry.
            var catalogPath = PerformanceCatalogSetup.ResolveCatalogPath();
            if (!File.Exists(catalogPath))
            {
                Debug.LogWarning(
                    "[PERFORMANCE] No generated catalog at " + catalogPath +
                    " — run TruthCardGame/Performance/Generate Presentation Catalog before pressing Play.");
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log(
                "[PERFORMANCE] Scene wired: 2 anchors placed (anchor-room-center, anchor-chair), " +
                "host bound to TARGET_Lara's presentation controllers, smoke panel ready. " +
                "Save the scene to keep the wiring.");
        }

        private static Transform EnsureAnchorPoint(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            var existing = parent.Find(name);
            var point = existing == null ? new GameObject(name).transform : existing;
            Undo.RegisterCompleteObjectUndo(point, "Wire Performance Stage");
            point.SetParent(parent, worldPositionStays: false);
            point.position = position;
            point.rotation = rotation;
            return point;
        }
    }
}
