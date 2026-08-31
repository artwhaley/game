using System.Collections.Generic;
using System.Data.Common;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>Seeds the v9 hierarchy from the existing slash-delimited Card paths.</summary>
    internal static class Migration9Transform
    {
        public static void Transform(DbConnection connection, DbTransaction transaction)
        {
            var paths = new List<string>();
            Sql.QueryAll(connection, transaction,
                "SELECT DISTINCT folder_path FROM card WHERE folder_path IS NOT NULL AND trim(folder_path) <> '';",
                reader => paths.Add(reader.GetString(0)));

            foreach (var path in paths)
            {
                CardFolderRepository.EnsurePath(connection, transaction, path);
            }
        }
    }
}
