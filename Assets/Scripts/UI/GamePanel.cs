using UnityEngine;
using UnityEngine.UI;

namespace TruthCardGame
{
    /// <summary>Game-screen view: current card title, status line, and action buttons.</summary>
    public sealed class GamePanel : MonoBehaviour
    {
        [SerializeField] private Text cardTitle;
        [SerializeField] private Text status;
        [SerializeField] private Button drawNextButton;
        [SerializeField] private Button menuButton;
        [SerializeField] private GameManager gameManager;

        private void Awake()
        {
            if (gameManager != null)
            {
                drawNextButton.onClick.AddListener(gameManager.DrawNextCard);
                menuButton.onClick.AddListener(gameManager.ReturnToMenu);
            }
        }

        public void ShowDrawing(Card card)
        {
            cardTitle.text = card.Title;
            status.text = "Executing…";
            drawNextButton.interactable = false;
        }

        public void ShowDone(Card card)
        {
            status.text = "Done.";
            drawNextButton.interactable = true;
        }

        public void ShowNoCards()
        {
            cardTitle.text = "No matching cards";
            status.text = "Every tag is excluded or the deck is empty.";
            drawNextButton.interactable = false;
        }
    }
}
