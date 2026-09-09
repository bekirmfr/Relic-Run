using System;
using RelicRun.Core.Determinism;
using RelicRun.Core.Meta;
using RelicRun.Core.Presentation;
using RelicRun.Game.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The title screen, drawn.
    /// </summary>
    /// <remarks>
    /// It works out nothing. Every string and every number it shows arrives on a
    /// <see cref="TitleCard"/> that Core built, and the only decisions here are the ones a widget
    /// is allowed to make: what colour a locked banner is, and how wide a bar looks.
    ///
    /// That division is what makes the title testable at all — "what does a level-two delver see"
    /// is a question about a struct, answered by <c>dotnet test</c>, rather than a question about
    /// a screen nobody can open on a build server.
    /// </remarks>
    public sealed class TitlePanel : MetaPanel
    {
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _level;
        [SerializeField] private Image _levelBar;

        [SerializeField] private TMP_Text _best;
        [SerializeField] private TMP_Text _crowns;

        [SerializeField] private TMP_Text _kicker;
        [SerializeField] private TMP_Text _sub;
        [SerializeField] private Button _play;

        [SerializeField] private Button _daily;
        [SerializeField] private TMP_Text _dailyLeft;
        [SerializeField] private TMP_Text _dailyRight;

        [SerializeField] private Button _versus;
        [SerializeField] private TMP_Text _versusLeft;
        [SerializeField] private TMP_Text _versusRight;

        [SerializeField] private Button _how;

        /// <summary>The four screens a delver reads rather than plays.</summary>
        [SerializeField] private Button _board;

        [SerializeField] private Button _relics;

        [SerializeField] private Button _bestiary;

        [SerializeField] private Button _profile;

        /// <summary>
        /// What an open mode's captions look like, and what a shut one's do.
        /// </summary>
        /// <remarks>
        /// Dimmed rather than hidden, because a delver who cannot see the arena has no reason to
        /// keep playing toward it. The source shows both banners from the first launch and writes
        /// the level that opens them across the front — the lock IS the advertisement.
        ///
        /// The colours live here rather than in Core for the reason every colour in this port
        /// does: a view-model carrying them could not be read by anything that was not a screen.
        /// </remarks>
        private static readonly Color Open = new Color(0.90f, 0.87f, 0.80f);

        private static readonly Color Shut = new Color(0.55f, 0.52f, 0.46f);

        public override Page Shows
        {
            get { return Page.Title; }
        }

        /// <summary>The clock counts today's Daily down, so this one really does tick.</summary>
        public override bool Ticks
        {
            get { return true; }
        }

        /// <summary>Where PLAY would go, as of the last draw.</summary>
        /// <remarks>
        /// Kept because the button is pressed later than it is drawn, and the answer can change
        /// underneath it — at midnight, or when a run finishes. Read at press time rather than
        /// captured when the handler was attached.
        /// </remarks>
        private Play _goes;

        private void Awake()
        {
            // Bound once, and each reads its answer when pressed rather than when bound. Go
            // looks up the listener at call time, and PLAY looks up its destination too — the
            // scene wires itself after Awake, and the destination changes at midnight.
            Press(_play, () => Go(_goes.Goes));
            Press(_daily, () => Go(Page.Modes));
            Press(_versus, () => Go(Page.Staging));
            Press(_how, () => Go(Page.How));

            Press(_board, () => Go(Page.Board));
            Press(_relics, () => Go(Page.RelicBook));
            Press(_bestiary, () => Go(Page.Bestiary));
            Press(_profile, () => Go(Page.Profile));
        }

        private static void Press(Button button, Action what)
        {
            if (button != null) button.onClick.AddListener(() => what());
        }

        public override void Draw(SaveVault vault, DateTimeOffset now)
        {
            SaveState earned = vault.Earned;
            bool done = earned.DailyDone.Contains(DailySeed.For(now));

            Show(TitleCards.Of(earned, vault.Chosen, done, false, now));
        }

        /// <summary>Puts a card on the screen.</summary>
        /// <remarks>
        /// The whole card each time, not just the clock. A screen that redrew only the parts it
        /// believed had changed would eventually be wrong about one of them, and a card is a
        /// struct and some strings.
        /// </remarks>
        public void Show(TitleCard card)
        {
            _goes = card.Play;

            Put(_name, card.Name);
            Put(_level, "LV " + card.Level);

            if (_levelBar != null)
            {
                // A capped delver gets a full bar rather than a fraction of a level that does not
                // exist, which is the other reading of a progress of zero at the top.
                _levelBar.fillAmount = card.Capped ? 1f : Mathf.Clamp01((float)card.Progress);
            }

            Put(_best, "BEST " + card.Best);
            Put(_crowns, "CROWNS " + card.Crowns);

            Put(_kicker, card.Kicker);
            Put(_sub, card.Sub);

            Dress(_daily, _dailyLeft, _dailyRight, card.Daily);
            Dress(_versus, _versusLeft, _versusRight, card.Versus);
        }

        /// <summary>
        /// A banner, in the state its mode is in.
        /// </summary>
        /// <remarks>
        /// A shut banner is still PRESSABLE. The source lets a delver open a locked mode and be
        /// told why it is locked, which is more use than a button that does nothing — a button
        /// that does nothing is indistinguishable from one that is broken.
        /// </remarks>
        private static void Dress(Button button, TMP_Text left, TMP_Text right, ModeLine line)
        {
            Put(left, line.Left);
            Put(right, line.Right);

            Color ink = line.Open ? Open : Shut;

            if (left != null) left.color = ink;
            if (right != null) right.color = ink;

            if (button != null) button.interactable = true;
        }

        private static void Put(TMP_Text text, string what)
        {
            if (text != null) text.text = what;
        }
    }
}
