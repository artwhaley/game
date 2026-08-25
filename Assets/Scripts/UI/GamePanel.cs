using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TruthCardGame
{
    /// <summary>
    /// Game-screen view: current card title, status line, action buttons, and
    /// the choice prompt overlay that ChoiceAction drives through GameManager.
    /// </summary>
    public sealed class GamePanel : MonoBehaviour
    {
        [SerializeField] private Text cardTitle;
        [SerializeField] private Text status;
        [SerializeField] private Button drawNextButton;
        [SerializeField] private Button menuButton;
        [SerializeField] private GameManager gameManager;

        // Choice prompt overlay (created by SceneBuilder, inactive by default).
        [SerializeField] private GameObject promptRoot;
        [SerializeField] private Text promptText;
        [SerializeField] private RectTransform promptContainer;

        private readonly List<GameObject> _promptButtons = new List<GameObject>();
        private Action<int> _onPromptChosen;
        private bool _promptActive;

        private void Awake()
        {
            if (gameManager != null)
            {
                drawNextButton.onClick.AddListener(gameManager.DrawNextCard);
                menuButton.onClick.AddListener(gameManager.ReturnToMenu);
            }
        }

        // ---------- card status ----------

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

        public void ShowSessionComplete()
        {
            cardTitle.text = "Session complete";
            status.text = "Returning to menu…";
            drawNextButton.interactable = false;
        }

        // ---------- choice prompt ----------

        /// <summary>Shows the choice overlay with one button per option. Buttons resolve via onChosen.</summary>
        public void ShowPrompt(string prompt, IReadOnlyList<string> options, Action<int> onChosen)
        {
            if (promptRoot == null)
            {
                Debug.LogError("[TruthCardGame] GamePanel has no prompt overlay. Rebuild with TruthCardGame → Build Scenes.");
                return;
            }
            if (_promptActive)
            {
                Debug.LogError("[TruthCardGame] GamePanel: prompt already shown; ignoring duplicate.");
                return;
            }

            _promptActive = true;
            _onPromptChosen = onChosen;
            promptText.text = prompt;

            for (var i = 0; i < options.Count; i++)
            {
                var index = i;
                _promptButtons.Add(CreatePromptButton(options[index], () => ResolvePrompt(index)));
            }

            promptRoot.SetActive(true);
        }

        private void ResolvePrompt(int index)
        {
            promptRoot.SetActive(false);
            foreach (var button in _promptButtons)
            {
                Destroy(button);
            }
            _promptButtons.Clear();
            _promptActive = false;
            var callback = _onPromptChosen;
            _onPromptChosen = null;
            callback?.Invoke(index);
        }

        private GameObject CreatePromptButton(string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("PromptButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(promptContainer, false);
            var img = go.GetComponent<Image>();
            img.sprite = WhiteSprite();
            img.color = new Color(0.25f, 0.45f, 0.9f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(onClick);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 56f);

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = label;
            text.fontSize = 28;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            return go;
        }

        private static Sprite _white;
        private static Sprite WhiteSprite()
        {
            if (_white == null)
            {
                var tex = Texture2D.whiteTexture;
                _white = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            }
            return _white;
        }
    }
}