using Cysharp.Threading.Tasks;
using RelicRun.Core.Presentation;
using RelicRun.Game.Data;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
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

        [Tooltip("The floor below, which rises into the window during a descent.")]
        [SerializeField] private RectTransform _next;

        [SerializeField] private Image _nextPicture;
        [SerializeField] private GameContent _content;

        private int _foes;
        private int _stride;
        private double _from;
        private double _to;
        private float _started;
        private float _over;

        /// <summary>
        /// How far the hall has been lifted, in units, out of one window height.
        /// </summary>
        /// <remarks>
        /// The descent, and the only thing on this view that moves vertically. Both bands ride
        /// it: the floor being left sits at the lift and the floor below sits one window under
        /// that, so lifting by exactly one window swaps which of them fills it.
        /// </remarks>
        private float _lift;

        private float _fellFrom;
        private float _fellAt;
        private float _falling;

        /// <summary>
        /// The hall this view loaded, so it can be let go of.
        /// </summary>
        /// <remarks>
        /// An AssetReference caches its handle on the ASSET, so loading one twice throws — which
        /// nothing noticed while a screen showed one fight and then went away. A delve shows
        /// thirteen, and the second floor came back as "Attempting to load AssetReference that
        /// has already been loaded" with no backdrop behind it.
        ///
        /// Kept rather than released after use, unlike a locale: a hall is four and a half
        /// megabytes and every floor of the run is in it, so letting go between floors would be
        /// paying for the fetch twelve more times.
        /// </remarks>
        private AssetReferenceSprite _held;

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

            // A floor opens flush against its own window, whatever a descent left behind.
            _lift = 0f;
            _falling = 0f;

            if (_next != null) _next.gameObject.SetActive(false);

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

        /// <summary>
        /// The walk down: this floor rises out of the window and the next one rises into it.
        /// </summary>
        /// <remarks>
        /// Two bands and one number. The floor being left is lifted by a full window height while
        /// the floor below — which starts one window under it — arrives exactly where the first
        /// one was. That is the source's own arrangement: a slider holding both, translated up by
        /// a hundred per cent.
        ///
        /// The band below opens at its LEFT door, which is what makes the slide read as a
        /// descent rather than a jump even when both bands are the same picture. A delve fights
        /// its way to the right-hand door of a floor; arriving at the left-hand door of the next
        /// one is the whole story the animation tells.
        ///
        /// Within a delve both bands ARE the same picture, because a hall is one image per
        /// dungeon rather than one per floor. The bazaar is the source's exception — it has a
        /// hall of its own — and that art is not imported yet, so this is the line that changes
        /// when it is.
        /// </remarks>
        public void Descend(float seconds)
        {
            if (_next == null || _window == null) return;

            _next.gameObject.SetActive(true);

            if (_nextPicture != null && _picture != null)
            {
                _nextPicture.sprite = _picture.sprite;
                _nextPicture.enabled = _picture.enabled;
            }

            _fellFrom = _lift;
            _fellAt = Time.unscaledTime;
            _falling = seconds;

            if (_falling <= 0f) Landed();
            else Lift(_lift);
        }

        /// <summary>How far down the walk is, as a height in units.</summary>
        private float Fallen()
        {
            if (_falling <= 0f) return _lift;

            float over = Mathf.Clamp01((Time.unscaledTime - _fellAt) / _falling);

            return Mathf.Lerp(_fellFrom, _window.rect.height, Ease(over));
        }

        /// <summary>
        /// Arrived. The floor that rose into the window becomes the floor underfoot.
        /// </summary>
        /// <remarks>
        /// The lift goes back to nothing rather than staying at one window height, because the
        /// next descent has to start from a hall sitting still. Leaving it lifted would work
        /// exactly once.
        /// </remarks>
        private void Landed()
        {
            _falling = 0f;
            _lift = 0f;

            if (_nextPicture != null && _picture != null && _nextPicture.sprite != null)
            {
                _picture.sprite = _nextPicture.sprite;
                _picture.enabled = _nextPicture.enabled;
            }

            if (_next != null) _next.gameObject.SetActive(false);

            _from = 0d;
            _to = 0d;
            _over = 0f;
            _stride = 0;

            Place(0d);
        }

        private double Showing()
        {
            if (_over <= 0f) return _to;

            float over = Mathf.Clamp01((Time.unscaledTime - _started) / _over);

            return _from + (_to - _from) * Ease(over);
        }

        private void Update()
        {
            if (_falling > 0f)
            {
                Lift(Fallen());

                if (Time.unscaledTime - _fellAt >= _falling) Landed();

                return;
            }

            if (_over <= 0f) return;

            Place(Showing());

            if (Time.unscaledTime - _started >= _over) _over = 0f;
        }

        /// <summary>
        /// Puts both bands where a descent this far along says they go.
        /// </summary>
        /// <remarks>
        /// The band below is always exactly one window under the one above it, and is always
        /// drawn at its left door. Panning it would be panning a floor nobody has walked yet.
        /// </remarks>
        private void Lift(float lifted)
        {
            _lift = lifted;

            Place(Showing());

            if (_next == null || _window == null) return;

            Place(_next, 0d, lifted - _window.rect.height);
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

        /// <summary>Puts the hall where a pan of this much says it goes.</summary>
        private void Place(double pan)
        {
            Place(_art, pan, _lift);
        }

        /// <summary>The same, for either band, at a height of its own.</summary>
        /// <remarks>
        /// A band is as wide as its own aspect at the window's height and is anchored to the
        /// window's left edge, so the pan is an offset rather than a fraction — see
        /// <see cref="HallPan"/>. Stretched instead, a hall would put its doors somewhere other
        /// than its edges and the walk would begin in the middle of a wall.
        /// </remarks>
        private void Place(RectTransform band, double pan, float y)
        {
            if (_window == null || band == null) return;

            float tall = _window.rect.height;
            float wide = (float)HallPan.Width(tall, Aspect(), 1d);

            band.anchorMin = new Vector2(0f, 0f);
            band.anchorMax = new Vector2(0f, 1f);
            band.pivot = new Vector2(0f, 0.5f);
            band.sizeDelta = new Vector2(wide, 0f);

            band.anchoredPosition = new Vector2(
                (float)HallPan.Offset(_window.rect.width, wide, pan), y);
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

            // A hall already in hand is not fetched again — see _held.
            if (!ReferenceEquals(address, _held)) Drop();

            // Through .Task rather than awaiting the handle. An AsyncOperationHandle<T> converts
            // implicitly to the non-generic handle, and UniTask's awaiter for THAT one yields
            // void — so awaiting the handle directly compiles the sprite away and then complains
            // it cannot turn void into one. The scene service loads its prefabs the same way.
            bool ours = !address.IsValid();

            AsyncOperationHandle<Sprite> fetching = ours
                ? address.LoadAssetAsync<Sprite>()
                : address.OperationHandle.Convert<Sprite>();

            Sprite drawn = await fetching.Task;

            if (ours) _held = address;

            // The fight may have ended, or moved on to another hall, while this was in flight.
            if (this == null || _picture == null) return;

            _picture.sprite = drawn;
            _picture.enabled = drawn != null;

            Place(Showing());
        }

        /// <summary>Lets go of whatever hall this view fetched.</summary>
        /// <remarks>
        /// Only what it fetched ITSELF. A reference somebody else loaded is somebody else's to
        /// release, and taking it out from under them would leave them holding a sprite that has
        /// been unloaded.
        /// </remarks>
        private void Drop()
        {
            if (_held == null) return;

            if (_held.IsValid()) _held.ReleaseAsset();

            _held = null;
        }

        private void OnDestroy()
        {
            Drop();
        }
    }
}
