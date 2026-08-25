using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TruthCardGame
{
    /// <summary>
    /// Owns one game session: the player, the phase driver, and the draw loop.
    /// Lives in the Game scene. The UI reports through GamePanel.
    ///
    /// The CardExecutor is built fresh per draw with the driver's current
    /// phase filter; after each card the driver is told it completed, and it
    /// decides whether the phase advances or the session ends.
    /// </summary>
    public sealed class GameManager : MonoBehaviour, ICoroutineRunner, IPromptService
    {
        [SerializeField] private CardDeck deck;
        [SerializeField] private GamePanel panel;
        [SerializeField] private DirectorPlayer directorPlayer;
        [SerializeField] private string playerName = "Player";

        private Player _player;
        private GameServices _services;
        private SessionDriver _driver;
        private bool _busy;

        private void Awake()
        {
            if (deck == null)
            {
                Debug.LogError("[TruthCardGame] GameManager has no deck assigned. Rebuild with TruthCardGame → Build Scenes.");
                return;
            }
            if (SessionConfig.SelectedSession == null)
            {
                Debug.LogError("[TruthCardGame] No session selected. Pick one on the setup screen (TruthCardGame → Build Scenes).");
                return;
            }

            _player = new Player(playerName);
            _services = new GameServices(runner: this, prompts: this, cutscene: directorPlayer);
            _driver = new SessionDriver(SessionConfig.SelectedSession);
        }

        private void Start()
        {
            DrawNextCard();
        }

        /// <summary>Draws and executes the next card for the current phase, unless one is still running.</summary>
        public void DrawNextCard()
        {
            if (_busy || _driver == null || _driver.IsComplete) return;

            var executor = new CardExecutor(deck, this, _driver.CurrentMustInclude, _driver.CurrentMustExclude);
            if (executor.TryDrawCard(out var card))
            {
                _busy = true;
                panel.ShowDrawing(card);
                StartCoroutine(RunCard(card, executor));
            }
            else
            {
                // No card matches this phase's filter — advance early and try the next phase.
                _driver.OnNoMatchingCard();
                if (_driver.IsComplete)
                {
                    CompleteSession();
                }
                else
                {
                    DrawNextCard();
                }
            }
        }

        private IEnumerator RunCard(Card card, CardExecutor executor)
        {
            var context = new GameContext(_player, _services);
            yield return executor.ExecuteCard(card, context);
            panel.ShowDone(card);
            _busy = false;

            _driver.OnCardCompleted();
            if (_driver.IsComplete)
            {
                CompleteSession();
            }
        }

        /// <summary>Session finished — show it and head back to the menu.</summary>
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

        public void ReturnToMenu()
        {
            SceneManager.LoadScene("MainMenu");
        }

        public void StartRoutine(IEnumerator routine)
        {
            StartCoroutine(routine);
        }

        // ---------- IPromptService ----------

        public CustomYieldInstruction Ask(string prompt, IReadOnlyList<string> options, Action<int> onChosen)
        {
            var handle = new PromptHandle();
            panel.ShowPrompt(prompt, options, i =>
            {
                handle.Resolve();
                onChosen?.Invoke(i);
            });
            return handle;
        }
    }
}