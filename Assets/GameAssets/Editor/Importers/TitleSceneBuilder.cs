using System.Collections.Generic;
using GameLift.Scene;
using RelicRun.Game.Data;
using RelicRun.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEngine;
using static RelicRun.Editor.Importers.Scenery;

namespace RelicRun.Editor.Importers
{
    /// <summary>
    /// Builds the title screen, so nobody has to drag fourteen references in by hand.
    /// </summary>
    /// <remarks>
    /// The same arrangement the fight is built under: a scene here is a PREFAB under
    /// <c>Assets/Scenes/</c>, addressed, named by a key on a <c>SceneConfig</c>, and listed in the
    /// settings asset the service reads. Miss any one of those three and the failure is the same
    /// shape — <c>LoadScene</c> is called, nothing happens, and nothing is said about it.
    ///
    /// It writes its own prefab rather than editing the GameLift sample's <c>MenuScene</c>. The
    /// sample menu is scaffolding this game does not want, and deleting somebody else's screen to
    /// make room is a worse habit than leaving it on disk unused. What this DOES take over is the
    /// key: <c>menu_scene</c> is where the app starts, so the config that claims it is repointed
    /// here.
    ///
    /// A scaffold, not a screen. No art, no frames, no sparkles — the source draws twenty-six
    /// animated motes behind this and none of them are here. What it is for is seeing that a
    /// delver's save, their level, their clock and the one button that changes what it does are
    /// all real.
    /// </remarks>
    public static class TitleSceneBuilder
    {
        /// <summary>The scene prefab this writes.</summary>
        public const string ScenePath = "Assets/Scenes/TitleScene.prefab";

        /// <summary>What the built hierarchy is called, so a rebuild replaces it.</summary>
        public const string RootName = "Title";

        /// <summary>
        /// The config that names the key the app starts on.
        /// </summary>
        /// <remarks>
        /// The GameLift sample's own, repointed. A second config claiming <c>menu_scene</c> would
        /// never be reached — the service takes the FIRST one claiming a key — so the scene that
        /// loaded would be whichever somebody wrote earlier and forgot.
        /// </remarks>
        public const string ConfigPath = "Assets/Samples/Game Lift/1.0.0/Starter/" +
            "ScriptableObjects/SceneServiceSettings/MenuSceneConfig.asset";

        /// <summary>The width the phone layout is drawn against, in authored pixels.</summary>
        /// <remarks>
        /// Nothing here is given a fixed width. Every panel is inset from the canvas edges
        /// instead, because a whole scale factor gives a 1440-wide phone 480 units and a
        /// 1080-wide one 540 — a fixed width takes a different share of each.
        /// </remarks>
        private const float Margin = 24f;

        private const string BuildItem = "Tools/Relic Run/Build Title Scene";

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
                Debug.LogWarning("no ui face in the content, so the title will use whatever " +
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
                // Before anything is built, because the level bar needs it and a bar without it
                // is a bar that cannot show a fraction.
                White();

                Replace(scene);
                Fit(scene, face);

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

            Debug.Log("built the title into " + ScenePath + ". It is what " + SceneKeys.MenuScene +
                      " now loads.", AssetDatabase.LoadAssetAtPath<GameObject>(ScenePath));
        }

        /// <summary>
        /// The scene prefab itself, made once if it is not there.
        /// </summary>
        /// <remarks>
        /// It carries a <c>LifetimeScope</c> of its own, which is not decoration: the scene asks
        /// its scope for the save vault, and a scope instantiated under the application's root is
        /// parented to it, so what is registered up there is reachable from down here. Without
        /// one the title still opens — and shows a delver who has never played, every time.
        ///
        /// <see cref="TitleScene"/> requires it, so adding the component adds the scope. This
        /// used to name the scope type here, which does not compile: RelicRun.Editor has no
        /// VContainer reference, and giving it one to state a rule the component already owns is
        /// the wrong way round.
        /// </remarks>
        private static void Make()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ScenePath) != null) return;

            // The scope comes with it: TitleScene requires one, and AddComponent honours that.
            // Named there rather than here, because RelicRun.Editor has no VContainer reference
            // and should not grow one to state a rule that belongs to the component.
            var made = new GameObject("TitleScene", typeof(TitleScene));

            PrefabUtility.SaveAsPrefabAsset(made, ScenePath);
            Object.DestroyImmediate(made);

            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);

            Debug.Log("made " + ScenePath + ", which is the prefab the title is built into.");
        }

        /// <summary>
        /// Clears out the last build, by name and only that name.
        /// </summary>
        /// <remarks>
        /// Everything else in the prefab — the lifetime scope, whatever somebody adds tomorrow —
        /// is left alone, because a generator that tidied up after other people would eventually
        /// tidy away something that mattered.
        /// </remarks>
        private static void Replace(GameObject scene)
        {
            for (int i = scene.transform.childCount - 1; i >= 0; i--)
            {
                Transform child = scene.transform.GetChild(i);

                if (child.name == RootName) Object.DestroyImmediate(child.gameObject);
            }
        }

        /// <summary>Builds the whole hierarchy under one child of the scene.</summary>
        private static void Fit(GameObject scene, TMP_FontAsset face)
        {
            var root = new GameObject(RootName);
            root.transform.SetParent(scene.transform, false);

            GameObject canvas = Canvas();
            canvas.transform.SetParent(root.transform, false);

            // Top: who the delver is, and how far along they are.
            GameObject who = Strip(canvas, "Who", 1f, -60f, 72f);

            GameObject name = Say(who, face, "DELVER", Text(24), TextAlignmentOptions.Left,
                new Vector2(0f, 22f), new Vector2(0f, 32f), true);
            GameObject level = Say(who, face, "LV 1", Text(16), TextAlignmentOptions.Right,
                new Vector2(0f, 22f), new Vector2(0f, 24f), true);

            GameObject barPanel = Panel(who, "LevelBar", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(0f, -6f), new Vector2(0f, 8f));
            GameObject levelBar = Bar(barPanel, "Fill", new Color(0.89f, 0.70f, 0.25f),
                Vector2.zero, 8f);

            GameObject best = Say(who, face, "BEST 0", Text(16), TextAlignmentOptions.Left,
                new Vector2(0f, -26f), new Vector2(0f, 24f), true);
            GameObject crowns = Say(who, face, "CROWNS 0", Text(16), TextAlignmentOptions.Right,
                new Vector2(0f, -26f), new Vector2(0f, 24f), true);

            // Middle: the one button whose meaning changes.
            GameObject featured = Strip(canvas, "Featured", 0.5f, 40f, 128f);

            GameObject kicker = Say(featured, face, "DUNGEONS", Text(16),
                TextAlignmentOptions.Center, new Vector2(0f, 48f), new Vector2(0f, 24f), true);

            GameObject playPanel = Panel(featured, "PlayPanel", new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(0f, 8f), new Vector2(0f, 56f));
            GameObject play = Press(playPanel, "Play", face, "PLAY", Text(32),
                new Color(0.55f, 0.22f, 0.18f), new Color(0.95f, 0.92f, 0.86f), 56f);

            GameObject sub = Say(featured, face, "", Text(16), TextAlignmentOptions.Center,
                new Vector2(0f, -40f), new Vector2(0f, 24f), true);

            // Below: the two modes, shown from the first launch whether they are open or not.
            GameObject daily = Banner(canvas, face, "Daily", -120f, "TODAY");
            GameObject versus = Banner(canvas, face, "Versus", -224f, "VERSUS");

            // Bottom: the one thing that is neither a mode nor a stat.
            GameObject howPanel = Strip(canvas, "HowPanel", 0f, 56f, 40f);
            GameObject how = Press(howPanel, "How", face, "HOW TO PLAY", Text(16),
                new Color(0.16f, 0.15f, 0.12f), new Color(0.70f, 0.67f, 0.60f), 40f);

            var view = root.AddComponent<TitleView>();

            Wire(view, new[]
            {
                Pair("_name", name.GetComponent<TMP_Text>()),
                Pair("_level", level.GetComponent<TMP_Text>()),
                Pair("_levelBar", levelBar.GetComponent<UnityEngine.UI.Image>()),
                Pair("_best", best.GetComponent<TMP_Text>()),
                Pair("_crowns", crowns.GetComponent<TMP_Text>()),
                Pair("_kicker", kicker.GetComponent<TMP_Text>()),
                Pair("_sub", sub.GetComponent<TMP_Text>()),
                Pair("_play", play.GetComponent<UnityEngine.UI.Button>()),
                Pair("_daily", daily.GetComponent<UnityEngine.UI.Button>()),
                Pair("_dailyLeft", Left(daily)),
                Pair("_dailyRight", Right(daily)),
                Pair("_versus", versus.GetComponent<UnityEngine.UI.Button>()),
                Pair("_versusLeft", Left(versus)),
                Pair("_versusRight", Right(versus)),
                Pair("_how", how.GetComponent<UnityEngine.UI.Button>()),
            });

            Wire(scene.GetComponent<TitleScene>(), new[] { Pair("_view", view) });
        }

        /// <summary>
        /// A full-width row, inset from both edges.
        /// </summary>
        /// <remarks>
        /// Anchored to a horizontal stretch and given a negative width, which is how a Unity rect
        /// says "the parent's width, less this much". A row with a fixed width would take a
        /// different share of the screen on every device the game runs on.
        /// </remarks>
        private static GameObject Strip(GameObject parent, string name, float vertical, float at,
            float height)
        {
            return Panel(parent, name, new Vector2(0f, vertical), new Vector2(1f, vertical),
                new Vector2(0f, at), new Vector2(-Margin * 2f, height));
        }

        /// <summary>
        /// One of the two mode banners: a pressable block with a caption at each end.
        /// </summary>
        /// <remarks>
        /// Pressable whether the mode is open or not. A locked banner that could not be pressed
        /// would be indistinguishable from one that is broken, and the source lets a delver open
        /// a shut mode and be told what would open it.
        /// </remarks>
        private static GameObject Banner(GameObject canvas, TMP_FontAsset face, string name,
            float at, string title)
        {
            GameObject strip = Strip(canvas, name + "Panel", 0.5f, at, 88f);

            GameObject banner = Press(strip, name, face, title, Text(24),
                new Color(0.13f, 0.12f, 0.10f), new Color(0.90f, 0.87f, 0.80f), 88f);

            Say(banner, face, "", Text(16), TextAlignmentOptions.Left,
                new Vector2(0f, -26f), new Vector2(0f, 24f), true).name = "Left";

            Say(banner, face, "", Text(16), TextAlignmentOptions.Right,
                new Vector2(0f, -26f), new Vector2(0f, 24f), true).name = "Right";

            return banner;
        }

        private static TMP_Text Left(GameObject banner)
        {
            return banner.transform.Find("Left").GetComponent<TMP_Text>();
        }

        private static TMP_Text Right(GameObject banner)
        {
            return banner.transform.Find("Right").GetComponent<TMP_Text>();
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
                Debug.LogError("no scene config at " + ConfigPath + " to point at the title");
                return;
            }

            config.SceneKey = SceneKeys.MenuScene;
            config.SceneReference = new UnityEngine.AddressableAssets.AssetReference(guid);

            EditorUtility.SetDirty(config);

            var settings = AssetDatabase.LoadAssetAtPath<SceneServiceSettings>(
                "Assets/Samples/Game Lift/1.0.0/Starter/ScriptableObjects/" +
                "SceneServiceSettings/SceneServiceSettings.asset");

            if (settings == null)
            {
                Debug.LogError("no scene service settings, so nothing lists the title");
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
                               ", so the title may never be the scene that loads", other);
            }

            EditorUtility.SetDirty(settings);
        }
    }
}
