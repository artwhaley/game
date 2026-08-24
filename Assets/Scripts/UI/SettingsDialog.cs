using UnityEngine;
using UnityEngine.UI;

namespace TruthCardGame
{
    /// <summary>
    /// Stub settings dialog — reachable and closable, contents come later.
    /// The scene builder wires root (the overlay) and starts it inactive.
    /// </summary>
    public sealed class SettingsDialog : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Button closeButton;

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        public void Show()
        {
            root.SetActive(true);
        }

        public void Close()
        {
            root.SetActive(false);
        }
    }
}
