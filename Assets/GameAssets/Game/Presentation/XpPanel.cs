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

        [Header("The fill")]
        [SerializeField] private float _fullBarSeconds = 1.2f;
        [SerializeField] private float _shortestSeconds = 0.25f;

        [Header("The level-up")]
        [SerializeField] private float _holdSeconds = 1.6f;
        [SerializeField] private float _punch = 1.25f;

        private int _earned;
        private int _showing = 1;
        private double _progress;
        private bool _celebrating;
        private IReadOnlyList<string> _gainsList;

        /// <summary>The bar's path, or null when nothing has been handed over.</summary>
        private List<XpLeg> _legs;

        /// <summary>What the run earned besides experience, shown once the bar has stopped.</summary>
        private IReadOnlyList<string> _achieved;

        /// <summary>Everything every level reached on the way handed over, for the resting screen.</summary>
        private readonly List<Unlock> _unlocked = new List<Unlock>();

        private int _leg;
        private float _clock;
        private bool _holding;
        private bool _playing;
        private bool _played;

        private void Awake()
        {
            // The first press while the bar is moving finishes it rather than leaving: a delver
            // tapping through should still land on what they earned, not skip the only telling.
            if (_on != null)
            {
                _on.onClick.AddListener(() =>
                {
                    if (_playing) Finish();
                    else Go(Page.Title);
                });
            }
        }

        /// <summary>Hands the screen what a run earned.</summary>
        /// <remarks>
        /// Told rather than read, for the reason the end-of-run screen is: by the time this
        /// opens, the save already holds the new total, so the save cannot say what changed.
        /// The experience held BEFORE the run is the one number it needs; the level, the fill
        /// and every boundary on the way come out of it.
        /// </remarks>
        public void Show(int before, int earned, IReadOnlyList<string> achieved)
        {
            _earned = earned;
            _achieved = achieved;
            _legs = XpTracks.Of(before, earned);

            _unlocked.Clear();
            foreach (XpLeg leg in _legs) _unlocked.AddRange(leg.Unlocks);

            _played = false;
            _playing = false;

            Rest(_legs[0].Level, _legs[0].From, false, null);
        }

        public override void Draw(SaveVault vault, DateTimeOffset now)
        {
            // Nothing has been handed over, so the delver's standing level is what is shown.
            // A screen that opened blank would look broken; one showing where they actually are
            // is merely uneventful, which is what it is.
            if (_legs == null)
            {
                _showing = vault.Earned.Level;
                _progress = Core.Run.Progression.Progress(vault.Earned.Xp);
                Paint();
                return;
            }

            // Played once, on the first showing. Coming back to it shows where it ended.
            if (_played) Finish();
            else Play();
        }

        private void Play()
        {
            _leg = 0;
            _clock = 0f;
            _holding = false;
            _playing = true;
            _played = true;

            Rest(_legs[0].Level, _legs[0].From, false, null);
        }

        private void Update()
        {
            if (!_playing) return;

            XpLeg leg = _legs[_leg];
            _clock += Time.unscaledDeltaTime;

            if (_holding)
            {
                // The level text swells and settles while the level-up is read.
                float held = _clock / Mathf.Max(0.01f, _holdSeconds);
                Swell(held < 1f ? Mathf.Lerp(_punch, 1f, held) : 1f);

                if (held < 1f) return;

                _holding = false;
                _clock = 0f;
                _leg++;
                Rest(_legs[_leg].Level, _legs[_leg].From, false, null);
                return;
            }

            float seconds = Mathf.Max(_shortestSeconds,
                _fullBarSeconds * (float)(leg.To - leg.From));
            float t = Mathf.Clamp01(_clock / seconds);

            // Eased out, so the bar arrives rather than stops.
            float eased = 1f - (1f - t) * (1f - t);
            Rest(leg.Level, leg.From + (leg.To - leg.From) * eased, false, null);

            if (t < 1f) return;

            if (leg.Reaches > 0)
            {
                _holding = true;
                _clock = 0f;
                Rest(leg.Reaches, 0d, true, XpTracks.Lines(leg.Unlocks));
                return;
            }

            Finish();
        }

        /// <summary>Puts the screen where the bar ends: final level, final fill, everything earned.</summary>
        private void Finish()
        {
            _playing = false;
            _holding = false;
            Swell(1f);

            if (_legs == null) return;

            XpLeg last = _legs[_legs.Count - 1];
            bool levelled = _legs.Count > 1;

            var lines = XpTracks.Lines(_unlocked);
            if (_achieved != null) lines.AddRange(_achieved);

            Rest(last.Level, last.To, levelled, lines);
        }

        private void Rest(int level, double progress, bool celebrating, IReadOnlyList<string> gains)
        {
            _showing = level;
            _progress = progress;
            _celebrating = celebrating;
            _gainsList = gains;

            Paint();
        }

        private void Swell(float scale)
        {
            if (_level != null) _level.transform.localScale = Vector3.one * scale;
        }

        private void Paint()
        {
            XpCard card = XpCards.Of(_earned, _showing, _progress, _celebrating, _gainsList);

            Put(_kicker, card.Kicker, card.LevelledUp ? Lit : Ink);
            Put(_gained, card.Gained, Lit);
            Put(_level, card.Level, card.LevelledUp ? Lit : Ink);
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
