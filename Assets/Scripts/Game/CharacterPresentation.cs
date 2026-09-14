using UnityEngine;

namespace TruthCardGame
{
    /// <summary>Applies the character's final presentation pose in one order.</summary>
    [DefaultExecutionOrder(900)]
    [DisallowMultipleComponent]
    public sealed class CharacterPresentation : MonoBehaviour
    {
        [SerializeField] private CharacterRig rig;
        [SerializeField] private CharacterFaceController face;
        [SerializeField] private CharacterGazeController gaze;

        public void Configure(CharacterRig sourceRig, CharacterFaceController sourceFace, CharacterGazeController sourceGaze)
        {
            rig = sourceRig;
            face = sourceFace;
            gaze = sourceGaze;
        }

        private void Awake()
        {
            if (rig == null) rig = GetComponent<CharacterRig>();
            if (face == null) face = GetComponent<CharacterFaceController>();
            if (gaze == null) gaze = GetComponent<CharacterGazeController>();
        }

        private void OnEnable()
        {
            if (rig == null) rig = GetComponent<CharacterRig>();
            if (rig != null) rig.SetPresentation(this);
        }

        private void OnDisable()
        {
            if (rig != null) rig.ClearPresentation(this);
        }

        private void LateUpdate()
        {
            // Animator/Playable evaluation has already happened. Face fixes
            // precede gaze, and private attachment skeletons follow the final
            // head/body pose.
            if (face != null) face.ApplyFinalPose();
            if (gaze != null) gaze.ApplyFinalPose();
            if (rig != null) rig.ApplyAttachments();
        }
    }
}
