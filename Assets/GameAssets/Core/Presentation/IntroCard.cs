using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RelicRun.Core.Combat;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Presentation
{
    /// <summary>What the screen says when a foe steps up.</summary>
    public struct IntroCard
    {
        public int Species;

        /// <summary>Which column of the sheet draws it: guard, elite, or boss.</summary>
        public int Variant;

        public EnemyRank Rank;

        /// <summary>The key its name is translated under.</summary>
        public string NameKey;

        /// <summary>The line over the picture, or empty for an ordinary guard. English.</summary>
        public string Called;

        /// <summary>What colour that line is, as <c>#RRGGBB</c>. Empty when there is no line.</summary>
        public string Hex;

        /// <summary>Whether there is a line at all.</summary>
        public bool Ranked;

        /// <summary>Its four numbers, on one line, already written out. English.</summary>
        public string Stats;

        /// <summary>How thick the frame around the picture is, and what colour.</summary>
        public int Edge;

        public string EdgeHex;

        /// <summary>Whether the frame is lit, which only the bottom of a run is.</summary>
        public bool Lit;

        /// <summary>
        /// Whether this is the merchant rather than something that will hit you.
        /// </summary>
        /// <remarks>
        /// The bazaar floor is walked into exactly like a fight — down the hall, met, announced —
        /// and then the card offers a TRADE instead of a FIGHT. One card with a flag rather than
        /// two cards, because it IS the same card: a delver arriving on floor seven should not be
        /// able to tell they are about to be sold something until they read it.
        /// </remarks>
        public bool Trading;

        /// <summary>What the merchant says, when it is the merchant. English.</summary>
        public string Line;
    }

    /// <summary>
    /// The card a fight opens on.
    /// </summary>
    /// <remarks>
    /// Three and a bit seconds where nothing happens, which is the most deliberate pause in the
    /// game. It is the only moment a delver reads a foe's numbers BEFORE they matter — every
    /// other look at them is a look at a fight already going badly — and it is what makes a
    /// floor boss feel like an event rather than like a guard with more health.
    ///
    /// It says something different from the bestiary, on purpose. <see cref="EnemyCards.Called"/>
    /// names the THING — a dungeon king is a dungeon king whether or not anybody is fighting it.
    /// This announces the FIGHT, so the same rank reads as FINAL BOSS: the bestiary is a
    /// reference and this is a threat. The source keeps both spellings and so does this.
    /// </remarks>
    public static class IntroCards
    {
        public const string KingWord = "FINAL BOSS";

        public const string BossWord = "FLOOR BOSS";

        public const string EliteWord = "HONOR GUARD";

        /// <summary>A guard is announced by name and nothing else.</summary>
        public const string GuardWord = "";

        public const string KingHex = "#E3B341";

        public const string BossHex = "#AEB6C0";

        public const string EliteHex = "#9B8BD0";

        /// <summary>The frame a foe of no particular rank stands in.</summary>
        public const string PlainHex = "#3A3328";

        /// <summary>What sits between two of the four numbers.</summary>
        public const string Between = " · ";

        /// <summary>What the bazaar's kicker says over the merchant.</summary>
        public const string TradeKicker = "NEUTRAL · THE HOARD BAZAAR";

        /// <summary>And what it calls him.</summary>
        public const string TradeName = "The Merchant";

        /// <summary>
        /// The five things he says, one of which a visit picks.
        /// </summary>
        /// <remarks>
        /// English, and untranslated at the source. Chosen off the run's own stream rather than
        /// at random, so a seed says the same thing twice — see <see cref="Trader"/>.
        /// </remarks>
        public static readonly IReadOnlyList<string> Lines = new[]
        {
            "\u201CAh. A live one. Everything here is for sale \u2014 including advice.\u201D",
            "\u201CI don't fight, delver. I price. Come, see what the dead left behind.\u201D",
            "\u201CThe scales never lie. Your gold, my curios \u2014 one deal only.\u201D",
            "\u201CYou'll want something from me. They always do.\u201D",
            "\u201CCoin now, regret later. That's the usual arrangement.\u201D",
        };

        /// <summary>
        /// The card for the merchant, with whichever line this visit drew.
        /// </summary>
        /// <remarks>
        /// The line is picked by INDEX rather than rolled here, because Core's streams belong to
        /// the run and a card is not entitled to draw from one. A caller that has a number hands
        /// it over; one that has not passes anything and gets the first.
        /// </remarks>
        public static IntroCard Trader(int line)
        {
            IReadOnlyList<string> said = Lines;

            int at = said.Count > 0 ? ((line % said.Count) + said.Count) % said.Count : 0;

            return new IntroCard
            {
                Species = -1,
                Variant = 0,
                Rank = EnemyRank.Guard,
                NameKey = null,
                Called = TradeKicker,
                Hex = KingHex,
                Ranked = true,
                Stats = string.Empty,
                Edge = 2,
                EdgeHex = KingHex,
                Lit = true,
                Trading = true,
                Line = said.Count > 0 ? said[at] : string.Empty,
            };
        }

        /// <summary>The card for whoever is stepping up in this snapshot.</summary>
        public static IntroCard Of(CombatSnapshot state)
        {
            EnemyRank rank = state.EnemyRank;
            bool ranked = rank != EnemyRank.Guard;

            return new IntroCard
            {
                Species = state.EnemyIndex,
                Variant = state.EnemyVariant,
                Rank = rank,
                NameKey = EnemyCards.NameKey(state.EnemyIndex),
                Called = Called(rank),
                Hex = ranked ? Coloured(rank) : string.Empty,
                Ranked = ranked,
                Stats = Numbers(state),
                Edge = rank == EnemyRank.King || rank == EnemyRank.Boss ? 3 : 2,
                EdgeHex = rank == EnemyRank.King ? KingHex
                    : rank == EnemyRank.Boss ? BossHex
                    : PlainHex,
                Lit = rank == EnemyRank.King,
            };
        }

        /// <summary>What a rank is announced as. English.</summary>
        public static string Called(EnemyRank rank)
        {
            switch (rank)
            {
                case EnemyRank.King: return KingWord;
                case EnemyRank.Boss: return BossWord;
                case EnemyRank.Elite: return EliteWord;
                default: return GuardWord;
            }
        }

        /// <summary>And what colour it is announced in.</summary>
        public static string Coloured(EnemyRank rank)
        {
            switch (rank)
            {
                case EnemyRank.King: return KingHex;
                case EnemyRank.Boss: return BossHex;
                case EnemyRank.Elite: return EliteHex;
                default: return PlainHex;
            }
        }

        /// <summary>
        /// The four numbers, on one line.
        /// </summary>
        /// <remarks>
        /// Five, in fact, and in a different order from the bestiary's — health, attack, armour,
        /// speed, luck. That is the source's own line and it is not the card's: a bestiary entry
        /// is compared against other entries, so it leads with what varies most, where this is
        /// read once against a delver's own four and matches the order those are drawn in.
        ///
        /// Health is the foe's FULL pool. A card is shown before anybody has swung, so anything
        /// else would be the same number written more carefully.
        /// </remarks>
        public static string Numbers(CombatSnapshot state)
        {
            var said = new StringBuilder();

            said.Append(Say(state.EnemyMaxHp)).Append(" HP");
            said.Append(Between).Append("ATK ").Append(Say(state.EnemyAtk));
            said.Append(Between).Append("DEF ").Append(Say(state.EnemyArmor));
            said.Append(Between).Append("SPD ").Append(Say(state.EnemySpd));
            said.Append(Between).Append("LCK ").Append(Say(state.EnemyLck));

            return said.ToString();
        }

        private static string Say(int number)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }
    }
}
