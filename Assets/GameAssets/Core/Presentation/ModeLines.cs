using System.Globalization;
using RelicRun.Core.Meta;

namespace RelicRun.Core.Presentation
{
    /// <summary>What one of the two banners says about the mode behind it.</summary>
    public struct ModeLine
    {
        /// <summary>Whether this delver may play it at all.</summary>
        public bool Open;

        /// <summary>The left caption: what it is, or that it is shut.</summary>
        public string Left;

        /// <summary>The right caption: how it stands, or what would open it.</summary>
        public string Right;

        /// <summary>A short word for the state it is in, shown on the tile in the mode picker.</summary>
        public string Tag;
    }

    /// <summary>
    /// The two banners, which the title and the mode picker both draw.
    /// </summary>
    /// <remarks>
    /// Both screens show them and the source writes the captions out twice, identically. Here
    /// they are written once: two copies of "what does the Daily say right now" is two answers
    /// the moment somebody edits one, and the delver sees both screens within seconds of each
    /// other.
    ///
    /// The captions are English in every locale, which is the source's doing — they are literals
    /// in its markup and never pass through its translation function. Ported as they are, because
    /// inventing eight translations of a line the game has never shown anybody is not a port.
    /// </remarks>
    public static class ModeLines
    {
        public const string Locked = "LOCKED";

        /// <summary>What the Daily's tile says when today's has been played.</summary>
        /// <remarks>
        /// The source writes a tick after this word. Dropped, for the reason the title's star is:
        /// neither shipped face has a glyph for it, a browser borrows one from a system emoji
        /// font and a baked bitmap face has nothing to borrow, and the result would be an empty
        /// box on a tile whose whole job is to say one word clearly.
        /// </remarks>
        public const string Done = "DONE";

        /// <summary>And when it has not.</summary>
        public const string Floors = "13 FLOORS";

        /// <summary>
        /// The Daily's banner.
        /// </summary>
        /// <param name="best">Today's best score, which is not the same as the best ever.</param>
        /// <param name="left">The countdown, already formatted.</param>
        public static ModeLine Daily(int level, bool done, bool running, int best, string left)
        {
            bool open = level >= Career.DailyOpensAt;

            return new ModeLine
            {
                Open = open,
                Left = open ? "TODAY · " + left : Locked,
                Right = open
                    ? "BEST " + Number(best) + " · " +
                      (running ? "IN PROGRESS" : done ? "DONE TODAY" : "1 TRY")
                    : "REACH DELVER LV " + Number(Career.DailyOpensAt) + " IN DUNGEONS",

                // Three states on the tile, not two. A delver halfway through today's run is not
                // a delver who has finished it, and is not one who has yet to start.
                Tag = !open ? Locked : running ? "IN PROGRESS" : done ? Done : Floors,
            };
        }

        /// <summary>The arena's banner.</summary>
        public static ModeLine Versus(int level)
        {
            bool open = level >= Career.VersusOpensAt;

            return new ModeLine
            {
                Open = open,
                Left = open ? "8 DELVERS · 3 LIVES EACH" : Locked,
                Right = open
                    ? "LAST DELVER STANDING"
                    : "REACH DELVER LV " + Number(Career.VersusOpensAt),
                Tag = open ? "8 DELVERS" : Locked,
            };
        }

        private static string Number(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
