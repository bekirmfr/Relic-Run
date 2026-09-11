namespace RelicRun.Core.Presentation
{
    /// <summary>Everything the screen that offers a second chance shows.</summary>
    public struct ReviveCard
    {
        /// <summary>The floor the delver fell on.</summary>
        public int Floor;

        /// <summary>What they have.</summary>
        public int Sparks;

        /// <summary>What rising costs.</summary>
        public int Cost;

        /// <summary>Whether they can pay it.</summary>
        public bool Afford;

        /// <summary>How much is left afterwards, or what they are short by.</summary>
        /// <remarks>
        /// One number either way, and the sign says which. A delver deciding whether to spend
        /// their whole balance wants to know what is left more than they want to know the price
        /// again — and one who cannot pay wants to know by how much.
        /// </remarks>
        public int After;
    }

    /// <summary>
    /// The one second chance a run gets.
    /// </summary>
    /// <remarks>
    /// Offered once per delve and never again: <c>Delve</c> asks only while <c>Revived</c> is
    /// false, so a delver who rises and falls again has fallen for good. The engine's question is
    /// a single yes — it does not know what sparks are, and it should not: rising is a fact about
    /// the RUN, and paying for it is a fact about the delver's account, which outlives the run.
    ///
    /// The source offers two ways to say yes — watch an advertisement, or spend a hundred and
    /// fifty sparks. Only the second is ported. An advertisement button with no advertisement
    /// behind it is a button that lies, and the port has no ad SDK wired; the line that adds it
    /// back is the one that makes this card carry two prices instead of one.
    /// </remarks>
    public static class ReviveCards
    {
        /// <summary>The source's <c>REVIVE_SPARKS</c>.</summary>
        public const int Cost = 150;

        /// <summary>The offer, for a delver who fell on this floor holding these sparks.</summary>
        public static ReviveCard Of(int floor, int sparks)
        {
            bool afford = sparks >= Cost;

            return new ReviveCard
            {
                Floor = floor,
                Sparks = sparks,
                Cost = Cost,
                Afford = afford,
                After = afford ? sparks - Cost : Cost - sparks,
            };
        }
    }
}
