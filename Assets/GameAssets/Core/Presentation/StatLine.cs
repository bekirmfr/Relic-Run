using System.Collections.Generic;
using RelicRun.Core.Combat;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Presentation
{
    /// <summary>
    /// One stat, as a thing to show: what it is called and what it reads.
    /// </summary>
    /// <remarks>
    /// The hit points are NOT one of these and that is the arrangement rather than an omission.
    /// A pool is read as a proportion — how much is left of what there was — and a bar answers
    /// that at a glance in a way a pair of numbers never does. Everything else is read as a
    /// figure, and a figure wants a label beside it.
    ///
    /// No colour here. Which stat is which colour is the screen's business, and a view-model
    /// handing out hex values would be choosing the palette from inside Core — the same rule the
    /// log's line kinds live under.
    /// </remarks>
    public readonly struct StatLine
    {
        public readonly Stat Stat;

        /// <summary>The short label the source prints: ATK, DEF, SPD, LCK.</summary>
        public readonly string Label;

        public readonly int Value;

        public StatLine(Stat stat, string label, int value)
        {
            Stat = stat;
            Label = label;
            Value = value;
        }

        public override string ToString() { return Label + " " + Value; }
    }

    /// <summary>
    /// The four stats a fighter shows, for either side of a fight.
    /// </summary>
    /// <remarks>
    /// One description for both, which is the point. The delver and the foe are the same kind of
    /// thing wearing the same kinds of relic, and a screen that described them with two different
    /// lists would eventually describe them differently — a stat added to one and forgotten on
    /// the other.
    ///
    /// In the source's own order, which is not alphabetical and not arbitrary: attack and defence
    /// are what a blow is made of, speed and luck are how often it lands.
    /// </remarks>
    public static class StatLines
    {
        /// <summary>The labels, which are the source's and are not translated.</summary>
        /// <remarks>
        /// Three letters each, in a face drawn on an eight-pixel grid. The source leaves them in
        /// English in every locale, and a longer word would not fit the row it sits in — so this
        /// is a layout constraint as much as a translation decision, and it is the source's.
        /// </remarks>
        public const string Attack = "ATK";

        public const string Defence = "DEF";

        public const string Speed = "SPD";

        public const string Luck = "LCK";

        /// <summary>What the delver's panel shows.</summary>
        public static List<StatLine> Delver(CombatSnapshot state)
        {
            return Four(state.HeroAtk, state.HeroDef, state.HeroSpd, state.HeroLck);
        }

        /// <summary>What the foe's panel shows.</summary>
        public static List<StatLine> Foe(CombatSnapshot state)
        {
            return Four(state.EnemyAtk, state.EnemyArmor, state.EnemySpd, state.EnemyLck);
        }

        private static List<StatLine> Four(int atk, int def, int spd, int lck)
        {
            return new List<StatLine>
            {
                new StatLine(Stat.Atk, Attack, atk),
                new StatLine(Stat.Def, Defence, def),
                new StatLine(Stat.Spd, Speed, spd),
                new StatLine(Stat.Lck, Luck, lck),
            };
        }
    }
}
