using System;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using TruthCardGame.Content;
using TruthCardGame.Content.Sqlite;
using TruthCardGame.Core;
using UnityEngine;

namespace TruthCardGame.Performance
{
    /// <summary>
    /// Ticket 04 visual smoke harness: drives the real Core planning stack —
    /// PerformanceDirector + PerformancePlanner + UnityPerformanceHost — with
    /// the V1 Conversation Performance Event loaded from the canonical content
    /// database. This is the same code path the Perform action uses at runtime;
    /// it simply gives the developer buttons so staging can be seen before the
    /// full session wiring (Ticket 04 slice 3+) exists.
    ///
    /// Deliberately not part of the shipped game loop: it is a disposable
    /// showcase panel wired by the editor scene setup, in the spirit of the
    /// Phase 00 rig showcase. It owns no game rules and mutates nothing — the
    /// database is opened read-only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerformanceSmokePanel : MonoBehaviour
    {
        private const string EventId = "perf-event-playful-tease";
        private const string DatabaseRelativePath = "Content/GameContent.db";

        private UnityPerformanceHost _host;
        private PerformanceDirector _director;
        private ConversationPerformanceEventDefinition _performanceEvent;
        private CancellationTokenSource _lifetimeCts;
        private Vector2 _scroll;
        private bool _busy;
        private string _lastOutcome = "(idle)";

        public void Bind(UnityPerformanceHost sourceHost)
        {
            _host = sourceHost;
        }

        private void Start()
        {
            _lifetimeCts = new CancellationTokenSource();
            if (_host == null)
            {
                _lastOutcome = "no performance host bound";
                return;
            }

            try
            {
                var rng = new UnityRandomSource();
                _director = new PerformanceDirector(
                    _host.Catalog, _host, rng, new UnityGameLog());
                _performanceEvent = LoadEventReadOnly();
                _lastOutcome = "ready: event '" + EventId + "' loaded from the content database";
            }
            catch (Exception ex)
            {
                _lastOutcome = "startup failed: " + ex.Message;
                Debug.LogError("[PERFORMANCE] Smoke panel startup failed: " + ex);
            }
        }

        /// <summary>
        /// Loads the event through the real repository code path, read-only.
        /// Missing database or missing event are loud, named failures — a smoke
        /// harness that silently plans against a default event would prove
        /// nothing about the authored content.
        /// </summary>
        private static ConversationPerformanceEventDefinition LoadEventReadOnly()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            var databasePath = Path.Combine(projectRoot, DatabaseRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(databasePath))
            {
                throw new FileNotFoundException(
                    "canonical content database not found at " + databasePath, databasePath);
            }

            // We vendor Microsoft.Data.Sqlite.Core, so nothing initializes
            // SQLitePCLRaw on our behalf.
            Batteries_V2.Init();

            using (var connection = new SqliteConnection(
                       "Data Source=" + databasePath + ";Mode=ReadOnly;Pooling=False"))
            {
                connection.Open();
                var performanceEvent = PerformanceCatalogRepository.ReadEvent(connection, EventId);
                if (performanceEvent == null)
                {
                    throw new InvalidOperationException(
                        "performance event '" + EventId + "' is not in the content database; " +
                        "run dotnet run --project DotNet/Game.Content.Sqlite.Tool -- --author-v1-performance");
                }
                return performanceEvent;
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 340, 360, 420), GUI.skin.box);
            GUILayout.Label("Ticket 04: semantic performance smoke");
            GUILayout.Label("Core plans; Unity stages; gaze on the green marker");

            GUI.enabled = !_busy && _director != null && _performanceEvent != null;
            if (GUILayout.Button("Perform (plan + stage via Core)")) _ = RunAsync(token => _director.PerformAsync(_performanceEvent, token));
            GUILayout.Space(6);
            if (GUILayout.Button("Refresh acting (dialogue-start seam)")) _ = RunAsync(token => _director.RefreshAtDialogueStartAsync(token));
            GUILayout.Space(6);
            if (GUILayout.Button("Stop")) _ = RunAsync(token => _director.StopAsync(token));
            GUI.enabled = true;

            GUILayout.Space(8);
            GUILayout.Label("Last outcome:");
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(150));
            GUILayout.Label(_lastOutcome);
            GUILayout.EndScrollView();

            GUILayout.Space(4);
            GUILayout.Label("Host decisions (newest last):");
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(80));
            if (_host != null)
            {
                foreach (var decision in _host.Decisions) GUILayout.Label(decision);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private async Task RunAsync(Func<CancellationToken, Task> run)
        {
            if (_busy || _lifetimeCts == null) return;
            _busy = true;
            try
            {
                await run(_lifetimeCts.Token);
                _lastOutcome = "accepted at " + DateTime.UtcNow.ToString("HH:mm:ss") +
                               "; committed state: " + _director.CommittedState +
                               "; acting: " + _director.Acting;
            }
            catch (OperationCanceledException)
            {
                _lastOutcome = "cancelled";
            }
            catch (Exception ex)
            {
                _lastOutcome = "refused/failed: " + ex.Message;
                Debug.LogWarning("[PERFORMANCE] Smoke panel: " + ex.Message);
            }
            finally
            {
                _busy = false;
            }
        }

        private void OnDestroy()
        {
            _lifetimeCts?.Cancel();
            _lifetimeCts?.Dispose();
            _lifetimeCts = null;
        }
    }
}
