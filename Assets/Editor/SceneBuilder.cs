using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using UnityEngine.UI;
using Unity.Cinemachine;

namespace TruthCardGame.EditorTools
{
    /// <summary>
    /// Builds the three game scenes from code so the whole UI structure is
    /// readable, reproducible, and diffable — no manual inspector wiring.
    /// Run via menu bar TruthCardGame → Build Scenes (also creates sample content).
    /// </summary>
    public static class SceneBuilder
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string MainMenuPath = ScenesFolder + "/MainMenu.unity";
        private const string GameSetupPath = ScenesFolder + "/GameSetup.unity";
        private const string GamePath = ScenesFolder + "/Game.unity";

        private static Sprite _white;

        [MenuItem("TruthCardGame/Build Scenes")]
        public static void BuildAllScenes()
        {
            SampleContentBuilder.EnsureSampleContent();
            BuildMainMenuScene();
            BuildGameSetupScene();
            BuildGameScene();
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainMenuPath, true),
                new EditorBuildSettingsScene(GameSetupPath, true),
                new EditorBuildSettingsScene(GamePath, true),
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TruthCardGame] Scenes built and registered in Build Settings.");
        }

        // ---------- scene construction ----------

        private static void CreateScene(string path, Action build)
        {
            EnsureFolder("Assets", "Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            build();
            EditorSceneManager.SaveScene(scene, path);
        }

        private static void BuildMainMenuScene()
        {
            CreateScene(MainMenuPath, () =>
            {
                CreateCamera();
                var canvas = CreateCanvas();
                CreateEventSystem();

                var root = CreatePanel(canvas.transform, "Root", new Color(0.12f, 0.12f, 0.16f));
                Stretch(root.rectTransform);
                AddVerticalLayout(root.gameObject, 24f, new RectOffset(0, 0, 100, 40));

                var title = CreateText(root.transform, "TRUTH OR DARE", 76, TextAnchor.MiddleCenter, Color.white);
                title.rectTransform.sizeDelta = new Vector2(0f, 110f);

                var subtitle = CreateText(root.transform, "A single-player card game", 28, TextAnchor.MiddleCenter, new Color(0.75f, 0.75f, 0.75f));
                subtitle.rectTransform.sizeDelta = new Vector2(0f, 44f);

                var startButton = CreateButton(root.transform, "Start Game");
                startButton.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 64f);

                var settingsButton = CreateButton(root.transform, "Settings");
                settingsButton.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 64f);

                var dialog = CreateSettingsDialog(canvas.transform);

                var controllerGo = new GameObject("MenuController");
                var controller = controllerGo.AddComponent<MenuController>();
                SetField(controller, "startButton", startButton);
                SetField(controller, "settingsButton", settingsButton);
                SetField(controller, "settingsDialog", dialog);
            });
        }

        private static void BuildGameSetupScene()
        {
            CreateScene(GameSetupPath, () =>
            {
                CreateCamera();
                var canvas = CreateCanvas();
                CreateEventSystem();

                var root = CreatePanel(canvas.transform, "Root", new Color(0.12f, 0.12f, 0.16f));
                Stretch(root.rectTransform);
                AddVerticalLayout(root.gameObject, 20f, new RectOffset(80, 80, 80, 80));

                var title = CreateText(root.transform, "Choose what to draw", 56, TextAnchor.MiddleCenter, Color.white);
                title.rectTransform.sizeDelta = new Vector2(0f, 80f);

                var hint = CreateText(root.transform, "Tag ON = allowed · Tag OFF = excluded", 26, TextAnchor.MiddleCenter, new Color(0.75f, 0.75f, 0.75f));
                hint.rectTransform.sizeDelta = new Vector2(0f, 40f);

                var containerGo = new GameObject("TagList", typeof(RectTransform));
                containerGo.transform.SetParent(root.transform, false);
                var container = containerGo.GetComponent<RectTransform>();
                AddVerticalLayout(containerGo, 12f, new RectOffset(0, 0, 0, 0));
                var fitter = containerGo.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                var startButton = CreateButton(root.transform, "Start Game");
                startButton.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 64f);

                var controllerGo = new GameObject("GameSetupController");
                var controller = controllerGo.AddComponent<GameSetupController>();
                SetField(controller, "deck", AssetDatabase.LoadAssetAtPath<CardDeck>(SampleContentBuilder.StarterDeckPath));
                SetField(controller, "toggleContainer", container);
                SetField(controller, "startButton", startButton);
            });
        }

        private static void BuildGameScene()
        {
            CreateScene(GamePath, () =>
            {
                CreateGameCameraCinemachine();
                var vcam = CreateVirtualCamera();
                var npc = CreateNpcCube();
                var director = CreateDirector();
                var canvas = CreateCanvas();
                CreateEventSystem();

                var root = CreatePanel(canvas.transform, "Root", new Color(0.12f, 0.12f, 0.16f));
                Stretch(root.rectTransform);
                AddVerticalLayout(root.gameObject, 16f, new RectOffset(100, 100, 90, 90));

                var heading = CreateText(root.transform, "Current card", 30, TextAnchor.MiddleLeft, new Color(0.7f, 0.7f, 0.7f));
                heading.rectTransform.sizeDelta = new Vector2(0f, 40f);

                var cardTitle = CreateText(root.transform, "—", 64, TextAnchor.MiddleLeft, Color.white);
                cardTitle.rectTransform.sizeDelta = new Vector2(0f, 96f);

                var status = CreateText(root.transform, "Ready.", 28, TextAnchor.MiddleLeft, new Color(0.9f, 0.9f, 0.9f));
                status.rectTransform.sizeDelta = new Vector2(0f, 44f);

                var spacer = new GameObject("Spacer", typeof(RectTransform));
                spacer.transform.SetParent(root.transform, false);
                spacer.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 120f);

                var rowGo = new GameObject("ButtonRow", typeof(RectTransform));
                rowGo.transform.SetParent(root.transform, false);
                var row = rowGo.GetComponent<RectTransform>();
                row.sizeDelta = new Vector2(0f, 64f);
                var rowLayout = rowGo.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 20f;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = true;
                rowLayout.childForceExpandHeight = false;

                var drawNext = CreateButton(row.transform, "Draw Next Card");
                var menuButton = CreateButton(row.transform, "Main Menu");

                var panelGo = new GameObject("GamePanel");
                var panel = panelGo.AddComponent<GamePanel>();
                var managerGo = new GameObject("GameManager");
                var manager = managerGo.AddComponent<GameManager>();
                SetField(panel, "cardTitle", cardTitle);
                SetField(panel, "status", status);
                SetField(panel, "drawNextButton", drawNext);
                SetField(panel, "menuButton", menuButton);
                SetField(panel, "gameManager", manager);
                SetField(manager, "deck", AssetDatabase.LoadAssetAtPath<CardDeck>(SampleContentBuilder.StarterDeckPath));
                SetField(manager, "panel", panel);

                SetField(manager, "directorPlayer", director.GetComponent<DirectorPlayer>());

                // Aim the virtual camera at the NPC so a camera-cut timeline reads clearly.
                vcam.LookAt = npc.transform;

                drawNext.interactable = false;
            });
        }

        /// <summary>Game-scene camera with a CinemachineBrain so a cut to a virtual camera is smooth.</summary>
        private static Camera CreateGameCameraCinemachine()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.12f);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            go.AddComponent<AudioListener>();
            go.AddComponent<CinemachineBrain>();
            return cam;
        }

        /// <summary>A Cinemachine camera showing the NPC.</summary>
        private static CinemachineCamera CreateVirtualCamera()
        {
            var go = new GameObject("Cutscene Camera");
            var vcam = go.AddComponent<CinemachineCamera>();
            vcam.Priority = 10;
            // Position it in front of the NPC and aim it back at origin.
            go.transform.position = new Vector3(0f, 1.5f, -3.5f);
            go.transform.LookAt(Vector3.zero);
            return vcam;
        }

        /// <summary>A primary-colored cube as the stand-in NPC for the PoC timeline.</summary>
        private static GameObject CreateNpcCube()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Npc";
            go.transform.position = new Vector3(0f, 0f, 0f);
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
                if (shader != null)
                {
                    renderer.material = new Material(shader);
                    renderer.material.color = new Color(0.3f, 0.6f, 1f);
                }
            }
            return go;
        }

        /// <summary>The cutscene driver object: a PlayableDirector + DirectorPlayer.</summary>
        private static GameObject CreateDirector()
        {
            var go = new GameObject("CutsceneDirector");
            go.AddComponent<PlayableDirector>();
            go.AddComponent<DirectorPlayer>();
            return go;
        }

        private static SettingsDialog CreateSettingsDialog(Transform canvas)
        {
            var overlay = CreatePanel(canvas, "SettingsDialog", new Color(0f, 0f, 0f, 0.65f));
            Stretch(overlay.rectTransform);

            var dialog = CreatePanel(overlay.transform, "Dialog", new Color(0.2f, 0.2f, 0.26f));
            var dlg = dialog.rectTransform;
            dlg.anchorMin = new Vector2(0.5f, 0.5f);
            dlg.anchorMax = new Vector2(0.5f, 0.5f);
            dlg.pivot = new Vector2(0.5f, 0.5f);
            dlg.sizeDelta = new Vector2(720f, 420f);
            AddVerticalLayout(dialog.gameObject, 20f, new RectOffset(40, 40, 40, 40));

            var title = CreateText(dialog.transform, "Settings", 44, TextAnchor.MiddleCenter, Color.white);
            title.rectTransform.sizeDelta = new Vector2(0f, 60f);

            var body = CreateText(dialog.transform, "Nothing here yet.", 26, TextAnchor.MiddleCenter, new Color(0.75f, 0.75f, 0.75f));
            body.rectTransform.sizeDelta = new Vector2(0f, 50f);

            var close = CreateButton(dialog.transform, "Close");
            close.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 56f);

            var controllerGo = new GameObject("SettingsDialogController");
            var controller = controllerGo.AddComponent<SettingsDialog>();
            SetField(controller, "root", overlay.gameObject);
            SetField(controller, "closeButton", close);

            overlay.gameObject.SetActive(false);
            return controller;
        }

        // ---------- UI building blocks ----------

        private static void CreateCamera()
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.08f, 0.12f);
            go.AddComponent<AudioListener>();
        }

        private static Canvas CreateCanvas()
        {
            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static void CreateEventSystem()
        {
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static Text CreateText(Transform parent, string content, int size, TextAnchor align, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = GetFont();
            text.text = content;
            text.fontSize = size;
            text.alignment = align;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Font GetFont()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        private static Image CreatePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = WhiteSprite();
            img.color = color;
            return img;
        }

        private static Button CreateButton(Transform parent, string label)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = WhiteSprite();
            img.color = new Color(0.25f, 0.45f, 0.9f);
            var button = go.GetComponent<Button>();
            button.targetGraphic = img;
            var text = CreateText(go.transform, label, 32, TextAnchor.MiddleCenter, Color.white);
            Stretch(text.rectTransform);
            return button;
        }

        private static VerticalLayoutGroup AddVerticalLayout(GameObject go, float spacing, RectOffset padding)
        {
            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = padding;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Sprite WhiteSprite()
        {
            if (_white == null)
            {
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _white = Sprite.Create(tex, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 100f);
            }
            return _white;
        }

        private static void SetField(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(fieldName).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string parent, string name)
        {
            var path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }
    }
}
