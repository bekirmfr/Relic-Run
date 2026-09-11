using RelicRun.Core.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// One relic on a table: its picture, its three lines, and its family's colour behind them.
    /// </summary>
    /// <remarks>
    /// Every screen that offers a relic offers the same card — the draft, the bazaar's shelf, and
    /// whatever comes after. Before this they each reached into the card by walking its children
    /// and taking texts in the order they happened to be made in, which works exactly until
    /// somebody reorders the hierarchy in the editor and a description appears where a name
    /// should be.
    ///
    /// So the card names its own parts. What a screen hands it is what a delver reads; how the
    /// card is put together is the card's business and the editor's.
    ///
    /// The GROUND carries the family's colour, which is the point of it. A delver drafting under
    /// pressure reads the colour before they read the word, and a shelf of five relics in five
    /// families should be five colours rather than five identical slabs with a small word on
    /// each.
    /// </remarks>
    public sealed class RelicCardView : MonoBehaviour
    {
        [Tooltip("The card's back. Tinted by the relic's family.")]
        [SerializeField] private Image _ground;

        [Tooltip("The family's colour at full strength, for a rule or a rim. Optional.")]
        [SerializeField] private Graphic _edge;

        [SerializeField] private Image _icon;

        [Header("The three lines")]
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _what;
        [SerializeField] private TMP_Text _under;

        [Header("Colour")]
        [Tooltip("How much of the family's colour reaches the back of the card.")]
        [Range(0f, 1f)]
        [SerializeField] private float _wash = 0.22f;

        /// <summary>
        /// The dark every card is mixed down toward.
        /// </summary>
        /// <remarks>
        /// A family's colour at full strength behind a sentence is a sentence nobody can read —
        /// GOLD is nearly white and GRACE is a pale violet. Mixed most of the way to the game's
        /// own ground they stay tellable apart at a glance and stay a background.
        /// </remarks>
        private static readonly Color Ground = new Color(0.078f, 0.067f, 0.047f);

        private static readonly Color Ink = new Color(0.906f, 0.878f, 0.824f);

        private static readonly Color Faint = new Color(0.545f, 0.506f, 0.447f);

        private static readonly Color Quiet = new Color(0.396f, 0.361f, 0.306f);

        /// <summary>The relic's picture, for whatever wants to carry it somewhere.</summary>
        public Image Icon
        {
            get { return _icon; }
        }

        /// <summary>The button this card is, for whoever wants to know it was pressed.</summary>
        public Button Press
        {
            get { return GetComponent<Button>(); }
        }

        /// <summary>
        /// Dresses the card.
        /// </summary>
        /// <param name="what">
        /// Already said and already painted, because only the caller knows whether it is a
        /// relic's description — which has live numbers and stat chips in it — or something else.
        /// </param>
        public void Show(Offered offered, string named, string what, string under, Sprite icon)
        {
            Color family = Family(offered.FamilyHex);

            if (_ground != null)
            {
                _ground.color = Color.Lerp(Ground, family, _wash);
                _ground.enabled = true;
            }

            if (_edge != null) _edge.color = family;

            Write(_name, named, Ink);
            Write(_what, what, Faint);
            Write(_under, under, Quiet);

            if (_icon == null) return;

            _icon.sprite = icon;
            _icon.enabled = icon != null;
            _icon.preserveAspect = true;
        }

        /// <summary>
        /// Tints the card's back without saying anything.
        /// </summary>
        /// <remarks>
        /// For a card that is not offering a relic — the bazaar's waking shelf, which is about a
        /// copy a delver already carries and has no family to show.
        /// </remarks>
        public void Plain()
        {
            if (_ground != null) _ground.color = Ground;

            if (_edge != null) _edge.color = Quiet;

            if (_icon != null) _icon.enabled = false;
        }

        /// <summary>
        /// A family's colour, or the ground when nobody wrote one down.
        /// </summary>
        /// <remarks>
        /// The hex is the SET catalog's, carried on the offer so that Core decides which colour a
        /// family is and a screen only decides how much of it to use.
        /// </remarks>
        private static Color Family(string hex)
        {
            Color drawn;

            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out drawn)
                ? drawn
                : Ground;
        }

        private static void Write(TMP_Text text, string said, Color ink)
        {
            if (text == null) return;

            text.text = said ?? string.Empty;
            text.color = ink;
        }
    }
}
