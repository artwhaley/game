using UnityEngine;
using UnityEngine.UI;

namespace TruthCardGame
{
    /// <summary>
    /// Settings dialog. The session-length modifier is obsolete (phase cadence
    /// is authored graph control now), so the dialog currently only hosts the
    /// close affordance; future settings slots land here.
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
