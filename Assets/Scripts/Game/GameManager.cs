using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TruthCardGame
{
    /// <summary>
    /// Owns one game session: the player, the executor, and the draw loop.
    /// Lives in the Game scene. The UI reports through GamePanel.
    /// </summary>
    public sealed class GameManager : MonoBehaviour, ICoroutineRunner
    {
        [SerializeField] private CardDeck deck;
        [SerializeField] private GamePanel panel;
        [SerializeField] private string playerName = "Player";

        private Player _player;
        private CardExecutor _executor;
        private bool _busy;

        private void Awake()
        {
            if (deck == null)
            {
                Debug.LogError("[TruthCardGame] GameManager has no deck assigned. Rebuild with TruthCardGame → Build Scenes.");
                return;
            }
            _player = new Player(playerName);
            _executor = new CardExecutor(deck, this, SessionConfig.MustIncludeTags, SessionConfig.MustExcludeTags);
        }

        private void Start()
        {
            DrawNextCard();
        }

        /// <summary>Draws and executes the next card, unless one is still running.</summary>
        public void DrawNextCard()
        {
            if (_busy || _executor == null) return;
            if (_executor.TryDrawCard(out var card))
            {
                _busy = true;
                panel.ShowDrawing(card);
                StartCoroutine(RunCard(card));
            }
            else
            {
                panel.ShowNoCards();
            }
        }

        private IEnumerator RunCard(Card card)
        {
            var context = new GameContext(_player);
            yield return _executor.ExecuteCard(card, context);
            panel.ShowDone(card);
            _busy = false;
        }

        public void ReturnToMenu()
        {
            SceneManager.LoadScene("MainMenu");
        }

        public void StartRoutine(IEnumerator routine)
        {
            StartCoroutine(routine);
        }
    }
}
