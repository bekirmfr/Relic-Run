using RelicRun.Core.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The dark rows a cathode tube leaves across everything.
    /// </summary>
    /// <remarks>
    /// One line in every three, and it sits over the whole game — the source draws it fixed to
    /// the viewport at a z-index above the shell and its popups, so nothing in the game is ever
    /// in front of it. That is the point of the effect: it is the SCREEN, not a layer of the
    /// scene, and a menu that floated above the scanlines would break the illusion in the one
    /// place a delver looks most.
    ///
    /// Its own canvas, built at run time, for the same reason. A scene owns its canvas and a run
    /// swaps scenes a dozen times; an overlay parented into one of them would blink out on every
    /// swap and would have to be rebuilt by whatever came next.
    ///
    /// The source multiplies a sixteen-per-cent black over the picture. Multiplying by black at
    /// that weight and alpha-blending black at that weight come to the same arithmetic — both
    /// leave the row at eighty-four per cent of what was under it — so this needs no blend mode
    /// of its own and no material, which is the difference between one draw call and a shader.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ScanLines : MonoBehaviour
    {
        [Tooltip("Whether to draw them at all. The source ships this on.")]
        [SerializeField] private bool _on = true;

        [Tooltip("One dark row in every this many, counted in the art's own pixels.")]
        [Min(2)]
        [SerializeField] private int _period = 3;

        [Tooltip("How much of the picture a dark row keeps out. The source uses .16.")]
        [Range(0f, 1f)]
        [SerializeField] private float _weight = 0.16f;

        [Tooltip("Above the popups, which are at 998. The screen is in front of everything.")]
        [SerializeField] private int _order = 1000;

        private RawImage _drawn;
        private Texture2D _rows;
        private int _scale;
        private int _wide;
        private int _tall;

        /// <summary>Whether the lines are being drawn.</summary>
        /// <remarks>
        /// Settable, because the one thing anybody ever wants to do with an effect like this is
        /// turn it off — to read something, to take a screenshot, or because they dislike it.
        /// </remarks>
        public bool On
        {
            get { return _on; }
            set
            {
                _on = value;

                if (_drawn != null) _drawn.enabled = value;
            }
        }

        private void Awake()
        {
            Build();
        }

        private void Update()
        {
            // The scale changes when the window does, and in the editor that is constantly. A
            // rebuild is a one-by-nine texture, so asking every frame costs less than the branch
            // that would avoid asking.
            if (Screen.width == _wide && Screen.height == _tall) return;

            Build();
        }

        /// <summary>
        /// Makes the overlay, and the little strip of rows it is tiled from.
        /// </summary>
        /// <remarks>
        /// The strip is as tall as one PERIOD at the current pixel scale, with the top art-pixel
        /// of it dark. Built at the scale rather than drawn at one unit per line, because the
        /// lines have to land on the art's own pixel grid: a scanline half an art-pixel thick
        /// reads as a blur over the picture rather than as a row of it being dimmer.
        /// </remarks>
        private void Build()
        {
            _wide = Screen.width;
            _tall = Screen.height;
            _scale = Mathf.Max(1, PixelScale.For(_wide, _tall));

            Canvas over = Overlay();

            if (over == null) return;

            int tall = Mathf.Max(2, _period) * _scale;

            Strip(tall);

            _drawn.texture = _rows;

            // The whole screen in one quad, repeated by the sampler rather than by geometry. A
            // tiled Image off a one-pixel strip would want a tile per three pixels of screen —
            // a hundred thousand quads on a phone — and Unity quietly draws nothing rather than
            // that. Which is exactly what it did: enabled, sprited, and invisible.
            _drawn.uvRect = new Rect(0f, 0f, 1f, _tall / (float)tall);

            _drawn.color = Color.white;
            _drawn.raycastTarget = false;
            _drawn.enabled = _on;
        }

        /// <summary>One period of rows: the first dark, the rest clear.</summary>
        private void Strip(int tall)
        {
            if (_rows != null && _rows.height == tall) return;

            if (_rows != null) Destroy(_rows);

            _rows = new Texture2D(1, tall, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var dark = new Color(0f, 0f, 0f, _weight);

            for (var y = 0; y < tall; y++)
            {
                _rows.SetPixel(0, y, y < _scale ? dark : Color.clear);
            }

            _rows.Apply(false, false);
        }

        /// <summary>
        /// The canvas the lines are drawn on, made once and kept.
        /// </summary>
        /// <remarks>
        /// Screen space overlay and nothing else: no camera, so nothing that renders a camera to
        /// a texture picks the lines up twice, and no scaler, so one unit is one screen pixel and
        /// the strip above can be measured in the pixels it will actually occupy.
        /// </remarks>
        private Canvas Overlay()
        {
            if (_drawn != null) return _drawn.canvas;

            var made = new GameObject("Scanlines", typeof(Canvas), typeof(CanvasScaler));

            made.transform.SetParent(transform, false);
            made.layer = gameObject.layer;

            var canvas = made.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = _order;

            CanvasScaler scaler = made.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            var sheet = new GameObject("Rows", typeof(RectTransform), typeof(RawImage));
            var rect = (RectTransform)sheet.transform;

            rect.SetParent(made.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _drawn = sheet.GetComponent<RawImage>();

            return canvas;
        }

        private void OnDestroy()
        {
            if (_rows != null) Destroy(_rows);
        }
    }
}
