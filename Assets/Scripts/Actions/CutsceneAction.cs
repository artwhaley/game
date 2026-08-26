using UnityEngine;
using UnityEngine.Timeline;

namespace TruthCardGame
{
    /// <summary>
    /// Plays a Timeline cutscene. The TimelineAsset stays Unity-side; the
    /// serialized resourceId is the stable authored key that portable content
    /// (JSON/WPF) references, so cutscene links survive authoring round trips.
    /// Minted once if left empty; rename to something readable while content
    /// is young. Execution lives in Game.Core + DirectorPlayer service.
    /// </summary>
    [CreateAssetMenu(fileName = "CutsceneAction", menuName = "TruthCardGame/Actions/Cutscene")]
    public sealed class CutsceneAction : CardAction
    {
        [SerializeField, Tooltip("The Timeline cutscene to play. Assign in the Timeline window.")]
        private TimelineAsset timeline;
        [SerializeField, Tooltip("Stable authored id referenced from portable content (e.g. \"cs:intro\"). Minted once if empty; never change after content references it.")]
        private string resourceId;

        public string ResourceId => resourceId;
        public TimelineAsset Timeline => timeline;
        public bool HasTimeline => timeline != null;

        /// <summary>Mints the stable resource id on first call; no-op once set. Called by OnValidate and authoring tooling.</summary>
        public void EnsureResourceId()
        {
            if (string.IsNullOrEmpty(resourceId)) resourceId = System.Guid.NewGuid().ToString("N");
        }

        // Hides the base OnValidate; covers both the entity id and the resource id.
        private void OnValidate()
        {
            EnsureId();
            EnsureResourceId();
        }

        public override TruthCardGame.Content.GameActionDefinition ToDefinition(UnityContentGraphBuilder builder)
        {
            if (builder == null)
            {
                if (timeline != null)
                {
                    Debug.LogError("[TruthCardGame] CutsceneAction has a timeline but no graph builder to bind it; converting as missing resource.");
                }
                return new TruthCardGame.Content.CutsceneActionDefinition
                {
                    Id = Id,
                    IsBlocking = IsBlocking,
                    ResourceId = resourceId
                };
            }

            builder.CollectCutscene(this);

            // Null/empty ResourceId preserves the current missing-timeline no-op behavior downstream.
            return new TruthCardGame.Content.CutsceneActionDefinition
            {
                Id = Id,
                IsBlocking = IsBlocking,
                ResourceId = resourceId
            };
        }
    }
}
