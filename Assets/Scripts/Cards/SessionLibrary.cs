using System.Collections.Generic;
using UnityEngine;

namespace TruthCardGame
{
    /// <summary>
    /// The list of sessions the setup screen offers. A single library asset
    /// holds all the game's session types.
    /// </summary>
    [CreateAssetMenu(fileName = "SessionLibrary", menuName = "TruthCardGame/Session Library")]
    public sealed class SessionLibrary : ScriptableObject
    {
        [SerializeField] private List<Session> sessions = new List<Session>();

        public IReadOnlyList<Session> Sessions => sessions;
    }
}