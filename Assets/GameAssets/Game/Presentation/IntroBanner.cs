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
    /// It is raised on the WALK step, which is the gap playback already reserves for setting off
    /// down the hall to meet whoever is arriving, and it comes down on the next event drawn. So
    /// its length is the pacing's <c>WalkMs</c> and nothing here counts time — which also means a
    /// delve watched with the intro skipped never builds one.
    ///
    /// The source lets a delver dismiss it early with a FIGHT button. That is not here yet: the
    /// playback loop waits a fixed span for a walk, so a button would take the card away and then
    /// stand looking at an empty room for the rest of it. It wants the same gate <c>Paused</c>
    /// uses, which arrives with the pause screen.
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
        [SerializeField] private GameContent _content;

        /// <summary>What the source says under the numbers, in the delver's own language.</summary>
        public const string BlocksKey = "blocksWay";

        /// <summary>How big the foe is drawn on the card.</summary>
        /// <remarks>
        /// The source's 232 pixels, which is eight times a 32-wide sprite less a little. It is
        /// the largest anything in this game is ever drawn, and deliberately so.
        /// </remarks>
        public const int Portrait = 232;

        private static readonly Color Ink = new Color(0.769f, 0.349f, 0.235f);

        private static readonly Color Faint = new Color(0.545f, 0.506f, 0.447f);

        private static readonly Color Quiet = new Color(0.396f, 0.361f, 0.306f);

        /// <summary>What the game says, handed down rather than fetched.</summary>
        public Locale Words { get; set; }

        /// <summary>Whether the card is up.</summary>
        public bool Showing
        {
            get { return gameObject.activeSelf; }
        }

        /// <summary>Raises the card for whoever is stepping up.</summary>
        public void Show(IntroCard card)
        {
            gameObject.SetActive(true);

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
