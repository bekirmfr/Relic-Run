using System.Collections.Generic;
using GameLift.Scene;
using RelicRun.Game.Data;
using RelicRun.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;

namespace RelicRun.Editor.Importers
{
    /// <summary>
    /// Wires the fight into the game scene, so nobody has to drag fifteen references in by hand.
    /// </summary>
    /// <remarks>
    /// A scene in this project is a PREFAB under <c>Assets/Scenes/</c>. <c>Corescene.unity</c> is
    /// empty and stays that way; the GameLift package's <c>SceneService</c> loads a scene prefab
    /// by key through a <c>SceneConfig</c> that addresses it. So this edits
    /// <c>GameScene.prefab</c> — which already carries the game's <c>LifetimeScope</c> and its
    /// camera — rather than writing a second scene beside it.
    ///
    /// Generated rather than authored, for the same reason the content is: a hierarchy assembled
    /// by hand is one nobody can diff, nobody can rebuild after a mistake, and nobody can
    /// describe except by opening it — and fifteen references are fifteen chances to be ninety
    /// per cent wired and silently wrong.
    ///
    /// It is a scaffold, not a screen. The layout is legible and nothing more: no art, no frames,
    /// no hall behind it. What it is for is watching a real fight play out at its real pace,
    /// which is the first moment any of the last three phases can be seen at all.
    /// </remarks>
    public static class FightSceneBuilder
    {
        /// <summary>The scene prefab the fight is built into.</summary>
        public const string ScenePath = "Assets/Scenes/GameScene.prefab";

        /// <summary>What the built hierarchy is called, so a rebuild replaces it.</summary>
        public const string RootName = "Fight";

        /// <summary>Where the config that makes the scene loadable lives.</summary>
        /// <remarks>Beside the one the GameLift sample already ships, so both are in one place.</remarks>
        public const string ConfigPath = "Assets/Samples/Game Lift/1.0.0/Starter/" +
            "ScriptableObjects/SceneServiceSettings/GameSceneConfig.asset";
        public const string FlierPrefab = "Assets/GameAssets/Game/Presentation/FlyingNumber.prefab";
        public const string LinePrefab = "Assets/GameAssets/Game/Presentation/LogLine.prefab";

        /// <summary>The canvas is authored at this size and scales to whatever it lands on.</summary>
        private static readonly Vector2 Reference = new Vector2(1080f, 1920f);

        /// <summary>Named, so the things that need it run first can say so.</summary>
        private const string BuildItem = "Tools/Relic Run/Build Fight Scene";

        [MenuItem(BuildItem, priority = 120)]
        public static void Build()
        {
            var content = AssetDatabase.LoadAssetAtPath<GameContent>(ContentPaths.GameContentAsset);
            if (content == null)
            {
                Debug.LogError("no content — run Tools ▸ Relic Run ▸ Import Content first");
                return;
            }

            GameObject scene = PrefabUtility.LoadPrefabContents(ScenePath);
            if (scene == null)
            {
                Debug.LogError("no scene prefab at " + ScenePath);
                return;
            }

            try
            {
                TMP_FontAsset face = content.Fonts == null ? null : content.Fonts.For(FontBook.Ui);

                FlyingNumber flier = Flier(face);
                LogLine line = Line(face);

                Replace(scene);
                Fit(scene, content, face, flier, line);

                PrefabUtility.SaveAsPrefabAsset(scene, ScenePath);
            }
            finally
            {
                // Prefab contents live outside any scene and leak if they are not unloaded, which
                // shows up as an editor that grows heavier every time the menu item is used.
                PrefabUtility.UnloadPrefabContents(scene);
            }

            AssetDatabase.Refresh();
            Register();

            Debug.Log("wired the fight into " + ScenePath + " and registered it as " +
                      SceneKeys.GameScene + ". It is a scaffold: legible, and nothing more.",
                      AssetDatabase.LoadAssetAtPath<GameObject>(ScenePath));
        }

        /// <summary>Where the toggle that decides what the app opens on lives.</summary>
        private const string StartItem = "Tools/Relic Run/Start In The Fight";

        /// <summary>
        /// Sends startup to the fight rather than to the menu, and back again.
        /// </summary>
        /// <remarks>
        /// Registering the scene made it reachable; this is what makes it REACHED. The two are
        /// worth keeping apart, and the confusion between them is easy: a scene can be perfectly
        /// loadable and still never load, because nothing asks for it.
        ///
        /// In this project only one thing asks. <c>AppStartupOrchestrator</c> — the sample's
        /// copy, under <c>Assets/</c> — calls <c>LoadScene(DefaultSceneConfig.SceneKey)</c>
        /// directly and never reaches <c>SceneFlowController.LoadFirstScene</c>, so the package's
        /// own <c>LoadImmediateUntil</c> shortcut is not consulted at all. Which is just as well:
        /// that path calls <c>LoadNextLevelData</c> first, and with an empty <c>Levels</c> list —
        /// this project has one — it divides by zero before it ever gets to the scene.
        ///
        /// A toggle rather than a one-way switch, and a checked one, so the menu says where
        /// startup currently goes instead of making somebody open an asset to find out. This
        /// matters more than it sounds: the menu scene is presently EMPTY, so a build that opens
        /// on it shows a blank canvas and looks broken rather than looking like a menu.
        /// </remarks>
        [MenuItem(StartItem, priority = 121)]
        private static void StartInTheFight()
        {
            SceneServiceSettings settings = Settings();
            if (settings == null) return;

            SceneConfig fight = AssetDatabase.LoadAssetAtPath<SceneConfig>(ConfigPath);
            if (fight == null)
            {
                Debug.LogError("there is no fight to start in — run " + BuildItem + " first");
                return;
            }

            SceneConfig going = Starting(settings) ? Claiming(settings, SceneKeys.MenuScene) : fight;

            // Never to nothing. The orchestrator reads DefaultSceneConfig.SceneKey without
            // asking whether it is there, so an unset default is not a game that starts on
            // nothing — it is a game that throws on its first line and shows a black screen.
            if (going == null)
            {
                Debug.LogError("nothing claims " + SceneKeys.MenuScene + " to go back to, and " +
                               "startup with no default config throws rather than doing nothing");
                return;
            }

            settings.DefaultSceneConfig = going;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log("the app now opens on " + going.SceneKey, going);
        }

        [MenuItem(StartItem, validate = true)]
        private static bool ShowWhereStartupGoes()
        {
            // Quietly. This is asked every single time the Tools menu is opened, and a
            // project with no settings asset would print the same error forever.
            SceneServiceSettings settings = Settings(false);
            Menu.SetChecked(StartItem, settings != null && Starting(settings));
            return true;
        }

        /// <summary>Whether startup currently goes to the fight.</summary>
        private static bool Starting(SceneServiceSettings settings)
        {
            return settings.DefaultSceneConfig != null
                && settings.DefaultSceneConfig.SceneKey == SceneKeys.GameScene;
        }

        /// <summary>The listed config claiming a key, or null if none does.</summary>
        private static SceneConfig Claiming(SceneServiceSettings settings, string key)
        {
            if (settings.SceneConfigs == null) return null;

            foreach (SceneConfig config in settings.SceneConfigs)
            {
                if (config != null && config.SceneKey == key) return config;
            }

            return null;
        }

        /// <summary>
        /// Makes the scene loadable: addressed, configured, and listed.
        /// </summary>
        /// <remarks>
        /// Three separate things, and a scene is only loadable when all three are true. The
        /// prefab has to be addressable, because <c>SceneConfig</c> holds an
        /// <c>AssetReference</c> and nothing else. A config has to exist and name a key. And the
        /// settings asset has to list that config, because <c>SceneService</c> looks the key up
        /// there and there only.
        ///
        /// Miss any one and the failure is the same shape: <c>LoadScene</c> is called, nothing
        /// happens, and nothing is said about it.
        /// </remarks>
        private static void Register()
        {
            IDictionary<string, string> addressed = Addressing.Address(
                Addressing.SceneGroup, "Assets/Scenes", new[] { "GameScene" }, ".prefab");

            string guid;
            if (!addressed.TryGetValue("GameScene", out guid))
            {
                Debug.LogError("could not address " + ScenePath + ", so nothing can load it");
                return;
            }

            SceneServiceSettings settings = Settings();
            if (settings == null) return;

            SceneConfig config = Config(guid);

            if (settings.SceneConfigs == null) settings.SceneConfigs = new List<SceneConfig>();

            // The service takes the FIRST config claiming a key. A second one claiming the same
            // key is not an error anywhere and never will be: it is simply never reached, and
            // the scene that loads is the one somebody wrote earlier and forgot.
            foreach (SceneConfig other in settings.SceneConfigs)
            {
                if (other == null || other == config) continue;
                if (other.SceneKey != SceneKeys.GameScene) continue;

                Debug.LogError(other.name + " already claims " + SceneKeys.GameScene +
                               ", so the fight will never be the scene that loads", other);
            }

            if (!settings.SceneConfigs.Contains(config))
            {
                settings.SceneConfigs.Add(config);
                EditorUtility.SetDirty(settings);
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The config for the game scene, made if it is not there.
        /// </summary>
        /// <remarks>
        /// Beside the config the GameLift sample already ships, so the scenes this project has
        /// are described in one place. <c>RemoveAllOtherScenes</c> is on: a fight drawn over the
        /// top of the menu is not something anybody wants to look at, and the source has no
        /// notion of two screens at once.
        /// </remarks>
        private static SceneConfig Config(string guid)
        {
            var config = AssetDatabase.LoadAssetAtPath<SceneConfig>(ConfigPath);
            bool made = config == null;

            if (made) config = ScriptableObject.CreateInstance<SceneConfig>();

            config.SceneKey = SceneKeys.GameScene;
            config.SceneReference = new AssetReference(guid);
            config.RemoveAllOtherScenes = true;

            if (made) AssetDatabase.CreateAsset(config, ConfigPath);
            else EditorUtility.SetDirty(config);

            return config;
        }

        /// <summary>
        /// The one settings asset the service reads.
        /// </summary>
        /// <remarks>
        /// Found by type rather than by path, because it belongs to the GameLift sample and a
        /// sample can be re-imported somewhere else. Two of them would be worse than none: the
        /// service reads whichever one it was given, and a config added to the other would look
        /// exactly like a config that did nothing.
        /// </remarks>
        private static SceneServiceSettings Settings(bool complain = true)
        {
            string[] found = AssetDatabase.FindAssets("t:SceneServiceSettings");

            if (found.Length == 0)
            {
                if (complain)
                {
                    Debug.LogError("this project has no SceneServiceSettings, so no scene is loadable");
                }

                return null;
            }

            if (found.Length > 1 && complain)
            {
                Debug.LogWarning(found.Length + " SceneServiceSettings assets — registering in " +
                                 AssetDatabase.GUIDToAssetPath(found[0]) + ", which may not be " +
                                 "the one the game reads");
            }

            return AssetDatabase.LoadAssetAtPath<SceneServiceSettings>(
                AssetDatabase.GUIDToAssetPath(found[0]));
        }

        /// <summary>
        /// Throws away whatever the last run built, so a rebuild replaces rather than repeats.
        /// </summary>
        /// <remarks>
        /// By name, and only the one name. Everything else in the scene prefab — the lifetime
        /// scope, the camera, whatever somebody adds tomorrow — is left exactly alone, because a
        /// generator that tidied up after other people would eventually tidy away something that
        /// mattered.
        /// </remarks>
        private static void Replace(GameObject scene)
        {
            for (int i = scene.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = scene.transform.GetChild(i);
                if (child.name == RootName) Object.DestroyImmediate(child.gameObject);
            }
        }

        /// <summary>Builds the fight's whole hierarchy under one child of the scene.</summary>
        private static void Fit(GameObject scene, GameContent content, TMP_FontAsset face,
            FlyingNumber flier, LogLine line)
        {
            var root = new GameObject(RootName);
            root.transform.SetParent(scene.transform, false);

            GameObject canvas = Canvas();
            canvas.transform.SetParent(root.transform, false);

            var view = canvas.AddComponent<CombatView>();

            GameObject foe = Panel(canvas, "Foe", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -520f), new Vector2(-80f, 460f));
            GameObject art = Box(foe, "Art", new Vector2(0f, 0.5f), new Vector2(192f, 192f),
                new Vector2(140f, 40f));
            GameObject foeName = Say(foe, face, "Foe", 34f, TextAlignmentOptions.Left,
                new Vector2(0f, 190f), new Vector2(600f, 44f));
            GameObject foeHealth = Bar(foe, "Health", new Color(0.62f, 0.24f, 0.20f),
                new Vector2(0f, 120f));
            GameObject foeGauge = Bar(foe, "Gauge", new Color(0.75f, 0.60f, 0.25f),
                new Vector2(0f, 84f), 14f);
            GameObject foeFliers = Anchor(foe, "Fliers", new Vector2(140f, 140f));

            GameObject delver = Panel(canvas, "Delver", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 260f), new Vector2(-80f, 300f));
            GameObject heroHealth = Bar(delver, "Health", new Color(0.35f, 0.62f, 0.35f),
                new Vector2(0f, 60f));
            GameObject heroGauge = Bar(delver, "Gauge", new Color(0.75f, 0.60f, 0.25f),
                new Vector2(0f, 24f), 14f);
            GameObject heroText = Say(delver, face, "100", 40f, TextAlignmentOptions.Left,
                new Vector2(0f, 120f), new Vector2(400f, 48f));
            GameObject heroFliers = Anchor(delver, "Fliers", new Vector2(-260f, 120f));

            GameObject purse = Panel(canvas, "Purse", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-180f, -80f), new Vector2(300f, 80f));
            GameObject gold = Say(purse, face, "0", 40f, TextAlignmentOptions.Right,
                Vector2.zero, new Vector2(280f, 60f));
            GameObject goldFliers = Anchor(purse, "Fliers", new Vector2(0f, -40f));

            GameObject log = Log(canvas);

            Wire(view, new[]
            {
                Pair("_heroHealth", heroHealth.GetComponent<Image>()),
                Pair("_heroGauge", heroGauge.GetComponent<Image>()),
                Pair("_heroHealthText", heroText.GetComponent<TMP_Text>()),
                Pair("_heroFliers", (RectTransform)heroFliers.transform),
                Pair("_enemyHealth", foeHealth.GetComponent<Image>()),
                Pair("_enemyGauge", foeGauge.GetComponent<Image>()),
                Pair("_enemyName", foeName.GetComponent<TMP_Text>()),
                Pair("_enemyArt", art.GetComponent<Image>()),
                Pair("_enemyFliers", (RectTransform)foeFliers.transform),
                Pair("_gold", gold.GetComponent<TMP_Text>()),
                Pair("_goldFliers", (RectTransform)goldFliers.transform),
                Pair("_log", (RectTransform)log.transform),
                Pair("_flier", flier),
                Pair("_line", line),
                Pair("_content", content),
            });

            var harness = root.AddComponent<FightHarness>();
            Wire(harness, new[] { Pair("_view", view), Pair("_content", content) });

            // On the SCENE's root, not on the fight's. SceneService looks for one of these on the
            // object it instantiates, and it will not go hunting through the children for it.
            var entry = scene.GetComponent<FightScene>();
            if (entry == null) entry = scene.AddComponent<FightScene>();

            Wire(entry, new[] { Pair("_view", view), Pair("_harness", harness) });
        }

        /* ---------- wiring ---------- */

        private struct Wiring
        {
            public string Field;
            public Object Value;
        }

        private static Wiring Pair(string field, Object value)
        {
            return new Wiring { Field = field, Value = value };
        }

        /// <summary>
        /// Fills a component's serialized references, and complains about any it cannot find.
        /// </summary>
        /// <remarks>
        /// By name, which is the only way in from outside — and the reason each miss is reported
        /// rather than skipped. A renamed field would otherwise leave a reference quietly empty,
        /// and an empty reference in a scene looks exactly like one nobody got round to.
        /// </remarks>
        private static void Wire(Component component, Wiring[] wiring)
        {
            var serialized = new SerializedObject(component);

            foreach (Wiring one in wiring)
            {
                SerializedProperty property = serialized.FindProperty(one.Field);
                if (property == null)
                {
                    Debug.LogError(component.GetType().Name + " has no field called " + one.Field +
                                   " — the builder and the component have drifted apart");
                    continue;
                }

                property.objectReferenceValue = one.Value;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /* ---------- the pieces ---------- */

        private static GameObject Canvas()
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));

            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = Reference;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // One canvas unit to one authored pixel, which is what keeps a point-filtered sprite
            // landing on whole pixels instead of between two of them.
            scaler.referencePixelsPerUnit = 100f;

            return canvas;
        }

        private static GameObject Panel(GameObject parent, string name, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 at, Vector2 size)
        {
            var panel = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)panel.transform;

            rect.SetParent(parent.transform, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = at;
            rect.sizeDelta = size;

            return panel;
        }

        private static GameObject Box(GameObject parent, string name, Vector2 anchor,
            Vector2 size, Vector2 at)
        {
            var box = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)box.transform;

            rect.SetParent(parent.transform, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = at;

            Image image = box.GetComponent<Image>();
            image.color = Color.white;
            image.preserveAspect = true;
            image.enabled = false;

            return box;
        }

        /// <summary>A bar that fills from the left, which is what both gauges and both healths are.</summary>
        private static GameObject Bar(GameObject parent, string name, Color colour, Vector2 at,
            float height = 26f)
        {
            var bar = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)bar.transform;

            rect.SetParent(parent.transform, false);
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(600f, height);
            rect.anchoredPosition = at;

            Image image = bar.GetComponent<Image>();
            image.color = colour;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 1f;

            return bar;
        }

        private static GameObject Say(GameObject parent, TMP_FontAsset face, string what,
            float size, TextAlignmentOptions how, Vector2 at, Vector2 box)
        {
            var said = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = (RectTransform)said.transform;

            rect.SetParent(parent.transform, false);
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = box;
            rect.anchoredPosition = at;

            var text = said.GetComponent<TextMeshProUGUI>();
            text.text = what;
            text.fontSize = size;
            text.alignment = how;
            text.color = new Color(0.90f, 0.87f, 0.80f);
            if (face != null) text.font = face;

            return said;
        }

        /// <summary>A point for numbers to fly from. It draws nothing itself.</summary>
        private static GameObject Anchor(GameObject parent, string name, Vector2 at)
        {
            var anchor = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)anchor.transform;

            rect.SetParent(parent.transform, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = at;

            return anchor;
        }

        /// <summary>
        /// The log, newest at the bottom, laid out by Unity rather than by arithmetic.
        /// </summary>
        /// <remarks>
        /// A content-size fitter and a vertical layout, so a line of any length finds its own
        /// height. The alternative is measuring text by hand, which is wrong the first time
        /// somebody translates a line into German.
        /// </remarks>
        private static GameObject Log(GameObject parent)
        {
            GameObject panel = Panel(parent, "Log", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -80f), new Vector2(900f, 620f));

            var group = panel.AddComponent<VerticalLayoutGroup>();
            group.childAlignment = TextAnchor.LowerLeft;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = true;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.spacing = 4f;

            return panel;
        }

        /* ---------- the prefabs ---------- */

        private static FlyingNumber Flier(TMP_FontAsset face)
        {
            var made = new GameObject("FlyingNumber", typeof(RectTransform),
                typeof(TextMeshProUGUI), typeof(FlyingNumber));

            var text = made.GetComponent<TextMeshProUGUI>();
            text.fontSize = 40f;
            text.alignment = TextAlignmentOptions.Center;
            if (face != null) text.font = face;

            var rect = (RectTransform)made.transform;
            rect.sizeDelta = new Vector2(260f, 56f);

            Wire(made.GetComponent<FlyingNumber>(), new[] { Pair("_text", text) });

            return Save(made, FlierPrefab).GetComponent<FlyingNumber>();
        }

        private static LogLine Line(TMP_FontAsset face)
        {
            var made = new GameObject("LogLine", typeof(RectTransform),
                typeof(TextMeshProUGUI), typeof(LogLine));

            var text = made.GetComponent<TextMeshProUGUI>();
            text.fontSize = 24f;
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.Normal;
            if (face != null) text.font = face;

            Wire(made.GetComponent<LogLine>(), new[] { Pair("_text", text) });

            return Save(made, LinePrefab).GetComponent<LogLine>();
        }

        private static GameObject Save(GameObject made, string path)
        {
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(made, path);
            Object.DestroyImmediate(made);

            return saved;
        }
    }
}
