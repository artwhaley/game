using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using TruthCardGame.Core;

namespace TruthCardGame
{
    /// <summary>
    /// Thin Unity host around the portable GameSessionEngine. Converts the
    /// selected session/deck once, builds host services, forwards Continue to
    /// RunUntilYieldAsync (plus the automatic first run at start), and
    /// owns presentation-only behavior: the 1.6 s completion beat and menu
    /// return. No game rules live here — Core owns progression, eligibility,
    /// action sequencing, and completion.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        [SerializeField] private CardDeck deck;
        [SerializeField] private GamePanel panel;
        [SerializeField] private DirectorPlayer directorPlayer;

        private CutsceneBindingRegistry _cutsceneRegistry;
        private GameSessionEngine _engine;
        private CancellationTokenSource _lifetimeCts;
        private bool _waitingForContinue;
        private string _currentCardTitle;

        private void Awake()
        {
            if (deck == null)
            {
                Debug.LogError("[TruthCardGame] GameManager has no deck assigned. Rebuild with TruthCardGame → Build Scenes.");
                return;
            }
            if (SessionConfig.SelectedSession == null)
            {
                Debug.LogError("[TruthCardGame] No session selected. Pick one on the setup screen.");
                return;
            }

            _lifetimeCts = new CancellationTokenSource();
            _cutsceneRegistry = new CutsceneBindingRegistry();
            if (directorPlayer != null)
            {
                directorPlayer.Bind(_cutsceneRegistry);
            }

            // Cutscene service stays optional (Core has tested missing-service
            // no-op behavior); scenes without a DirectorPlayer must still run.
            var services = new CoreServices(
                delay: new UnityGameDelay(),
                log: new UnityGameLog(),
                prompts: new UnityPromptService(panel),
                cutscene: directorPlayer);

            // Build the portable reference graph once from the selected
            // session + deck; Core executes this in-memory snapshot, never SQL.
            var builder = new UnityContentGraphBuilder(_cutsceneRegistry);
            var content = builder.Build(SessionConfig.SelectedSession, deck);

            // Default spawn: temperatures start at their authored defaults
            // (Happiness 50) unless a host override is supplied.
            _engine = new GameSessionEngine(content, SessionConfig.SelectedSession.Id, services);

            _engine.CardStarted += card =>
            {
                _currentCardTitle = card.Title;
                panel.ShowDrawing(card.Title);
            };
            _engine.CardFinished += card =>
            {
                _currentCardTitle = card.Title;
                panel.ShowDone(card.Title);
            };
        }

        private void Start()
        {
            // The first run starts automatically; authored WaitForContinue
            // instances are the only player-paced boundary.
            _waitingForContinue = false;
            RunAdvance();
        }

        /// <summary>User-triggered Continue; forwarded to Core orchestration.</summary>
        public void DrawNextCard()
        {
            RunAdvance();
        }

        private async void RunAdvance()
        {
            if (_engine == null || _lifetimeCts == null) return;

            try
            {
                var result = _waitingForContinue
                    ? await _engine.ContinueAsync(_lifetimeCts.Token)
                    : await _engine.RunUntilYieldAsync(_lifetimeCts.Token);
                _waitingForContinue = result.Kind == AdvanceResultKind.WaitForContinue;
                if (result.Kind == AdvanceResultKind.WaitForContinue)
                {
                    // The card stays on screen while the player decides; the
                    // status line is what signals the wait.
                    panel.ShowWaitingForContinue(_currentCardTitle);
                }
                else if (result.Kind == AdvanceResultKind.SessionCompleted)
                {
                    _waitingForContinue = false;
                    CompleteSession();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during teardown/scene change.
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public void ReturnToMenu()
        {
            SceneManager.LoadScene("MainMenu");
        }

        /// <summary>Session finished — show it, then head back to the menu (host presentation only).</summary>
        private void CompleteSession()
        {
            panel.ShowSessionComplete();
            StartCoroutine(ReturnToMenuAfterDelay(1.6f));
        }

        private IEnumerator ReturnToMenuAfterDelay(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            ReturnToMenu();
        }

        private void OnDestroy()
        {
            _lifetimeCts?.Cancel();
            _lifetimeCts?.Dispose();
            _lifetimeCts = null;
        }
    }
}
