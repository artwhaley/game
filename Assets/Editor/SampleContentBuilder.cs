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

        [MenuItem("TruthCardGame/Create Sample Content")]
        public static void EnsureSampleContent()
        {
            EnsureFolder("Assets", "Content");
            EnsureFolder("Assets/Content", "Actions");
            EnsureFolder("Assets/Content", "Cards");
            EnsureFolder("Assets/Content", "Deck");

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

            var courageBoost = GetOrCreateCard(CardsFolder + "/CourageBoost.asset", "Courage Boost", new[] { "party", "truth" }, courage);
            var crowdWatches = GetOrCreateCard(CardsFolder + "/TheCrowdWatches.asset", "The Crowd Watches", new[] { "party", "dare" }, blocking);
            var ambientWhispers = GetOrCreateCard(CardsFolder + "/AmbientWhispers.asset", "Ambient Whispers", new[] { "solo", "truth" }, continuous);
            var dareCelebrate = GetOrCreateCard(CardsFolder + "/DareAndCelebrate.asset", "Dare & Celebrate", new[] { "party", "dare" }, courage, blocking);
            var twinWhispers = GetOrCreateCard(CardsFolder + "/TwinWhispers.asset", "Twin Whispers", new[] { "solo" }, continuous, continuous);

            var deck = AssetDatabase.LoadAssetAtPath<CardDeck>(StarterDeckPath);
            if (deck == null)
            {
                deck = ScriptableObject.CreateInstance<CardDeck>();
                AssetDatabase.CreateAsset(deck, StarterDeckPath);
            }
            var cards = new[] { courageBoost, crowdWatches, ambientWhispers, dareCelebrate, twinWhispers };
            var so = new SerializedObject(deck);
            var cardsProp = so.FindProperty("cards");
            cardsProp.arraySize = cards.Length;
            for (var i = 0; i < cards.Length; i++)
            {
                cardsProp.GetArrayElementAtIndex(i).objectReferenceValue = cards[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TruthCardGame] Sample content ready: 3 actions, 5 cards, starter deck.");
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
