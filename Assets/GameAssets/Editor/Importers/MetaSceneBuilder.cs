using System.Collections.Generic;
using GameLift.Popup;
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

        /// <summary>The two modals, which are prefabs the popup service instantiates.</summary>
        public const string SettingsPrefab =
            "Assets/GameAssets/Game/Presentation/SettingsPopup.prefab";

        public const string WelcomePrefab =
            "Assets/GameAssets/Game/Presentation/WelcomePopup.prefab";

        /// <summary>Where the popup service looks for what it may open. The sample's own.</summary>
        public const string PopupsPath = "Assets/Samples/Game Lift/1.0.0/Starter/" +
            "ScriptableObjects/Popups/PopupSettings.asset";

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

        /// <summary>How much of a tile's edge shows around its fill. The source draws 2.</summary>
        private const float Border = 2f;

        /// <summary>
        /// The size the small print is drawn at: the face's own, undoubled.
        /// </summary>
        /// <remarks>
        /// The captions under a banner and the line under PLAY are META text — the source sets
        /// them at five and a half pixels inside a 390-wide shell. Drawn at sixteen they are
        /// three times that, and the screenshot showed exactly what that costs: the Daily's two
        /// captions ran into each other as LOCKEDREACH DELVER LV 3 IN DUNGEONS, and the line
        /// under PLAY wrapped and dropped its last word out of the row.
        ///
        /// Eight is the size the ui face is baked at, so it is also the one size that is drawn
        /// pixel for pixel with no scaling at all.
        /// </remarks>
        private const int Small = 8;

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

            if (content.Locales == null)
            {
                Debug.LogWarning("no locale book in the content, so the menu will speak in keys");
            }

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

                Modals(face);

                Replace(scene);
                Fit(scene, content, face, tile);

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
        private static void Fit(GameObject scene, GameContent content,
            TMP_FontAsset face, HallTileView tile)
        {
            var root = new GameObject(RootName);
            root.transform.SetParent(scene.transform, false);

            GameObject canvas = Canvas();
            canvas.transform.SetParent(root.transform, false);

            TitlePanel title = Title(canvas, face);
            ModesPanel modes = Modes(canvas, face);
            LevelsPanel levels = Levels(canvas, face, tile);

            BoardPanel board = Sheet<BoardPanel>(canvas, face, "BoardPanel");
            ProfilePanel profile = Sheet<ProfilePanel>(canvas, face, "ProfilePanel");
            BestiaryPanel bestiary = Sheet<BestiaryPanel>(canvas, face, "BestiaryPanel");
            RelicBookPanel relics = Sheet<RelicBookPanel>(canvas, face, "RelicBookPanel");
            HowPanel how = Sheet<HowPanel>(canvas, face, "HowPanel");

            OverPanel over = Over(canvas, face);
            XpPanel xp = Xp(canvas, face);
            StagingPanel staging = Staging(canvas, face, tile);

            MetaScene shell = scene.GetComponent<MetaScene>();

            if (shell == null)
            {
                Debug.LogError("the menu prefab's root is not a MetaScene, so nothing can route " +
                               "between the panels that were just built");
                return;
            }

            Wire(shell, new[] { Pair("_locales", content.Locales) });

            var panels = new SerializedObject(shell).FindProperty("_panels");

            var built = new MetaPanel[]
            {
                title, modes, levels, board, profile, bestiary, relics, how, over, xp, staging,
            };

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

            GameObject kicker = Line(featured, face, "DUNGEONS", Small,
                TextAlignmentOptions.Center, 48f);

            GameObject playPanel = Panel(featured, "PlayPanel", new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(0f, 8f), new Vector2(0f, 56f));
            GameObject play = Press(playPanel, "Play", face, "PLAY", Text(32),
                new Color(0.55f, 0.22f, 0.18f), new Color(0.95f, 0.92f, 0.86f), 56f);

            GameObject sub = Line(featured, face, "", Small, TextAlignmentOptions.Center, -40f);

            GameObject daily = Banner(panel, face, "Daily", -120f, "TODAY");
            GameObject versus = Banner(panel, face, "Versus", -224f, "VERSUS");

            // The four screens a delver reads rather than plays. The source puts them behind a
            // row of icons; this is the same row with words on it, because an icon nobody has
            // drawn yet is a button that says nothing at all.
            GameObject nav = Strip(panel, "Nav", 0f, 104f, 36f);

            // Five buttons across, placed by fraction rather than by a layout group. A group
            // sizes its children, and Scenery.Press builds a rect that is STRETCHED to its
            // parent — the two disagree, and what a screenshot showed was five buttons about ten
            // units wide with their labels running one letter per line.
            GameObject board = Slot(nav, face, "Board", "RUNS", 0, 5);
            GameObject relics = Slot(nav, face, "Relics", "RELICS", 1, 5);
            GameObject bestiary = Slot(nav, face, "Bestiary", "FOES", 2, 5);
            GameObject profile = Slot(nav, face, "Profile", "DELVER", 3, 5);

            // The odd one in the row: it opens a modal rather than going to a screen. It sits
            // here because that is where a delver looks for it, not because it is the same kind
            // of thing as the four beside it.
            GameObject settings = Slot(nav, face, "Settings", "SETTINGS", 4, 5);

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
                Pair("_board", board.GetComponent<Button>()),
                Pair("_relics", relics.GetComponent<Button>()),
                Pair("_bestiary", bestiary.GetComponent<Button>()),
                Pair("_profile", profile.GetComponent<Button>()),
                Pair("_settings", settings.GetComponent<Button>()),
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

            Line(banner, face, "", Small, TextAlignmentOptions.Left, -26f).name = "Left";
            Line(banner, face, "", Small, TextAlignmentOptions.Right, -26f).name = "Right";

            return banner;
        }

        /* ---------- the screens that are a heading and a list ---------- */

        /// <summary>
        /// A screen that is a heading and a list of lines.
        /// </summary>
        /// <remarks>
        /// Four of them are exactly this, so they are built by one method rather than four that
        /// drift. What differs between them is what the lines SAY, and that is each panel's own
        /// business in <c>ReadingPanels</c>.
        ///
        /// It scrolls, which is not optional: the relic book is fifty rows and the bestiary is
        /// thirteen with a paragraph each. A list that ran off the bottom of the screen would be
        /// a list whose last entries nobody could read.
        /// </remarks>
        private static T Sheet<T>(GameObject canvas, TMP_FontAsset face, string name)
            where T : SheetPanel
        {
            GameObject panel = Full(canvas, name);

            GameObject head = Strip(panel, "Head", 1f, -44f, 40f);
            GameObject back = Press(head, "Back", face, "‹ BACK", Text(16),
                new Color(0.16f, 0.15f, 0.12f), new Color(0.70f, 0.67f, 0.60f), 40f);

            GameObject note = Line(panel, face, "", Small, TextAlignmentOptions.Left, 0f);
            Place(note, 1f, -84f, 16f);

            GameObject title = Line(panel, face, "", 24, TextAlignmentOptions.Left, 0f);
            Place(title, 1f, -108f, 32f);

            // The window the list is seen through. A mask rather than a shorter list, so a row
            // that scrolls past the top is clipped instead of vanishing a frame early.
            GameObject window = Panel(panel, "Window", new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(0f, -70f), new Vector2(-Margin * 2f, -140f));

            window.AddComponent<RectMask2D>();

            var scroll = panel.AddComponent<ScrollRect>();

            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            scroll.viewport = (RectTransform)window.transform;

            // Pinned to the TOP and grown downward by a fitter, so a list of any length starts
            // where the heading left off rather than being centred in whatever room it has.
            GameObject list = Panel(window, "List", new Vector2(0f, 1f), new Vector2(1f, 1f),
                Vector2.zero, Vector2.zero);

            var rect = (RectTransform)list.transform;
            rect.pivot = new Vector2(0.5f, 1f);

            var rows = list.AddComponent<VerticalLayoutGroup>();

            rows.childForceExpandWidth = true;
            rows.childForceExpandHeight = false;
            rows.childControlHeight = true;
            rows.childControlWidth = true;
            rows.spacing = 4f;

            var fitter = list.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = rect;

            // The row template, kept inactive: the panel spawns from it, and a live copy sitting
            // in the layout would be a row nobody put there.
            GameObject row = Line(list, face, "", Small, TextAlignmentOptions.TopLeft, 0f);
            row.name = "Row";
            row.GetComponent<TMP_Text>().textWrappingMode = TextWrappingModes.Normal;
            row.SetActive(false);

            var view = panel.AddComponent<T>();

            Wire(view, new[]
            {
                Pair("_title", title.GetComponent<TMP_Text>()),
                Pair("_note", note.GetComponent<TMP_Text>()),
                Pair("_list", (RectTransform)list.transform),
                Pair("_row", row.GetComponent<TMP_Text>()),
                Pair("_back", back.GetComponent<Button>()),
            });

            return view;
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

            Line(tile, face, "", Small, TextAlignmentOptions.Right, 26f).name = "Tag";
            Line(tile, face, "", Small, TextAlignmentOptions.Left, -30f).name = "Left";
            Line(tile, face, "", Small, TextAlignmentOptions.Right, -30f).name = "Right";

            return tile;
        }

        /* ---------- the end of a run ---------- */

        private static OverPanel Over(GameObject canvas, TMP_FontAsset face)
        {
            GameObject panel = Full(canvas, "OverPanel");

            GameObject title = Line(panel, face, "", 32, TextAlignmentOptions.Center, 0f);
            Place(title, 1f, -120f, 40f);

            GameObject under = Line(panel, face, "", 32, TextAlignmentOptions.Center, 0f);
            Place(under, 1f, -164f, 40f);

            GameObject score = Line(panel, face, "", 48, TextAlignmentOptions.Center, 0f);
            Place(score, 0.5f, 40f, 56f);

            GameObject note = Line(panel, face, "", Small, TextAlignmentOptions.Center, 0f);
            Place(note, 0.5f, -8f, 16f);

            GameObject rate = Line(panel, face, "", Small, TextAlignmentOptions.Center, 0f);
            Place(rate, 0.5f, -32f, 16f);

            GameObject stats = Line(panel, face, "", Small, TextAlignmentOptions.Center, 0f);
            Place(stats, 0.5f, -72f, 16f);

            GameObject homePanel = Strip(panel, "HomePanel", 0f, 108f, 48f);
            GameObject home = Press(homePanel, "Home", face, "CONTINUE", Text(16),
                new Color(0.89f, 0.70f, 0.25f), new Color(0.08f, 0.07f, 0.06f), 48f);

            GameObject boardPanel = Strip(panel, "BoardPanel", 0f, 56f, 40f);
            GameObject board = Press(boardPanel, "Board", face, "RUNS", Text(16),
                new Color(0.16f, 0.15f, 0.12f), new Color(0.70f, 0.67f, 0.60f), 40f);

            var view = panel.AddComponent<OverPanel>();

            Wire(view, new[]
            {
                Pair("_title", title.GetComponent<TMP_Text>()),
                Pair("_titleUnder", under.GetComponent<TMP_Text>()),
                Pair("_score", score.GetComponent<TMP_Text>()),
                Pair("_note", note.GetComponent<TMP_Text>()),
                Pair("_rate", rate.GetComponent<TMP_Text>()),
                Pair("_stats", stats.GetComponent<TMP_Text>()),
                Pair("_home", home.GetComponent<Button>()),
                Pair("_board", board.GetComponent<Button>()),
            });

            return view;
        }

        /* ---------- what it earned ---------- */

        private static XpPanel Xp(GameObject canvas, TMP_FontAsset face)
        {
            GameObject panel = Full(canvas, "XpPanel");

            GameObject kicker = Line(panel, face, "", Small, TextAlignmentOptions.Center, 0f);
            Place(kicker, 1f, -120f, 16f);

            GameObject gained = Line(panel, face, "", 48, TextAlignmentOptions.Center, 0f);
            Place(gained, 1f, -168f, 56f);

            GameObject level = Line(panel, face, "", 24, TextAlignmentOptions.Center, 0f);
            Place(level, 0.5f, 40f, 32f);

            GameObject barPanel = Panel(panel, "BarPanel", new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(0f, 8f), new Vector2(-Margin * 2f, 12f));
            GameObject bar = Bar(barPanel, "Fill", new Color(0.49f, 0.60f, 0.42f),
                Vector2.zero, 12f);

            GameObject next = Line(panel, face, "", Small, TextAlignmentOptions.Center, 0f);
            Place(next, 0.5f, -16f, 16f);

            GameObject gains = Line(panel, face, "", Small, TextAlignmentOptions.Center, 0f);
            Place(gains, 0.5f, -64f, 72f);

            GameObject onPanel = Strip(panel, "OnPanel", 0f, 56f, 48f);
            GameObject on = Press(onPanel, "On", face, "CONTINUE", Text(16),
                new Color(0.89f, 0.70f, 0.25f), new Color(0.08f, 0.07f, 0.06f), 48f);

            var view = panel.AddComponent<XpPanel>();

            Wire(view, new[]
            {
                Pair("_kicker", kicker.GetComponent<TMP_Text>()),
                Pair("_gained", gained.GetComponent<TMP_Text>()),
                Pair("_level", level.GetComponent<TMP_Text>()),
                Pair("_next", next.GetComponent<TMP_Text>()),
                Pair("_bar", bar.GetComponent<Image>()),
                Pair("_gains", gains.GetComponent<TMP_Text>()),
                Pair("_on", on.GetComponent<Button>()),
            });

            return view;
        }

        /* ---------- the staging hall ---------- */

        private static StagingPanel Staging(GameObject canvas, TMP_FontAsset face,
            HallTileView tile)
        {
            GameObject panel = Full(canvas, "StagingPanel");

            GameObject head = Strip(panel, "Head", 1f, -44f, 40f);
            GameObject back = Press(head, "Back", face, "‹ BACK", Text(16),
                new Color(0.16f, 0.15f, 0.12f), new Color(0.70f, 0.67f, 0.60f), 40f);

            GameObject grid = Strip(panel, "Grid", 1f, -180f, 200f);
            var layout = grid.AddComponent<GridLayoutGroup>();

            layout.cellSize = new Vector2(TileSide, TileSide);
            layout.spacing = new Vector2(8f, 8f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 4;
            layout.childAlignment = TextAnchor.UpperCenter;

            GameObject hall = Line(panel, face, "", 24, TextAlignmentOptions.Center, 0f);
            Place(hall, 0.5f, 40f, 32f);

            GameObject roster = Line(panel, face, "", Small, TextAlignmentOptions.Center, 0f);
            Place(roster, 0.5f, 4f, 16f);

            GameObject fightPanel = Strip(panel, "FightPanel", 0f, 56f, 48f);
            GameObject fight = Press(fightPanel, "Fight", face, StagingPanel.FightLabel, Text(16),
                new Color(0.89f, 0.70f, 0.25f), new Color(0.08f, 0.07f, 0.06f), 48f);

            var view = panel.AddComponent<StagingPanel>();

            Wire(view, new[]
            {
                Pair("_tile", tile),
                Pair("_grid", (RectTransform)grid.transform),
                Pair("_hall", hall.GetComponent<TMP_Text>()),
                Pair("_roster", roster.GetComponent<TMP_Text>()),
                Pair("_fight", fight.GetComponent<Button>()),
                Pair("_fightLabel", fight.GetComponentInChildren<TMP_Text>(true)),
                Pair("_back", back.GetComponent<Button>()),
            });

            return view;
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

            GameObject lore = Line(panel, face, "", Small, TextAlignmentOptions.TopLeft, 0f);
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
            GameObject statLine = Line(stats, face, "", Small, TextAlignmentOptions.Left, 0f);
            statLine.name = "StatLine";
            statLine.SetActive(false);

            GameObject relics = Line(panel, face, "", Small, TextAlignmentOptions.Center, 0f);
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

        /* ---------- the modals ---------- */

        /// <summary>
        /// Builds both popup prefabs and lists them where the popup service looks.
        /// </summary>
        /// <remarks>
        /// The same three-part rule the scenes live under, in a different spelling: a popup has to
        /// BE a prefab, has to name itself with a PopupId, and has to be listed in PopupSettings —
        /// because <c>Create&lt;T&gt;</c> searches that list and silently returns null otherwise.
        /// A modal that opens nothing and says nothing is the failure this exists to prevent.
        /// </remarks>
        private static void Modals(TMP_FontAsset face)
        {
            SettingsPopup settings = Settings(face);
            WelcomePopup welcome = Welcome(face);

            var listed = AssetDatabase.LoadAssetAtPath<PopupSettings>(PopupsPath);

            if (listed == null)
            {
                Debug.LogError("no popup settings at " + PopupsPath + ", so no modal can open");
                return;
            }

            if (listed.popupBases == null) listed.popupBases = new List<PopupBase>();

            Listed(listed, settings);
            Listed(listed, welcome);

            EditorUtility.SetDirty(listed);
        }

        /// <summary>Puts one popup on the list, replacing an older build of the same one.</summary>
        /// <remarks>
        /// By TYPE rather than by reference, because a rebuild writes a new prefab and the entry
        /// pointing at the old one would still be there — first match wins, so the list would
        /// keep opening the popup somebody built yesterday.
        /// </remarks>
        private static void Listed(PopupSettings settings, PopupBase popup)
        {
            if (popup == null) return;

            for (int i = settings.popupBases.Count - 1; i >= 0; i--)
            {
                PopupBase other = settings.popupBases[i];

                if (other == null || other.GetType() == popup.GetType())
                {
                    settings.popupBases.RemoveAt(i);
                }
            }

            settings.popupBases.Add(popup);
        }

        private static SettingsPopup Settings(TMP_FontAsset face)
        {
            GameObject card;
            GameObject made = Modal("SettingsPopup", typeof(SettingsPopup), 470f, out card);

            GameObject title = Line(card, face, "", 24, TextAlignmentOptions.Center, 210f);

            GameObject languageTitle = Line(card, face, "", Small, TextAlignmentOptions.Left, 170f);

            GameObject tongues = Panel(card, "Tongues", new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(0f, 96f), new Vector2(-Margin * 2f, 120f));

            var grid = tongues.AddComponent<GridLayoutGroup>();

            grid.cellSize = new Vector2(88f, 32f);
            grid.spacing = new Vector2(6f, 6f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.UpperCenter;

            // The template, kept inactive: the popup spawns from it, and a live copy sitting in
            // the grid would be a language nobody shipped.
            GameObject tongue = Press(tongues, "Tongue", face, "", Small,
                new Color(0.13f, 0.12f, 0.10f), new Color(0.73f, 0.69f, 0.63f), 32f);

            tongue.SetActive(false);

            GameObject soundLabel = Line(card, face, "", Small, TextAlignmentOptions.Left, 24f);

            GameObject soundPanel = Panel(card, "SoundPanel", new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(0f, -8f), new Vector2(-Margin * 2f, 40f));
            GameObject sound = Press(soundPanel, "Sound", face, "", Text(16),
                new Color(0.16f, 0.15f, 0.12f), new Color(0.90f, 0.87f, 0.80f), 40f);

            GameObject nameLabel = Line(card, face, "", Small, TextAlignmentOptions.Left, -56f);

            TMP_InputField typed = Box(card, face, "Name", -88f);

            GameObject closePanel = Panel(card, "ClosePanel", new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(0f, -150f), new Vector2(-Margin * 2f, 40f));
            GameObject close = Press(closePanel, "Close", face, "CLOSE", Text(16),
                new Color(0.16f, 0.15f, 0.12f), new Color(0.70f, 0.67f, 0.60f), 40f);

            var popup = made.GetComponent<SettingsPopup>();

            Wire(popup, new[]
            {
                Pair("_title", title.GetComponent<TMP_Text>()),
                Pair("_languageTitle", languageTitle.GetComponent<TMP_Text>()),
                Pair("_tongues", (RectTransform)tongues.transform),
                Pair("_tongue", tongue.GetComponent<Button>()),
                Pair("_soundLabel", soundLabel.GetComponent<TMP_Text>()),
                Pair("_sound", sound.GetComponent<Button>()),
                Pair("_nameLabel", nameLabel.GetComponent<TMP_Text>()),
                Pair("_name", typed),
                Pair("_close", close.GetComponent<Button>()),
            });

            Wire(popup, new[] { Pair("canvasGroup", made.GetComponent<CanvasGroup>()) });

            return Save(made, SettingsPrefab).GetComponent<SettingsPopup>();
        }

        private static WelcomePopup Welcome(TMP_FontAsset face)
        {
            GameObject card;
            GameObject made = Modal("WelcomePopup", typeof(WelcomePopup), 300f, out card);

            GameObject title = Line(card, face, "", 24, TextAlignmentOptions.Center, 120f);

            GameObject sub = Line(card, face, "", Small, TextAlignmentOptions.Center, 64f);
            sub.GetComponent<TMP_Text>().textWrappingMode = TextWrappingModes.Normal;
            ((RectTransform)sub.transform).sizeDelta = new Vector2(-Margin * 2f, 64f);

            TMP_InputField typed = Box(card, face, "Name", -8f);

            GameObject beginPanel = Panel(card, "BeginPanel", new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(0f, -80f), new Vector2(-Margin * 2f, 48f));
            GameObject begin = Press(beginPanel, "Begin", face, "", Text(16),
                new Color(0.89f, 0.70f, 0.25f), new Color(0.08f, 0.07f, 0.06f), 48f);

            var popup = made.GetComponent<WelcomePopup>();

            Wire(popup, new[]
            {
                Pair("_title", title.GetComponent<TMP_Text>()),
                Pair("_sub", sub.GetComponent<TMP_Text>()),
                Pair("_name", typed),
                Pair("_begin", begin.GetComponent<Button>()),
            });

            Wire(popup, new[] { Pair("canvasGroup", made.GetComponent<CanvasGroup>()) });

            return Save(made, WelcomePrefab).GetComponent<WelcomePopup>();
        }

        /// <summary>
        /// A modal's shell: a full-screen dimmer with a card on it.
        /// </summary>
        /// <remarks>
        /// The dimmer is what makes it modal. It fills the popup canvas and takes raycasts, so a
        /// press outside the card lands on nothing rather than on the screen behind — which is the
        /// difference between a modal and a floating panel.
        /// </remarks>
        private static GameObject Modal(string name, System.Type popup, float tall,
            out GameObject card)
        {
            var made = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup),
                typeof(Image), popup);

            var rect = (RectTransform)made.transform;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            Image dim = made.GetComponent<Image>();
            dim.sprite = White();
            dim.color = new Color(0.03f, 0.03f, 0.02f, 0.92f);

            // The card. Without it every line sat on the raw dimmer at the canvas edge, and the
            // screen behind showed through between them — which is a floating list of labels
            // rather than a modal. It is inset, opaque, and the thing everything else hangs on.
            card = Panel(made, "Card", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                Vector2.zero, new Vector2(-Margin * 2f, tall));

            var back = card.AddComponent<Image>();

            back.sprite = White();
            back.color = new Color(0.09f, 0.08f, 0.07f);

            return made;
        }

        /// <summary>
        /// A box a delver types a name into.
        /// </summary>
        /// <remarks>
        /// Built by hand rather than from Unity's own prefab, because that one arrives with its
        /// own font, its own colours and a nine-sliced rounded background — three things this
        /// game would have to undo.
        ///
        /// The character limit is Core's, so the box refuses the seventeenth letter rather than
        /// accepting it and having the cleaner drop it. Both still happen: this is a courtesy,
        /// and Naming.Clean is the rule.
        /// </remarks>
        private static TMP_InputField Box(GameObject parent, TMP_FontAsset face, string name,
            float at)
        {
            GameObject panel = Panel(parent, name, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, at), new Vector2(-Margin * 2f, 40f));

            var back = panel.AddComponent<Image>();
            back.sprite = White();
            back.color = new Color(0.11f, 0.10f, 0.08f);

            var field = panel.AddComponent<TMP_InputField>();

            GameObject area = Panel(panel, "TextArea", Vector2.zero, Vector2.one,
                Vector2.zero, new Vector2(-16f, -8f));

            area.AddComponent<RectMask2D>();

            GameObject shown = Say(area, face, "", Text(16), TextAlignmentOptions.Left,
                Vector2.zero, new Vector2(0f, 24f), true);

            var text = shown.GetComponent<TextMeshProUGUI>();

            field.textViewport = (RectTransform)area.transform;
            field.textComponent = text;
            field.targetGraphic = back;
            field.characterLimit = Naming.Longest;
            field.lineType = TMP_InputField.LineType.SingleLine;

            return field;
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

            // The fill sits inside the frame with two units showing all round, which IS the
            // border. One image doing both jobs made the chosen tile gold and its gold number
            // invisible — a bug only a screenshot could report.
            GameObject inside = Panel(made, "Fill", Vector2.zero, Vector2.one, Vector2.zero,
                new Vector2(-Border * 2f, -Border * 2f));

            var fill = inside.AddComponent<Image>();
            fill.sprite = White();
            fill.color = new Color(0.10f, 0.08f, 0.06f);
            fill.raycastTarget = false;

            GameObject number = Say(inside, face, "1", Text(24), TextAlignmentOptions.Center,
                new Vector2(0f, 8f), new Vector2(0f, 28f), true);
            number.name = "Number";

            GameObject tag = Say(inside, face, "", Text(8), TextAlignmentOptions.Center,
                new Vector2(0f, -18f), new Vector2(0f, 12f), true);
            tag.name = "Tag";

            Wire(made.GetComponent<HallTileView>(), new[]
            {
                Pair("_press", made.GetComponent<Button>()),
                Pair("_frame", frame),
                Pair("_fill", fill),
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

        /// <summary>
        /// One button of a row of equal buttons, placed by fraction of the row.
        /// </summary>
        /// <remarks>
        /// Anchored to a share of its parent's width rather than sized by a layout group, so the
        /// row works out on any canvas and nothing has to agree with anything about who controls
        /// the rect. The gap is taken out of each button's own width, which is why the row has no
        /// spacing setting to keep in step.
        /// </remarks>
        private static GameObject Slot(GameObject row, TMP_FontAsset face, string name,
            string label, int index, int across)
        {
            var made = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)made.transform;

            rect.SetParent(row.transform, false);

            float share = 1f / across;

            rect.anchorMin = new Vector2(index * share, 0f);
            rect.anchorMax = new Vector2((index + 1) * share, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(NavGap * 0.5f, 0f);
            rect.offsetMax = new Vector2(-NavGap * 0.5f, 0f);

            Image face_ = made.GetComponent<Image>();
            face_.sprite = White();
            face_.color = new Color(0.16f, 0.15f, 0.12f);

            made.GetComponent<Button>().targetGraphic = face_;

            GameObject said = Say(made, face, label, Text(Small), TextAlignmentOptions.Center,
                Vector2.zero, new Vector2(0f, 20f), true);

            var text = said.GetComponent<TextMeshProUGUI>();

            text.color = new Color(0.70f, 0.67f, 0.60f);

            // A nav label is one word and must never wrap. Wrapping is what turned this row into
            // five columns of single letters, and a word that does not fit should overflow
            // visibly rather than rearrange itself into something unreadable.
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;

            return made;
        }

        /// <summary>The gap between two buttons in a row, split between them.</summary>
        private const float NavGap = 6f;

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
