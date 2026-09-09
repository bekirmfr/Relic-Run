using System.Collections.Generic;
using GameLift.Scene;
using RelicRun.Core.Presentation;
using RelicRun.Game.Data;
using RelicRun.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static RelicRun.Editor.Importers.Scenery;

namespace RelicRun.Editor.Importers
{
    /// <summary>
    /// Builds every screen that is not a run, so nobody has to drag thirty references in by hand.
    /// </summary>
    /// <remarks>
    /// The same arrangement the fight is built under: a scene here is a PREFAB under
    /// <c>Assets/Scenes/</c>, addressed, named by a key on a <c>SceneConfig</c>, and listed in the
    /// settings asset the service reads. Miss any one of those three and the failure is the same
    /// shape — <c>LoadScene</c> is called, nothing happens, and nothing is said about it.
    ///
    /// One scene holds every meta screen as a panel, which is the source's arrangement: they are
    /// a few hundred objects between them, and loading each as its own addressable prefab would
    /// buy a wait and a flicker per navigation for nothing.
    ///
    /// It writes its own prefab rather than editing the GameLift sample's <c>MenuScene</c>. That
    /// menu is scaffolding this game does not want, and deleting somebody else's screen to make
    /// room is a worse habit than leaving it on disk unused. What this DOES take over is the key:
    /// <c>menu_scene</c> is where the app starts.
    ///
    /// A scaffold, not a screen. No art, no frames, and none of the twenty-six animated motes the
    /// source draws behind its title. What it is for is seeing that a delver's save, their level,
    /// their clock, the halls they have opened and the buttons between them are all real.
    /// </remarks>
    public static class MetaSceneBuilder
    {
        /// <summary>The scene prefab this writes.</summary>
        public const string ScenePath = "Assets/Scenes/TitleScene.prefab";

        /// <summary>What the built hierarchy is called, so a rebuild replaces it.</summary>
        public const string RootName = "Meta";

        /// <summary>The config naming the key the app starts on. The sample's own, repointed.</summary>
        public const string ConfigPath = "Assets/Samples/Game Lift/1.0.0/Starter/" +
            "ScriptableObjects/SceneServiceSettings/MenuSceneConfig.asset";

        public const string SettingsPath = "Assets/Samples/Game Lift/1.0.0/Starter/" +
            "ScriptableObjects/SceneServiceSettings/SceneServiceSettings.asset";

        /// <summary>One hall's square in the dungeon grid, as a prefab the panel spawns.</summary>
        public const string TilePrefab = "Assets/GameAssets/Game/Presentation/HallTile.prefab";

        /// <summary>
        /// How far every row is inset from the canvas edges.
        /// </summary>
        /// <remarks>
        /// Nothing here is given a fixed width. Every panel is inset instead, because a whole
        /// scale factor gives a 1440-wide phone 480 units and a 1080-wide one 540 — a fixed width
        /// takes a different share of each.
        /// </remarks>
        private const float Margin = 24f;

        /// <summary>A hall's square, in authored pixels. The source draws 72.</summary>
        private const float TileSide = 72f;

        private const string BuildItem = "Tools/Relic Run/Build Menu Scene";

        [MenuItem(BuildItem, priority = 122)]
        public static void Build()
        {
            var content = AssetDatabase.LoadAssetAtPath<GameContent>(ContentPaths.GameContentAsset);

            if (content == null)
            {
                Debug.LogError("no content — run Tools ▸ Relic Run ▸ Import Content first");
                return;
            }

            TMP_FontAsset face = content.Fonts == null ? null : content.Fonts.For(FontBook.Ui);

            if (face == null)
            {
                Debug.LogWarning("no ui face in the content, so the menu will use whatever " +
                                 "TextMeshPro defaults to");
            }

            Make();

            GameObject scene = PrefabUtility.LoadPrefabContents(ScenePath);

            if (scene == null)
            {
                Debug.LogError("no scene prefab at " + ScenePath);
                return;
            }

            try
            {
                // Before anything is built, because every bar and every button face needs it and
                // an Image without a sprite ignores its own type entirely.
                White();

                HallTileView tile = HallTile(face);

                Replace(scene);
                Fit(scene, face, tile);

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

            Debug.Log("built the menu into " + ScenePath + ". It is what " + SceneKeys.MenuScene +
                      " loads.", AssetDatabase.LoadAssetAtPath<GameObject>(ScenePath));
        }

        /// <summary>
        /// The scene prefab itself, made once if it is not there.
        /// </summary>
        /// <remarks>
        /// The scope comes with it: <see cref="MetaScene"/> requires one, and AddComponent
        /// honours that. Named there rather than here, because RelicRun.Editor has no VContainer
        /// reference and should not grow one to state a rule that belongs to the component.
        /// </remarks>
        private static void Make()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(ScenePath);

            if (existing != null && existing.GetComponent<MetaScene>() != null) return;

            if (existing != null)
            {
                // The prefab is there and its root is the WRONG THING — an earlier version of
                // this builder wrote a different component, and that script has since been
                // deleted. Unity leaves a missing-script reference behind, which cannot be asked
                // what it was and cannot be replaced in place.
                //
                // Deleting and remaking is safe here and nowhere else: every object in this
                // prefab is generated by the lines below. The one rule a generator must not break
                // is writing through something a person authored, and there is nothing authored
                // in this file.
                AssetDatabase.DeleteAsset(ScenePath);

                Debug.Log("the menu prefab was built by an older version of this builder and its " +
                          "root component no longer exists, so it is being made again.");
            }

            var made = new GameObject("MetaScene", typeof(MetaScene));

            PrefabUtility.SaveAsPrefabAsset(made, ScenePath);
            Object.DestroyImmediate(made);

            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);

            Debug.Log("made " + ScenePath + ", which is the prefab the menu is built into.");
        }

        /// <summary>
        /// Clears out the last build, by name and only that name.
        /// </summary>
        /// <remarks>
        /// Everything else in the prefab — the lifetime scope, whatever somebody adds tomorrow —
        /// is left alone, because a generator that tidied up after other people would eventually
        /// tidy away something that mattered.
        ///
        /// It also clears the hierarchy the FIRST version of this builder wrote, so an existing
        /// prefab does not end up with a title screen behind the new one.
        /// </remarks>
        private static void Replace(GameObject scene)
        {
            for (int i = scene.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = scene.transform.GetChild(i);

                if (child.name == RootName || child.name == "Title")
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        /// <summary>Builds every panel under one child of the scene.</summary>
        private static void Fit(GameObject scene, TMP_FontAsset face, HallTileView tile)
        {
            var root = new GameObject(RootName);
            root.transform.SetParent(scene.transform, false);

            GameObject canvas = Canvas();
            canvas.transform.SetParent(root.transform, false);

            TitlePanel title = Title(canvas, face);
            ModesPanel modes = Modes(canvas, face);
            LevelsPanel levels = Levels(canvas, face, tile);

            MetaScene shell = scene.GetComponent<MetaScene>();

            if (shell == null)
            {
                Debug.LogError("the menu prefab's root is not a MetaScene, so nothing can route " +
                               "between the panels that were just built");
                return;
            }

            var panels = new SerializedObject(shell).FindProperty("_panels");

            var built = new MetaPanel[] { title, modes, levels };

            panels.arraySize = built.Length;

            for (var i = 0; i < built.Length; i++)
            {
                panels.GetArrayElementAtIndex(i).objectReferenceValue = built[i];
            }

            panels.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        /* ---------- the title ---------- */

        private static TitlePanel Title(GameObject canvas, TMP_FontAsset face)
        {
            GameObject panel = Full(canvas, "TitlePanel");

            GameObject who = Strip(panel, "Who", 1f, -60f, 72f);

            GameObject name = Line(who, face, "DELVER", 24, TextAlignmentOptions.Left, 22f);
            GameObject level = Line(who, face, "LV 1", 16, TextAlignmentOptions.Right, 22f);

            GameObject barPanel = Panel(who, "LevelBar", new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(0f, -6f), new Vector2(0f, 8f));
            GameObject levelBar = Bar(barPanel, "Fill", new Color(0.89f, 0.70f, 0.25f),
                Vector2.zero, 8f);

            GameObject best = Line(who, face, "BEST 0", 16, TextAlignmentOptions.Left, -26f);
            GameObject crowns = Line(who, face, "CROWNS 0", 16, TextAlignmentOptions.Right, -26f);

            GameObject featured = Strip(panel, "Featured", 0.5f, 40f, 128f);

            GameObject kicker = Line(featured, face, "DUNGEONS", 16,
                TextAlignmentOptions.Center, 48f);

            GameObject playPanel = Panel(featured, "PlayPanel", new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(0f, 8f), new Vector2(0f, 56f));
            GameObject play = Press(playPanel, "Play", face, "PLAY", Text(32),
                new Color(0.55f, 0.22f, 0.18f), new Color(0.95f, 0.92f, 0.86f), 56f);

            GameObject sub = Line(featured, face, "", 16, TextAlignmentOptions.Center, -40f);

            GameObject daily = Banner(panel, face, "Daily", -120f, "TODAY");
            GameObject versus = Banner(panel, face, "Versus", -224f, "VERSUS");

            GameObject howPanel = Strip(panel, "HowPanel", 0f, 56f, 40f);
            GameObject how = Press(howPanel, "How", face, "HOW TO PLAY", Text(16),
                new Color(0.16f, 0.15f, 0.12f), new Color(0.70f, 0.67f, 0.60f), 40f);

            var view = panel.AddComponent<TitlePanel>();

            Wire(view, new[]
            {
                Pair("_name", name.GetComponent<TMP_Text>()),
                Pair("_level", level.GetComponent<TMP_Text>()),
                Pair("_levelBar", levelBar.GetComponent<Image>()),
                Pair("_best", best.GetComponent<TMP_Text>()),
                Pair("_crowns", crowns.GetComponent<TMP_Text>()),
                Pair("_kicker", kicker.GetComponent<TMP_Text>()),
                Pair("_sub", sub.GetComponent<TMP_Text>()),
                Pair("_play", play.GetComponent<Button>()),
                Pair("_daily", daily.GetComponent<Button>()),
                Pair("_dailyLeft", Named(daily, "Left")),
                Pair("_dailyRight", Named(daily, "Right")),
                Pair("_versus", versus.GetComponent<Button>()),
                Pair("_versusLeft", Named(versus, "Left")),
                Pair("_versusRight", Named(versus, "Right")),
                Pair("_how", how.GetComponent<Button>()),
            });

            return view;
        }

        /// <summary>
        /// One of the two mode banners: a pressable block with a caption at each end.
        /// </summary>
        /// <remarks>
        /// Pressable whether the mode is open or not. A locked banner that could not be pressed
        /// would be indistinguishable from one that is broken, and the source lets a delver open
        /// a shut mode and be told what would open it.
        /// </remarks>
        private static GameObject Banner(GameObject panel, TMP_FontAsset face, string name,
            float at, string title)
        {
            GameObject strip = Strip(panel, name + "Panel", 0.5f, at, 88f);

            GameObject banner = Press(strip, name, face, title, Text(24),
                new Color(0.13f, 0.12f, 0.10f), new Color(0.90f, 0.87f, 0.80f), 88f);

            Line(banner, face, "", 16, TextAlignmentOptions.Left, -26f).name = "Left";
            Line(banner, face, "", 16, TextAlignmentOptions.Right, -26f).name = "Right";

            return banner;
        }

        /* ---------- the mode picker ---------- */

        private static ModesPanel Modes(GameObject canvas, TMP_FontAsset face)
        {
            GameObject panel = Full(canvas, "ModesPanel");

            GameObject head = Strip(panel, "Head", 1f, -44f, 40f);
            GameObject back = Press(head, "Back", face, "‹ BACK", Text(16),
                new Color(0.16f, 0.15f, 0.12f), new Color(0.70f, 0.67f, 0.60f), 40f);

            GameObject daily = Tile(panel, face, "Daily", 140f, "DAILY DELVE");
            GameObject versus = Tile(panel, face, "Versus", 20f, "VERSUS");
            GameObject dungeons = Tile(panel, face, "Dungeons", -100f, "DUNGEONS");

            var view = panel.AddComponent<ModesPanel>();

            Wire(view, new[]
            {
                Pair("_daily", daily.GetComponent<Button>()),
                Pair("_dailyTag", Named(daily, "Tag")),
                Pair("_dailyLeft", Named(daily, "Left")),
                Pair("_dailyRight", Named(daily, "Right")),

                Pair("_versus", versus.GetComponent<Button>()),
                Pair("_versusTag", Named(versus, "Tag")),
                Pair("_versusLeft", Named(versus, "Left")),
                Pair("_versusRight", Named(versus, "Right")),

                Pair("_dungeons", dungeons.GetComponent<Button>()),
                Pair("_dungeonsTag", Named(dungeons, "Tag")),

                Pair("_back", back.GetComponent<Button>()),
            });

            return view;
        }

        /// <summary>
        /// One mode's tile: a pressable block with a title, a state word, and two captions.
        /// </summary>
        /// <remarks>
        /// The same shape as the title's banners and deliberately so — a delver moves between the
        /// two screens in a second, and a mode that looked different in each would read as two
        /// different modes.
        /// </remarks>
        private static GameObject Tile(GameObject panel, TMP_FontAsset face, string name,
            float at, string title)
        {
            GameObject strip = Strip(panel, name + "Panel", 0.5f, at, 104f);

            GameObject tile = Press(strip, name, face, title, Text(24),
                new Color(0.13f, 0.12f, 0.10f), new Color(0.90f, 0.87f, 0.80f), 104f);

            Line(tile, face, "", 16, TextAlignmentOptions.Right, 26f).name = "Tag";
            Line(tile, face, "", 16, TextAlignmentOptions.Left, -30f).name = "Left";
            Line(tile, face, "", 16, TextAlignmentOptions.Right, -30f).name = "Right";

            return tile;
        }

        /* ---------- the dungeon list ---------- */

        private static LevelsPanel Levels(GameObject canvas, TMP_FontAsset face, HallTileView tile)
        {
            GameObject panel = Full(canvas, "LevelsPanel");

            GameObject head = Strip(panel, "Head", 1f, -44f, 40f);
            GameObject back = Press(head, "Back", face, "‹ BACK", Text(16),
                new Color(0.16f, 0.15f, 0.12f), new Color(0.70f, 0.67f, 0.60f), 40f);

            // A grid of squares, laid out by Unity rather than by arithmetic. The catalog owns
            // how many halls there are, so nothing here counts them.
            GameObject grid = Strip(panel, "Grid", 1f, -180f, 200f);
            var layout = grid.AddComponent<GridLayoutGroup>();

            layout.cellSize = new Vector2(TileSide, TileSide);
            layout.spacing = new Vector2(8f, 8f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 4;
            layout.childAlignment = TextAnchor.UpperCenter;

            GameObject title = Line(panel, face, "", 24, TextAlignmentOptions.Left, 0f);
            Place(title, 0.5f, 56f, 32f);

            GameObject lore = Line(panel, face, "", 16, TextAlignmentOptions.TopLeft, 0f);
            Place(lore, 0.5f, -6f, 96f);
            lore.GetComponent<TMP_Text>().textWrappingMode = TextWrappingModes.Normal;

            GameObject stats = Strip(panel, "Stats", 0f, 190f, 128f);
            var rows = stats.AddComponent<VerticalLayoutGroup>();

            rows.childForceExpandWidth = true;
            rows.childForceExpandHeight = false;
            rows.childControlHeight = true;
            rows.childControlWidth = true;
            rows.spacing = 2f;

            // The row template, kept inactive: the panel spawns from it and a live copy sitting
            // in the layout would be an eighth row nobody asked for.
            GameObject statLine = Line(stats, face, "", 16, TextAlignmentOptions.Left, 0f);
            statLine.name = "StatLine";
            statLine.SetActive(false);

            GameObject relics = Line(panel, face, "", 16, TextAlignmentOptions.Center, 0f);
            Place(relics, 0f, 118f, 24f);

            GameObject delvePanel = Strip(panel, "DelvePanel", 0f, 56f, 48f);
            GameObject delve = Press(delvePanel, "Delve", face, LevelsCards.DelveLabel, Text(16),
                new Color(0.89f, 0.70f, 0.25f), new Color(0.08f, 0.07f, 0.06f), 48f);

            var view = panel.AddComponent<LevelsPanel>();

            Wire(view, new[]
            {
                Pair("_tile", tile),
                Pair("_grid", (RectTransform)grid.transform),
                Pair("_title", title.GetComponent<TMP_Text>()),
                Pair("_lore", lore.GetComponent<TMP_Text>()),
                Pair("_stats", (RectTransform)stats.transform),
                Pair("_statLine", statLine.GetComponent<TMP_Text>()),
                Pair("_relics", relics.GetComponent<TMP_Text>()),
                Pair("_delve", delve.GetComponent<Button>()),
                Pair("_delveLabel", delve.GetComponentInChildren<TMP_Text>(true)),
                Pair("_back", back.GetComponent<Button>()),
            });

            return view;
        }

        /// <summary>One hall's square, saved as a prefab because the panel spawns ten of them.</summary>
        private static HallTileView HallTile(TMP_FontAsset face)
        {
            var made = new GameObject("HallTile", typeof(RectTransform), typeof(Image),
                typeof(Button), typeof(HallTileView));

            var rect = (RectTransform)made.transform;
            rect.sizeDelta = new Vector2(TileSide, TileSide);

            Image frame = made.GetComponent<Image>();
            frame.sprite = White();
            frame.color = new Color(0.29f, 0.27f, 0.21f);

            made.GetComponent<Button>().targetGraphic = frame;

            GameObject number = Say(made, face, "1", Text(24), TextAlignmentOptions.Center,
                new Vector2(0f, 8f), new Vector2(0f, 28f), true);
            number.name = "Number";

            GameObject tag = Say(made, face, "", Text(8), TextAlignmentOptions.Center,
                new Vector2(0f, -18f), new Vector2(0f, 12f), true);
            tag.name = "Tag";

            Wire(made.GetComponent<HallTileView>(), new[]
            {
                Pair("_press", made.GetComponent<Button>()),
                Pair("_frame", frame),
                Pair("_number", number.GetComponent<TMP_Text>()),
                Pair("_tag", tag.GetComponent<TMP_Text>()),
            });

            return Save(made, TilePrefab).GetComponent<HallTileView>();
        }

        /* ---------- the pieces this screen needs ---------- */

        /// <summary>A panel filling the whole canvas, which is what a screen is.</summary>
        private static GameObject Full(GameObject canvas, string name)
        {
            return Panel(canvas, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        /// <summary>A full-width row, inset from both edges.</summary>
        private static GameObject Strip(GameObject parent, string name, float vertical, float at,
            float height)
        {
            return Panel(parent, name, new Vector2(0f, vertical), new Vector2(1f, vertical),
                new Vector2(0f, at), new Vector2(-Margin * 2f, height));
        }

        /// <summary>A line of text stretched across its parent, at a height.</summary>
        private static GameObject Line(GameObject parent, TMP_FontAsset face, string what,
            int size, TextAlignmentOptions how, float at)
        {
            return Say(parent, face, what, Text(size), how, new Vector2(0f, at),
                new Vector2(0f, Text(size) + 8), true);
        }

        /// <summary>Moves a line onto its own row, since Say puts it at its parent's middle.</summary>
        private static void Place(GameObject said, float vertical, float at, float height)
        {
            var rect = (RectTransform)said.transform;

            rect.anchorMin = new Vector2(0f, vertical);
            rect.anchorMax = new Vector2(1f, vertical);
            rect.sizeDelta = new Vector2(-Margin * 2f, height);
            rect.anchoredPosition = new Vector2(0f, at);
        }

        private static TMP_Text Named(GameObject parent, string name)
        {
            Transform found = parent.transform.Find(name);

            if (found != null) return found.GetComponent<TMP_Text>();

            Debug.LogError("no child called " + name + " under " + parent.name);
            return null;
        }

        /* ---------- making it loadable ---------- */

        /// <summary>
        /// Makes the scene loadable: addressed, configured, and listed.
        /// </summary>
        /// <remarks>
        /// Three separate things, and a scene is only loadable when all three are true. The
        /// prefab has to be addressable, because <c>SceneConfig</c> holds an
        /// <c>AssetReference</c> and nothing else. A config has to exist and name a key. And the
        /// settings asset has to list that config, because <c>SceneService</c> looks the key up
        /// there and there only.
        /// </remarks>
        private static void Register()
        {
            IDictionary<string, string> addressed = Addressing.Address(
                Addressing.SceneGroup, "Assets/Scenes", new[] { "TitleScene" }, ".prefab");

            string guid;

            if (!addressed.TryGetValue("TitleScene", out guid))
            {
                Debug.LogError("could not address " + ScenePath + ", so nothing can load it");
                return;
            }

            var config = AssetDatabase.LoadAssetAtPath<SceneConfig>(ConfigPath);

            if (config == null)
            {
                Debug.LogError("no scene config at " + ConfigPath + " to point at the menu");
                return;
            }

            config.SceneKey = SceneKeys.MenuScene;
            config.SceneReference = new UnityEngine.AddressableAssets.AssetReference(guid);

            // On, the same as the fight's. Two screens at once is not something anybody wants to
            // look at, and the source has no notion of it — coming back from a run should replace
            // the run rather than draw the menu over the top of it.
            config.RemoveAllOtherScenes = true;

            EditorUtility.SetDirty(config);

            var settings = AssetDatabase.LoadAssetAtPath<SceneServiceSettings>(SettingsPath);

            if (settings == null)
            {
                Debug.LogError("no scene service settings, so nothing lists the menu");
                return;
            }

            if (settings.SceneConfigs == null) settings.SceneConfigs = new List<SceneConfig>();

            if (!settings.SceneConfigs.Contains(config)) settings.SceneConfigs.Add(config);

            // The service takes the FIRST config claiming a key. A second one claiming the same
            // key is not an error anywhere and never will be: it is simply never reached, and the
            // scene that loads is the one somebody wrote earlier and forgot.
            foreach (SceneConfig other in settings.SceneConfigs)
            {
                if (other == null || other == config) continue;
                if (other.SceneKey != SceneKeys.MenuScene) continue;

                Debug.LogError(other.name + " also claims " + SceneKeys.MenuScene +
                               ", so the menu may never be the scene that loads", other);
            }

            EditorUtility.SetDirty(settings);
        }
    }
}
