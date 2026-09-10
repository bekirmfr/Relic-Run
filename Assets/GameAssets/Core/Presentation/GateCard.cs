using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RelicRun.Core.Combat;
using RelicRun.Core.Run;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Presentation
{
    /// <summary>What is waiting one floor down, as much of it as a delver is shown.</summary>
    public struct WaitingBelow
    {
        public int Species;

        /// <summary>Which column of the sheet draws it: guard, elite, or boss.</summary>
        public int Variant;

        public EnemyRank Rank;

        /// <summary>The key its name is translated under.</summary>
        public string NameKey;

        /// <summary>How many stand in FRONT of it. Zero when it is alone.</summary>
        public int Behind;

        /// <summary>
        /// What the whole pack is carrying, near enough.
        /// </summary>
        /// <remarks>
        /// The sum of what each would drop, which is what a delver is being asked to weigh the
        /// purse against. Approximate on screen — the source writes it with a tilde — because a
        /// fight pays more than the drops when a relic is turning blows into coins.
        /// </remarks>
        public int Gold;

        /// <summary>Its line of numbers, already written out. English.</summary>
        public string Line;

        /// <summary>Whether there is anything to peek at.</summary>
        public bool Known;
    }

    /// <summary>Everything the gate between two floors shows.</summary>
    public struct GateCard
    {
        /// <summary>The floor just cleared.</summary>
        public int Floor;

        /// <summary>And the one below it.</summary>
        public int Next;

        /// <summary>
        /// What is in the purse.
        /// </summary>
        /// <remarks>
        /// One number, shown three times: what is held, what is at risk, and what would be kept.
        /// They are the same number and the screen says all three anyway, because that IS the
        /// decision and a delver reading it once tends to read it as only one of the three.
        /// </remarks>
        public int Gold;

        public WaitingBelow Below;

        /// <summary>Whether the run is at its last gate, with the crown below.</summary>
        public bool Crowned;
    }

    /// <summary>
    /// The gate: a cleared floor, a full purse, and the stairs going down.
    /// </summary>
    /// <remarks>
    /// The only decision in this game that is about the WHOLE run rather than about a floor. Every
    /// other choice a delver makes is between two things they will find out about in a minute;
    /// this one asks them to bet everything the run has earned on a fight they have been shown
    /// exactly one card of.
    ///
    /// That card is the reason the peek exists and the reason it is deliberately partial. The
    /// worst of them by name, how many stand in front of it, and roughly what the floor pays —
    /// enough to make the bet informed and not enough to make it arithmetic.
    /// </remarks>
    public static class GateCards
    {
        /// <summary>What sits between two facts on the peek's line.</summary>
        public const string Between = " · ";

        /// <summary>The gate after clearing a floor, with the purse and what waits below.</summary>
        public static GateCard Of(int floor, int gold, IReadOnlyList<EnemyState> below)
        {
            return new GateCard
            {
                Floor = floor,
                Next = floor + 1,
                Gold = gold,
                Below = Peek(below),
                Crowned = floor + 1 >= DelveRun.MaxFloor,
            };
        }

        /// <summary>
        /// The foe at the BACK of the pack below, and how many stand in front of it.
        /// </summary>
        /// <remarks>
        /// The last, not the first — which is the opposite of what it sounds like it should be,
        /// and the source is right. A pack is fought front to back with the worst of it at the
        /// end, so the first foe is the least informative thing on the floor: it says a delver
        /// can survive the next thirty seconds, which they could already guess. What they are
        /// actually betting the purse against is whatever is at the back.
        ///
        /// It also makes the wording true. "BEHIND 2 GUARDS" describes something standing behind
        /// two other things, which is exactly where the last of a pack is.
        /// </remarks>
        public static WaitingBelow Peek(IReadOnlyList<EnemyState> below)
        {
            if (below == null || below.Count == 0) return new WaitingBelow { Known = false };

            EnemyState lead = below[below.Count - 1];

            if (lead == null) return new WaitingBelow { Known = false };

            var peek = new WaitingBelow
            {
                Species = lead.SpeciesIndex,
                Variant = lead.Variant,
                Rank = lead.Rank,
                NameKey = EnemyCards.NameKey(lead.SpeciesIndex),
                Behind = below.Count - 1,
                Gold = Purse(below),
                Known = true,
            };

            peek.Line = Numbers(lead, peek.Behind, peek.Gold);

            return peek;
        }

        /// <summary>What a pack is carrying, added up.</summary>
        public static int Purse(IReadOnlyList<EnemyState> pack)
        {
            var gold = 0;

            if (pack == null) return gold;

            foreach (EnemyState foe in pack)
            {
                if (foe != null) gold += foe.Drop;
            }

            return gold;
        }

        /// <summary>
        /// The peek's line: what is behind, what it takes, and what it pays.
        /// </summary>
        /// <remarks>
        /// The count comes first, because "and three of them to get through first" changes the
        /// decision more than any of the numbers after it do. It is left off entirely when the
        /// foe is alone — "BEHIND 0 GUARDS" is a sentence that makes an empty hall sound crowded.
        /// </remarks>
        public static string Numbers(EnemyState lead, int behind, int gold)
        {
            var said = new StringBuilder();

            if (behind > 0)
            {
                said.Append("BEHIND ").Append(Say(behind));
                said.Append(behind == 1 ? " GUARD" : " GUARDS").Append(Between);
            }

            said.Append(Say(lead.MaxHp)).Append(" HP");
            said.Append(Between).Append("ATK ").Append(Say(lead.Atk));
            said.Append(Between).Append("~").Append(Say(gold)).Append(" GOLD");

            return said.ToString();
        }

        private static string Say(int number)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }
    }
}
