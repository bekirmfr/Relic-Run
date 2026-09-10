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
    /// The mode picker: three ways to play, and which of them this delver may use.
    /// </summary>
    /// <remarks>
    /// Every caption arrives on a <see cref="ModesCard"/>, and the banners on it are the same
    /// ones the title draws — built once in Core so the two screens cannot disagree about what
    /// today's Daily says.
    ///
    /// A shut mode is drawn dim and stays PRESSABLE. The source does the same: pressing it is how
    /// a delver finds out what would open it, and a tile that did nothing would be one they read
    /// as broken.
    /// </remarks>
    public sealed class ModesPanel : MetaPanel
    {
        [SerializeField] private Button _daily;
        [SerializeField] private TMP_Text _dailyTag;
        [SerializeField] private TMP_Text _dailyLeft;
        [SerializeField] private TMP_Text _dailyRight;

        [SerializeField] private Button _versus;
        [SerializeField] private TMP_Text _versusTag;
        [SerializeField] private TMP_Text _versusLeft;
        [SerializeField] private TMP_Text _versusRight;

        [SerializeField] private Button _dungeons;
        [SerializeField] private TMP_Text _dungeonsTag;

        [SerializeField] private Button _back;

        private static readonly Color Open = new Color(0.90f, 0.87f, 0.80f);

        private static readonly Color Shut = new Color(0.55f, 0.52f, 0.46f);

        /// <summary>Whether today's Daily could be started as of the last draw.</summary>
        /// <remarks>
        /// Read at press time rather than captured when the handler was attached, because it
        /// changes underneath the button: at midnight, and the moment a run is banked.
        /// </remarks>
        private bool _canDaily;

        public override Page Shows
        {
            get { return Page.Modes; }
        }

        /// <summary>The Daily's banner carries the same countdown the title does.</summary>
        public override bool Ticks
        {
            get { return true; }
        }

        private void Awake()
        {
            Press(_dungeons, () => Go(Page.Levels));

            // Refused here rather than by a disabled button, so the press still lands and the
            // delver still gets the banner explaining why. A button that cannot be pressed says
            // nothing; one that can be pressed and declines says what it wants.
            //
            // Which run it means, said out loud. Today's Daily and a delve into a hall are the
            // same destination and not the same thing: the Daily is one fixed seed the whole
            // world shares, and a delve is a fresh one nobody else will ever see.
            Press(_daily, () =>
            {
                if (_canDaily) Go(new Play { Goes = Page.Run, StartsTheDaily = true });
            });
            Press(_versus, () => Go(Page.Staging));

            Press(_back, () => Go(Page.Title));
        }

        public override void Draw(SaveVault vault, DateTimeOffset now)
        {
            SaveState earned = vault.Earned;
            bool done = earned.DailyDone.Contains(DailySeed.For(now));

            Show(ModesCards.Of(earned, done, false, now));
        }

        public void Show(ModesCard card)
        {
            _canDaily = card.CanDaily;

            Dress(_dailyTag, _dailyLeft, _dailyRight, card.Daily);
            Dress(_versusTag, _versusLeft, _versusRight, card.Versus);

            if (_dungeonsTag != null)
            {
                _dungeonsTag.text = card.Dungeons;
                _dungeonsTag.color = Open;
            }
        }

        private static void Dress(TMP_Text tag, TMP_Text left, TMP_Text right, ModeLine line)
        {
            Color ink = line.Open ? Open : Shut;

            Put(tag, line.Tag, ink);
            Put(left, line.Left, ink);
            Put(right, line.Right, ink);
        }

        private static void Put(TMP_Text text, string what, Color ink)
        {
            if (text == null) return;

            text.text = what;
            text.color = ink;
        }

        private static void Press(Button button, Action what)
        {
            if (button != null) button.onClick.AddListener(() => what());
        }
    }
}
