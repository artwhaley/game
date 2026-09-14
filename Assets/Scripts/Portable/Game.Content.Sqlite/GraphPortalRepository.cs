using System;
using System.Collections.Generic;
using System.Data.Common;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>
    /// WPF-only persistence for matched bridge endpoints. This repository never
    /// touches the portable snapshot and keys every pair to one real graph edge.
    /// </summary>
    public sealed class GraphPortalPairDefinition
    {
        public string Id { get; set; }
        public string GraphId { get; set; }
        public string EdgeId { get; set; }
        public string Label { get; set; }
        public int ColorSlot { get; set; }
        public double SourceX { get; set; }
        public double SourceY { get; set; }
        public double TargetX { get; set; }
        public double TargetY { get; set; }
    }

    public static class GraphPortalRepository
    {
        public static List<GraphPortalPairDefinition> LoadSession(DbConnection connection, string sessionId)
        {
            return Load(connection, "wpf_session_edge_portal_pair", "session_id", sessionId);
        }

        public static List<GraphPortalPairDefinition> LoadPhase(DbConnection connection, string phaseId)
        {
            return Load(connection, "wpf_phase_edge_portal_pair", "phase_id", phaseId);
        }

        private static List<GraphPortalPairDefinition> Load(DbConnection connection, string table,
            string ownerColumn, string ownerId)
        {
            RequireOwner(ownerId);
            var result = new List<GraphPortalPairDefinition>();
            Sql.QueryAll(connection,
                $"SELECT id, {ownerColumn}, edge_id, label, color_slot, source_x, source_y, target_x, target_y " +
                $"FROM {table} WHERE {ownerColumn} = @owner ORDER BY label, id;",
                reader =>
                {
                    var pair = new GraphPortalPairDefinition
                    {
                        Id = reader.GetString(0),
                        GraphId = reader.GetString(1),
                        EdgeId = reader.GetString(2),
                        Label = reader.GetString(3),
                        ColorSlot = reader.GetInt32(4),
                        SourceX = reader.GetDouble(5),
                        SourceY = reader.GetDouble(6),
                        TargetX = reader.GetDouble(7),
                        TargetY = reader.GetDouble(8),
                    };
                    Validate(pair, table);
                    result.Add(pair);
                }, ("owner", ownerId));
            return result;
        }

        public static GraphPortalPairDefinition GetByEdge(DbConnection connection, string graphKind,
            string graphId, string edgeId)
        {
            RequireOwner(graphId);
            if (string.IsNullOrWhiteSpace(edgeId)) throw new ArgumentException("Edge id required.", nameof(edgeId));
            var table = Table(graphKind);
            GraphPortalPairDefinition result = null;
            Sql.QueryAll(connection,
                $"SELECT id, {(graphKind == "session" ? "session_id" : "phase_id")}, edge_id, label, color_slot, source_x, source_y, target_x, target_y " +
                $"FROM {table} WHERE edge_id = @edge;",
                reader => result = new GraphPortalPairDefinition
                {
                    Id = reader.GetString(0), GraphId = reader.GetString(1), EdgeId = reader.GetString(2),
                    Label = reader.GetString(3), ColorSlot = reader.GetInt32(4),
                    SourceX = reader.GetDouble(5), SourceY = reader.GetDouble(6),
                    TargetX = reader.GetDouble(7), TargetY = reader.GetDouble(8),
                }, ("edge", edgeId));
            if (result != null)
            {
                Validate(result, table);
                if (!string.Equals(result.GraphId, graphId, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Portal pair '{result.Id}' edge '{edgeId}' belongs to graph '{result.GraphId}', not '{graphId}'.");
            }
            return result;
        }

        public static void Create(DbConnection connection, string graphKind, GraphPortalPairDefinition pair)
        {
            var table = Table(graphKind);
            Validate(pair, table);
            var ownerColumn = graphKind == "session" ? "session_id" : "phase_id";
            Sql.Execute(connection, null,
                $"INSERT INTO {table} (id, {ownerColumn}, edge_id, label, color_slot, source_x, source_y, target_x, target_y) " +
                "VALUES (@id, @owner, @edge, @label, @color, @sx, @sy, @tx, @ty);",
                ("id", pair.Id), ("owner", pair.GraphId), ("edge", pair.EdgeId), ("label", pair.Label.Trim()),
                ("color", pair.ColorSlot), ("sx", pair.SourceX), ("sy", pair.SourceY),
                ("tx", pair.TargetX), ("ty", pair.TargetY));
        }

        public static void Restore(DbConnection connection, string graphKind, GraphPortalPairDefinition pair)
        {
            Create(connection, graphKind, pair);
        }

        public static void UpdateSource(DbConnection connection, string graphKind, string pairId, double x, double y)
        {
            UpdatePoint(connection, graphKind, pairId, "source_x", "source_y", x, y);
        }

        public static void UpdateTarget(DbConnection connection, string graphKind, string pairId, double x, double y)
        {
            UpdatePoint(connection, graphKind, pairId, "target_x", "target_y", x, y);
        }

        private static void UpdatePoint(DbConnection connection, string graphKind, string pairId,
            string xColumn, string yColumn, double x, double y)
        {
            if (string.IsNullOrWhiteSpace(pairId)) throw new ArgumentException("Portal pair id required.", nameof(pairId));
            ValidateCoordinate(x, "x");
            ValidateCoordinate(y, "y");
            var table = Table(graphKind);
            Sql.Execute(connection, null,
                $"UPDATE {table} SET {xColumn} = @x, {yColumn} = @y WHERE id = @id;",
                ("x", x), ("y", y), ("id", pairId));
        }

        public static void Delete(DbConnection connection, string graphKind, string pairId)
        {
            if (string.IsNullOrWhiteSpace(pairId)) throw new ArgumentException("Portal pair id required.", nameof(pairId));
            Sql.Execute(connection, null,
                $"DELETE FROM {Table(graphKind)} WHERE id = @id;", ("id", pairId));
        }

        private static string Table(string graphKind)
        {
            if (string.Equals(graphKind, "session", StringComparison.OrdinalIgnoreCase))
                return "wpf_session_edge_portal_pair";
            if (string.Equals(graphKind, "phase", StringComparison.OrdinalIgnoreCase))
                return "wpf_phase_edge_portal_pair";
            throw new ArgumentException("Graph kind must be 'session' or 'phase'.", nameof(graphKind));
        }

        private static void RequireOwner(string ownerId)
        {
            if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("Graph id required.", nameof(ownerId));
        }

        private static void Validate(GraphPortalPairDefinition pair, string table)
        {
            if (pair == null) throw new ArgumentNullException(nameof(pair));
            if (string.IsNullOrWhiteSpace(pair.Id)) throw new InvalidOperationException($"{table}: portal pair id is empty.");
            if (string.IsNullOrWhiteSpace(pair.GraphId)) throw new InvalidOperationException($"{table}: graph id is empty.");
            if (string.IsNullOrWhiteSpace(pair.EdgeId)) throw new InvalidOperationException($"{table}: edge id is empty.");
            if (string.IsNullOrWhiteSpace(pair.Label)) throw new InvalidOperationException($"{table}: portal label is empty.");
            if (pair.ColorSlot < 0 || pair.ColorSlot > 7) throw new InvalidOperationException($"{table}: color slot must be 0 through 7.");
            ValidateCoordinate(pair.SourceX, "source_x");
            ValidateCoordinate(pair.SourceY, "source_y");
            ValidateCoordinate(pair.TargetX, "target_x");
            ValidateCoordinate(pair.TargetY, "target_y");
        }

        private static void ValidateCoordinate(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidOperationException($"Portal coordinate '{name}' must be finite.");
        }
    }
}
