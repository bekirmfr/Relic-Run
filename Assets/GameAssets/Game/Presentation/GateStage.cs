using System.Globalization;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;
using RelicRun.Game.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The gate: a cleared floor, a full purse, and the stairs going down.
    /// </summary>
    /// <remarks>
    /// The only decision in this game that is about the whole run. Every other choice a delver
    /// makes is between two things they find out about in a minute; this one asks them to bet
    /// everything the run has earned on a fight they have been shown one card of.
    ///
    /// So the screen is built around one number said three times — what is held, what is at risk,
    /// what would be kept — and one peek at what is down there. What lives in this file is only
    /// the drawing; which foe is peeked at, and what the line under it says, is
    /// <see cref="GateCards"/>' answer.
    /// </remarks>
    public sealed class GateStage : RunStage
    {
        [SerializeField] private TMP_Text _kicker;
        [SerializeField] private TMP_Text _title;

        [Tooltip("The purse, which is the whole decision.")]
        [SerializeField] private TMP_Text _gold;

        [SerializeField] private TMP_Text _purseLabel;

        [Header("Risk it all")]
        [SerializeField] private Button _descend;
        [SerializeField] private TMP_Text _descendKicker;
        [SerializeField] private TMP_Text _descendLabel;
        [SerializeField] private TMP_Text _descendRisk;

        [Header("Walk away")]
        [SerializeField] private Button _leave;
        [SerializeField] private TMP_Text _leaveKicker;
        [SerializeField] private TMP_Text _leaveLabel;
        [SerializeField] private TMP_Text _leaveKeep;

        [Header("What is down there")]
        [SerializeField] private GameObject _peek;
        [SerializeField] private Image _peekArt;
        [SerializeField] private TMP_Text _peekKicker;
        [SerializeField] private TMP_Text _peekName;
        [SerializeField] private TMP_Text _peekStats;
        [SerializeField] private GameContent _content;

        /// <summary>The gold everything on this screen is about.</summary>
        private static readonly Color Coin = new Color(0.890f, 0.702f, 0.255f);

        /// <summary>And the green of the way out, which is the only green in a run.</summary>
        private static readonly Color Away = new Color(0.486f, 0.604f, 0.416f);

        private static readonly Color Ink = new Color(0.906f, 0.878f, 0.824f);

        /// <summary>
        /// What is written ON the gold, which is the game's ground rather than its ink.
        /// </summary>
        /// <remarks>
        /// The descend button is a filled gold slab, so everything on it reads dark — the same
        /// treatment the end screen's CONTINUE gets. Written cream, as it was first, the label
        /// on the most consequential button in the game was the least legible thing on screen.
        /// </remarks>
        private static readonly Color Coal = new Color(0.078f, 0.071f, 0.059f);

        /// <summary>And the same, quieter, for the line that only labels the button.</summary>
        private static readonly Color Faded = new Color(0.078f, 0.071f, 0.059f, 0.65f);

        private static readonly Color Faint = new Color(0.545f, 0.506f, 0.447f);

        /// <summary>What the danger under the floor is written in.</summary>
        private static readonly Color Blood = new Color(0.478f, 0.180f, 0.094f);

        private static readonly Color Warn = new Color(0.769f, 0.349f, 0.235f);

        public override AskKind Answers
        {
            get { return AskKind.CashOut; }
        }

        private void Awake()
        {
            // The answer is one boolean and the two buttons are its two values. Wired once, in
            // Awake, because unlike the draft's cards there are exactly two of them for the whole
            // run and rewiring them every floor would be work for nothing.
            if (_descend != null)
            {
                _descend.onClick.AddListener(() => Decide(new Answer { Yes = false }));
            }

            if (_leave != null)
            {
                _leave.onClick.AddListener(() => Decide(new Answer { Yes = true }));
            }
        }

        /// <summary>Puts the decision on the screen.</summary>
        public override void Draw(Ask ask, RunState run)
        {
            GateCard gate = GateCards.Of(ask.Floor, run.Gold, ask.Pack);

            string purse = gate.Gold.ToString(CultureInfo.InvariantCulture);

            Write(_kicker, Say("floorCleared", "n", Count(gate.Floor)), Faint);
            Write(_title, Say("stairsDown"), Ink);
            Write(_gold, "◆ " + purse, Coin);
            Write(_purseLabel, Say("inThePurse"), Faint);

            Write(_descendKicker, Say("riskItAll"), Faded);
            Write(_descendLabel, Say("descendBtn"), Coal);
            Write(_descendRisk, Say("riskGold", "n", purse), Blood);

            Write(_leaveKicker, Say("walkAway"), Away);
            Write(_leaveLabel, Say("cashOut"), Away);
            Write(_leaveKeep, Say("keepGold", "n", purse), Away);

            Peek(gate.Below);
        }

        /// <summary>
        /// What is waiting one floor down, or nothing at all.
        /// </summary>
        /// <remarks>
        /// The foe at the BACK of the pack, which is the one worth knowing about — a pack is
        /// fought front to back with the worst of it last, so the first foe would only tell a
        /// delver they survive the next thirty seconds.
        /// </remarks>
        private void Peek(WaitingBelow below)
        {
            if (_peek == null) return;

            _peek.SetActive(below.Known);

            if (!below.Known) return;

            Write(_peekKicker, Say("waitingBelow"), Warn);
            Write(_peekName, Say(below.NameKey), Ink);
            Write(_peekStats, below.Line, Faint);

            if (_peekArt == null) return;

            Sprite drawn = _content != null && _content.Enemies != null
                ? _content.Enemies.For(below.Species, below.Variant)
                : null;

            _peekArt.sprite = drawn;
            _peekArt.enabled = drawn != null;
        }

        private static void Write(TMP_Text text, string what, Color ink)
        {
            if (text == null) return;

            text.text = what;
            text.color = ink;
        }

        /// <summary>
        /// A floor number, written the way a machine writes numbers.
        /// </summary>
        /// <remarks>
        /// Invariant on purpose. Every language's table fills <c>{n}</c> from this, and a culture
        /// that groups thousands or writes its own digits would put a separator in a number that
        /// never exceeds thirteen.
        /// </remarks>
        private static string Count(int number)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }
    }
}
