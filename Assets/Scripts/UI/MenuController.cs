using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TruthCardGame
{
    /// <summary>Main menu actions: start the game or open settings.</summary>
    public sealed class MenuController : MonoBehaviour
    {
        [SerializeField] private Button startButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private SettingsDialog settingsDialog;

        private void Awake()
        {
            if (startButton != null) startButton.onClick.AddListener(StartGame);
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
        }

        public void StartGame()
        {
            SceneManager.LoadScene("GameSetup");
        }

        public void OpenSettings()
        {
            settingsDialog.Show();
        }
    }
}
