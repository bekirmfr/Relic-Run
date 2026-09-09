using System;
using RelicRun.Core.Determinism;
using RelicRun.Core.Meta;

namespace RelicRun.Core.Presentation
{
    /// <summary>Everything the mode picker shows.</summary>
    public struct ModesCard
    {
        public ModeLine Daily;

        public ModeLine Versus;

        /// <summary>
        /// The dungeons, which are always open and therefore have no banner state.
        /// </summary>
        /// <remarks>
        /// The one mode with no lock on it. A delver who has just installed the game can delve,
        /// and everything else is earned from there — so the tile says how deep they have got
        /// rather than whether they may go.
        /// </remarks>
        public string Dungeons;

        /// <summary>Whether pressing the Daily starts today's run rather than doing nothing.</summary>
        public bool CanDaily;

        /// <summary>Whether pressing the arena opens the staging hall.</summary>
        public bool CanVersus;

        /// <summary>How long today's Daily has left.</summary>
        public string Left;
    }

    /// <summary>
    /// The mode picker, worked out.
    /// </summary>
    /// <remarks>
    /// Three ways to play, and the screen's whole job is saying which of them this delver may
    /// use. The banners come from <see cref="ModeLines"/>, which the title draws too — written
    /// once, because two copies of "what does the Daily say right now" become two answers the
    /// moment somebody edits one, and a delver sees both screens within seconds of each other.
    ///
    /// A shut mode is still shown and still pressable. The source greys it, writes the level that
    /// would open it across the front, and does nothing when pressed — the lock IS the
    /// advertisement, and a tile that could not be pressed would be one a delver reads as broken.
    /// </remarks>
    public static class ModesCards
    {
        /// <summary>What the dungeons tile says, given how deep the delver has reached.</summary>
        public const string Deepest = "DEEPEST ";

        public static ModesCard Of(SaveState save, bool dailyDone, bool dailyRunning,
            DateTimeOffset now)
        {
            if (save == null) save = new SaveState();

            int level = save.Level;
            uint seed = DailySeed.For(now);
            string left = Countdown.Text(DailySeed.ResetsIn(now));

            return new ModesCard
            {
                Daily = ModeLines.Daily(level, dailyDone, dailyRunning,
                    save.DailyBestFor(seed), left),
                Versus = ModeLines.Versus(level),

                Dungeons = Deepest + Career.Frontier(save),

                // Open is not the same as playable. Today's Daily is one run, so a delver who has
                // reached the level and already played is refused for a different reason than one
                // who has not reached it — and both are refused.
                CanDaily = level >= Career.DailyOpensAt && !dailyDone && !dailyRunning,
                CanVersus = level >= Career.VersusOpensAt,

                Left = left,
            };
        }
    }
}
