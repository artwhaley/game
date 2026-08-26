using System;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Collision-resistant opaque content IDs (32 lowercase hex chars). IDs
    /// are opaque strings — nothing parses them as GUIDs.
    /// </summary>
    public static class StableIds
    {
        public static string New()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
