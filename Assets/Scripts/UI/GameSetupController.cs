using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TruthCardGame
{
    /// <summary>
    /// Setup screen: pick which session (game type) to play. Reads the
    /// SessionLibrary asset, builds one button per session, and writes the
    /// selection to SessionConfig before loading the Game scene.
    /// </summary>
    public sealed class GameSetupController : MonoBehaviour
    {
        [SerializeField] private SessionLibrary library;
        [SerializeField] private RectTransform buttonContainer;
        [SerializeField] private Button startButton;

        private Session _selected;

        private void Start()
        {
            if (library == null)
            {
                Debug.LogError("[TruthCardGame] GameSetupController has no session library assigned. Rebuild with TruthCardGame → Build Scenes.");
                return;
            }
            foreach (var session in library.Sessions)
            {
                if (session == null) continue;
                CreateSessionButton(session);
            }
            if (startButton != null) startButton.onClick.AddListener(StartGame);
        }

        public void StartGame()
        {
            if (_selected == null)
            {
                Debug.LogWarning("[TruthCardGame] Pick a session first.");
                return;
            }
            SessionConfig.SelectedSession = _selected;
            SceneManager.LoadScene("Game");
        }

        private void CreateSessionButton(Session session)
        {
            var go = new GameObject("SessionButton", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(buttonContainer, false);
            var img = go.GetComponent<Image>();
            img.sprite = WhiteSprite();
            img.color = new Color(0.25f, 0.45f, 0.9f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(() => _selected = session);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 64f);

            var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(go.transform, false);
            var text = label.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = session.Title;
            text.fontSize = 30;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
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