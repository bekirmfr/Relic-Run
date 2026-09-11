using RelicRun.Core.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// One copy of one relic, with everything it has to say about itself.
    /// </summary>
    /// <remarks>
    /// Four things at once in a box the size of a thumbnail: what the relic is, how close it is
    /// to doing its thing, whether it has a second clock running, and how much of its budget is
    /// left. The source solves the crowding by putting three of them on the EDGES — a charge bar
    /// along the bottom, a hairline above it, a budget down the right side — so the icon keeps
    /// the middle and a delver reads the shape without reading anything.
    ///
    /// It decides nothing. Every number comes from a <see cref="RelicMeter"/> built in Core from
    /// the shelf and one event's snapshot, so what a slot shows is a fact about the fight rather
    /// than about the widget. What lives here is what the source keeps in CSS: which colours,
    /// which edge, how long a flash lasts.
    /// </remarks>
    public sealed class RelicSlot : MonoBehaviour
    {
        [SerializeField] private Image _frame;
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _badge;

        [Tooltip("Along the bottom: progress toward the next time this fires.")]
        [SerializeField] private Image _charge;

        [Tooltip("A hairline above the charge, for a copy counting two different things.")]
        [SerializeField] private Image _hairline;

        [Tooltip("Down the right side: how much of its budget is left.")]
        [SerializeField] private Image _uses;

        /// <summary>Gold, and the same gold as the winding gauges, because it is the same idea.</summary>
        private static readonly Color Charge = new Color(0.89f, 0.70f, 0.25f);

        /// <summary>Violet, so a second clock is legible as a DIFFERENT clock at a glance.</summary>
        private static readonly Color Attached = new Color(0.72f, 0.65f, 0.94f);

        private static readonly Color Plenty = new Color(0.49f, 0.60f, 0.42f);
        private static readonly Color Nearly = new Color(0.77f, 0.56f, 0.42f);

        /// <summary>
        /// The plate behind the icon, and the same plate once the relic is spent.
        /// </summary>
        /// <remarks>
        /// A spent relic stays on the shelf, so it is dimmed rather than removed — it is still
        /// occupying a slot the delver cannot use for anything else, and a tray that quietly
        /// dropped it would make the shelf look roomier than it is.
        /// </remarks>
        private static readonly Color Plate = new Color(0.07f, 0.06f, 0.05f, 0.85f);

        private static readonly Color Spent = new Color(0.07f, 0.06f, 0.05f, 0.40f);

        /// <summary>The icon's own tint, which is no tint at all until it fires.</summary>
        private static readonly Color Lit = Color.white;

        /// <summary>How long a copy stays lit after it fires.</summary>
        private const float FlashSeconds = 0.35f;

        private float _flash;

        /// <summary>
        /// Which relic this slot is, set once when the shelf is laid out.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Show"/> because the shelf does not change during a fight and
        /// the meter changes every event. Doing both per event would mean re-fetching a sprite
        /// two hundred times to draw the same picture.
        /// </remarks>
        public void Bind(RelicCopy copy, Sprite icon)
        {
            if (_icon != null)
            {
                _icon.sprite = icon;

                // Forty-nine of the fifty relics have a cell. The one that does not draws
                // nothing rather than drawing a white box where a picture should be.
                _icon.enabled = icon != null;
            }

            name = copy.Relic.ToString();

            // Blank, until something says otherwise. The bars are authored full — a Filled image
            // has to be, or there is nothing to see while building the prefab — so a slot that
            // was laid out and not yet drawn showed a complete gold charge and a full budget on
            // every relic, including the ones that have neither. It lasted only until the first
            // event, which is precisely the kind of wrong that survives review: too brief to
            // notice and perfectly wrong while it lasts.
            Blank();
        }

        /// <summary>Every gauge off, which is what a copy with nothing to say looks like.</summary>
        private void Blank()
        {
            if (_charge != null) _charge.enabled = false;
            if (_hairline != null) _hairline.enabled = false;
            if (_uses != null) _uses.enabled = false;
            if (_badge != null) _badge.text = "";
            if (_frame != null) _frame.color = Plate;
            if (_icon != null) _icon.color = Lit;
        }

        /// <summary>Draws this copy as it stood at one moment of the fight.</summary>
        /// <summary>
        /// Hides or shows the relic's picture, leaving the slot itself where it is.
        /// </summary>
        /// <remarks>
        /// For the moment between a relic being taken and its icon arriving. The SLOT stays —
        /// hiding it would shuffle the whole shelf sideways and then shuffle it back — so what
        /// goes away is only the picture the flight is carrying.
        /// </remarks>
        public void Veil(bool hidden)
        {
            if (_icon != null) _icon.enabled = !hidden && _icon.sprite != null;
        }

        public void Show(RelicMeter meter)
        {
            Cadence charge = meter.Charge;
            Cadence second = meter.Hairline;

            Fill(_charge, charge, Charge);
            Fill(_hairline, second, Attached);

            if (_uses != null)
            {
                _uses.enabled = meter.Uses.Any;

                if (meter.Uses.Any)
                {
                    _uses.fillAmount = Part(meter.Uses.Left, meter.Uses.Cap);

                    // Two colours rather than a gradient. A bar that shades continuously says
                    // "getting worse"; what a delver needs to know is whether they are about to
                    // run out, and that is a threshold, not a slope.
                    _uses.color = meter.Uses.Left <= Budget.BadgeFrom ? Nearly : Plenty;
                }
            }

            if (_badge != null) _badge.text = meter.Uses.Badge;

            if (_frame != null)
            {
                _frame.color = meter.Uses.Spent ? Spent : Plate;
            }

            // The icon fades with its plate. Dimming the backing alone leaves a bright picture
            // sitting on a dead slot, which reads as the relic still being live.
            if (_icon != null && _flash <= 0f)
            {
                _icon.color = meter.Uses.Spent ? new Color(1f, 1f, 1f, 0.45f) : Lit;
            }
        }

        /// <summary>
        /// Lights up, because this copy is the one that just fired.
        /// </summary>
        /// <remarks>
        /// The reason <c>CombatEvent</c> carries an inventory index at all. Relics are per-copy,
        /// so "the Whetstone fired" is not enough to know which of two icons to light, and a tray
        /// that flashed both would be showing something that did not happen.
        /// </remarks>
        public void Fire()
        {
            _flash = FlashSeconds;
        }

        private void Update()
        {
            if (_flash <= 0f || _icon == null) return;

            _flash -= Time.unscaledDeltaTime;

            // Unscaled, like the playback clock. A fight watched at double speed should not also
            // flash twice as fast — the beat is the pacing's business, not the widget's.
            float over = _flash <= 0f ? 0f : _flash / FlashSeconds;

            _icon.color = Color.Lerp(Lit, Charge, over);
        }

        private static void Fill(Image bar, Cadence cadence, Color colour)
        {
            if (bar == null) return;

            bar.enabled = cadence.Any;
            if (!cadence.Any) return;

            bar.color = colour;
            bar.fillAmount = Part(cadence.Filled, cadence.Total);
        }

        /// <summary>
        /// A fraction, without dividing by a total that might be zero.
        /// </summary>
        /// <remarks>
        /// A cadence with no total is not drawn at all, so this should never see one — which is
        /// exactly the kind of should that turns into a NaN width and an invisible bar nobody can
        /// explain.
        /// </remarks>
        private static float Part(int filled, int total)
        {
            if (total <= 0) return 0f;

            float part = (float)filled / total;

            return part < 0f ? 0f : (part > 1f ? 1f : part);
        }
    }
}
