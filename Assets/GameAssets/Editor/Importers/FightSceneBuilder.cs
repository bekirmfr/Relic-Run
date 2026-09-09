using System.Collections.Generic;
using GameLift.Scene;
using RelicRun.Core.Presentation;
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
        public const string SlotPrefab = "Assets/GameAssets/Game/Presentation/RelicSlot.prefab";
        public const string ChipPrefab = "Assets/GameAssets/Game/Presentation/StatChip.prefab";

        /// <summary>
        /// The fight the harness shows, which is AUTHORED and therefore never regenerated.
        /// </summary>
        /// <remarks>
        /// Made once if it is missing and left alone forever after. It exists because these
        /// settings used to be fields on the harness, and the harness lives inside the hierarchy
        /// this builder deletes and rebuilds — so every value somebody typed was thrown away by
        /// the next rebuild, quietly, leaving a fight nobody had asked for.
        ///
        /// The rule is the same one the scene config lives under: a generator may wire a
        /// reference to authored data and must never write through it.
        /// </remarks>
        public const string FightAsset = "Assets/GameAssets/Content/Fight.asset";

        /// <summary>
        /// A relic slot is a square, and this is its side in authored pixels.
        /// </summary>
        /// <remarks>
        /// The source draws 34, in a 390-wide shell. This layout is halved from a 1080 reference
        /// and lands near 480 units on a common phone, so 34 is close enough to the same share of
        /// the screen to keep the source's proportions without a conversion nobody can check.
        /// </remarks>
        private const float SlotSide = 34f;

        /// <summary>
        /// One white pixel, which is what every bar on this screen is actually made of.
        /// </summary>
        /// <remarks>
        /// Not decoration. An <c>Image</c> with NO sprite ignores its own type: Unity's
        /// <c>OnPopulateMesh</c> checks for a sprite first and falls back to a plain quad, so a
        /// Filled image with nothing in it draws a full rectangle and <c>fillAmount</c> does
        /// nothing whatsoever.
        ///
        /// That was the state of every bar in this scene. Both healths, both attack gauges and
        /// all three gauges on a relic slot were Filled, horizontal, correctly wired, and
        /// permanently full — a foe at zero hit points still had a full red bar, and the attack
        /// gauges never moved because nothing they were told could reach the screen.
        ///
        /// One pixel rather than Unity's built-in UISprite, which is rounded and nine-sliced. A
        /// bar three units tall with rounded ends is a bar with no ends.
        /// </remarks>
        public const string WhitePath = "Assets/GameAssets/Art/Sheets/White.png";

        /// <summary>
        /// Every size below is in authored pixels, and every text size is a multiple of eight.
        /// </summary>
        /// <remarks>
        /// The canvas is <c>ConstantPixelSize</c> at a whole factor, so one unit here is one, two
        /// or three screen pixels and never one and a half. The layout was first written against
        /// a 1080-wide reference and is halved.
        ///
        /// How many units wide that leaves is NOT fixed, and it is worth not forgetting: a
        /// 1080x2400 phone gets 2x and 540 units, a 1440x3088 one gets 3x and 480 — the bigger
        /// screen has the SMALLER canvas, because a whole factor that fits 390x844 three times
        /// divides the screen more finely. Anything anchored or stretched handles that; anything
        /// given a fixed width in units takes a different share of the screen on each device.
        ///
        /// Text sizes go through <see cref="PixelScale.Snap"/> rather than being typed, because
        /// the ui face is a bitmap baked at eight pixels and a size of twenty draws it at two and
        /// a half times. Twenty is the exact kind of number that looks reasonable in a source
        /// file and puts the smear straight back, so it is not possible to write one here.
        /// </remarks>
        private static int Text(int size)
        {
            return PixelScale.Snap(size);
        }

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

                // Before anything is built, because every bar needs it and a bar without it is
                // a bar that cannot show a fraction.
                White();

                FlyingNumber flier = Flier(face);
                LogLine line = Line(face);
                RelicSlot slot = Slot(face);
                StatChip chip = Chip(face);

                Replace(scene);
                Fit(scene, content, face, flier, line, slot, chip);

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

        /// <summary>
        /// The fight settings, made if they are not there and left alone if they are.
        /// </summary>
        /// <remarks>
        /// Never overwritten. That is the whole point of the asset, and it is the sort of rule a
        /// generator breaks by being helpful — "refresh it to the defaults" would throw away the
        /// interesting fight somebody had built to reproduce something.
        /// </remarks>
        private static FightSettings Fight()
        {
            var made = AssetDatabase.LoadAssetAtPath<FightSettings>(FightAsset);
            if (made != null) return made;

            ContentPaths.EnsureFolder(ContentPaths.Content);

            made = ScriptableObject.CreateInstance<FightSettings>();
            AssetDatabase.CreateAsset(made, FightAsset);

            Debug.Log("made " + FightAsset + " — the fight the harness shows. Edit it there: it " +
                      "is authored, so rebuilding the scene will not touch it.", made);

            return made;
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

        /// <summary>Whether pressing Play lands in the fight rather than in the menu.</summary>
        /// <remarks>Public so a tool can ask before offering to press Play for somebody.</remarks>
        public static bool OpensOnTheFight()
        {
            SceneServiceSettings settings = Settings(false);

            return settings != null && Starting(settings);
        }

        /// <summary>Points startup at the fight, if it is not pointed there already.</summary>
        public static void OpenOnTheFight()
        {
            SceneServiceSettings settings = Settings();
            if (settings == null || Starting(settings)) return;

            StartInTheFight();
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
            FlyingNumber flier, LogLine line, RelicSlot slot, StatChip chip)
        {
            var root = new GameObject(RootName);
            root.transform.SetParent(scene.transform, false);

            GameObject canvas = Canvas();
            canvas.transform.SetParent(root.transform, false);

            var view = canvas.AddComponent<CombatView>();

            // FIRST, so everything else draws in front of it. A canvas paints its children in
            // hierarchy order, so the backdrop is not a layer setting — it is a position, and one
            // that any later insertion could quietly take.
            HallView hall = Hall(canvas, content);

            GameObject foe = Panel(canvas, "Foe", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -260f), new Vector2(-40f, 230f));
            GameObject art = Box(foe, "Art", new Vector2(0f, 0.5f), new Vector2(96f, 96f),
                new Vector2(70f, 20f));
            GameObject foeName = Say(foe, face, "Foe", Text(17), TextAlignmentOptions.Left,
                new Vector2(0f, 95f), new Vector2(300f, 32f));
            GameObject foeHealth = Bar(foe, "Health", new Color(0.62f, 0.24f, 0.20f),
                new Vector2(0f, 60f));
            GameObject foeGauge = Bar(foe, "Gauge", new Color(0.75f, 0.60f, 0.25f),
                new Vector2(0f, 42f), 8f);
            // Right of the art and under the bars it describes. The sprite sits on the left of
            // this panel, so a left-aligned stat line would be printed straight through it.
            StatRow foeStats = Stats(foe, "Stats", chip, TextAnchor.MiddleRight,
                new Vector2(0f, 22f));
            RelicTray foeRelics = Carried(foe, content, slot);
            GameObject foeFliers = Anchor(foe, "Fliers", new Vector2(70f, 70f));

            GameObject delver = Panel(canvas, "Delver", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 130f), new Vector2(-40f, 150f));
            GameObject heroHealth = Bar(delver, "Health", new Color(0.35f, 0.62f, 0.35f),
                new Vector2(0f, 30f));
            GameObject heroGauge = Bar(delver, "Gauge", new Color(0.75f, 0.60f, 0.25f),
                new Vector2(0f, 12f), 8f);
            GameObject heroText = Say(delver, face, "100", Text(20), TextAlignmentOptions.Left,
                new Vector2(0f, 60f), new Vector2(200f, 32f));
            StatRow heroStats = Stats(delver, "Stats", chip, TextAnchor.MiddleLeft,
                new Vector2(0f, -44f));
            GameObject heroFliers = Anchor(delver, "Fliers", new Vector2(-130f, 60f));

            GameObject purse = Panel(canvas, "Purse", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-90f, -40f), new Vector2(150f, 40f));
            GameObject gold = Say(purse, face, "0", Text(20), TextAlignmentOptions.Right,
                Vector2.zero, new Vector2(140f, 32f));
            GameObject goldFliers = Anchor(purse, "Fliers", new Vector2(0f, -20f));

            GameObject log = Log(canvas);
            RelicTray tray = Tray(canvas, content, slot);

            Wire(view, new[]
            {
                Pair("_heroHealth", heroHealth.GetComponent<Image>()),
                Pair("_heroGauge", heroGauge.GetComponent<Image>()),
                Pair("_heroHealthText", heroText.GetComponent<TMP_Text>()),
                Pair("_heroFliers", (RectTransform)heroFliers.transform),
                Pair("_enemyHealth", foeHealth.GetComponent<Image>()),
                Pair("_enemyGauge", foeGauge.GetComponent<Image>()),
                Pair("_enemyName", foeName.GetComponent<TMP_Text>()),
                Pair("_enemyStats", foeStats),
                Pair("_heroStats", heroStats),
                Pair("_enemyRelics", foeRelics),
                Pair("_enemyArt", art.GetComponent<Image>()),
                Pair("_enemyFliers", (RectTransform)foeFliers.transform),
                Pair("_gold", gold.GetComponent<TMP_Text>()),
                Pair("_goldFliers", (RectTransform)goldFliers.transform),
                Pair("_log", (RectTransform)log.transform),
                Pair("_flier", flier),
                Pair("_line", line),
                Pair("_hall", hall),
                Pair("_tray", tray),
                Pair("_content", content),
            });

            var harness = root.AddComponent<FightHarness>();

            Wire(harness, new[]
            {
                Pair("_view", view),
                Pair("_content", content),
                Pair("_fight", Fight()),
            });

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
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(PixelCanvas));

            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();

            // Set here as well as at run time so the prefab is not misleading to open. What is
            // authored is a starting point; PixelCanvas replaces the factor with the one the
            // actual screen earns, every time the screen changes.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            // One canvas unit to one authored pixel, which is what keeps a point-filtered sprite
            // landing on whole pixels instead of between two of them.
            scaler.referencePixelsPerUnit = 100f;

            Wire(canvas.GetComponent<PixelCanvas>(), new[] { Pair("_scaler", scaler) });

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

        /// <summary>
        /// A bar that fills from the left, which is what both gauges and both healths are.
        /// </summary>
        /// <remarks>
        /// Its WIDTH comes from its panel, not from a number here. It was 300 units, which took
        /// 56% of the canvas on a 1080-wide phone and 62% on a 1440-wide one — the same bar
        /// showing a different amount of screen, because a whole scale factor gives the bigger
        /// screen the smaller canvas.
        ///
        /// A health bar is the one thing on this screen that has to be read as a PROPORTION. A
        /// delver judges how much trouble they are in by how much of the bar is left, so a bar
        /// whose full length is a different fraction of the screen on each device is quietly
        /// telling each of them something different.
        ///
        /// Only the height stays authored, because that is thickness rather than measure.
        /// </remarks>
        private static GameObject Bar(GameObject parent, string name, Color colour, Vector2 at,
            float height = 13f)
        {
            var bar = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)bar.transform;

            rect.SetParent(parent.transform, false);
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            // Stretched, so x is an inset from the panel's width rather than a width. Zero means
            // the panel's width exactly; the panel already holds the margin.
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = new Vector2(0f, at.y);

            Image image = bar.GetComponent<Image>();
            image.sprite = White();
            image.color = colour;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 1f;

            return bar;
        }

        /// <param name="stretch">
        /// Whether the box takes its width from the panel rather than from <paramref name="box"/>.
        /// Right-aligned text needs it: a fixed width right-aligns against an edge that is not
        /// the panel's, so the text drifts as the canvas changes size — and the canvas changes
        /// size on every device.
        /// </param>
        private static GameObject Say(GameObject parent, TMP_FontAsset face, string what,
            int size, TextAlignmentOptions how, Vector2 at, Vector2 box, bool stretch = false)
        {
            var said = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = (RectTransform)said.transform;

            rect.SetParent(parent.transform, false);
            rect.anchorMin = new Vector2(stretch ? 0f : 0f, 0.5f);
            rect.anchorMax = new Vector2(stretch ? 1f : 0f, 0.5f);
            rect.pivot = new Vector2(stretch ? 0.5f : 0f, 0.5f);
            rect.sizeDelta = stretch ? new Vector2(0f, box.y) : box;
            rect.anchoredPosition = stretch ? new Vector2(0f, at.y) : at;

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
            // Stretched to the same margin as the panels above and below it, so all three share
            // one left edge. It was 450 units wide and centred, which is 94% of the canvas at 3x
            // and 83% at 2x — nearly touching the edges on one device and inset on another, with
            // the log's own left edge landing somewhere different from the bars' every time.
            //
            // This one is not only tidiness. The width decides where a line WRAPS, so a fixed
            // width means a translated line breaks in a different place on every device, and
            // whether it fits at all is decided by which phone somebody happened to test on.
            GameObject panel = Panel(parent, "Log", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, -40f), new Vector2(-40f, 310f));

            // The panel CLIPS and a child inside it grows. This is the whole fix for a log that
            // squashed: a VerticalLayoutGroup given more children than fit does not overflow, it
            // divides the space it has — so at sixty lines in three hundred units every line was
            // allotted five, and they drew through one another.
            //
            // So the group moves to a child that sizes itself to its content and is pinned to the
            // BOTTOM. New lines push the whole column up past the top edge, where the mask cuts
            // them off, which is what a combat log is meant to look like.
            panel.AddComponent<RectMask2D>();

            var lines = new GameObject("Lines", typeof(RectTransform));
            var rect = (RectTransform)lines.transform;

            rect.SetParent(panel.transform, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;

            var group = lines.AddComponent<VerticalLayoutGroup>();
            group.childAlignment = TextAnchor.LowerLeft;
            group.childForceExpandHeight = false;
            group.childForceExpandWidth = true;
            group.childControlHeight = true;
            group.childControlWidth = true;
            group.spacing = 2f;

            var fitter = lines.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return lines;
        }

        /// <summary>
        /// The shelf, along the bottom, under the delver.
        /// </summary>
        /// <remarks>
        /// Under the delver because it is the delver's, which is the source's arrangement and
        /// also the readable one: a gauge filling on a relic and a gauge filling on the hero are
        /// the same kind of fact and belong near each other.
        ///
        /// Left-aligned rather than centred. Slots are added in inventory order as a run goes on,
        /// and a centred row would shuffle every icon sideways each time one arrived — so the
        /// relic a delver had learned the position of would move for the sake of symmetry.
        /// </remarks>
        private static RelicTray Tray(GameObject parent, GameContent content, RelicSlot slot)
        {
            // Above the Delver panel, which spans 55 to 205 units up from the bottom. At 230 the
            // tray sits clear of it rather than a few units into its top edge.
            GameObject panel = Panel(parent, "Tray", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 230f), new Vector2(-40f, SlotSide));

            var row = panel.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.LowerLeft;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.spacing = 4f;

            var tray = panel.AddComponent<RelicTray>();

            Wire(tray, new[]
            {
                Pair("_row", (RectTransform)panel.transform),
                Pair("_slot", slot),
                Pair("_content", content),
            });

            return tray;
        }

        /// <summary>
        /// A fighter's stats, as a row of chips.
        /// </summary>
        /// <remarks>
        /// The foe's align right and the delver's left, which is the source's arrangement: the
        /// two panels read outward from the middle of the screen, so each side's numbers sit
        /// nearest its own body rather than both crowding the same edge.
        /// </remarks>
        private static StatRow Stats(GameObject parent, string name, StatChip chip,
            TextAnchor align, Vector2 at)
        {
            GameObject panel = Panel(parent, name, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                at, new Vector2(0f, ChipTall));

            var row = panel.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = align;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.spacing = 8f;

            var made = panel.AddComponent<StatRow>();

            Wire(made, new[]
            {
                Pair("_row", (RectTransform)panel.transform),
                Pair("_chip", chip),
            });

            return made;
        }

        /// <summary>How tall one stat chip is, and therefore how tall a row of them is.</summary>
        private const float ChipTall = 16f;

        /// <summary>
        /// The hall behind the fight: a window, and a wider picture sliding inside it.
        /// </summary>
        /// <remarks>
        /// Two objects rather than one, because they do different jobs. The window is the size of
        /// the screen and MASKS; the art inside it is as wide as its own shape makes it at that
        /// height, which is almost always wider than the screen, and it is the art that moves.
        ///
        /// The picture is left disabled and empty. The halls are addressable — they are the
        /// reason Addressables is in this project, at four and a half megabytes for a set a floor
        /// uses one of — so what fills this arrives after the fight has already started, and a
        /// white rectangle waiting for it would be worse than a dark one.
        /// </remarks>
        private static HallView Hall(GameObject parent, GameContent content)
        {
            GameObject window = Panel(parent, "Hall", new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero);

            window.AddComponent<RectMask2D>();

            var art = new GameObject("Art", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)art.transform;

            rect.SetParent(window.transform, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);

            Image picture = art.GetComponent<Image>();
            picture.color = Color.white;
            picture.enabled = false;

            // Nothing behind the hall reads a click, and the hall itself is scenery. Left on,
            // the full-screen window would swallow every press meant for what is drawn over it.
            picture.raycastTarget = false;

            var view = window.AddComponent<HallView>();

            Wire(view, new[]
            {
                Pair("_window", (RectTransform)window.transform),
                Pair("_art", rect),
                Pair("_picture", picture),
                Pair("_content", content),
            });

            return view;
        }

        /// <summary>
        /// What the foe is carrying, along the bottom of its panel.
        /// </summary>
        /// <remarks>
        /// The same slot prefab the delver's shelf uses, and no meters — a foe's relic has no
        /// cadence to show, because every counter in the snapshot belongs to the delver. The tray
        /// blanks its gauges when it binds, so one that is begun and never shown is a row of
        /// icons, which is exactly what this wants.
        ///
        /// Right-aligned, unlike the delver's. It is the source's arrangement and it reads: the
        /// two shelves grow away from each other rather than both creeping rightward from the
        /// same edge, so at a glance it is obvious which belongs to whom.
        /// </remarks>
        private static RelicTray Carried(GameObject parent, GameContent content, RelicSlot slot)
        {
            // Inside the panel near its foot, not below it: the foe's panel ends where the empty
            // middle of the screen begins, and a row hung off its bottom edge would be floating
            // in that gap rather than belonging to the foe.
            GameObject panel = Panel(parent, "Carried", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 25f), new Vector2(0f, SlotSide));

            var row = panel.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.LowerRight;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.spacing = 4f;

            var tray = panel.AddComponent<RelicTray>();

            Wire(tray, new[]
            {
                Pair("_row", (RectTransform)panel.transform),
                Pair("_slot", slot),
                Pair("_content", content),
            });

            return tray;
        }

        /// <summary>
        /// The white pixel, written once and loaded ever after.
        /// </summary>
        /// <remarks>
        /// Generated rather than committed as art, because it is not art — it is a consequence of
        /// how <c>Image</c> works, and a checked-in PNG of one white pixel is a thing nobody can
        /// look at and understand.
        ///
        /// Point filtered and uncompressed, like everything else on this screen. A compressed
        /// single pixel is not smaller and a filtered one is not white.
        /// </remarks>
        private static Sprite White()
        {
            var found = AssetDatabase.LoadAssetAtPath<Sprite>(WhitePath);
            if (found != null) return found;

            ContentPaths.EnsureFolder(ContentPaths.Sheets);

            var pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();

            System.IO.File.WriteAllBytes(
                System.IO.Path.Combine(ContentPaths.ProjectRoot, WhitePath),
                pixel.EncodeToPNG());

            Object.DestroyImmediate(pixel);
            AssetDatabase.ImportAsset(WhitePath, ImportAssetOptions.ForceSynchronousImport);

            var importer = AssetImporter.GetAtPath(WhitePath) as TextureImporter;

            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(WhitePath);
        }

        /* ---------- the prefabs ---------- */

        /// <summary>
        /// One stat: a slot for an icon, a dim label, and the number itself.
        /// </summary>
        /// <remarks>
        /// The icon slot is built and left empty, because the source labels its stats with words
        /// and draws no glyph for any of them. It is here so that the day somebody draws five
        /// little icons, the row takes them without being rebuilt — and a chip with no sprite
        /// hides its image rather than reserving a blank square.
        /// </remarks>
        private static StatChip Chip(TMP_FontAsset face)
        {
            var made = new GameObject("StatChip", typeof(RectTransform),
                typeof(HorizontalLayoutGroup), typeof(StatChip));

            var rect = (RectTransform)made.transform;
            rect.sizeDelta = new Vector2(0f, ChipTall);

            var row = made.GetComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.childControlWidth = true;
            row.childControlHeight = false;
            row.spacing = 2f;

            var fitter = made.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            GameObject icon = Box(made, "Icon", new Vector2(0.5f, 0.5f),
                new Vector2(ChipTall, ChipTall), Vector2.zero);

            // A layout group with childControlWidth sizes children from their PREFERRED width,
            // and a bare Image has none — so without this the icon would be square in the
            // inspector and nothing at all in the row, the day a sprite is finally bound to it.
            var space = icon.AddComponent<LayoutElement>();
            space.preferredWidth = ChipTall;
            space.preferredHeight = ChipTall;

            GameObject label = Say(made, face, "ATK", Text(8), TextAlignmentOptions.Left,
                Vector2.zero, new Vector2(24f, ChipTall));

            GameObject value = Say(made, face, "0", Text(8), TextAlignmentOptions.Left,
                Vector2.zero, new Vector2(24f, ChipTall));

            label.name = "Label";
            value.name = "Value";

            Wire(made.GetComponent<StatChip>(), new[]
            {
                Pair("_icon", icon.GetComponent<Image>()),
                Pair("_label", label.GetComponent<TMP_Text>()),
                Pair("_value", value.GetComponent<TMP_Text>()),
            });

            return Save(made, ChipPrefab).GetComponent<StatChip>();
        }

        /// <summary>
        /// One relic slot: an icon in the middle and three gauges on the edges.
        /// </summary>
        /// <remarks>
        /// The crowding is the design problem. Four facts have to fit in a 34-unit square — what
        /// the relic is, how close it is to firing, whether a second clock is running, and how
        /// much budget is left — so the source puts three of them on the EDGES and leaves the
        /// middle to the picture. A delver then reads the shape without reading anything: a gold
        /// line creeping along the bottom, a violet hairline above it when a copy is counting two
        /// things, a bar down the right that shortens as a relic runs out.
        ///
        /// Every bar is a FILLED image, because a Simple one ignores fillAmount and sits there
        /// full — the same trap the health bars are gated against, and worse here, where a full
        /// bar means "about to fire".
        /// </remarks>
        private static RelicSlot Slot(TMP_FontAsset face)
        {
            var made = new GameObject("RelicSlot", typeof(RectTransform), typeof(Image),
                typeof(RelicSlot));

            var rect = (RectTransform)made.transform;
            rect.sizeDelta = new Vector2(SlotSide, SlotSide);

            // The plate the icon sits on. Its colour is the widget's business and is set every
            // time the slot is drawn, because it is also how a spent relic is greyed out.
            Image frame = made.GetComponent<Image>();
            frame.color = new Color(0.07f, 0.06f, 0.05f, 0.85f);

            GameObject icon = Box(made, "Icon", new Vector2(0.5f, 0.5f),
                new Vector2(SlotSide - 6f, SlotSide - 6f), Vector2.zero);
            icon.GetComponent<Image>().enabled = true;
            icon.GetComponent<Image>().preserveAspect = true;

            // Bottom edge, full width: progress toward the next time this fires.
            GameObject charge = Edge(made, "Charge", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1.5f), new Vector2(0f, 3f), Image.FillMethod.Horizontal,
                (int)Image.OriginHorizontal.Left);

            // A hairline above it, for a copy counting two different things at once.
            GameObject hairline = Edge(made, "Hairline", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 5f), new Vector2(0f, 2f), Image.FillMethod.Horizontal,
                (int)Image.OriginHorizontal.Left);

            // Right edge, draining downward, because a budget runs out rather than fills up.
            GameObject uses = Edge(made, "Uses", new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-1.5f, 0f), new Vector2(3f, 0f), Image.FillMethod.Vertical,
                (int)Image.OriginVertical.Bottom);

            GameObject badge = Say(made, face, "", Text(8), TextAlignmentOptions.TopRight,
                Vector2.zero, new Vector2(SlotSide, SlotSide));

            Wire(made.GetComponent<RelicSlot>(), new[]
            {
                Pair("_frame", frame),
                Pair("_icon", icon.GetComponent<Image>()),
                Pair("_badge", badge.GetComponent<TMP_Text>()),
                Pair("_charge", charge.GetComponent<Image>()),
                Pair("_hairline", hairline.GetComponent<Image>()),
                Pair("_uses", uses.GetComponent<Image>()),
            });

            return Save(made, SlotPrefab).GetComponent<RelicSlot>();
        }

        /// <summary>A thin filled bar pinned along one edge of a slot.</summary>
        private static GameObject Edge(GameObject parent, string name, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 at, Vector2 size, Image.FillMethod how, int from)
        {
            var bar = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)bar.transform;

            rect.SetParent(parent.transform, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = at;

            Image image = bar.GetComponent<Image>();
            image.sprite = White();
            image.color = Color.white;
            image.type = Image.Type.Filled;
            image.fillMethod = how;
            image.fillOrigin = from;
            image.fillAmount = 1f;

            return bar;
        }


        private static FlyingNumber Flier(TMP_FontAsset face)
        {
            var made = new GameObject("FlyingNumber", typeof(RectTransform),
                typeof(TextMeshProUGUI), typeof(FlyingNumber));

            var text = made.GetComponent<TextMeshProUGUI>();
            text.fontSize = Text(20);
            text.alignment = TextAlignmentOptions.Center;
            if (face != null) text.font = face;

            var rect = (RectTransform)made.transform;
            rect.sizeDelta = new Vector2(130f, 32f);

            Wire(made.GetComponent<FlyingNumber>(), new[] { Pair("_text", text) });

            return Save(made, FlierPrefab).GetComponent<FlyingNumber>();
        }

        private static LogLine Line(TMP_FontAsset face)
        {
            var made = new GameObject("LogLine", typeof(RectTransform),
                typeof(TextMeshProUGUI), typeof(LogLine));

            var text = made.GetComponent<TextMeshProUGUI>();
            text.fontSize = Text(12);
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
