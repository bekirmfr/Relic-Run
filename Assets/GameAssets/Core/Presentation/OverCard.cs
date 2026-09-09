using System.Globalization;
using RelicRun.Core.Meta;
using RelicRun.Core.Run;

namespace RelicRun.Core.Presentation
{
    /// <summary>Everything the end of a run shows.</summary>
    public struct OverCard
    {
        /// <summary>How it ended, which decides most of the rest.</summary>
        public RunEnding Ending;

        /// <summary>The line across the top, in two parts so a screen can break it where it likes.</summary>
        public string Title;

        public string TitleUnder;

        /// <summary>The bragging number.</summary>
        public int Score;

        /// <summary>Nought to three, on how far down the run reached.</summary>
        public int Stars;

        /// <summary>Gold actually taken home. Nothing, on a death.</summary>
        public int Banked;

        public int Floor;

        public int Kills;

        /// <summary>Experience paid out.</summary>
        public int Xp;

        /// <summary>Whether this beat everything before it.</summary>
        public bool Best;

        /// <summary>The line under the score: whether it was a best, and what became of the purse.</summary>
        public string Note;

        /// <summary>
        /// What the hall paid, as a rate, or null when the run earned nothing to scale.
        /// </summary>
        /// <remarks>
        /// The source shows this whenever a run finished, because it is the answer to "why did I
        /// get so little experience for that" — a delver farming the first hall at level twelve
        /// is earning a tenth, and nothing else on the screen says so.
        /// </remarks>
        public string Rate;
    }

    /// <summary>
    /// The end of a run, worked out.
    /// </summary>
    /// <remarks>
    /// Three endings and they are not variations on each other. Dying loses the purse; cashing
    /// out keeps it; clearing keeps it and means something. The source writes a different
    /// sentence for each, and they are English literals in its markup rather than translated —
    /// so they are carried as written, like the title's.
    /// </remarks>
    public static class OverCards
    {
        /// <summary>What is said when the delver did not come back.</summary>
        public const string Died = "The dark";

        public const string DiedUnder = "kept you.";

        /// <summary>And when they reached the bottom.</summary>
        public const string Cleared = "You cleared";

        public const string ClearedUnder = "the delve.";

        /// <summary>And when they took what they had and left.</summary>
        public const string Cashed = "You walked out";

        public const string CashedUnder = "with the purse.";

        /// <summary>The rest of the line under the score, one per ending.</summary>
        public const string LostPurse = "the purse stayed in the dark.";

        public const string KeptAll = "the hoard is lighter for it.";

        public const string KeptSome = "the purse came home.";

        /// <summary>
        /// What the end of a run says, given how it ended.
        /// </summary>
        /// <param name="best">
        /// Whether this run beat every one before it. Passed rather than compared, because the
        /// save has ALREADY been written by the time this screen is drawn — the new best is in
        /// it, so asking the save would answer yes forever.
        /// </param>
        /// <param name="newBest">
        /// The translated words for a new best, or null. The one translated thing on this screen,
        /// which is why it arrives rather than being spelled.
        /// </param>
        public static OverCard Of(RunReward reward, RunEnding ending, int floor, int kills,
            bool best, string newBest, double rate)
        {
            return new OverCard
            {
                Ending = ending,

                Title = ending == RunEnding.Died ? Died
                    : ending == RunEnding.Cleared ? Cleared : Cashed,
                TitleUnder = ending == RunEnding.Died ? DiedUnder
                    : ending == RunEnding.Cleared ? ClearedUnder : CashedUnder,

                Score = reward.Score,
                Stars = reward.Stars,

                // Nothing comes home from a death, whatever the run had collected. The reward
                // already knows that; this does not decide it a second time.
                Banked = reward.Banked,

                Floor = floor,
                Kills = kills,
                Xp = reward.Xp,
                Best = best,

                Note = Under(ending, best, newBest),
                Rate = rate > 0d ? "XP ×" + rate.ToString("0.00", CultureInfo.InvariantCulture)
                    : null,
            };
        }

        /// <summary>The line under the score.</summary>
        /// <remarks>
        /// The new-best half is translated and the rest is not, which is the source's arrangement
        /// and looks like an oversight until you notice every other line on the screen is a
        /// literal too. The one word a delver most wants to see is the one word it translates.
        /// </remarks>
        private static string Under(RunEnding ending, bool best, string newBest)
        {
            string what = ending == RunEnding.Died ? LostPurse
                : ending == RunEnding.Cleared ? KeptAll : KeptSome;

            return best && !string.IsNullOrEmpty(newBest) ? newBest + " · " + what : what;
        }
    }
}
