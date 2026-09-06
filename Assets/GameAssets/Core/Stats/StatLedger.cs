using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;

namespace RelicRun.Core.Stats
{
    public enum Stat
    {
        Atk = 0,
        Def = 1,
        Spd = 2,
        Lck = 3,
    }

    public enum EnemyRank
    {
        None = 0,
        Guard = 1,
        Elite = 2,
        Boss = 3,
        King = 4,
    }

    /// <summary>One labelled contribution to a stat. The label is part of the contract.</summary>
    public readonly struct StatModifier
    {
        public readonly Stat Stat;

        /// <summary>Human-readable source, shown verbatim on the stat breakdown card.</summary>
        public readonly string Source;

        public readonly int Amount;

        /// <summary><c>"base"</c> for the base stat, <c>"dyn"</c> for in-fight entries, else empty.</summary>
        public readonly string Tag;

        public StatModifier(Stat stat, string source, int amount, string tag = "")
        {
            Stat = stat;
            Source = source;
            Amount = amount;
            Tag = tag ?? string.Empty;
        }

        public override string ToString()
        {
            return Source + " " + (Amount >= 0 ? "+" : "") + Amount;
        }
    }

    /// <summary>Everything the ledger needs to resolve a stat. Read-only to the ledger.</summary>
    public sealed class StatContext
    {
        /// <summary>Inventory in draft order, duplicates included. Never collapse this to counts.</summary>
        public IReadOnlyList<RelicId> Items = System.Array.Empty<RelicId>();

        /// <summary>Relics with at least one awakened copy.</summary>
        public IReadOnlyCollection<RelicId> Awakened = System.Array.Empty<RelicId>();

        public bool IsVersus;

        public int BaseAtk = 5;
        public int BaseDef;
        public int BaseSpd = 25;
        public int BaseLck = 10;

        /// <summary>Permanent ATK banked from Adrenaline Vial activations.</summary>
        public int Adrenaline;

        public int MidasBonus;
        public int AtkBonus;
        public int DefBonus;
        public int SpdBonus;
        public int LuckBonus;

        public int Php = 100;
        public int Pmax = 100;
        public int Gold;
        public int Floor;

        public EnemyRank FoeRank = EnemyRank.None;

        /// <summary>In-fight modifiers the engine pushes in, e.g. Fury or Sentinel Bell.</summary>
        public IReadOnlyList<StatModifier> Mods = System.Array.Empty<StatModifier>();
    }

    /// <summary>
    /// The single source of truth for hero stats. Port of <c>heroStatRows</c> / <c>heroStatOf</c>.
    /// </summary>
    /// <remarks>
    /// Architecture invariant 4: nothing anywhere computes a stat inline. The HUD, the stat
    /// breakdown card, both engines, the sandbox and the balance harness all read through here,
    /// which is why they can never disagree. Engines contribute by pushing labelled entries into
    /// <see cref="StatContext.Mods"/> and reading the total back.
    ///
    /// Rows for the 28 cut relics are deliberately absent — see docs/relics-cut.md. The source's
    /// Awakener's Loop, which would have made awakened copies count as three, is one of them, so
    /// an awakened relic counts as exactly one extra copy here.
    /// </remarks>
    public static class StatLedger
    {
        /// <summary>Appends the labelled rows for one stat, in the source's order.</summary>
        public static void Rows(StatContext ctx, Stat stat, List<StatModifier> into)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (into == null) throw new ArgumentNullException(nameof(into));

            var c = new Counter(ctx);

            switch (stat)
            {
                case Stat.Atk: AtkRows(ctx, c, into); break;
                case Stat.Def: DefRows(ctx, c, into); break;
                case Stat.Spd: SpdRows(ctx, c, into); break;
                case Stat.Lck: LckRows(ctx, c, into); break;
            }

            // In-fight entries always come last, so the breakdown reads static-then-dynamic.
            for (int i = 0; i < ctx.Mods.Count; i++)
            {
                StatModifier m = ctx.Mods[i];
                if (m.Stat == stat && m.Amount != 0)
                {
                    into.Add(new StatModifier(stat, m.Source, m.Amount, string.IsNullOrEmpty(m.Tag) ? "dyn" : m.Tag));
                }
            }
        }

        /// <summary>Convenience overload allocating its own list.</summary>
        public static List<StatModifier> Rows(StatContext ctx, Stat stat)
        {
            var rows = new List<StatModifier>();
            Rows(ctx, stat, rows);
            return rows;
        }

        /// <summary>The folded total, with the stat's floor applied.</summary>
        public static int Of(StatContext ctx, Stat stat)
        {
            var rows = new List<StatModifier>();
            Rows(ctx, stat, rows);

            int total = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                total += rows[i].Amount;
            }

            switch (stat)
            {
                // A hero always swings for something, and always acts eventually.
                case Stat.Atk: return Math.Max(1, total);
                case Stat.Spd: return Math.Max(ctx.IsVersus ? 10 : 15, total);
                default: return Math.Max(0, total);
            }
        }

        // -------- rows, in source order --------

        private static void AtkRows(StatContext ctx, Counter c, List<StatModifier> r)
        {
            Add(r, Stat.Atk, "Base ATK", ctx.BaseAtk, "base");
            Add(r, Stat.Atk, "Whetstone", 2 * c.Effective(RelicId.Whetstone));
            Add(r, Stat.Atk, "Adrenaline (permanent)", ctx.Adrenaline);
            Add(r, Stat.Atk, "Midas Blade", MidasAtk(ctx, c));
            Add(r, Stat.Atk, "Run bonuses", ctx.AtkBonus);
            Add(r, Stat.Atk, "Blood Pact", (c.IsAwake(RelicId.BloodPact) ? 8 : 5) * c.Count(RelicId.BloodPact));
            Add(r, Stat.Atk, "Millstone Pendant", 5 * c.Effective(RelicId.MillstonePendant));

            if (c.Count(RelicId.BerserkerCharm) > 0 && Bloodied(ctx))
            {
                Add(r, Stat.Atk, "Berserker Charm (bloodied)", 4);
            }

            int duelist = c.Effective(RelicId.DuelistsOath);
            if (duelist > 0 && (ctx.IsVersus || IsWorthyBlood(ctx.FoeRank)))
            {
                Add(r, Stat.Atk, "Duelist's Oath (worthy blood)", 4 * duelist);
            }

            if (c.SetCount(RelicKind.Edge) >= 3) Add(r, Stat.Atk, "Edge set (3)", 1);
            if (c.SetCount(RelicKind.Curse) >= 3) Add(r, Stat.Atk, "Curse set (3)", 2);
            if (c.SetCount(RelicKind.Greed) >= 7) Add(r, Stat.Atk, "Greed set (7)", ctx.Gold / 100);
        }

        private static void DefRows(StatContext ctx, Counter c, List<StatModifier> r)
        {
            Add(r, Stat.Def, "Base DEF", ctx.BaseDef, "base");
            Add(r, Stat.Def, "Iron Skin", 2 * c.Effective(RelicId.IronSkin));
            Add(r, Stat.Def, "Padded Hide", c.Effective(RelicId.PaddedHide));
            Add(r, Stat.Def, "Run bonuses", ctx.DefBonus);

            if (c.Effective(RelicId.GildedPlate) > 0)
            {
                int per = c.IsAwake(RelicId.GildedPlate) ? 25 : 50;
                Add(r, Stat.Def, "Auric Skin (gold)", ctx.Gold / per);
            }

            if (c.SetCount(RelicKind.Guard) >= 3) Add(r, Stat.Def, "Guard set (3)", 3);
        }

        private static void SpdRows(StatContext ctx, Counter c, List<StatModifier> r)
        {
            int baseSpd = ctx.BaseSpd;
            Add(r, Stat.Spd, "Base SPD", baseSpd, "base");

            // Boots scale off the BASE, so levelling makes them better, and the awakened
            // Berserker bonus then compounds on base + boots.
            int bootsBoost = JsMath.RoundToInt(baseSpd * 0.25 * c.Effective(RelicId.SwiftBoots));
            if (c.Count(RelicId.SwiftBoots) > 0)
            {
                Add(r, Stat.Spd, "Swift Boots (+25%/copy)", bootsBoost);
            }

            if (c.IsAwake(RelicId.BerserkerCharm) && c.Count(RelicId.BerserkerCharm) > 0 && Bloodied(ctx))
            {
                Add(r, Stat.Spd, "Berserker Charm ✦ (bloodied)", JsMath.RoundToInt((baseSpd + bootsBoost) * 0.25));
            }

            Add(r, Stat.Spd, "Wind Anklet", 2 * c.Effective(RelicId.WindAnklet));

            int millstone = c.Effective(RelicId.MillstonePendant);
            if (millstone > 0) Add(r, Stat.Spd, "Millstone Pendant", -5 * millstone);

            Add(r, Stat.Spd, "Run bonuses", ctx.SpdBonus);

            if (c.SetCount(RelicKind.Pace) >= 3) Add(r, Stat.Spd, "Pace set (3)", 2);
        }

        private static void LckRows(StatContext ctx, Counter c, List<StatModifier> r)
        {
            Add(r, Stat.Lck, "Base LCK", ctx.BaseLck, "base");

            if (c.Effective(RelicId.LuckyClover) > 0) Add(r, Stat.Lck, "Lucky Clover", 10);

            Add(r, Stat.Lck, "Weighted Dice", 5 * c.Effective(RelicId.WeightedDice));
            Add(r, Stat.Lck, "Loaded Horseshoe",
                (c.IsAwake(RelicId.LoadedHorseshoe) ? 9 : 5) * c.Effective(RelicId.LoadedHorseshoe));

            if (c.Effective(RelicId.FortunesDebt) > 0) Add(r, Stat.Lck, "Fortune's Debt", 10);

            Add(r, Stat.Lck, "Run bonuses (events)", ctx.LuckBonus);

            if (c.SetCount(RelicKind.Luck) >= 3) Add(r, Stat.Lck, "Luck set (3)", 5);
        }

        // -------- helpers --------

        /// <summary>Zero contributions are not recorded — the breakdown card lists only live rows.</summary>
        private static void Add(List<StatModifier> rows, Stat stat, string source, int amount, string tag = "")
        {
            if (amount != 0)
            {
                rows.Add(new StatModifier(stat, source, amount, tag));
            }
        }

        private static bool Bloodied(StatContext ctx)
        {
            return ctx.Php < ctx.Pmax / 2.0;
        }

        private static bool IsWorthyBlood(EnemyRank rank)
        {
            return rank == EnemyRank.Elite || rank == EnemyRank.Boss || rank == EnemyRank.King;
        }

        /// <summary>
        /// Awakened Midas Blade re-reads the purse, so its bonus tracks gold gained since the
        /// banked figure rather than freezing at pickup.
        /// </summary>
        private static int MidasAtk(StatContext ctx, Counter c)
        {
            int bonus = ctx.MidasBonus;
            if (c.IsAwake(RelicId.MidasBlade) && c.Count(RelicId.MidasBlade) > 0)
            {
                bonus += Math.Max(0, (ctx.Gold / 50) - ctx.MidasBonus);
            }

            return bonus;
        }

        /// <summary>Counts copies, awakenings and kind totals for one context.</summary>
        private readonly struct Counter
        {
            private readonly StatContext _ctx;
            private readonly int[] _kindCounts;
            private readonly int _hollowIdols;

            public Counter(StatContext ctx)
            {
                _ctx = ctx;
                _kindCounts = new int[8];
                int idols = 0;

                for (int i = 0; i < ctx.Items.Count; i++)
                {
                    RelicId id = ctx.Items[i];
                    _kindCounts[(int)RelicCatalog.KindOf(id)]++;
                    if (id == RelicId.HollowIdol) idols++;
                }

                _hollowIdols = idols;
            }

            public int Count(RelicId id)
            {
                int n = 0;
                for (int i = 0; i < _ctx.Items.Count; i++)
                {
                    if (_ctx.Items[i] == id) n++;
                }

                return n;
            }

            public bool IsAwake(RelicId id)
            {
                foreach (RelicId awake in _ctx.Awakened)
                {
                    if (awake == id) return true;
                }

                return false;
            }

            /// <summary>Copies held, plus one for an awakened copy.</summary>
            public int Effective(RelicId id)
            {
                return Count(id) + (IsAwake(id) ? 1 : 0);
            }

            /// <summary>Relics of a kind. Hollow Idol counts itself toward every set.</summary>
            public int SetCount(RelicKind kind)
            {
                return _kindCounts[(int)kind] + _hollowIdols;
            }
        }
    }
}
