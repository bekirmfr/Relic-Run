using System;
using System.Collections.Generic;
using RelicRun.Core.Presentation;
using RelicRun.Game.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// What a run earned, and any level it crossed.
    /// </summary>
    /// <remarks>
    /// The only place a level-up is ever announced. The level is banked whether or not a delver
    /// sees this, which is exactly why <see cref="Pages.Home"/> refuses to skip it: what is lost
    /// by missing it is not a level, it is the telling.
    ///
    /// It shows a level rather than THE level. The bar travels from where the run started to
    /// where it ended, one boundary at a time, and what is written beside it is whichever level
    /// the travelling has reached.
    /// </remarks>
    public sealed class XpPanel : MetaPanel
    {
        [SerializeField] private TMP_Text _kicker;
        [SerializeField] private TMP_Text _gained;
        [SerializeField] private TMP_Text _level;
        [SerializeField] private TMP_Text _next;
        [SerializeField] private Image _bar;
        [SerializeField] private TMP_Text _gains;
        [SerializeField] private Button _on;

        private static readonly Color Ink = new Color(0.90f, 0.87f, 0.80f);

        private static readonly Color Lit = new Color(0.89f, 0.70f, 0.25f);

        public override Page Shows
        {
            get { return Page.Xp; }
        }

        private int _earned;
        private int _showing = 1;
        private double _progress;
        private bool _celebrating;
        private IReadOnlyList<string> _gainsList;

        private void Awake()
        {
            if (_on != null) _on.onClick.AddListener(() => Go(Page.Title));
        }

        /// <summary>Hands the screen what a run earned.</summary>
        /// <remarks>
        /// Told rather than read, for the reason the end-of-run screen is: by the time this
        /// opens, the save already holds the new total, so the save cannot say what changed.
        /// </remarks>
        public void Show(int earned, int level, double progress, bool celebrating,
            IReadOnlyList<string> gains)
        {
            _earned = earned;
            _showing = level;
            _progress = progress;
            _celebrating = celebrating;
            _gainsList = gains;

            Paint();
        }

        public override void Draw(SaveVault vault, DateTimeOffset now)
        {
            // Nothing has been handed over, so the delver's standing level is what is shown.
            // A screen that opened blank would look broken; one showing where they actually are
            // is merely uneventful, which is what it is.
            if (_gainsList == null)
            {
                _showing = vault.Earned.Level;
                _progress = Core.Run.Progression.Progress(vault.Earned.Xp);
            }

            Paint();
        }

        private void Paint()
        {
            XpCard card = XpCards.Of(_earned, _showing, _progress, _celebrating, _gainsList);

            Put(_kicker, card.Kicker, card.LevelledUp ? Lit : Ink);
            Put(_gained, card.Gained, Lit);
            Put(_level, card.Level, Ink);
            Put(_next, card.Next, Ink);

            if (_bar != null) _bar.fillAmount = Mathf.Clamp01((float)card.Progress);

            if (_gains != null)
            {
                var lines = new System.Text.StringBuilder();

                foreach (string gain in card.Gains)
                {
                    if (lines.Length > 0) lines.Append('\n');
                    lines.Append(gain);
                }

                _gains.text = lines.ToString();
                _gains.color = Lit;
            }
        }

        private static void Put(TMP_Text text, string what, Color ink)
        {
            if (text == null) return;

            text.text = what;
            text.color = ink;
        }
    }
}
