using RelicRun.Core.Presentation;
using RelicRun.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Editor.Importers
{
    /// <summary>
    /// The pieces every generated screen is built out of.
    /// </summary>
    /// <remarks>
    /// Lifted out of the fight's builder when a second screen needed the same panels, the same
    /// bars and the same one white pixel. There was never a version of this project with two
    /// copies of them: the builders import it with <c>using static</c>, so a call site reads
    /// exactly as it did when the methods lived next door.
    ///
    /// What is here is the generic scaffolding only — a panel, a box, a bar, a line of text, a
    /// button. Anything that knows what it is FOR belongs to the builder that knows.
    /// </remarks>
    public static class Scenery
    {
        /// <summary>
        /// One white pixel, which is what every bar in this game is actually made of.
        /// </summary>
        /// <remarks>
        /// Not decoration. An <c>Image</c> with NO sprite ignores its own type: Unity's
        /// <c>OnPopulateMesh</c> checks for a sprite first and falls back to a plain quad, so a
        /// Filled image with nothing in it draws a full rectangle and <c>fillAmount</c> does
        /// nothing whatsoever.
        ///
        /// That was the state of every bar in the fight scene for a while: both healths, both
        /// attack gauges and all three gauges on a relic slot were Filled, horizontal, correctly
        /// wired, and permanently full. A foe at zero hit points still had a full red bar.
        ///
        /// One pixel rather than Unity's built-in UISprite, which is rounded and nine-sliced. A
        /// bar three units tall with rounded ends is a bar with no ends.
        /// </remarks>
        public const string WhitePath = "Assets/GameAssets/Art/Sheets/White.png";

        /// <summary>
        /// A text size, snapped to the grid the bitmap face is baked on.
        /// </summary>
        /// <remarks>
        /// Sizes go through <see cref="PixelScale.Snap"/> rather than being typed, because the ui
        /// face is a bitmap baked at eight pixels and a size of twenty draws it at two and a half
        /// times. Twenty is the exact kind of number that looks reasonable in a source file and
        /// puts the smear straight back, so it is not possible to write one.
        /// </remarks>
        public static int Text(int size)
        {
            return PixelScale.Snap(size);
        }

        /* ---------- wiring ---------- */

        public struct Wiring
        {
            public string Field;
            public Object Value;
        }

        public static Wiring Pair(string field, Object value)
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
        public static void Wire(Component component, Wiring[] wiring)
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

        /// <summary>
        /// The camera every screen is drawn through.
        /// </summary>
        /// <remarks>
        /// This game draws itself entirely in UI — halls, heroes, relics and bars are all Images —
        /// so it is tempting to conclude it needs no camera at all, and for a while the menu had
        /// none. That is wrong twice over.
        ///
        /// It is wrong for the game: a scene with no camera has no clear colour, so whatever is
        /// outside the interface is undefined, <c>Camera.main</c> is null for anything that ever
        /// reaches for it, and the render pipeline is asked to draw a frame with nothing to draw
        /// it from.
        ///
        /// And it is wrong for anybody trying to LOOK at the game. Screen Space - Overlay UI is
        /// composited after every camera by the canvas itself, so no camera can render it to a
        /// texture: the only place it exists is the back buffer, which refreshes when the Game
        /// view repaints and not when anybody asks. Driving the editor from a terminal, that
        /// meant screenshots that lagged a navigation behind, or came back byte-identical to the
        /// last one. Through a camera the same capture is synchronous and exact — the same screen
        /// twice gives the same bytes twice.
        ///
        /// Orthographic, because nothing here has depth. The clear colour is the game's own dark
        /// ground rather than the editor's blue, so a screen that fails to draw looks like this
        /// game failing rather than like a different program.
        /// </remarks>
        public static Camera Eye(GameObject parent)
        {
            var made = new GameObject("Eye", typeof(Camera));

            made.transform.SetParent(parent.transform, false);
            made.transform.localPosition = new Vector3(0f, 0f, -10f);

            // Tagged, so Camera.main finds it. Anything reaching for the main camera at runtime
            // gets this one; untagged, Camera.main stays null, which is the state that had a
            // stray editor camera answering for the game.
            made.tag = "MainCamera";

            Camera eye = made.GetComponent<Camera>();

            eye.orthographic = true;
            eye.clearFlags = CameraClearFlags.SolidColor;
            eye.backgroundColor = new Color(0.047f, 0.043f, 0.035f, 1f);
            eye.nearClipPlane = 0.1f;
            eye.farClipPlane = 100f;

            // Everything. A mask that excluded the UI layer would render a clear colour and
            // nothing else, which looks exactly like a screen that failed to build.
            eye.cullingMask = ~0;

            return eye;
        }

        /// <summary>A canvas that scales by whole pixels and keeps doing so.</summary>
        /// <param name="eye">
        /// What draws it. Screen Space - Camera rather than Overlay, so the whole interface goes
        /// through something that can be rendered on demand — see <see cref="Eye"/> for why that
        /// is worth the extra reference.
        /// </param>
        public static GameObject Canvas(Camera eye)
        {
            var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(PixelCanvas));

            Aim(canvas.GetComponent<Canvas>(), eye);

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

        /// <summary>
        /// Points a canvas at a camera, or leaves it overlaid when there is none.
        /// </summary>
        /// <remarks>
        /// The fallback is not politeness. A canvas set to Screen Space - Camera with no camera
        /// draws NOTHING — not a warning, not a blank screen with the interface missing, but an
        /// empty frame — so a missing camera has to leave the canvas somewhere it still works.
        ///
        /// The plane distance sits between the near and far clips. At or beyond either, the
        /// canvas is clipped away and the result is the same empty frame.
        /// </remarks>
        public static void Aim(Canvas canvas, Camera eye)
        {
            if (canvas == null) return;

            if (eye == null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                return;
            }

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = eye;
            canvas.planeDistance = 10f;
        }

        public static GameObject Panel(GameObject parent, string name, Vector2 anchorMin,
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

        public static GameObject Box(GameObject parent, string name, Vector2 anchor,
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
        /// A bar that fills from the left.
        /// </summary>
        /// <remarks>
        /// Its WIDTH comes from its panel, not from a number here. A bar given a fixed width in
        /// units takes a different share of the screen on each device — a whole scale factor
        /// gives a 1440-wide phone 480 units and a 1080-wide one 540 — and a bar read as a
        /// PROPORTION then tells each delver something slightly different.
        ///
        /// Only the height stays authored, because that is thickness rather than measure.
        /// </remarks>
        public static GameObject Bar(GameObject parent, string name, Color colour, Vector2 at,
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
        /// Right-aligned and centred text need it: a fixed width aligns against an edge that is
        /// not the panel's, so the text drifts as the canvas changes size — and the canvas
        /// changes size on every device.
        /// </param>
        public static GameObject Say(GameObject parent, TMP_FontAsset face, string what,
            int size, TextAlignmentOptions how, Vector2 at, Vector2 box, bool stretch = false)
        {
            var said = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rect = (RectTransform)said.transform;

            rect.SetParent(parent.transform, false);
            rect.anchorMin = new Vector2(0f, 0.5f);
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

        /// <summary>A point for things to fly from. It draws nothing itself.</summary>
        public static GameObject Anchor(GameObject parent, string name, Vector2 at)
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
        /// Something a delver can press: a filled panel with a line of text on it.
        /// </summary>
        /// <remarks>
        /// The background image is what takes the press, so it is enabled and opaque rather than
        /// hidden like <see cref="Box"/>'s. An invisible graphic still raycasts, but a button
        /// nobody can see is a button nobody presses on purpose.
        ///
        /// Stretched across its parent, so what sets a button's width is the panel it is in.
        /// </remarks>
        public static GameObject Press(GameObject parent, string name, TMP_FontAsset face,
            string label, int size, Color fill, Color ink, float height)
        {
            var button = new GameObject(name, typeof(RectTransform), typeof(Image),
                typeof(Button));
            var rect = (RectTransform)button.transform;

            rect.SetParent(parent.transform, false);
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(0f, height);
            rect.anchoredPosition = Vector2.zero;

            Image image = button.GetComponent<Image>();
            image.sprite = White();
            image.color = fill;
            image.type = Image.Type.Simple;

            Button press = button.GetComponent<Button>();
            press.targetGraphic = image;

            GameObject said = Say(button, face, label, size, TextAlignmentOptions.Center,
                Vector2.zero, new Vector2(0f, height), true);

            said.GetComponent<TextMeshProUGUI>().color = ink;

            return button;
        }

        /// <summary>The white pixel, made on first use and left alone after.</summary>
        public static Sprite White()
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

        /// <summary>Writes a built hierarchy out as a prefab and clears up after itself.</summary>
        public static GameObject Save(GameObject made, string path)
        {
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(made, path);
            Object.DestroyImmediate(made);

            return saved;
        }
    }
}
