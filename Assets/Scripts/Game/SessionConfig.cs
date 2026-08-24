using System.Collections.Generic;

namespace TruthCardGame
{
    /// <summary>
    /// State handed from the setup screen to the game screen across a scene
    /// load. Deliberately simple (static, not persisted) — this is a dev
    /// environment and sessions don't need saving yet. Revisit only if
    /// sessions ever need to survive a restart.
    /// </summary>
    public static class SessionConfig
    {
        public static List<string> MustIncludeTags { get; set; } = new List<string>();
        public static List<string> MustExcludeTags { get; set; } = new List<string>();
    }
}
