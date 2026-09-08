using RelicRun.Core.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// Holds the canvas at a whole number of screen pixels per authored pixel.
    /// </summary>
    /// <remarks>
    /// <see cref="CanvasScaler.ScaleMode.ScaleWithScreenSize"/> is the sensible default for
    /// almost any interface and is wrong for this one. It gives whatever fraction makes the
    /// reference resolution fit — 1.118 on a common phone — and a face baked on an eight-pixel
    /// grid drawn at 1.118 times is a face where some rows of a glyph are five screen pixels and
    /// the next is six. That is what "the fonts look ugly" turned out to mean, and no typeface
    /// fixes it.
    ///
    /// So: constant pixel size, and the factor is a whole number from <see cref="PixelScale"/>.
    /// The canvas is then the screen divided by that factor, which is a different number of
    /// units on different devices — deliberately. The promise is the design AREA, 390x844, the
    /// shell the source draws in; whatever is left over is room the layout may use. Anchors and
    /// layout groups handle that; a fixed-size hierarchy would not, which is why the built scene
    /// has none.
    ///
    /// Watched every frame rather than hooked, because there is no reliable event for it. A
    /// phone rotates, an editor Game view is dragged, a desktop window is resized, and comparing
    /// two integers per frame is cheaper than being wrong about any of them.
    /// </remarks>
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class PixelCanvas : MonoBehaviour
    {
        [SerializeField] private CanvasScaler _scaler;

        private int _width;
        private int _height;

        private void OnEnable()
        {
            Fit();
        }

        private void Update()
        {
            if (Screen.width == _width && Screen.height == _height) return;

            Fit();
        }

        /// <summary>
        /// Sets the scale for the screen as it is now.
        /// </summary>
        /// <remarks>
        /// Public so a test or a tool can ask for it without waiting a frame, and so the
        /// arithmetic has one home. Everything it decides comes from <see cref="PixelScale"/>,
        /// which is where it can be tested without a screen at all.
        /// </remarks>
        public void Fit()
        {
            if (_scaler == null) _scaler = GetComponent<CanvasScaler>();
            if (_scaler == null) return;

            _width = Screen.width;
            _height = Screen.height;

            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            _scaler.scaleFactor = PixelScale.For(_width, _height);
        }
    }
}
