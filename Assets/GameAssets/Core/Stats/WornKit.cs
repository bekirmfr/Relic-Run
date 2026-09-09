using System.Collections.Generic;
using RelicRun.Core.Content;

namespace RelicRun.Core.Stats
{
    /// <summary>
    /// What one relic is worth to whoever is wearing it, as data.
    /// </summary>
    /// <remarks>
    /// A row, not a branch. The difference is the whole point: adding a relic that thickens its
    /// wearer means adding a line to <see cref="WornKit.Table"/>, and nothing anywhere grows an
    /// <c>if</c>. Every place that turned a kit into numbers had its own copy of these four
    /// relics written out by hand, and the copies were free to disagree — which is not a
    /// hypothetical, since a third copy was being added when this was written.
    /// </remarks>
    public readonly struct WornEffect
    {
        public readonly RelicId Relic;

        /// <summary>Added to the hit-point pool, per copy.</summary>
        /// <remarks>
        /// The pool is not a <see cref="Stat"/> and is not treated as one. A stat is COMPUTED
        /// from its rows every time it is read; a pool is a resource with a starting value that
        /// is then spent. Folding the two together would mean a wound healing itself the moment
        /// anything recalculated.
        /// </remarks>
        public readonly int Pool;

        /// <summary>Which stat this contributes to, when it contributes to one.</summary>
        public readonly Stat Stat;

        public readonly bool TouchesAStat;

        /// <summary>What each copy is worth to that stat.</summary>
        public readonly int Each;

        /// <summary>
        /// What this relic multiplies its stat by, when it multiplies rather than adds.
        /// </summary>
        /// <remarks>
        /// A different shape from a row and treated as one. Rows ADD, so they can be listed,
        /// summed in any order and shown to a delver one line at a time; a multiplier is
        /// order-dependent — base, then rows, then scale — so it is applied where the stat block
        /// is built rather than carried as a modifier. One is a breakdown, the other is a rule
        /// about the total.
        /// </remarks>
        public readonly double Times;

        /// <summary>
        /// Whether a second copy is worth anything.
        /// </summary>
        /// <remarks>
        /// The Lucky Clover is not, and that is the source's rule rather than an oversight: it
        /// grants its luck once however many are worn. Written as a flag because it is a property
        /// OF the relic, and the alternative is the caller remembering which relics are special.
        /// </remarks>
        public readonly bool Stacks;

        private WornEffect(RelicId relic, int pool, Stat stat, bool touchesAStat, int each,
            bool stacks, double times)
        {
            Relic = relic;
            Pool = pool;
            Stat = stat;
            TouchesAStat = touchesAStat;
            Each = each;
            Stacks = stacks;
            Times = times;
        }

        public static WornEffect Thickens(RelicId relic, int pool)
        {
            return new WornEffect(relic, pool, Stat.Atk, false, 0, true, 1d);
        }

        public static WornEffect Raises(RelicId relic, Stat stat, int each, bool stacks = true)
        {
            return new WornEffect(relic, 0, stat, true, each, stacks, 1d);
        }

        /// <summary>A relic that multiplies a stat rather than adding to it.</summary>
        public static WornEffect Scales(RelicId relic, Stat stat, double times)
        {
            return new WornEffect(relic, 0, stat, false, 0, false, times);
        }
    }

    /// <summary>
    /// What a hand of relics is worth to the body wearing it.
    /// </summary>
    /// <remarks>
    /// The delver never asks this, and that is not an omission. A delver's relics are applied as
    /// they are TAKEN — an Ox Heart thickens them on the spot, in <c>Pickup.Take</c> — so their
    /// pool already includes everything they carry. This is for a body that arrives complete:
    /// a versus rival built from a kit, or a foe somebody typed out in full.
    ///
    /// A GENERATED delve foe deliberately does not ask either. Its numbers come from the floor's
    /// tables, which were written with its kit already in mind, so applying the kit again would
    /// count the same relic twice and every recorded fight would disagree.
    /// </remarks>
    public static class WornKit
    {
        /// <summary>
        /// Everything a worn relic does to a stat block.
        /// </summary>
        /// <remarks>
        /// The numbers are the source's. Thirteen to the pool for an Ox Heart is the same
        /// thirteen <c>Pickup.Take</c> gives a delver on the spot; the rest are lifted from the
        /// versus rival, which is the one body the source builds from a kit rather than from a
        /// table of its own.
        ///
        /// Short, and that is the honest state of it rather than a stub. Most relics do their
        /// work in the FIGHT, through the engine's own reads — a Thorn Vest returns damage, it
        /// does not raise a stat. These five are the ones that change the body before a blow is
        /// struck.
        /// </remarks>
        public static readonly IReadOnlyList<WornEffect> Table = new[]
        {
            WornEffect.Thickens(RelicId.OxHeart, 13),
            WornEffect.Raises(RelicId.Whetstone, Stat.Atk, 1),
            WornEffect.Raises(RelicId.IronSkin, Stat.Def, 1),
            WornEffect.Raises(RelicId.LuckyClover, Stat.Lck, 15, stacks: false),
            WornEffect.Scales(RelicId.SwiftBoots, Stat.Spd, 1.25d),
        };

        /// <summary>What this hand adds to the pool it is worn on.</summary>
        public static int Pool(IReadOnlyList<RelicId> relics)
        {
            var total = 0;

            foreach (WornEffect worn in Table)
            {
                if (worn.Pool == 0) continue;

                total += worn.Pool * Held(relics, worn.Relic);
            }

            return total;
        }

        /// <summary>
        /// This hand's contributions to the stats, each labelled with the relic that gave it.
        /// </summary>
        /// <remarks>
        /// Modifiers rather than arithmetic, so the number can be explained. "Armour 3" tells a
        /// delver nothing about why the thing in front of them is hard to hurt; "Iron Skin +1"
        /// alongside it does, and it is the same list the stat card already reads for the hero.
        /// </remarks>
        public static List<StatModifier> Modifiers(IReadOnlyList<RelicId> relics)
        {
            var rows = new List<StatModifier>();

            foreach (WornEffect worn in Table)
            {
                if (!worn.TouchesAStat) continue;

                int copies = Held(relics, worn.Relic);
                if (copies == 0) continue;

                int amount = worn.Stacks ? worn.Each * copies : worn.Each;

                rows.Add(new StatModifier(worn.Stat, RelicCatalog.KeyOf(worn.Relic), amount));
            }

            return rows;
        }

        /// <summary>
        /// What this hand multiplies a stat by, after its rows have been added.
        /// </summary>
        /// <remarks>
        /// Once, however many copies are worn — which is the source's rule for the only relic
        /// that does this. Compounding it would make a second pair of boots worth more than the
        /// first, and nothing else in the game works that way.
        /// </remarks>
        public static double Scale(IReadOnlyList<RelicId> relics, Stat stat)
        {
            var times = 1d;

            foreach (WornEffect worn in Table)
            {
                if (worn.Times == 1d || worn.Stat != stat) continue;
                if (Held(relics, worn.Relic) == 0) continue;

                times *= worn.Times;
            }

            return times;
        }

        /// <summary>The rows for one stat, folded.</summary>
        public static int Of(IReadOnlyList<StatModifier> mods, Stat stat)
        {
            if (mods == null) return 0;

            var total = 0;

            for (int i = 0; i < mods.Count; i++)
            {
                if (mods[i].Stat == stat) total += mods[i].Amount;
            }

            return total;
        }

        private static int Held(IReadOnlyList<RelicId> relics, RelicId id)
        {
            if (relics == null) return 0;

            var many = 0;

            for (int i = 0; i < relics.Count; i++)
            {
                if (relics[i] == id) many++;
            }

            return many;
        }
    }
}
