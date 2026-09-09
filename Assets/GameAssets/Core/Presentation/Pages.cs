using RelicRun.Core.Meta;

namespace RelicRun.Core.Presentation
{
    /// <summary>
    /// One of the game's screens.
    /// </summary>
    /// <remarks>
    /// The source keeps this as a string on one React state object and switches on it in a
    /// hundred places. The names here are its names, so a line of source and a line of port can
    /// be read against each other without a translation table.
    ///
    /// <see cref="Run"/> is the odd one and is deliberately not broken up: a run has its own
    /// stages — draft, combat, travel, merchant — and those are the run layer's business rather
    /// than this one's. Every other screen has no inside.
    /// </remarks>
    public enum Page
    {
        Title = 0,
        Modes = 1,
        Levels = 2,
        Run = 3,
        Staging = 4,
        Over = 5,
        Xp = 6,
        Board = 7,
        Profile = 8,
        Bestiary = 9,
        RelicBook = 10,
        How = 11,
    }

    /// <summary>
    /// What the title's PLAY button does.
    /// </summary>
    /// <remarks>
    /// Two answers rather than one, because "go to the run screen" is not an instruction: a run
    /// has to be started, and today's Daily is not started the same way as a delve into a hall.
    /// Returning both together is what stops a caller routing to the run screen without saying
    /// which run it is showing.
    /// </remarks>
    public struct Play
    {
        public Page Goes;

        /// <summary>Whether arriving there means starting today's Daily.</summary>
        public bool StartsTheDaily;
    }

    /// <summary>
    /// Where a button goes, given what the delver has.
    /// </summary>
    /// <remarks>
    /// Every rule here is a line of the source lifted whole, and each is lifted because it is a
    /// decision with more than one input and exactly one right answer. A wrong answer is a
    /// delver stuck on a screen, or sent past something they earned.
    ///
    /// What a screen DRAWS is not here — not its layout, not its animation, not which of the
    /// three widths it uses. Only which screen comes next.
    /// </remarks>
    public static class Pages
    {
        /// <summary>
        /// Where the title's PLAY goes.
        /// </summary>
        /// <remarks>
        /// The source calls it <c>playFeatured</c>, and it is the one button on the title that
        /// does not always do the same thing: it routes to the best mode the delver has open.
        /// Below level three that is the dungeons, because nothing else exists yet. From three it
        /// is today's Daily — until today's is done, at which point it is the arena if the delver
        /// has reached five and the dungeons if they have not.
        ///
        /// The order is the point. A delver who has not played today is sent to the thing that
        /// expires today; one who has is sent to the deepest thing they own.
        ///
        /// The thresholds are <see cref="Career"/>'s own, asked rather than repeated, so the
        /// button that routes here and the mode that would refuse to start cannot drift apart.
        /// </remarks>
        public static Play Featured(SaveState save, bool dailyDone)
        {
            var dungeons = new Play { Goes = Page.Levels, StartsTheDaily = false };

            if (save == null) return dungeons;

            int level = save.Level;

            if (level < Career.DailyOpensAt) return dungeons;

            if (!dailyDone) return new Play { Goes = Page.Run, StartsTheDaily = true };

            return level >= Career.VersusOpensAt
                ? new Play { Goes = Page.Staging, StartsTheDaily = false }
                : dungeons;
        }

        /// <summary>
        /// Where the home button goes.
        /// </summary>
        /// <remarks>
        /// The title, except in one case: a run that ended with experience the delver has not
        /// been shown stops at the experience screen on the way. Anywhere else, with nothing
        /// earned, or once it has been shown, home is home.
        ///
        /// The exception exists because the experience screen is the only place a level-up is
        /// ever announced. Skipping it does not lose the level — that is banked already — it
        /// loses the only telling of it, and the delver finds out by noticing a number changed.
        /// </remarks>
        public static Page Home(Page from, int xpGained, bool xpAlreadyShown)
        {
            if (from == Page.Over && xpGained > 0 && !xpAlreadyShown) return Page.Xp;

            return Page.Title;
        }

        /// <summary>
        /// Where closing the board goes.
        /// </summary>
        /// <remarks>
        /// The board is reachable from the title and from the end of a run, and closing it has to
        /// return to whichever it was. The source keeps that on its state object; here it is a
        /// parameter, because a screen that has to remember where it was opened from is a screen
        /// with one more thing to get wrong.
        /// </remarks>
        public static Page CloseBoard(Page openedFrom)
        {
            return openedFrom == Page.Over ? Page.Over : Page.Title;
        }
    }
}
