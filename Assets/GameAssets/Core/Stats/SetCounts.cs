using RelicRun.Core.Content;

namespace RelicRun.Core.Stats
{
    /// <summary>
    /// How a Hollow Idol is counted toward the sets.
    /// </summary>
    /// <remarks>
    /// The Idol pays fifteen of the pool to count itself toward every set — one relic that
    /// nudges eight families at once, which is what makes a set build reachable without
    /// drafting narrowly. Awakened, it stops hedging: it throws three more behind whichever
    /// family the delver already leans on, which is usually the difference between a tier and
    /// the next one up.
    ///
    /// The rule lives here rather than in either place that needs it, because both need the
    /// same answer: the fight reads set counts for its own effects, and the stat ledger reads
    /// them for the bonuses it displays. A set tier the fight granted and the ledger did not
    /// show would be a lie on the character sheet.
    ///
    /// The source's answer was different and smaller — an awakened copy simply counted twice
    /// toward every set — and it could never fire, because a Hollow Idol does not stack there
    /// and the bazaar only wakes what stacks.
    /// </remarks>
    public static class SetCounts
    {
        /// <summary>
        /// How many families there are, which is how long a count of them is.
        /// </summary>
        /// <remarks>
        /// Here rather than as an 8 typed at each place that needs an array, which is what it was
        /// — and now that a foe counts its own sets there are two such places, which is one more
        /// than a magic number survives. Pinned to <c>RelicKind</c> by a test, since the whole
        /// point is that the two cannot drift.
        /// </remarks>
        public const int Kinds = 8;

        /// <summary>What an awakened Hollow Idol adds to the family it backs.</summary>
        public const int IdolBacksTheDominant = 3;

        /// <summary>No family leads.</summary>
        public const int NoKind = -1;

        /// <summary>
        /// The family a loadout leans on: the one it holds most of.
        /// </summary>
        /// <remarks>
        /// Ties go to the family that comes first in <see cref="RelicKind"/>, which is arbitrary
        /// but has to be SOMETHING — a rule that depended on inventory order would make the same
        /// loadout score differently for having been drafted in another sequence.
        ///
        /// An empty hand leads with nothing, which is the case that stops a lone awakened Idol
        /// from backing Guard for no reason.
        /// </remarks>
        public static int Dominant(int[] kindCounts)
        {
            int best = NoKind;
            int most = 0;

            for (int kind = 0; kind < kindCounts.Length; kind++)
            {
                if (kindCounts[kind] <= most) continue;
                most = kindCounts[kind];
                best = kind;
            }

            return best;
        }
    }
}
