using System;
using System.Collections.Generic;
using System.Data.Common;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// Authoring-only layout persistence (tickets 13+): node coordinates in
    /// wpf_session_node_layout / wpf_phase_node_layout and viewport pan/zoom in
    /// wpf_viewport_state. These tables are never part of snapshot content; the
    /// Core VM ignores them entirely. Rows are upserted (INSERT OR REPLACE) so
    /// dragging a node or panning the canvas is an idempotent write.
    /// </summary>
    public static class AuthoringLayoutRepository
    {
        // ---- node positions ----

        public static void SaveSessionNodePosition(DbConnection connection, string sessionId, string nodeId, double x, double y)
        {
            SaveNodePosition(connection, "wpf_session_node_layout", "session_id", sessionId, nodeId, x, y);
        }

        public static void SavePhaseNodePosition(DbConnection connection, string phaseId, string nodeId, double x, double y)
        {
            SaveNodePosition(connection, "wpf_phase_node_layout", "phase_id", phaseId, nodeId, x, y);
        }

        private static void SaveNodePosition(DbConnection connection, string table, string parentColumn, string parentId, string nodeId, double x, double y)
        {
            if (string.IsNullOrEmpty(parentId)) throw new ArgumentException("Parent id required.", nameof(parentId));
            if (string.IsNullOrEmpty(nodeId)) throw new ArgumentException("Node id required.", nameof(nodeId));

            Sql.Execute(connection, null,
                $"INSERT OR REPLACE INTO {table} ({parentColumn}, node_id, x, y) VALUES (@parent, @node, @x, @y);",
                ("parent", parentId), ("node", nodeId), ("x", x), ("y", y));
        }

        public static Dictionary<string, (double X, double Y)> LoadSessionNodePositions(DbConnection connection, string sessionId)
        {
            return LoadNodePositions(connection, "wpf_session_node_layout", "session_id", sessionId);
        }

        public static Dictionary<string, (double X, double Y)> LoadPhaseNodePositions(DbConnection connection, string phaseId)
        {
            return LoadNodePositions(connection, "wpf_phase_node_layout", "phase_id", phaseId);
        }

        private static Dictionary<string, (double X, double Y)> LoadNodePositions(DbConnection connection, string table, string parentColumn, string parentId)
        {
            var result = new Dictionary<string, (double X, double Y)>();
            Sql.QueryAll(connection,
                $"SELECT node_id, x, y FROM {table} WHERE {parentColumn} = @parent;",
                reader => result[reader.GetString(0)] = (reader.GetDouble(1), reader.GetDouble(2)),
                ("parent", parentId));
            return result;
        }

        /// <summary>Single node position or null when not yet placed (undo capture).</summary>
        public static (double X, double Y)? GetSessionNodePosition(DbConnection connection, string sessionId, string nodeId)
        {
            return GetNodePosition(connection, "wpf_session_node_layout", "session_id", sessionId, nodeId);
        }

        /// <summary>Single node position or null when not yet placed (undo capture).</summary>
        public static (double X, double Y)? GetPhaseNodePosition(DbConnection connection, string phaseId, string nodeId)
        {
            return GetNodePosition(connection, "wpf_phase_node_layout", "phase_id", phaseId, nodeId);
        }

        private static (double X, double Y)? GetNodePosition(DbConnection connection, string table, string parentColumn, string parentId, string nodeId)
        {
            (double X, double Y)? result = null;
            Sql.QueryAll(connection,
                $"SELECT x, y FROM {table} WHERE {parentColumn} = @parent AND node_id = @node;",
                reader => result = (reader.GetDouble(0), reader.GetDouble(1)),
                ("parent", parentId), ("node", nodeId));
            return result;
        }

        public static void DeleteSessionNodePosition(DbConnection connection, string sessionId, string nodeId)
        {
            Sql.Execute(connection, null,
                "DELETE FROM wpf_session_node_layout WHERE session_id = @parent AND node_id = @node;",
                ("parent", sessionId), ("node", nodeId));
        }

        public static void DeletePhaseNodePosition(DbConnection connection, string phaseId, string nodeId)
        {
            Sql.Execute(connection, null,
                "DELETE FROM wpf_phase_node_layout WHERE phase_id = @parent AND node_id = @node;",
                ("parent", phaseId), ("node", nodeId));
        }

        // ---- viewport ----

        public static void SaveViewport(DbConnection connection, string scopeKind, string scopeId, double zoom, double x, double y)
        {
            if (string.IsNullOrEmpty(scopeKind)) throw new ArgumentException("Scope kind required.", nameof(scopeKind));
            if (string.IsNullOrEmpty(scopeId)) throw new ArgumentException("Scope id required.", nameof(scopeId));

            Sql.Execute(connection, null,
                "INSERT OR REPLACE INTO wpf_viewport_state (scope_kind, scope_id, zoom, x, y) VALUES (@kind, @scope, @zoom, @x, @y);",
                ("kind", scopeKind), ("scope", scopeId), ("zoom", zoom), ("x", x), ("y", y));
        }

        /// <summary>Returns (zoom, x, y) or null when no viewport row exists yet.</summary>
        public static (double Zoom, double X, double Y)? LoadViewport(DbConnection connection, string scopeKind, string scopeId)
        {
            (double Zoom, double X, double Y)? result = null;
            Sql.QueryAll(connection,
                "SELECT zoom, x, y FROM wpf_viewport_state WHERE scope_kind = @kind AND scope_id = @scope;",
                reader => result = (reader.GetDouble(0), reader.GetDouble(1), reader.GetDouble(2)),
                ("kind", scopeKind), ("scope", scopeId));
            return result;
        }
    }
}
