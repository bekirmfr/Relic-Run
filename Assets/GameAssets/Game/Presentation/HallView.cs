using Cysharp.Threading.Tasks;
using RelicRun.Core.Presentation;
using RelicRun.Game.Data;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The hall behind the fight, sliding one stride at a time toward the far door.
    /// </summary>
    /// <remarks>
    /// <see cref="Walk"/> used only to swap the foe's sprite, so the three and a half seconds
    /// the pacing sets aside for the approach read as a pause with nothing in it — and the
    /// obvious response was to tick Skip Intro, which threw away the one thing on screen that
    /// says how far into a floor a delver is.
    ///
    /// Where it slides to is <see cref="HallPan"/>'s answer and is tested there. What is here is
    /// what a widget owns: the picture, the easing, and the fact that a hall too narrow for its
    /// window is left alone rather than stretched — a stretched hall puts its doors somewhere
    /// other than its edges, and then the walk begins in the middle of a wall.
    /// </remarks>
    public sealed class HallView : MonoBehaviour
    {
        [Tooltip("The window the hall is seen through. Masks whatever is past its edges.")]
        [SerializeField] private RectTransform _window;

        [Tooltip("The art itself, which is wider than the window and slides within it.")]
        [SerializeField] private RectTransform _art;

        [SerializeField] private Image _picture;
        [SerializeField] private GameContent _content;

        private int _foes;
        private int _stride;
        private double _from;
        private double _to;
        private float _started;
        private float _over;

        /// <summary>
        /// Opens a floor at its left door.
        /// </summary>
        /// <param name="foes">How many foes this floor holds. The strides are one more.</param>
        /// <param name="tier">Which hall, counting from one as the catalog does.</param>
        public void Begin(int foes, int tier)
        {
            _foes = foes;
            _stride = 0;
            _from = 0d;
            _to = 0d;
            _over = 0f;

            Place(0d);
            Fetch(tier).Forget();
        }

        /// <summary>
        /// Takes one stride, which is the walk to whoever is arriving.
        /// </summary>
        /// <remarks>
        /// From wherever it currently IS rather than from the last stride's end, so a walk that
        /// begins while the previous one is still settling carries on from the visible position
        /// instead of jumping back to catch up.
        /// </remarks>
        public void Walk()
        {
            _stride++;

            _from = Showing();
            _to = HallPan.Of(_stride, _foes);
            _started = Time.unscaledTime;

            _over = _content != null && _content.Presentation != null
                ? _content.Presentation.ToPacing().PanMs / 1000f
                : 0f;

            if (_over <= 0f) Place(_to);
        }

        private double Showing()
        {
            if (_over <= 0f) return _to;

            float over = Mathf.Clamp01((Time.unscaledTime - _started) / _over);

            return _from + (_to - _from) * Ease(over);
        }

        private void Update()
        {
            if (_over <= 0f) return;

            Place(Showing());

            if (Time.unscaledTime - _started >= _over) _over = 0f;
        }

        /// <summary>
        /// Ease in and out, because a hall does not start and stop instantly.
        /// </summary>
        /// <remarks>
        /// The source's <c>ease-in-out</c>, which is a cubic either side of the midpoint. Linear
        /// would be a room on rails; what this is drawing is somebody setting off and arriving.
        /// </remarks>
        private static float Ease(float t)
        {
            return t < 0.5f
                ? 4f * t * t * t
                : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
        }

        /// <summary>Puts the art where a pan of this much says it goes.</summary>
        private void Place(double pan)
        {
            if (_window == null || _art == null) return;

            float tall = _window.rect.height;
            float wide = (float)HallPan.Width(tall, Aspect(), 1d);

            _art.anchorMin = new Vector2(0f, 0f);
            _art.anchorMax = new Vector2(0f, 1f);
            _art.pivot = new Vector2(0f, 0.5f);
            _art.sizeDelta = new Vector2(wide, 0f);

            _art.anchoredPosition = new Vector2(
                (float)HallPan.Offset(_window.rect.width, wide, pan), 0f);
        }

        /// <summary>
        /// The art's own shape, or a square while there is no art.
        /// </summary>
        /// <remarks>
        /// A square rather than the window's shape, so a hall that has not loaded is obviously
        /// absent rather than quietly filling the screen with nothing.
        /// </remarks>
        private double Aspect()
        {
            Sprite drawn = _picture == null ? null : _picture.sprite;

            return drawn == null ? 1d : drawn.rect.width / (double)drawn.rect.height;
        }

        /// <summary>
        /// Fetches the hall, which is the one piece of content worth waiting for.
        /// </summary>
        /// <remarks>
        /// Addressable, and the reason Addressables is in this project at all: the halls are four
        /// and a half megabytes and a floor descends exactly one of them. Awaited rather than
        /// blocked on — a fight that would not start until its backdrop arrived would be a fight
        /// held up by scenery.
        /// </remarks>
        private async UniTaskVoid Fetch(int tier)
        {
            if (_picture == null || _content == null || _content.Halls == null) return;

            var address = _content.Halls.For(tier);
            if (address == null) return;

            // Through .Task rather than awaiting the handle. An AsyncOperationHandle<T> converts
            // implicitly to the non-generic handle, and UniTask's awaiter for THAT one yields
            // void — so awaiting the handle directly compiles the sprite away and then complains
            // it cannot turn void into one. The scene service loads its prefabs the same way.
            Sprite drawn = await address.LoadAssetAsync<Sprite>().Task;

            // The fight may have ended, or moved on to another hall, while this was in flight.
            if (this == null || _picture == null) return;

            _picture.sprite = drawn;
            _picture.enabled = drawn != null;

            Place(Showing());
        }
    }
}
