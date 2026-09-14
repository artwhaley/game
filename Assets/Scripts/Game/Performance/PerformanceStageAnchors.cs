using System;
using System.Collections.Generic;
using TruthCardGame.Content;
using TruthCardGame.Core;
using UnityEngine;

namespace TruthCardGame.Performance
{
    /// <summary>
    /// Scene-side counterpart to the performance registry: where each anchor
    /// actually is, which transform the character occupies, who it looks at and
    /// where the run starts.
    ///
    /// This is a separate component because a registry is a project asset and
    /// Unity assets cannot reference scene objects. The registry stays the
    /// authority for capabilities; this component only supplies placement, and
    /// the host refuses any anchor it cannot place.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceStageAnchors : MonoBehaviour
    {
        [Serializable]
        public sealed class AnchorBinding
        {
            [Tooltip("Stable anchor id, matching the performance registry.")]
            public string anchorId = "";

            [Tooltip("Where the character stands or sits for this anchor.")]
            public Transform anchor = null;
        }

        [SerializeField] private Transform characterRoot;
        [SerializeField] private AnchorBinding[] anchors = Array.Empty<AnchorBinding>();
        [Tooltip("The player stand-in the character gazes at while gaze is on.")]
        [SerializeField] private Transform playerGazeTarget;
        [SerializeField] private string startingAnchorId = "";
        [SerializeField] private string startingPostureId = PresentationPostures.Standing;

        public Transform CharacterRoot => characterRoot;
        public Transform PlayerGazeTarget => playerGazeTarget;
        public string StartingAnchorId => startingAnchorId;
        public string StartingPostureId => startingPostureId;

        public void Configure(
            Transform sourceCharacterRoot, AnchorBinding[] sourceAnchors,
            Transform sourcePlayerGazeTarget, string sourceStartingAnchorId, string sourceStartingPostureId)
        {
            characterRoot = sourceCharacterRoot;
            anchors = sourceAnchors ?? Array.Empty<AnchorBinding>();
            playerGazeTarget = sourcePlayerGazeTarget;
            startingAnchorId = sourceStartingAnchorId ?? "";
            startingPostureId = string.IsNullOrEmpty(sourceStartingPostureId)
                ? PresentationPostures.Standing
                : sourceStartingPostureId;
        }

        /// <summary>Resolves an anchor id to its scene transform, or false when it is unplaced.</summary>
        public bool TryGetAnchor(string anchorId, out Transform anchor)
        {
            anchor = null;
            if (string.IsNullOrEmpty(anchorId)) return false;
            foreach (var binding in anchors)
            {
                if (binding == null || binding.anchor == null) continue;
                if (!string.Equals(binding.anchorId, anchorId, StringComparison.Ordinal)) continue;
                anchor = binding.anchor;
                return true;
            }
            return false;
        }

        /// <summary>Anchor ids this scene places, for reporting against the catalog.</summary>
        public List<string> PlacedAnchorIds()
        {
            var placed = new List<string>();
            foreach (var binding in anchors)
            {
                if (binding == null) continue;
                if (string.IsNullOrEmpty(binding.anchorId) || binding.anchor == null) continue;
                if (!placed.Contains(binding.anchorId)) placed.Add(binding.anchorId);
            }
            return placed;
        }

        /// <summary>
        /// The run's declared starting state. It is a declaration, not a guess:
        /// the host verifies it against the catalog and refuses to start when the
        /// anchor is unplaced or the catalog disagrees.
        /// </summary>
        public PerformanceActorState InitialState()
        {
            return new PerformanceActorState(startingAnchorId ?? "", startingPostureId ?? "");
        }
    }
}
