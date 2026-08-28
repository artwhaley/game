using System;
using System.IO;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// Ticket 20: distribution-safe location for the FUTURE user profile
    /// database. OS user application-data conventions keep player data OUTSIDE
    /// the install directory, so replacing or unzipping a new game version
    /// never destroys it. This is ONLY the path seam — no profile domain tables
    /// exist yet (see Docs/GraphWorkbench/USER-PROFILE-DEFERRED.md).
    /// </summary>
    public static class UserProfilePaths
    {
        public const string AppFolderName = "TruthCardGame";
        public const string ProfileDatabaseFileName = "UserProfile.db";

        /// <summary>%LocalApplicationData%/TruthCardGame (falls back to the user profile folder).</summary>
        public static string BaseDirectory()
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root))
            {
                root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            return Path.Combine(root, AppFolderName);
        }

        /// <summary>Full path of the (future) profile DB — never inside the install directory.</summary>
        public static string ProfileDatabasePath()
        {
            return Path.Combine(BaseDirectory(), ProfileDatabaseFileName);
        }
    }
}
