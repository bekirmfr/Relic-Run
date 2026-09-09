using System.Collections.Generic;
using System.Globalization;
using RelicRun.Core.Run;

namespace RelicRun.Core.Presentation
{
    /// <summary>Everything the experience screen shows.</summary>
    public struct XpCard
    {
        /// <summary>The word across the top: whether this is a level or just progress.</summary>
        public string Kicker;

        /// <summary>What was earned, with its sign.</summary>
        public string Gained;

        /// <summary>The level being shown, which is not always the level reached.</summary>
        public string Level;

        /// <summary>What is next, or that there is nothing next.</summary>
        public string Next;

        /// <summary>How far along the shown level is, from nothing to one.</summary>
        public double Progress;

        /// <summary>Whether a level was crossed, which is the whole reason this screen stops you.</summary>
        public bool LevelledUp;

        /// <summary>What each new level granted, one line each.</summary>
        public IReadOnlyList<string> Gains;
    }

    /// <summary>
    /// The experience screen, worked out.
    /// </summary>
    /// <remarks>
    /// The only place a level-up is ever announced. The level itself is banked whether or not a
    /// delver sees this — which is exactly why <see cref="Pages.Home"/> refuses to skip it: the
    /// cost of missing it is not a lost level, it is a delver finding out by noticing a number
    /// changed.
    ///
    /// It shows a level rather than THE level, and that is deliberate. The bar animates from
    /// where the run started to where it ended, crossing one boundary at a time, so what is drawn
    /// is whichever level the animation has reached. A screen that always showed the final level
    /// would have a bar that filled past its own label.
    /// </remarks>
    public static class XpCards
    {
        public const string Gaining = "EXPERIENCE GAINED";

        public const string Levelled = "LEVEL UP";

        public const string AtTheTop = "MAX LEVEL";

        /// <summary>
        /// The experience screen, part-way through its animation.
        /// </summary>
        /// <param name="showing">
        /// The level the bar has reached, which counts up from where the run started. The screen
        /// owns the animation; this only says what to write beside it.
        /// </param>
        /// <param name="celebrating">
        /// Whether the bar has arrived at a level boundary. False while it is still travelling,
        /// so the heading does not shout LEVEL UP before the bar gets there.
        /// </param>
        public static XpCard Of(int gained, int showing, double progress, bool celebrating,
            IReadOnlyList<string> gains)
        {
            bool levelled = celebrating && gains != null && gains.Count > 0;
            bool capped = showing >= Progression.MaxLevel;

            return new XpCard
            {
                Kicker = levelled ? Levelled : Gaining,

                // Always signed, because a run that earned nothing still earned nothing rather
                // than losing something, and "+0" says that where "0" reads like an error.
                Gained = "+" + gained.ToString(CultureInfo.InvariantCulture),

                Level = "LEVEL " + showing.ToString(CultureInfo.InvariantCulture),
                Next = capped
                    ? AtTheTop
                    : "NEXT: LEVEL " + (showing + 1).ToString(CultureInfo.InvariantCulture),

                // A full bar at the ceiling rather than a fraction of a level that does not
                // exist — the same answer the title and the profile give.
                Progress = capped ? 1d : progress,

                LevelledUp = levelled,
                Gains = gains ?? new string[0],
            };
        }
    }
}
