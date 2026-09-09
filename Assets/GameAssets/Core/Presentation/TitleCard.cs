using System;
using System.Globalization;
using RelicRun.Core.Determinism;
using RelicRun.Core.Meta;
using RelicRun.Core.Run;

namespace RelicRun.Core.Presentation
{
    /// <summary>
    /// Everything the title screen shows.
    /// </summary>
    /// <remarks>
    /// Computed here rather than in the widget, for the reason every other view-model in this
    /// port is: the title changes what it says on four different inputs — level, whether today's
    /// Daily is done, whether one is running, and the clock — and a screen that worked all four
    /// out while drawing would be a screen nothing could ask a question of.
    ///
    /// The captions are English, and that is the source's doing rather than an omission. The
    /// title's kicker, its subtitle and both mode banners are written as string literals in the
    /// markup and never pass through its translation function, so they are English in every
    /// locale the game ships. Ported as they are, and said out loud here, because the alternative
    /// is inventing eight translations of a line the game has never shown anybody.
    ///
    /// One consequence rides along and it cost a wrong test to find. A literal that never passes
    /// through the translator never gets CLEANED either, so the emoji stripping that takes the
    /// arrow off "← Back" does not touch these lines — every character in them is really drawn.
    /// The source puts a star in front of the best score, and a browser draws it out of a system
    /// emoji font; a baked bitmap face has nothing to fall back to, and neither typeface this
    /// game ships covers it. So the star is dropped, which is also what the source's own cleaner
    /// would have done to it. The tests hold every one of these lines against what the pixel face
    /// can actually draw, and hold them RAW — asking with the cleaning on removes the very
    /// characters the question is about.
    /// </remarks>
    public struct TitleCard
    {
        /// <summary>What the delver calls themselves, or empty if they never said.</summary>
        public string Name;

        public int Level;

        /// <summary>How far through this level, from nothing to one.</summary>
        public double Progress;

        /// <summary>Whether there is no level above this one.</summary>
        public bool Capped;

        /// <summary>Best score in a single run, ever.</summary>
        public int Best;

        public int Crowns;

        /// <summary>Where the one PLAY button goes.</summary>
        public Play Play;

        /// <summary>The line above the PLAY button: which mode it is offering.</summary>
        public string Kicker;

        /// <summary>The line below it: why.</summary>
        public string Sub;

        public ModeLine Daily;

        public ModeLine Versus;

        /// <summary>The day's label, which is the seed's last four digits.</summary>
        public string Seed;

        /// <summary>How long today's Daily has left.</summary>
        public string Left;
    }

    /// <summary>
    /// The title screen, worked out.
    /// </summary>
    /// <remarks>
    /// Every string here is read off the source's markup. They are ugly to look at as literals
    /// and they are the game's own words, which is worth more than tidying them.
    /// </remarks>
    public static class TitleCards
    {
        /// <summary>
        /// What the title shows a delver at this moment.
        /// </summary>
        /// <param name="save">What they have earned.</param>
        /// <param name="prefs">What they have chosen. Only the name is read.</param>
        /// <param name="dailyDone">Whether today's Daily has been played.</param>
        /// <param name="dailyRunning">Whether one is being played right now.</param>
        /// <param name="now">
        /// The instant to count down from. Passed rather than read, because a view-model that
        /// asked the clock itself could not be tested at midnight and would be tested at noon
        /// forever.
        /// </param>
        public static TitleCard Of(SaveState save, Preferences prefs, bool dailyDone,
            bool dailyRunning, DateTimeOffset now)
        {
            if (save == null) save = new SaveState();

            int level = save.Level;
            uint seed = DailySeed.For(now);
            string left = Countdown.Text(DailySeed.ResetsIn(now));

            return new TitleCard
            {
                Name = prefs != null && prefs.Name != null ? prefs.Name : string.Empty,
                Level = level,
                Progress = Progression.Progress(save.Xp),
                Capped = level >= Progression.MaxLevel,
                Best = save.Best,
                Crowns = save.VsCrowns,

                Play = Pages.Featured(save, dailyDone),
                Kicker = Kick(level, dailyDone),
                Sub = Under(level, dailyDone, DailySeed.Label(seed), left),

                // Built where the mode picker builds them, so the two screens cannot
                // disagree about what today's Daily says — a delver sees both within seconds.
                Daily = ModeLines.Daily(level, dailyDone, dailyRunning,
                    save.DailyBestFor(seed), left),
                Versus = ModeLines.Versus(level),

                Seed = DailySeed.Label(seed),
                Left = left,
            };
        }

        /// <summary>The line above PLAY, naming the mode it is offering.</summary>
        private static string Kick(int level, bool dailyDone)
        {
            if (level < Career.DailyOpensAt)
            {
                return "DUNGEONS · DAILY UNLOCKS AT LV " + Number(Career.DailyOpensAt);
            }

            if (!dailyDone) return "TODAY'S DELVE · ONE TRY";

            return level >= Career.VersusOpensAt
                ? "VERSUS · 8 DELVERS · 3 LIVES"
                : "DUNGEONS · VERSUS UNLOCKS AT LV " + Number(Career.VersusOpensAt);
        }

        /// <summary>The line below PLAY, saying why it is offering that.</summary>
        private static string Under(int level, bool dailyDone, string seed, string left)
        {
            if (level < Career.DailyOpensAt)
            {
                return "Clear dungeons to reach Delver LV " + Number(Career.DailyOpensAt) + ".";
            }

            if (!dailyDone) return "Seed " + seed + " · resets in " + left;

            return level >= Career.VersusOpensAt
                ? "Today's delve is done — the arena is open."
                : "Today's delve is done — back to the dungeons.";
        }

        private static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
