using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Game.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The card a fight opens on: who is blocking the way, and what they are carrying.
    /// </summary>
    /// <remarks>
    /// Three and a bit seconds where nothing happens, which is the most deliberate pause in the
    /// game. It is the only look a delver gets at a foe's numbers BEFORE they matter — every
    /// other look is at a fight already going badly — and it is what makes a floor boss feel like
    /// an event rather than a guard with more health.
    ///
    /// It is raised on the MEET step, which playback reserves for exactly this and which happens
    /// AFTER the walk down the hall rather than during it. So its length is the pacing's
    /// <c>IntroMs</c>, and a delve watched with the intro skipped never builds one.
    ///
    /// It carries a FIGHT button and a timer that drains behind it. The two are the same answer:
    /// the fight starts when the button is pressed or when the timer runs out, whichever happens
    /// first, and a delver who reads faster than three seconds is not made to wait for the rest.
    /// The loop asks <see cref="Hurried"/> once a frame while the card is up — see
    /// <c>IPlaybackScreen.Impatient</c> — which is why pressing it actually shortens the wait
    /// rather than merely hiding the card and leaving the delver looking at an empty room.
    /// </remarks>
    public sealed class IntroBanner : MonoBehaviour
    {
        [Tooltip("The rank over the picture. Hidden for an ordinary guard.")]
        [SerializeField] private TMP_Text _rank;

        [Tooltip("The frame around the picture, whose colour and glow say what is in it.")]
        [SerializeField] private Image _frame;

        [SerializeField] private Image _art;
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _stats;
        [SerializeField] private TMP_Text _blocks;

        [Tooltip("The way out. Pressing it is the same answer as letting the timer run out.")]
        [SerializeField] private Button _fight;

        [SerializeField] private TMP_Text _fightLabel;

        [Tooltip("The bar behind the button, which fills as the three seconds go.")]
        [SerializeField] private Image _timer;

        [SerializeField] private GameContent _content;

        /// <summary>What the source says under the numbers, in the delver's own language.</summary>
        public const string BlocksKey = "blocksWay";

        /// <summary>And what the button says.</summary>
        public const string FightKey = "fightBtn";

        /// <summary>How big the foe is drawn on the card.</summary>
        /// <remarks>
        /// The source's 232 pixels, which is eight times a 32-wide sprite less a little. It is
        /// the largest anything in this game is ever drawn, and deliberately so.
        /// </remarks>
        public const int Portrait = 232;

        private static readonly Color Ink = new Color(0.769f, 0.349f, 0.235f);

        private static readonly Color Faint = new Color(0.545f, 0.506f, 0.447f);

        private static readonly Color Quiet = new Color(0.396f, 0.361f, 0.306f);

        /// <summary>What is written on the gold, which is the game's ground rather than its ink.</summary>
        private static readonly Color Coal = new Color(0.078f, 0.071f, 0.059f);

        /// <summary>What the game says, handed down rather than fetched.</summary>
        public Locale Words { get; set; }

        /// <summary>
        /// Whether the delver has said they are ready.
        /// </summary>
        /// <remarks>
        /// Asked by the playback loop rather than raised at it, because the loop is the thing
        /// holding the wait and a screen shouting at it would still not shorten one. Cleared when
        /// the card goes up, so a press left over from the last foe does not start this fight.
        /// </remarks>
        public bool Hurried { get; private set; }

        /// <summary>How long the card is held, so the timer drains at the rate it is held for.</summary>
        public float Held { get; set; }

        /// <summary>Where the foe is drawn, for whatever carries it into the fight.</summary>
        public RectTransform Picture
        {
            get { return _art != null ? (RectTransform)_art.transform : null; }
        }

        /// <summary>The foe on the card, so the same picture is the one that flies.</summary>
        public Sprite Drawn
        {
            get { return _art != null ? _art.sprite : null; }
        }

        private float _up;

        private void Awake()
        {
            if (_fight != null) _fight.onClick.AddListener(() => Hurried = true);
        }

        /// <summary>Whether the card is up.</summary>
        public bool Showing
        {
            get { return gameObject.activeSelf; }
        }

        /// <summary>Raises the card for whoever is stepping up.</summary>
        public void Show(IntroCard card)
        {
            gameObject.SetActive(true);

            // Cleared HERE rather than on the way out, so a press that arrived while the last
            // card was coming down cannot start this fight before it has been read.
            Hurried = false;
            _up = Time.unscaledTime;

            if (_fightLabel != null)
            {
                _fightLabel.text = Say(FightKey);
                _fightLabel.color = Coal;
            }

            if (_timer != null) _timer.fillAmount = 0f;

            if (_rank != null)
            {
                _rank.gameObject.SetActive(card.Ranked);
                _rank.text = card.Called;
                _rank.color = Tinted(card.Hex, Faint);
            }

            if (_frame != null)
            {
                var edge = _frame.GetComponent<Outline>();

                if (edge != null)
                {
                    edge.effectColor = Tinted(card.EdgeHex, Quiet);
                    edge.effectDistance = new Vector2(card.Edge, card.Edge);
                }

                // The glow is the frame's own shadow, spread rather than offset. Only the bottom
                // of a run has one, so it is turned off rather than merely made dark: a component
                // left running would still cost four draw calls a frame for nothing.
                var glow = _frame.GetComponent<Shadow>();

                if (glow != null && glow != edge) glow.enabled = card.Lit;
            }

            if (_art != null)
            {
                Sprite drawn = _content != null && _content.Enemies != null
                    ? _content.Enemies.For(card.Species, card.Variant)
                    : null;

                _art.sprite = drawn;
                _art.enabled = drawn != null;
            }

            if (_name != null)
            {
                _name.text = Say(card.NameKey);
                _name.color = Ink;
            }

            if (_stats != null)
            {
                _stats.text = card.Stats;
                _stats.color = Faint;
            }

            if (_blocks != null)
            {
                _blocks.text = Say(BlocksKey);
                _blocks.color = Quiet;
            }
        }

        /// <summary>Takes the card away, which is what the first blow of the fight does.</summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// The timer, filling behind the button as the card's own seconds go.
        /// </summary>
        /// <remarks>
        /// A wash over the button rather than a bar beside it, which is the source's
        /// arrangement: the thing running out and the thing that stops it running out are one
        /// object, so there is nothing to look between.
        ///
        /// It fills rather than empties, so what a delver sees growing is the part of the wait
        /// already spent — and a full button is one about to act on its own.
        /// </remarks>
        private void Update()
        {
            if (_timer == null || Held <= 0f) return;

            _timer.fillAmount = Mathf.Clamp01((Time.unscaledTime - _up) / Held);
        }

        /// <summary>A colour out of the card, or the one to fall back on.</summary>
        /// <remarks>
        /// Core hands colours over as <c>#RRGGBB</c> because Core has no idea what a Unity Color
        /// is. A string that will not parse is a content mistake and should look like one rather
        /// than like black on black.
        /// </remarks>
        private static Color Tinted(string hex, Color otherwise)
        {
            Color drawn;

            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out drawn)
                ? drawn
                : otherwise;
        }

        private string Say(string key)
        {
            return Words != null ? Words.Get(key) : key;
        }
    }
}
