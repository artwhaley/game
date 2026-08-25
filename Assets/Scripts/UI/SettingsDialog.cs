using UnityEngine;
using UnityEngine.UI;

namespace TruthCardGame
{
    /// <summary>
    /// Settings dialog: a session-length modifier slider (0.5x–3.0x, default 1).
    /// Writes SessionConfig.LengthModifier live — the SessionDriver reads it on
    /// every card completion, so the change applies to the next draw.
    /// </summary>
    public sealed class SettingsDialog : MonoBehaviour
    {
        [SerializeField] private GameObject root;
        [SerializeField] private Button closeButton;
        [SerializeField] private Slider lengthSlider;
        [SerializeField] private Text lengthValue;

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (lengthSlider != null)
            {
                lengthSlider.minValue = 0.5f;
                lengthSlider.maxValue = 3f;
                lengthSlider.value = SessionConfig.LengthModifier;
                lengthSlider.onValueChanged.AddListener(OnLengthChanged);
                RefreshLabel(lengthSlider.value);
            }
        }

        public void Show()
        {
            root.SetActive(true);
            if (lengthSlider != null)
            {
                // Reflect the current value each time the dialog opens.
                lengthSlider.value = SessionConfig.LengthModifier;
                RefreshLabel(lengthSlider.value);
            }
        }

        public void Close()
        {
            root.SetActive(false);
        }

        private void OnLengthChanged(float value)
        {
            SessionConfig.LengthModifier = value;
            RefreshLabel(value);
        }

        private void RefreshLabel(float value)
        {
            if (lengthValue != null)
            {
                lengthValue.text = $"{value:0.0}x";
            }
        }
    }
}