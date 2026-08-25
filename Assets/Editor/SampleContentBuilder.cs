using System;
using UnityEditor;
using UnityEngine;

namespace TruthCardGame.EditorTools
{
    /// <summary>
    /// Generates the starter card/action/deck assets. Idempotent: existing
    /// assets are left untouched, missing ones are created. Run via menu bar
    /// TruthCardGame → Create Sample Content (also invoked by Build Scenes).
    /// </summary>
    public static class SampleContentBuilder
    {
        public const string StarterDeckPath = "Assets/Content/Deck/StarterDeck.asset";

        private const string ActionsFolder = "Assets/Content/Actions";
        private const string CardsFolder = "Assets/Content/Cards";
        private const string DeckFolder = "Assets/Content/Deck";
        private const string SessionsFolder = "Assets/Content/Sessions";
        private const string SessionLibraryPath = SessionsFolder + "/SessionLibrary.asset";

        [MenuItem("TruthCardGame/Create Sample Content")]
        public static void EnsureSampleContent()
        {
            EnsureFolder("Assets", "Content");
            EnsureFolder("Assets/Content", "Actions");
            EnsureFolder("Assets/Content", "Cards");
            EnsureFolder("Assets/Content", "Deck");
            EnsureFolder("Assets/Content", "Sessions");

            var blocking = GetOrCreateAction<DebugAction>(ActionsFolder + "/Debug_Blocking.asset", action =>
            {
                SetString(action, "message", "A hush falls over the room. Everyone is watching you…");
                SetFloat(action, "delaySeconds", 2.5f);
                SetBool(action, "isBlocking", true);
            });

            var continuous = GetOrCreateAction<DebugAction>(ActionsFolder + "/Debug_Continuous.asset", action =>
            {
                SetString(action, "message", "A low murmur of whispers carries across the room…");
                SetFloat(action, "delaySeconds", 5f);
                SetBool(action, "isBlocking", false);
            });

            var courage = GetOrCreateAction<StatIncreaseAction>(ActionsFolder + "/CouragePlusOne.asset", action =>
            {
                SetString(action, "statKey", "courage");
                SetInt(action, "amount", 1);
            });

            // PoC cutscene: the timeline asset is authored by hand in the
            // Timeline window (this builder cannot). Assign it on this action
            // asset once authored — until then, drawing the card logs an error.
            var cutscene = GetOrCreateAction<CutsceneAction>(ActionsFolder + "/Cutscene_Intro.asset", action =>
            {
                SetBool(action, "isBlocking", true);
            });

            // A choice action: ask the player a question, branch on the answer.
            var choice = GetOrCreateAction<ChoiceAction>(ActionsFolder + "/Choice_FaceTheCrowd.asset", action =>
            {
                SetString(action, "prompt", "The crowd leans in. How do you answer?");
                SetBool(action, "isBlocking", true);
                // Two options: brave it (courage) or shrug it off (a beat).
                SetChoiceOption(action, 0, "Own it", courage);
                SetChoiceOption(action, 1, "Shrug it off", continuous);
            });

            var courageBoost = GetOrCreateCard(CardsFolder + "/CourageBoost.asset", "Courage Boost", new[] { "party", "truth" }, courage);
            var crowdWatches = GetOrCreateCard(CardsFolder + "/TheCrowdWatches.asset", "The Crowd Watches", new[] { "party", "dare" }, blocking);
            var ambientWhispers = GetOrCreateCard(CardsFolder + "/AmbientWhispers.asset", "Ambient Whispers", new[] { "solo", "truth" }, continuous);
            var dareCelebrate = GetOrCreateCard(CardsFolder + "/DareAndCelebrate.asset", "Dare & Celebrate", new[] { "party", "dare" }, courage, blocking);
            var twinWhispers = GetOrCreateCard(CardsFolder + "/TwinWhispers.asset", "Twin Whispers", new[] { "solo" }, continuous, continuous);
            var cutsceneIntro = GetOrCreateCard(CardsFolder + "/CutsceneIntro.asset", "A Familiar Face", new[] { "cutscene" }, cutscene);
            var crowdChoice = GetOrCreateCard(CardsFolder + "/FaceTheCrowd.asset", "Face the Crowd", new[] { "party", "dare" }, choice);

            var deck = AssetDatabase.LoadAssetAtPath<CardDeck>(StarterDeckPath);
            if (deck == null)
            {
                deck = ScriptableObject.CreateInstance<CardDeck>();
                AssetDatabase.CreateAsset(deck, StarterDeckPath);
            }
            var cards = new[] { courageBoost, crowdWatches, ambientWhispers, dareCelebrate, twinWhispers, cutsceneIntro, crowdChoice };
            var so = new SerializedObject(deck);
            var cardsProp = so.FindProperty("cards");
            cardsProp.arraySize = cards.Length;
            for (var i = 0; i < cards.Length; i++)
            {
                cardsProp.GetArrayElementAtIndex(i).objectReferenceValue = cards[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            EnsureSampleSessions();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TruthCardGame] Sample content ready: 5 actions, 7 cards, starter deck, 2 sessions.");
        }

        /// <summary>Creates a session library with two sessions, each ending in an authored ending phase.</summary>
        private static void EnsureSampleSessions()
        {
            var endingCard = GetOrCreateCard(CardsFolder + "/TheEnd.asset", "The End", new[] { "ending" }, courageForSessions());

            var relaxing = GetOrCreateSession(SessionsFolder + "/Relaxing.asset", "Relaxing", new[] { "relaxing" },
                CreatePhase(SessionsFolder + "/Phase_WarmUp.asset", "Warm Up", new[] { "solo" }, 2, 3),
                CreatePhase(SessionsFolder + "/Phase_Teasing.asset", "Teasing", new[] { "solo", "truth" }, 3, 4),
                CreatePhase(SessionsFolder + "/Phase_WindDown.asset", "Wind Down", new[] { "ending" }, 1, 2));

            var intense = GetOrCreateSession(SessionsFolder + "/Intense.asset", "Intense", new[] { "intense" },
                CreatePhase(SessionsFolder + "/Phase_Build.asset", "Build", new[] { "party" }, 3, 4),
                CreatePhase(SessionsFolder + "/Phase_HighIntensity.asset", "High Intensity", new[] { "party", "dare" }, 4, 5),
                CreatePhase(SessionsFolder + "/Phase_TheEnd.asset", "The End", new[] { "ending" }, 1, 2));

            var library = AssetDatabase.LoadAssetAtPath<SessionLibrary>(SessionLibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<SessionLibrary>();
                AssetDatabase.CreateAsset(library, SessionLibraryPath);
            }
            var so = new SerializedObject(library);
            var sessionsProp = so.FindProperty("sessions");
            sessionsProp.arraySize = 2;
            sessionsProp.GetArrayElementAtIndex(0).objectReferenceValue = relaxing;
            sessionsProp.GetArrayElementAtIndex(1).objectReferenceValue = intense;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A stat action used by the ending card (fresh instance so phases don't share it).</summary>
        private static CardAction courageForSessions()
        {
            return GetOrCreateAction<StatIncreaseAction>(ActionsFolder + "/CouragePlusOne.asset", action =>
            {
                SetString(action, "statKey", "courage");
                SetInt(action, "amount", 1);
            });
        }

        private static Session GetOrCreateSession(string path, string title, string[] tags, params Phase[] phases)
        {
            var session = AssetDatabase.LoadAssetAtPath<Session>(path);
            if (session == null)
            {
                session = ScriptableObject.CreateInstance<Session>();
                AssetDatabase.CreateAsset(session, path);
            }
            var so = new SerializedObject(session);
            so.FindProperty("title").stringValue = title;
            var tagsProp = so.FindProperty("tags");
            tagsProp.arraySize = tags.Length;
            for (var i = 0; i < tags.Length; i++)
            {
                tagsProp.GetArrayElementAtIndex(i).stringValue = tags[i];
            }
            var phasesProp = so.FindProperty("phases");
            phasesProp.arraySize = phases.Length;
            for (var i = 0; i < phases.Length; i++)
            {
                phasesProp.GetArrayElementAtIndex(i).objectReferenceValue = phases[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return session;
        }

        private static Phase CreatePhase(string path, string title, string[] includeTags, int min, int max)
        {
            var phase = AssetDatabase.LoadAssetAtPath<Phase>(path);
            if (phase == null)
            {
                phase = ScriptableObject.CreateInstance<Phase>();
                AssetDatabase.CreateAsset(phase, path);
            }
            var so = new SerializedObject(phase);
            so.FindProperty("title").stringValue = title;
            so.FindProperty("minCards").intValue = min;
            so.FindProperty("maxCards").intValue = max;
            var tagsProp = so.FindProperty("mustIncludeTags");
            tagsProp.arraySize = includeTags.Length;
            for (var i = 0; i < includeTags.Length; i++)
            {
                tagsProp.GetArrayElementAtIndex(i).stringValue = includeTags[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return phase;
        }

        // ---------- asset helpers ----------

        private static T GetOrCreateAction<T>(string path, Action<T> configure) where T : CardAction
        {
            var action = AssetDatabase.LoadAssetAtPath<T>(path);
            if (action != null) return action;
            action = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(action, path);
            configure(action);
            return action;
        }

        private static Card GetOrCreateCard(string path, string title, string[] tags, params CardAction[] actions)
        {
            var card = AssetDatabase.LoadAssetAtPath<Card>(path);
            if (card != null) return card;
            card = ScriptableObject.CreateInstance<Card>();
            AssetDatabase.CreateAsset(card, path);
            var so = new SerializedObject(card);
            so.FindProperty("title").stringValue = title;
            var tagsProp = so.FindProperty("tags");
            tagsProp.arraySize = tags.Length;
            for (var i = 0; i < tags.Length; i++)
            {
                tagsProp.GetArrayElementAtIndex(i).stringValue = tags[i];
            }
            var actionsProp = so.FindProperty("actions");
            actionsProp.arraySize = actions.Length;
            for (var i = 0; i < actions.Length; i++)
            {
                actionsProp.GetArrayElementAtIndex(i).objectReferenceValue = actions[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return card;
        }

        private static void SetChoiceOption(UnityEngine.Object target, int index, string label, CardAction action)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty("options");
            prop.arraySize = Math.Max(prop.arraySize, index + 1);
            var element = prop.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("label").stringValue = label;
            element.FindPropertyRelative("action").objectReferenceValue = action;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(UnityEngine.Object target, string field, string value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(UnityEngine.Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(UnityEngine.Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(UnityEngine.Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).boolValue = value;
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
