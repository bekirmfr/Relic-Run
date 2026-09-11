using System;
using System.Text;
using RelicRun.Core.Content;

namespace RelicRun.Core.Presentation
{
    /// <summary>
    /// The two things a relic's description cannot say on its own.
    /// </summary>
    /// <remarks>
    /// Forty-eight of the fifty describe themselves with a fixed sentence. Two do not, and both
    /// reached a delver as raw markup before this existed: the Midas Blade's <c>{n}</c>, which is
    /// a number only the purse knows, and the Weighted Dice's <c>[[LCK]]</c>, which the source
    /// draws as a small stat chip rather than as four characters and two pairs of brackets.
    ///
    /// Kept apart from the text tables because neither is a fact about the relic. The number is a
    /// fact about the RUN, and the chip is a fact about how a screen paints things.
    /// </remarks>
    public static class RelicWords
    {
        /// <summary>
        /// How much gold buys one point of the Midas Blade's attack.
        /// </summary>
        /// <remarks>
        /// Fifty, which is what <c>Pickup</c> charges and therefore what is true. The shipped
        /// English says SIXTY — the source's own prose disagrees with the source's own code, in
        /// both directions: its lore line says fifty and its description says sixty. The number
        /// shown to a delver is computed here from the rule rather than read out of the sentence,
        /// so the figure in brackets is right even while the sentence around it is not.
        /// </remarks>
        public const int MidasGold = 50;

        /// <summary>What a relic's description needs filling in, or -1 when it needs nothing.</summary>
        /// <remarks>
        /// One relic, and a whole function for it. Worth it because the alternative is a screen
        /// knowing which relic is special, and there would then be two screens knowing.
        /// </remarks>
        public static int Live(RelicId relic, int gold)
        {
            if (relic != RelicId.MidasBlade) return -1;

            // The same line Pickup uses to sharpen it. A blade taken with an empty purse is still
            // worth one attack, which is why the floor is one rather than zero.
            return Math.Max(1, (int)Math.Floor(gold / (double)MidasGold));
        }

        /// <summary>Whether a relic's description carries a number that changes.</summary>
        public static bool Lives(RelicId relic)
        {
            return relic == RelicId.MidasBlade;
        }

        /// <summary>
        /// Wraps every <c>[[STAT]]</c> in whatever a screen paints stat chips with.
        /// </summary>
        /// <remarks>
        /// The markup is the CALLER's, because Core does not know what a screen draws with — the
        /// port's screens are TextMeshPro and its tags are not a fact about a relic. What lives
        /// here is only the finding and the unwrapping, which is the part that would otherwise be
        /// written twice and get the nesting wrong once.
        ///
        /// Unmatched brackets are left exactly as they are. A description with a stray <c>[[</c>
        /// is a content bug, and swallowing the rest of the sentence to hide it would turn a
        /// visible mistake into an invisible one.
        /// </remarks>
        public static string Chips(string text, string open, string close)
        {
            if (string.IsNullOrEmpty(text)) return text;

            int at = text.IndexOf("[[", StringComparison.Ordinal);

            if (at < 0) return text;

            var said = new StringBuilder(text.Length + 16);
            var from = 0;

            while (at >= 0)
            {
                int end = text.IndexOf("]]", at + 2, StringComparison.Ordinal);

                if (end < 0) break;

                said.Append(text, from, at - from);
                said.Append(open ?? string.Empty);
                said.Append(text, at + 2, end - at - 2);
                said.Append(close ?? string.Empty);

                from = end + 2;
                at = text.IndexOf("[[", from, StringComparison.Ordinal);
            }

            said.Append(text, from, text.Length - from);

            return said.ToString();
        }
    }
}
