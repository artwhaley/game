using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>Transactional persistence for the Cards library folder tree.</summary>
    public static class CardFolderRepository
    {
        public static List<CardFolderDefinition> Load(DbConnection connection)
        {
            var folders = new List<CardFolderDefinition>();
            Sql.QueryAll(connection,
                "SELECT id, name, parent_id, path, sort_order FROM card_folder ORDER BY path;",
                reader => folders.Add(new CardFolderDefinition
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    ParentId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Path = reader.GetString(3),
                    SortOrder = reader.GetInt32(4)
                }));
            return folders;
        }

        public static void Create(DbConnection connection, string id, string name, string parentId)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Create(connection, transaction, id, name, parentId);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        internal static void Create(DbConnection connection, DbTransaction transaction,
            string id, string name, string parentId)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Folder id required.", nameof(id));
            var cleanName = NormalizeName(name);
            var parentPath = "";
            if (!string.IsNullOrEmpty(parentId))
            {
                parentPath = PathForId(connection, transaction, parentId);
                if (parentPath == null) throw new InvalidOperationException($"Parent folder '{parentId}' not found.");
            }
            var path = string.IsNullOrEmpty(parentPath) ? cleanName : parentPath + "/" + cleanName;
            Sql.Execute(connection, transaction,
                "INSERT INTO card_folder (id, parent_id, name, path, sort_order) VALUES (@id, @parent, @name, @path, 0);",
                ("id", id), ("parent", (object)parentId ?? DBNull.Value), ("name", cleanName), ("path", path));
        }

        internal static void CreateExact(DbConnection connection, DbTransaction transaction, CardFolderDefinition folder)
        {
            if (folder == null) throw new ArgumentNullException(nameof(folder));
            var name = NormalizeName(folder.Name);
            var path = NormalizePath(folder.Path);
            if (path.Length == 0) throw new ArgumentException("Folder path required.", nameof(folder));
            Sql.Execute(connection, transaction,
                "INSERT INTO card_folder (id, parent_id, name, path, sort_order) VALUES (@id, @parent, @name, @path, @sort);",
                ("id", folder.Id), ("parent", (object)folder.ParentId ?? DBNull.Value),
                ("name", name), ("path", path), ("sort", folder.SortOrder));
        }

        public static void Rename(DbConnection connection, string folderId, string name)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Rename(connection, transaction, folderId, name);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        internal static void Rename(DbConnection connection, DbTransaction transaction, string folderId, string name)
        {
            var current = Get(connection, transaction, folderId);
            if (current == null) throw new InvalidOperationException($"Folder '{folderId}' not found.");
            var cleanName = NormalizeName(name);
            var parentPath = string.IsNullOrEmpty(current.ParentId) ? "" : PathForId(connection, transaction, current.ParentId);
            var newPath = string.IsNullOrEmpty(parentPath) ? cleanName : parentPath + "/" + cleanName;
            string collision = null;
            Sql.QueryAll(connection, transaction,
                "SELECT id FROM card_folder WHERE path = @path COLLATE NOCASE AND id <> @id;",
                reader => collision = reader.GetString(0), ("path", newPath), ("id", folderId));
            if (collision != null) throw new InvalidOperationException($"A folder named '{cleanName}' already exists there.");

            Sql.Execute(connection, transaction,
                "UPDATE card_folder SET path = @new || substr(path, length(@old) + 1) " +
                "WHERE path = @old OR path LIKE @prefix;",
                ("new", newPath), ("old", current.Path), ("prefix", current.Path + "/%"));
            Sql.Execute(connection, transaction,
                "UPDATE card_folder SET name = @name WHERE id = @id;",
                ("name", cleanName), ("id", folderId));
            Sql.Execute(connection, transaction,
                "UPDATE card SET folder_path = @new || substr(folder_path, length(@old) + 1) " +
                "WHERE folder_path = @old OR folder_path LIKE @prefix;",
                ("new", newPath), ("old", current.Path), ("prefix", current.Path + "/%"));
        }

        public static void Delete(DbConnection connection, string folderId)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    Delete(connection, transaction, folderId);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        internal static void Delete(DbConnection connection, DbTransaction transaction, string folderId)
        {
            var current = Get(connection, transaction, folderId);
            if (current == null) throw new InvalidOperationException($"Folder '{folderId}' not found.");
            if (Count(connection, transaction, "SELECT COUNT(*) FROM card_folder WHERE parent_id = @id;", ("id", folderId)) > 0 ||
                Count(connection, transaction,
                    "SELECT COUNT(*) FROM card WHERE folder_path = @path OR folder_path LIKE @prefix;",
                    ("path", current.Path), ("prefix", current.Path + "/%")) > 0)
            {
                throw new InvalidOperationException("Folder must be empty before it can be deleted. Move its cards and child folders first.");
            }
            Sql.Execute(connection, transaction, "DELETE FROM card_folder WHERE id = @id;", ("id", folderId));
        }

        /// <summary>Lists the folder and every descendant, shallowest first.</summary>
        public static List<CardFolderDefinition> LoadSubtree(DbConnection connection, string folderId)
        {
            var subtree = new List<CardFolderDefinition>();
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    LoadSubtree(connection, transaction, folderId, subtree);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
            return subtree;
        }

        internal static void LoadSubtree(DbConnection connection, DbTransaction transaction,
            string folderId, List<CardFolderDefinition> subtree)
        {
            var root = Get(connection, transaction, folderId);
            if (root == null) throw new InvalidOperationException($"Folder '{folderId}' not found.");
            subtree.Add(root);
            var childIds = new List<string>();
            Sql.QueryAll(connection, transaction,
                "SELECT id FROM card_folder WHERE parent_id = @id ORDER BY sort_order, path;",
                reader => childIds.Add(reader.GetString(0)), ("id", folderId));
            foreach (var childId in childIds) LoadSubtree(connection, transaction, childId, subtree);
        }

        /// <summary>Deletes the folder subtree; contained cards are either deleted
        /// or moved to the unassigned root, per <paramref name="deleteCards"/>.</summary>
        public static void DeleteSubtree(DbConnection connection, string folderId, bool deleteCards)
        {
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    DeleteSubtree(connection, transaction, folderId, deleteCards);
                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        internal static void DeleteSubtree(DbConnection connection, DbTransaction transaction,
            string folderId, bool deleteCards)
        {
            var subtree = new List<CardFolderDefinition>();
            LoadSubtree(connection, transaction, folderId, subtree);
            var root = subtree[0];
            var prefix = root.Path + "/%";
            if (deleteCards)
            {
                var cardIds = new List<string>();
                Sql.QueryAll(connection, transaction,
                    "SELECT id FROM card WHERE folder_path = @path OR folder_path LIKE @prefix;",
                    reader => cardIds.Add(reader.GetString(0)), ("path", root.Path), ("prefix", prefix));
                foreach (var cardId in cardIds) CardRepository.Delete(connection, transaction, cardId);
            }
            else
            {
                // Relocate contained cards to the unassigned root.
                Sql.Execute(connection, transaction,
                    "UPDATE card SET folder_path = '' WHERE folder_path = @path OR folder_path LIKE @prefix;",
                    ("path", root.Path), ("prefix", prefix));
            }
            // Delete deepest-first: parent_id has ON DELETE RESTRICT, so children
            // must go before their parents (a single prefix DELETE may pick the
            // parent row first and trip the immediate FK check).
            for (var index = subtree.Count - 1; index >= 0; index--)
            {
                Sql.Execute(connection, transaction,
                    "DELETE FROM card_folder WHERE id = @id;", ("id", subtree[index].Id));
            }
        }

        /// <summary>Restores an entire captured subtree shallowest-first (parents
        /// exist before children, honoring parent_id's FK), then the cards.</summary>
        internal static void RestoreSubtree(DbConnection connection, DbTransaction transaction,
            IReadOnlyList<CardFolderDefinition> subtreeSnapshot)
        {
            if (subtreeSnapshot == null) throw new ArgumentNullException(nameof(subtreeSnapshot));
            for (var index = 0; index < subtreeSnapshot.Count; index++)
                CreateExact(connection, transaction, subtreeSnapshot[index]);
        }

        /// <summary>Moves a folder subtree under a new parent folder (or the root
        /// when newParentId is null). Rewrites descendant folder/card paths.
        /// Throws on name collisions and when the target is inside the moved
        /// subtree. Runs inside the caller's transaction.</summary>
        internal static void MoveSubtree(DbConnection connection, DbTransaction transaction,
            string folderId, string newParentId)
        {
            var current = Get(connection, transaction, folderId);
            if (current == null) throw new InvalidOperationException($"Folder '{folderId}' not found.");

            string newParentPath = "";
            if (!string.IsNullOrEmpty(newParentId))
            {
                if (string.Equals(newParentId, folderId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A folder cannot be moved into itself.");
                newParentPath = PathForId(connection, transaction, newParentId);
                if (newParentPath == null) throw new InvalidOperationException($"Parent folder '{newParentId}' not found.");
                if (newParentPath == current.Path ||
                    (newParentPath.Length > current.Path.Length &&
                     newParentPath.Substring(0, current.Path.Length + 1).Equals(current.Path + "/", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("A folder cannot be moved into one of its own subfolders.");
            }

            var newPath = string.IsNullOrEmpty(newParentPath) ? current.Name : newParentPath + "/" + current.Name;

            string collision = null;
            Sql.QueryAll(connection, transaction,
                "SELECT id FROM card_folder WHERE path = @path COLLATE NOCASE AND id <> @id;",
                reader => collision = reader.GetString(0), ("path", newPath), ("id", folderId));
            if (collision != null) throw new InvalidOperationException($"A folder named '{current.Name}' already exists there.");

            Sql.Execute(connection, transaction,
                "UPDATE card_folder SET path = @new || substr(path, length(@old) + 1) " +
                "WHERE path = @old OR path LIKE @prefix;",
                ("new", newPath), ("old", current.Path), ("prefix", current.Path + "/%"));
            Sql.Execute(connection, transaction,
                "UPDATE card_folder SET parent_id = @parent WHERE id = @id;",
                ("parent", (object)newParentId ?? DBNull.Value), ("id", folderId));
            Sql.Execute(connection, transaction,
                "UPDATE card SET folder_path = @new || substr(folder_path, length(@old) + 1) " +
                "WHERE folder_path = @old OR folder_path LIKE @prefix;",
                ("new", newPath), ("old", current.Path), ("prefix", current.Path + "/%"));
        }

        internal static void RestoreCards(DbConnection connection, DbTransaction transaction,
            IReadOnlyList<CardDefinition> cardSnapshots)
        {
            if (cardSnapshots == null) return;
            foreach (var card in cardSnapshots) CardRepository.Create(connection, transaction, card);
        }

        internal static string EnsurePath(DbConnection connection, DbTransaction transaction, string folderPath)
        {
            var normalized = NormalizePath(folderPath);
            if (normalized.Length == 0) return "";

            var parentId = (string)null;
            var currentPath = "";
            foreach (var part in normalized.Split('/'))
            {
                currentPath = currentPath.Length == 0 ? part : currentPath + "/" + part;
                var id = IdForPath(connection, transaction, currentPath);
                if (id == null)
                {
                    id = "folder-" + Guid.NewGuid().ToString("N");
                    Sql.Execute(connection, transaction,
                        "INSERT INTO card_folder (id, parent_id, name, path, sort_order) VALUES (@id, @parent, @name, @path, 0);",
                        ("id", id), ("parent", (object)parentId ?? DBNull.Value),
                        ("name", part), ("path", currentPath));
                }
                parentId = id;
            }
            return normalized;
        }

        internal static void SetCardsFolder(DbConnection connection, DbTransaction transaction,
            IReadOnlyList<string> cardIds, string folderPath)
        {
            var normalized = EnsurePath(connection, transaction, folderPath);
            for (var i = 0; i < cardIds.Count; i++)
            {
                Sql.Execute(connection, transaction,
                    "UPDATE card SET folder_path = @folder WHERE id = @id;",
                    ("folder", normalized), ("id", cardIds[i]));
            }
        }

        internal static void SetCardsFolders(DbConnection connection, DbTransaction transaction,
            IReadOnlyList<string> cardIds, IReadOnlyList<string> folderPaths)
        {
            if (cardIds.Count != folderPaths.Count) throw new ArgumentException("Card and folder counts must match.");
            for (var i = 0; i < cardIds.Count; i++)
            {
                var normalized = EnsurePath(connection, transaction, folderPaths[i]);
                Sql.Execute(connection, transaction,
                    "UPDATE card SET folder_path = @folder WHERE id = @id;",
                    ("folder", normalized), ("id", cardIds[i]));
            }
        }

        internal static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            var parts = path.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => NormalizeName(part)).ToArray();
            return string.Join("/", parts);
        }

        internal static string NormalizeName(string name)
        {
            var clean = (name ?? "").Trim();
            if (clean.Length == 0) throw new ArgumentException("Folder name required.", nameof(name));
            if (clean == "." || clean == ".." || clean.IndexOf('/') >= 0 || clean.IndexOf('\\') >= 0)
                throw new ArgumentException("Folder names cannot contain path separators.", nameof(name));
            return clean;
        }

        private static CardFolderDefinition Get(DbConnection connection, DbTransaction transaction, string id)
        {
            CardFolderDefinition folder = null;
            Sql.QueryAll(connection, transaction,
                "SELECT id, name, parent_id, path, sort_order FROM card_folder WHERE id = @id;",
                reader => folder = new CardFolderDefinition
                {
                    Id = reader.GetString(0), Name = reader.GetString(1),
                    ParentId = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Path = reader.GetString(3), SortOrder = reader.GetInt32(4)
                }, ("id", id));
            return folder;
        }

        private static string PathForId(DbConnection connection, DbTransaction transaction, string id)
        {
            string path = null;
            Sql.QueryAll(connection, transaction, "SELECT path FROM card_folder WHERE id = @id;",
                reader => path = reader.GetString(0), ("id", id));
            return path;
        }

        private static string IdForPath(DbConnection connection, DbTransaction transaction, string path)
        {
            string id = null;
            Sql.QueryAll(connection, transaction, "SELECT id FROM card_folder WHERE path = @path COLLATE NOCASE;",
                reader => id = reader.GetString(0), ("path", path));
            return id;
        }

        private static long Count(DbConnection connection, DbTransaction transaction, string sql,
            params (string Name, object Value)[] parameters)
        {
            long count = 0;
            Sql.QueryAll(connection, transaction, sql, reader => count = reader.GetInt64(0), parameters);
            return count;
        }
    }
}
