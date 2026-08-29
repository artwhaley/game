using System;
using System.IO;
using Microsoft.Data.Sqlite;
using TruthCardGame.Core;
using TruthCardGame.Profile.Sqlite;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// Reads the separate UserProfile database for selection. Missing is a
    /// legitimate new-user state; an existing unreadable database is a hard
    /// error so a run can never silently use the wrong consent/inventory state.
    /// </summary>
    public static class UserProfileSelectionLoader
    {
        public static UserProfileSnapshot LoadSnapshot()
        {
            return LoadSnapshot(UserProfilePaths.ProfileDatabasePath());
        }

        public static UserProfileSnapshot LoadSnapshot(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Profile path is required.", nameof(path));
            if (!File.Exists(path)) return new UserProfileSnapshot();

            using (var connection = new SqliteConnection("Data Source=" + path))
            {
                connection.Open();
                ProfileStore.EnsureSchema(connection);
                return ProfileStore.Load(connection).ToSnapshot();
            }
        }
    }
}
