using RelicRun.Game.Data;
using RelicRun.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace RelicRun.Editor.Importers
{
    /// <summary>
    /// Builds the fight scene, so nobody has to drag twelve references into an Inspector.
    /// </summary>
    /// <remarks>
    /// Generated rather than authored, for the same reason the content is. A scene assembled by
    /// hand is a scene nobody can diff, nobody can rebuild after a mistake, and nobody can
    /// describe except by opening it — and the twelve references <see cref="CombatView"/> needs
    /// are exactly the kind of thing that is ninety per cent wired and silently wrong.
    ///
    /// It is a scaffold, not a screen. The layout is legible and nothing more: no art, no frames,
    /// no hall behind it. What it is for is pressing Play and watching a real fight play out at
    /// its real pace, which is the first moment any of the last three phases can be seen at all.
    /// </remarks>
    public static class FightSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Fight.unity";
        public const string FlierPrefab = "Assets/GameAssets/Game/Presentation/FlyingNumber.prefab";
        public const string LinePrefab = "Assets/GameAssets/Game/Presentation/LogLine.prefab";

        /// <summary>The canvas is authored at this size and scales to whatever it lands on.</summary>
        private static readonly Vector2 Reference = new Vector2(1080f, 1920f);

        [MenuItem("Tools/Relic Run/Build Fight Scene", priority = 120)]
        public static void Build()
        {
            var content = AssetDatabase.LoadAssetAtPath<GameContent>(ContentPaths.GameContentAsset);
            if (content == null)
            {
                Debug.LogError("no content — run Tools ▸ Relic Run ▸ Import Content first");
                return;
            }

            TMP_FontAsset face = content.Fonts == null ? null : content.Fonts.For(FontBook.Ui);

            FlyingNumber flier = Flier(face);
            LogLine line = Line(face);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject canvas = Canvas();
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

            var harness = new GameObject("Harness").AddComponent<FightHarness>();
            Wire(harness, new[]
            {
                Pair("_view", view),
                Pair("_content", content),
            });

            new GameObject("EventSystem",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.EventSystems.StandaloneInputModule));

            System.IO.Directory.CreateDirectory(
                System.IO.Path.GetDirectoryName(System.IO.Path.Combine(
                    ContentPaths.ProjectRoot, ScenePath.Replace('/', System.IO.Path.DirectorySeparatorChar))));

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log("built " + ScenePath + " — open it and press Play to watch a fight.\n" +
                      "It is a scaffold: legible, and nothing more.",
                      AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
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
