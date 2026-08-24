using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TruthCardGame
{
    /// <summary>
    /// Setup screen: builds one toggle per distinct deck tag (default on).
    /// Tag ON = cards with that tag are allowed; OFF = cards with that tag are
    /// excluded this session. Start Game writes the filter to SessionConfig
    /// and loads the Game scene.
    /// </summary>
    public sealed class GameSetupController : MonoBehaviour
    {
        [SerializeField] private CardDeck deck;
        [SerializeField] private RectTransform toggleContainer;
        [SerializeField] private Button startButton;

        private readonly List<Toggle> _toggles = new List<Toggle>();

        private void Start()
        {
            if (startButton != null) startButton.onClick.AddListener(StartGame);
            if (deck == null)
            {
                Debug.LogError("[TruthCardGame] GameSetupController has no deck assigned. Rebuild with TruthCardGame → Build Scenes.");
                return;
            }
            foreach (var tag in deck.DistinctTags())
            {
                _toggles.Add(CreateToggle(tag));
            }
        }

        public void StartGame()
        {
            var excluded = new List<string>();
            foreach (var toggle in _toggles)
            {
                if (!toggle.isOn)
                {
                    excluded.Add(toggle.GetComponentInChildren<Text>().text);
                }
            }
            SessionConfig.MustIncludeTags = new List<string>();
            SessionConfig.MustExcludeTags = excluded;
            SceneManager.LoadScene("Game");
        }

        private Toggle CreateToggle(string tag)
        {
            var go = new GameObject("Toggle_" + tag, typeof(RectTransform), typeof(Image), typeof(Toggle));
            go.transform.SetParent(toggleContainer, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 48f);

            var image = go.GetComponent<Image>();
            image.sprite = WhiteSprite();
            image.color = new Color(0.16f, 0.16f, 0.2f);

            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = image;

            var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(go.transform, false);
            var text = label.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = tag;
            text.fontSize = 28;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.rectTransform.anchorMin = new Vector2(0f, 0f);
            text.rectTransform.anchorMax = new Vector2(1f, 1f);
            text.rectTransform.offsetMin = new Vector2(20f, 0f);
            text.rectTransform.offsetMax = new Vector2(-120f, 0f);

            var check = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            check.transform.SetParent(go.transform, false);
            var checkImage = check.GetComponent<Image>();
            checkImage.sprite = WhiteSprite();
            checkImage.color = new Color(0.35f, 0.8f, 0.45f);
            checkImage.rectTransform.anchorMin = new Vector2(1f, 0.5f);
            checkImage.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            checkImage.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            checkImage.rectTransform.sizeDelta = new Vector2(24f, 24f);
            checkImage.rectTransform.anchoredPosition = new Vector2(-44f, 0f);

            toggle.graphic = checkImage;
            toggle.isOn = true;
            return toggle;
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
