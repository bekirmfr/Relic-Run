using System.Globalization;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The one second chance a run gets.
    /// </summary>
    /// <remarks>
    /// Offered once per delve and never again — a delver who rises and falls again has fallen for
    /// good — so this is the last screen most runs ever show. What it asks is whether to spend a
    /// hundred and fifty sparks to stand back up on the floor that killed you, at half health,
    /// against the foe that did it and whatever is still behind it.
    ///
    /// The purse is NOT what pays. Sparks outlive a run, which is why the answer the engine gets
    /// is a bare yes and the spending happens in the scene against the save — see
    /// <c>FightScene.Rose</c>.
    /// </remarks>
    public sealed class ReviveStage : RunStage
    {
        [SerializeField] private TMP_Text _kicker;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _sub;

        [Header("Rise")]
        [SerializeField] private Button _rise;
        [SerializeField] private TMP_Text _riseKicker;
        [SerializeField] private TMP_Text _riseLabel;
        [SerializeField] private TMP_Text _riseAfter;

        [Header("Or not")]
        [SerializeField] private Button _accept;
        [SerializeField] private TMP_Text _acceptLabel;

        /// <summary>
        /// What the source says, in English.
        /// </summary>
        /// <remarks>
        /// Prose, and untranslated at the source with no key behind it — the same policy the
        /// event and the bazaar follow. The chrome around it has keys.
        /// </remarks>
        public const string Fallen = "YOU HAVE FALLEN";

        public const string Dark = "The dark is not done with you.";

        public const string Rise = "Rise once with full health — or let the purse go.";

        public const string Accept = "Accept death";

        /// <summary>The red a run ends in.</summary>
        private static readonly Color Blood = new Color(0.769f, 0.349f, 0.235f);

        private static readonly Color Ink = new Color(0.906f, 0.878f, 0.824f);

        private static readonly Color Faint = new Color(0.545f, 0.506f, 0.447f);

        private static readonly Color Dim = new Color(0.396f, 0.361f, 0.306f);

        /// <summary>What is written on the green, which is a ground rather than an ink.</summary>
        private static readonly Color Coal = new Color(0.078f, 0.071f, 0.059f);

        public override AskKind Answers
        {
            get { return AskKind.Revive; }
        }

        private void Awake()
        {
            // Rising is guarded by the balance rather than by the button being gone: a delver
            // short of sparks should see what they were short OF.
            if (_rise != null) _rise.onClick.AddListener(Spend);

            if (_accept != null) _accept.onClick.AddListener(() => Decide(new Answer()));
        }

        /// <summary>Whether the delver paid to rise, which the scene reads once it is answered.</summary>
        /// <remarks>
        /// Sparks are the save's, not the run's, so the stage cannot spend them itself. It says
        /// that it WOULD have, and the scene — which holds the save — does the spending.
        /// </remarks>
        public bool Paid { get; private set; }

        /// <summary>Puts the last question of the run on the screen.</summary>
        public override void Draw(Ask ask, RunState run)
        {
            Paid = false;

            int sparks = Sparks;

            ReviveCard card = ReviveCards.Of(ask.Floor, sparks);

            Write(_kicker, Fallen, Blood);
            Write(_title, Dark, Ink);
            Write(_sub, Rise, Faint);

            Write(_riseKicker, Say("spendSparks"), card.Afford ? Coal : Dim);
            Write(_riseLabel, "◆ " + Count(card.Cost), card.Afford ? Coal : Dim);

            Write(_riseAfter,
                card.Afford
                    ? Say("sparksLeft", "n", Count(card.After))
                    : Say("sparksShort", "n", Count(card.After)),
                card.Afford ? Coal : Blood);

            // Filled green when it can be taken, hollow when it cannot — and the colours are the
            // BUTTON's, set once when the scene was built. A Selectable repaints its target
            // graphic from its own block on every state change, so a colour written here would be
            // gone by the next frame.
            if (_rise != null) _rise.interactable = card.Afford;

            Write(_acceptLabel, Accept, Faint);
        }

        /// <summary>How many sparks the delver has, which the scene keeps up to date.</summary>
        /// <remarks>
        /// A plain property rather than a fetch, for the same reason <c>Words</c> is: the save is
        /// the scene's to hold, and a stage drawing itself is not allowed to wait for anything.
        /// </remarks>
        public int Sparks { get; set; }

        /// <summary>Rising, and saying so, so the scene knows to charge for it.</summary>
        private void Spend()
        {
            Paid = true;

            Decide(new Answer { Yes = true });
        }

        private static void Write(TMP_Text text, string what, Color ink)
        {
            if (text == null) return;

            text.text = what;
            text.color = ink;
        }

        private static string Count(int number)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }
    }
}
