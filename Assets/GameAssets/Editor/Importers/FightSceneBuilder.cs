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
using static RelicRun.Editor.Importers.Scenery;

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
        public const string MotePrefab = "Assets/GameAssets/Game/Presentation/Mote.prefab";

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
                Mote mote = Speck();

                Replace(scene);
                Fit(scene, content, face, flier, line, slot, chip, mote);

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
            FlyingNumber flier, LogLine line, RelicSlot slot, StatChip chip, Mote mote)
        {
            var root = new GameObject(RootName);
            root.transform.SetParent(scene.transform, false);

            // The fight's prefab has carried a camera since before any of this was generated, so
            // it is found rather than made — a second one would fight the first for the frame.
            Camera eye = scene.GetComponentInChildren<Camera>(true);

            if (eye == null)
            {
                eye = Eye(root);
                Debug.Log("the fight scene had no camera, so one was made for it");
            }

            // The inherited camera came with an ear on it, from back when every screen carried
            // its own. It is taken off here rather than left alone: the app's root scope holds
            // the only one now, and a second would be a second — see Scenery.Eye.
            AudioListener stale = eye.GetComponent<AudioListener>();
            if (stale != null) Object.DestroyImmediate(stale, true);

            GameObject canvas = Canvas(eye);
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

            HeroView delverArt = Delver(canvas);

            GameObject purse = Panel(canvas, "Purse", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-90f, -40f), new Vector2(150f, 40f));
            GameObject gold = Say(purse, face, "0", Text(20), TextAlignmentOptions.Right,
                Vector2.zero, new Vector2(140f, 32f));
            GameObject goldFliers = Anchor(purse, "Fliers", new Vector2(0f, -20f));

            GameObject log = Log(canvas);
            RelicTray tray = Tray(canvas, content, slot);

            // The row of foes belongs to the FIGHT, so it is built before the stages: a draft
            // covers the fight, and the row is part of what is being covered.
            FoeQueueView queue = Queue(canvas, content, face);

            // The stages, and then the rail. Hierarchy order is paint order on a canvas: a stage
            // is a full-screen panel that covers the fight while it is up, and the rail is built
            // after it so that where the delver IS stays visible on top of whatever they are
            // being asked. Both are last, so nothing built before them can be hidden by accident.
            DraftStage draft = Draft(canvas, content, face);
            FloorRailView railing = Rail(canvas, face);

            // Last of all, over everything including the rail. The card a fight opens on is the
            // one thing in this game that covers the whole screen on purpose.
            IntroBanner intro = Intro(canvas, content, face);

            Wire(view, new[]
            {
                Pair("_heroHealth", heroHealth.GetComponent<Image>()),
                Pair("_heroGauge", heroGauge.GetComponent<Image>()),
                Pair("_heroHealthText", heroText.GetComponent<TMP_Text>()),
                Pair("_heroFliers", (RectTransform)heroFliers.transform),
                Pair("_heroArt", delverArt),
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
                Pair("_mote", mote),
                Pair("_hall", hall),
                Pair("_tray", tray),
                Pair("_content", content),
                Pair("_intro", intro),
                Pair("_queue", queue),
            });

            // The authored fight, which is now a FALLBACK rather than the fight. It carries the
            // settings asset and nothing else: what it used to do — resolve a floor, drive the
            // playback, own the screen — is the scene's, so that a delve ordered by the menu and
            // a fight typed into an asset go down exactly the same road.
            var harness = root.AddComponent<FightHarness>();

            Wire(harness, new[] { Pair("_fight", Fight()) });

            // On the SCENE's root, not on the fight's. SceneService looks for one of these on the
            // object it instantiates, and it will not go hunting through the children for it.
            var entry = scene.GetComponent<FightScene>();
            if (entry == null) entry = scene.AddComponent<FightScene>();

            Wire(entry, new[]
            {
                Pair("_view", view),
                Pair("_content", content),
                Pair("_harness", harness),
                Pair("_rail", railing),
            });

            // An array, so it cannot be wired by Pair like everything else. It is also the one
            // reference here that GROWS: a stage per stop, added as each is built, and the scene
            // finds the right one by asking rather than by which slot it landed in.
            Stages(entry, new RunStage[] { draft });
        }

        /* ---------- wiring ---------- */

        /* ---------- the pieces ---------- */

        /// <summary>How big the delver is drawn.</summary>
        /// <remarks>
        /// The source's <c>120 / PACK.SIZE</c>, which for a thirty-two square is a shade under
        /// four times. Point-filtered, so that is four screen pixels a source pixel and not a
        /// blur — which is the whole reason this game keeps a pixel canvas.
        /// </remarks>
        private const float DelverSide = 120f;

        /// <summary>
        /// The delver, opposite the foe.
        /// </summary>
        /// <remarks>
        /// Bottom right, where the foe is top left. That is the source's arrangement and it is
        /// the readable one: the two bodies sit at opposite corners with the log running between
        /// them, so a delver's eye goes from what is hitting them to what is happening to them
        /// without crossing anything.
        ///
        /// Clear of the row of foes, which is bottom LEFT for the same reason — five tiles reach
        /// a little over two hundred units and this starts past three hundred.
        /// </remarks>
        private static HeroView Delver(GameObject parent)
        {
            GameObject panel = Panel(parent, "Delver Art", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-90f, 320f), new Vector2(DelverSide, DelverSide));

            var art = new GameObject("Art", typeof(RectTransform), typeof(RawImage));
            var rect = (RectTransform)art.transform;

            rect.SetParent(panel.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            // Nothing to draw until a fight dresses it. Enabled with a null texture, a RawImage
            // draws a white square — which is exactly what a delver should not look like.
            RawImage drawn = art.GetComponent<RawImage>();
            drawn.enabled = false;

            var view = panel.AddComponent<HeroView>();

            Wire(view, new[] { Pair("_art", drawn) });

            return view;
        }


        /// <summary>
        /// The row of foes on this floor, along the bottom of the fight.
        /// </summary>
        /// <remarks>
        /// Under the shelf, because it is the same kind of fact one row down: what the delver has
        /// and then what is left to spend it on. Left-aligned for the same reason the shelf is —
        /// the tiles are struck out one at a time as a floor is fought, and a centred row would
        /// shuffle the survivors sideways on every kill.
        /// </remarks>
        private static FoeQueueView Queue(GameObject parent, GameContent content, TMP_FontAsset face)
        {
            GameObject panel = Panel(parent, "Queue", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 272f), new Vector2(-40f, FoeQueues.Side));

            var row = panel.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.spacing = 6f;

            GameObject said = Say(panel, face, "NEXT", Text(9), TextAlignmentOptions.Left,
                Vector2.zero, new Vector2(38f, FoeQueues.Side));

            said.GetComponent<TMP_Text>().color = new Color(0.655f, 0.604f, 0.510f);

            GameObject tile = Box(panel, "Foe", new Vector2(0.5f, 0.5f),
                new Vector2(FoeQueues.Side, FoeQueues.Side), Vector2.zero);

            Image ground = tile.GetComponent<Image>();
            ground.sprite = White();
            ground.enabled = true;

            tile.AddComponent<Outline>();

            // The foe, and then the line through it. The view finds them by index in this order.
            GameObject art = Box(tile, "Art", new Vector2(0.5f, 0.5f),
                new Vector2(FoeQueues.Glyph, FoeQueues.Glyph), Vector2.zero);

            GameObject strike = Box(tile, "Strike", new Vector2(0.5f, 0.5f),
                new Vector2(FoeQueues.Side - 4f, 2f), Vector2.zero);

            Image line = strike.GetComponent<Image>();
            line.sprite = White();
            line.enabled = true;
            line.preserveAspect = false;

            // Struck through at an angle, which is what makes it read as crossed out rather than
            // as an underline somebody left on.
            strike.transform.localRotation = Quaternion.Euler(0f, 0f, 38f);

            art.GetComponent<Image>().preserveAspect = true;

            tile.SetActive(false);

            var view = panel.AddComponent<FoeQueueView>();

            Wire(view, new[]
            {
                Pair("_label", said),
                Pair("_tiles", (RectTransform)panel.transform),
                Pair("_tile", ground),
                Pair("_content", content),
            });

            return view;
        }

        /// <summary>
        /// The card a fight opens on.
        /// </summary>
        /// <remarks>
        /// A full-screen overlay, opaque, because the point of it is that everything else stops.
        /// It is the largest anything in this game is ever drawn — a 232-unit portrait on a
        /// 390-wide shell — and that scale is the whole announcement.
        /// </remarks>
        private static IntroBanner Intro(GameObject parent, GameContent content, TMP_FontAsset face)
        {
            GameObject panel = Panel(parent, "Intro", new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero);

            var ground = panel.AddComponent<Image>();
            ground.sprite = White();
            ground.color = new Color(0.031f, 0.027f, 0.020f, 0.98f);

            GameObject rank = Say(panel, face, "FLOOR BOSS", Text(9),
                TextAlignmentOptions.Center, new Vector2(0f, 210f),
                new Vector2(0f, 20f), true);

            GameObject frame = Box(panel, "Frame", new Vector2(0.5f, 0.5f),
                new Vector2(240f, 240f), new Vector2(0f, 60f));

            Image walls = frame.GetComponent<Image>();
            walls.sprite = White();
            walls.color = new Color(0.098f, 0.082f, 0.063f);
            walls.enabled = true;
            walls.preserveAspect = false;

            frame.AddComponent<Outline>();

            // The glow, which only the bottom of a run wears. A Shadow spread evenly rather than
            // offset is as close as a UI graphic gets to one without a second material.
            var glow = frame.AddComponent<Shadow>();
            glow.effectColor = new Color(0.890f, 0.702f, 0.255f, 0.5f);
            glow.effectDistance = new Vector2(6f, -6f);
            glow.enabled = false;

            GameObject art = Box(frame, "Art", new Vector2(0.5f, 0.5f),
                new Vector2(IntroBanner.Portrait, IntroBanner.Portrait), Vector2.zero);

            art.GetComponent<Image>().preserveAspect = true;

            GameObject named = Say(panel, face, "Foe", Text(28), TextAlignmentOptions.Center,
                new Vector2(0f, -100f), new Vector2(0f, 40f), true);

            GameObject stats = Say(panel, face, "0 HP", Text(11), TextAlignmentOptions.Center,
                new Vector2(0f, -136f), new Vector2(0f, 24f), true);

            GameObject blocks = Say(panel, face, "It blocks the way.", Text(12),
                TextAlignmentOptions.Center, new Vector2(0f, -162f), new Vector2(0f, 24f), true);

            var banner = panel.AddComponent<IntroBanner>();

            Wire(banner, new[]
            {
                Pair("_rank", rank.GetComponent<TMP_Text>()),
                Pair("_frame", walls),
                Pair("_art", art.GetComponent<Image>()),
                Pair("_name", named.GetComponent<TMP_Text>()),
                Pair("_stats", stats.GetComponent<TMP_Text>()),
                Pair("_blocks", blocks.GetComponent<TMP_Text>()),
                Pair("_content", content),
            });

            // Off, like every overlay. A fight raises it; the prefab should open on the fight.
            panel.SetActive(false);

            return banner;
        }


        /// <summary>How tall one relic on the draft table is.</summary>
        private const float CardHeight = 92f;

        /// <summary>The ground a stage is drawn on: the game's own dark, and opaque.</summary>
        /// <remarks>
        /// Opaque on purpose. A stage covers the fight rather than floating over it, and a
        /// translucent one would leave a half-visible foe behind a draft — which reads as a fight
        /// still happening while the delver is being asked to shop.
        /// </remarks>
        private static readonly Color Ground = new Color(0.07f, 0.06f, 0.05f, 1f);

        /// <summary>
        /// The rail across the top: where the delver is in the descent.
        /// </summary>
        /// <remarks>
        /// A label on the left and thirteen nodes on the right, laid out by Unity. The node is a
        /// template rather than thirteen authored objects, because how big each one is drawn is
        /// <c>FloorRails</c>' answer and changes as the delver walks — thirteen authored sizes
        /// would be thirteen chances to disagree with it.
        /// </remarks>
        private static FloorRailView Rail(GameObject parent, TMP_FontAsset face)
        {
            GameObject panel = Panel(parent, "Rail", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -34f), new Vector2(-40f, 24f));

            GameObject line = Say(panel, face, "FLOOR 1 / 13", Text(11),
                TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(120f, 24f));

            // Right-aligned, so the deepest floors sit against the same edge whatever the rail
            // is showing. The nodes change size as the delver walks — that is the whole encoding
            // — and a centred row would slide sideways under them on every floor.
            GameObject nodes = Panel(panel, "Nodes", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, 0f), new Vector2(0f, 24f));

            var row = nodes.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = TextAnchor.MiddleRight;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.spacing = 6f;

            GameObject node = Box(nodes, "Node", new Vector2(0.5f, 0.5f),
                new Vector2(FloorRails.PlainAway, FloorRails.PlainAway), Vector2.zero);

            Image dot = node.GetComponent<Image>();
            dot.sprite = White();
            dot.enabled = true;

            // The event marker, which the view finds as the node's FIRST child and only
            // recolours. Above the node rather than on it, so a node's size still reads as what
            // kind of floor it is with a marker sitting over it.
            GameObject mark = Box(node, "Event", new Vector2(0.5f, 1f), new Vector2(4f, 4f),
                new Vector2(0f, 6f));

            Image ink = mark.GetComponent<Image>();
            ink.sprite = White();
            ink.enabled = true;

            // Off, so it is a template and not a fourteenth floor. An inactive child is skipped
            // by the layout group as well as by the drawing, which is what makes this work.
            node.SetActive(false);

            var view = panel.AddComponent<FloorRailView>();

            Wire(view, new[]
            {
                Pair("_line", line.GetComponent<TMP_Text>()),
                Pair("_nodes", (RectTransform)nodes.transform),
                Pair("_node", dot),
            });

            return view;
        }

        /// <summary>
        /// The draft: the relics on offer, and what a fresh offer would cost.
        /// </summary>
        /// <remarks>
        /// A full-screen panel that covers the fight while the question is up. The cards are
        /// spawned from a template for the same reason the rail's nodes are: how many are offered
        /// is the delver's LEVEL talking — two, and three from level ten — so nothing authored
        /// here counts them.
        /// </remarks>
        private static DraftStage Draft(GameObject parent, GameContent content, TMP_FontAsset face)
        {
            GameObject panel = Panel(parent, "Draft", new Vector2(0f, 0f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero);

            var ground = panel.AddComponent<Image>();
            ground.sprite = White();
            ground.color = Ground;

            GameObject head = Panel(panel, "Head", new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -90f), new Vector2(-40f, 40f));

            GameObject title = Say(head, face, "TAKE A RELIC", Text(19),
                TextAlignmentOptions.Center, Vector2.zero, new Vector2(0f, 40f), true);

            GameObject cards = Panel(panel, "Cards", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, 20f), new Vector2(-40f, 0f));

            var stack = cards.AddComponent<VerticalLayoutGroup>();
            stack.childAlignment = TextAnchor.UpperCenter;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            stack.childControlWidth = true;
            stack.childControlHeight = true;
            stack.spacing = 12f;

            // The table is as tall as what is on it. Two relics are offered until level ten and
            // three after, and a relic's description is three lines in English and five in
            // German — so a table with a height typed into it would be right for one offer in
            // one language.
            var hugs = cards.AddComponent<ContentSizeFitter>();
            hugs.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Button card = Card(cards, face);

            GameObject foot = Panel(panel, "Foot", new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 120f), new Vector2(-120f, 40f));

            GameObject reroll = Press(foot, "Reroll", face, "REROLL", Text(15),
                new Color(0.20f, 0.17f, 0.12f), new Color(0.89f, 0.70f, 0.25f), 40f);

            var stage = panel.AddComponent<DraftStage>();

            Wire(stage, new[]
            {
                Pair("_title", title.GetComponent<TMP_Text>()),
                Pair("_cards", (RectTransform)cards.transform),
                Pair("_card", card),
                Pair("_reroll", reroll.GetComponent<Button>()),
                Pair("_rerollLabel", reroll.GetComponentInChildren<TMP_Text>(true)),
                Pair("_content", content),
            });

            // Off, like every stage. Which one is up is a fact about what the run has stopped to
            // ask, so a stage left showing in the prefab would be a screen nobody asked for —
            // and, on a scene that opens straight into a fight, one covering it.
            panel.SetActive(false);

            return stage;
        }

        /// <summary>
        /// One relic on the draft table: an icon, a name, what it does, and what it chains with.
        /// </summary>
        /// <remarks>
        /// The three lines are read back by ORDER rather than by name, so the order they are made
        /// in here is the order the stage dresses them in. The icon is deliberately not the
        /// button's own graphic: the press is taken by the card's ground, which covers the whole
        /// card, where an icon covers forty units of it.
        /// </remarks>
        private static Button Card(GameObject parent, TMP_FontAsset face)
        {
            var made = new GameObject("Card", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)made.transform;

            rect.SetParent(parent.transform, false);
            rect.sizeDelta = new Vector2(0f, CardHeight);

            Image ground = made.GetComponent<Image>();
            ground.sprite = White();
            ground.color = new Color(0.12f, 0.11f, 0.09f);

            Button press = made.GetComponent<Button>();
            press.targetGraphic = ground;

            // The icon beside the words, and both measured by Unity. Authored offsets were what
            // this had first, and a description that wrapped to three lines printed itself
            // straight through the relic's name — which is exactly the bug a layout cannot have.
            var beside = made.AddComponent<HorizontalLayoutGroup>();
            beside.childAlignment = TextAnchor.UpperLeft;
            beside.childForceExpandWidth = false;
            beside.childForceExpandHeight = false;
            beside.childControlWidth = true;
            beside.childControlHeight = true;
            beside.spacing = 12f;
            beside.padding = new RectOffset(12, 12, 10, 10);

            GameObject icon = Box(made, "Icon", new Vector2(0f, 0.5f), new Vector2(44f, 44f),
                Vector2.zero);

            var kept = icon.AddComponent<LayoutElement>();
            kept.minWidth = 44f;
            kept.preferredWidth = 44f;
            kept.minHeight = 44f;
            kept.preferredHeight = 44f;

            var lines = new GameObject("Lines", typeof(RectTransform));
            lines.transform.SetParent(made.transform, false);

            var down = lines.AddComponent<VerticalLayoutGroup>();
            down.childAlignment = TextAnchor.UpperLeft;
            down.childForceExpandWidth = true;
            down.childForceExpandHeight = false;
            down.childControlWidth = true;
            down.childControlHeight = true;
            down.spacing = 3f;

            // The one child allowed to take whatever is left. Without it the words are as wide as
            // they happen to be, and a short description would leave the card half empty while a
            // long one ran off the end of it.
            var takes = lines.AddComponent<LayoutElement>();
            takes.flexibleWidth = 1f;

            // In this order, because the stage dresses them by ORDER: name, what it does, and
            // then the line that says what it would chain with.
            Say(lines, face, "Relic", Text(17), TextAlignmentOptions.TopLeft,
                Vector2.zero, new Vector2(0f, 24f), true);

            Say(lines, face, "what it does", Text(13), TextAlignmentOptions.TopLeft,
                Vector2.zero, new Vector2(0f, 40f), true);

            Say(lines, face, "FAMILY", Text(11), TextAlignmentOptions.TopLeft,
                Vector2.zero, new Vector2(0f, 18f), true);

            made.SetActive(false);

            return press;
        }

        /// <summary>
        /// Hands the scene its stages.
        /// </summary>
        /// <remarks>
        /// By hand rather than through <c>Wire</c>, because an ARRAY of references is not one
        /// reference: a serialized array has to be sized before its elements exist, and the
        /// generic helper only knows how to set a single object.
        /// </remarks>
        private static void Stages(FightScene scene, RunStage[] stages)
        {
            var serialized = new SerializedObject(scene);

            SerializedProperty property = serialized.FindProperty("_stages");

            if (property == null)
            {
                Debug.LogError("FightScene has no field called _stages — the builder and the " +
                               "scene have drifted apart");
                return;
            }

            property.arraySize = stages.Length;

            for (var i = 0; i < stages.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = stages[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
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

        /* ---------- the prefabs ---------- */

        /// <summary>
        /// One speck of grit or blood: a coloured square, three units across.
        /// </summary>
        /// <remarks>
        /// Square, and drawn from the same white pixel every bar is made of. A round speck would
        /// need a sprite somebody has to draw, and a body made of eight-pixel glyphs does not
        /// come apart into circles.
        ///
        /// It does not raycast. Eight of these land on top of a fight several times a second,
        /// and any one of them swallowing a press would be a button that failed for no reason
        /// anybody could reproduce.
        /// </remarks>
        private static Mote Speck()
        {
            var made = new GameObject("Mote", typeof(RectTransform), typeof(Image), typeof(Mote));

            var rect = (RectTransform)made.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(MoteSide, MoteSide);

            Image speck = made.GetComponent<Image>();
            speck.sprite = White();
            speck.color = Color.white;
            speck.raycastTarget = false;

            Wire(made.GetComponent<Mote>(), new[] { Pair("_speck", speck) });

            return Save(made, MotePrefab).GetComponent<Mote>();
        }

        /// <summary>Three units, which is one pixel of the ui face at its usual size.</summary>
        private const float MoteSide = 3f;

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
    }
}
