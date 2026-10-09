using System;
using System.Collections.Generic;
using RelicRun.Core.Run;

namespace RelicRun.Core.Presentation
{
    /// <summary>What sort of thing a level-up hands over.</summary>
    /// <remarks>
    /// Relics and wardrobe pieces are named before anything grants them, so the screen that
    /// reveals unlocks is written once and the day they arrive is a list getting longer rather
    /// than a screen being rebuilt.
    /// </remarks>
    public enum UnlockKind
    {
        /// <summary>The stat curve and any structural perk, as the level-up screen words it.</summary>
        Perk = 0,

        /// <summary>A hall, and the art it is drawn against.</summary>
        Hall = 1,

        Relic = 2,

        Wardrobe = 3,
    }

    /// <summary>One thing a level handed over.</summary>
    public struct Unlock
    {
        public UnlockKind Kind;

        /// <summary>What is written for it.</summary>
        public string Text;

        /// <summary>
        /// What the screen looks up to draw it — a hall's art, a relic's id — or null when the
        /// words are all there is.
        /// </summary>
        public string Key;
    }

    /// <summary>One stretch of the experience bar, inside a single level.</summary>
    public struct XpLeg
    {
        /// <summary>The level written beside the bar while it travels this stretch.</summary>
        public int Level;

        /// <summary>Where the bar starts and stops, from nothing to one.</summary>
        public double From;

        public double To;

        /// <summary>The level the bar arrives at by filling, or zero if it stops short.</summary>
        public int Reaches;

        /// <summary>What arriving hands over. Empty when it stops short.</summary>
        public IReadOnlyList<Unlock> Unlocks;
    }

    /// <summary>
    /// The path the experience bar takes from where a run started to where it ended.
    /// </summary>
    /// <remarks>
    /// The source's bar crosses one level boundary at a time: fill to the end, announce the
    /// level, empty, fill again. So the path is a list of stretches rather than two numbers, and
    /// the screen walks them in order. Worked out here so the screen decides nothing, and so
    /// "how far does the bar go" is a test rather than something watched.
    ///
    /// It is given the experience the delver held BEFORE the run and what the run paid, and
    /// works out the rest, rather than being handed a level and a fraction that are both derived
    /// from that one number already.
    /// </remarks>
    public static class XpTracks
    {
        /// <summary>
        /// The bar's path.
        /// </summary>
        /// <param name="before">Experience held when the run started.</param>
        /// <param name="earned">Experience the run paid. Never negative; anything less is none.</param>
        /// <param name="more">
        /// Anything else a level hands over beyond its perk — relics and wardrobe pieces, when
        /// those exist. Asked once per level reached; null means nothing else.
        /// </param>
        public static List<XpLeg> Of(int before, int earned,
            Func<int, IEnumerable<Unlock>> more = null)
        {
            before = Math.Max(0, before);
            int after = before + Math.Max(0, earned);

            int level = Progression.LevelFor(before);
            int end = Progression.LevelFor(after);
            double from = Progression.Progress(before);

            var legs = new List<XpLeg>();

            while (level < end)
            {
                legs.Add(new XpLeg
                {
                    Level = level,
                    From = from,
                    To = 1d,
                    Reaches = level + 1,
                    Unlocks = At(level + 1, more),
                });

                level++;
                from = 0d;
            }

            legs.Add(new XpLeg
            {
                Level = end,
                From = from,
                To = Progression.Progress(after),
                Reaches = 0,
                Unlocks = new Unlock[0],
            });

            return legs;
        }

        /// <summary>What reaching one level hands over, perk first.</summary>
        public static List<Unlock> At(int level, Func<int, IEnumerable<Unlock>> more = null)
        {
            var unlocks = new List<Unlock>
            {
                new Unlock { Kind = UnlockKind.Perk, Text = Progression.PerkDescription(level) },
            };

            if (more != null)
            {
                IEnumerable<Unlock> extra = more(level);
                if (extra != null) unlocks.AddRange(extra);
            }

            return unlocks;
        }

        /// <summary>The words for a list of unlocks, one line each, for the gains list.</summary>
        public static List<string> Lines(IReadOnlyList<Unlock> unlocks)
        {
            var lines = new List<string>();
            if (unlocks == null) return lines;

            foreach (Unlock unlock in unlocks)
            {
                if (!string.IsNullOrEmpty(unlock.Text)) lines.Add(unlock.Text);
            }

            return lines;
        }
    }
}
