using System;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;
using RelicRun.Game.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The end of a run.
    /// </summary>
    /// <remarks>
    /// The one screen in the menu that is about something that just happened rather than about
    /// the save. It cannot draw itself from the vault, because the vault has already been
    /// written by the time it opens — the new best is in there, so asking whether this run beat
    /// the record would answer yes forever. The run tells it instead, through <see cref="Show"/>.
    ///
    /// Until the run layer exists nothing calls that, so the screen draws a placeholder rather
    /// than inventing a run. A screen showing somebody a fictional score is worse than one
    /// saying it has nothing to show.
    /// </remarks>
    public sealed class OverPanel : MetaPanel
    {
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _titleUnder;
        [SerializeField] private TMP_Text _score;
        [SerializeField] private TMP_Text _note;
        [SerializeField] private TMP_Text _rate;
        [SerializeField] private TMP_Text _stats;

        [SerializeField] private Button _home;
        [SerializeField] private Button _board;

        private static readonly Color Ink = new Color(0.90f, 0.87f, 0.80f);

        private static readonly Color Faint = new Color(0.42f, 0.40f, 0.35f);

        private static readonly Color Lit = new Color(0.89f, 0.70f, 0.25f);

        /// <summary>What the last run came to, or nothing if none has finished this session.</summary>
        private OverCard _card;

        private bool _have;

        public override Page Shows
        {
            get { return Page.Over; }
        }

        private void Awake()
        {
            // Home is not always the title. A run that earned experience stops at the experience
            // screen on the way, which is Core's rule and is asked rather than repeated here.
            if (_home != null)
            {
                _home.onClick.AddListener(() =>
                    Go(Pages.Home(Page.Over, _have ? _card.Xp : 0, _shown)));
            }

            if (_board != null) _board.onClick.AddListener(() => Go(Page.Board));
        }

        /// <summary>Whether the experience screen has already been through.</summary>
        /// <remarks>
        /// Kept here because it is a fact about this run rather than about the save, and because
        /// the rule that reads it — stop at the experience screen once, not every time — needs
        /// somewhere to remember that once has happened.
        /// </remarks>
        private bool _shown;

        /// <summary>Hands the screen a finished run.</summary>
        public void Show(OverCard card)
        {
            _card = card;
            _have = true;
            _shown = false;

            Paint();
        }

        /// <summary>Marks the experience as having been shown, so home goes home.</summary>
        public void Spent()
        {
            _shown = true;
        }

        public override void Draw(SaveVault vault, DateTimeOffset now)
        {
            Paint();
        }

        private void Paint()
        {
            if (!_have)
            {
                Put(_title, "NO RUN", Faint);
                Put(_titleUnder, "to show yet.", Faint);
                Put(_score, string.Empty, Ink);
                Put(_note, string.Empty, Faint);
                Put(_rate, string.Empty, Faint);
                Put(_stats, string.Empty, Faint);
                return;
            }

            Put(_title, _card.Title, Ink);
            Put(_titleUnder, _card.TitleUnder, Ink);

            Put(_score, _card.Score.ToString(), _card.Best ? Lit : Ink);
            Put(_note, _card.Note, _card.Best ? Lit : Faint);

            // Absent rather than blank when there is no rate to show: the line is an explanation,
            // and an explanation of nothing is a gap the eye stops on.
            Put(_rate, _card.Rate ?? string.Empty, Faint);

            Put(_stats, "FLOOR " + _card.Floor + "   KILLS " + _card.Kills +
                        "   GOLD " + _card.Banked + "   XP " + _card.Xp, Ink);
        }

        private static void Put(TMP_Text text, string what, Color ink)
        {
            if (text == null) return;

            text.text = what;
            text.color = ink;
        }
    }
}
