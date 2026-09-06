using System;
using System.Collections.Generic;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Run
{
    /// <summary>What a dungeon does to the foes inside it.</summary>
    public sealed class DungeonConfig
    {
        /// <summary>Multiplies enemy HP, attack and drops. 1.0 in the first dungeon.</summary>
        public double Multiplier = 1.0;

        /// <summary>The relic kit this dungeon's boss carries.</summary>
        public IReadOnlyList<RelicId> BossRelics = System.Array.Empty<RelicId>();

        /// <summary>When set, the Ghoolem replaces this dungeon's boss.</summary>
        public bool GhoolemBoss;
    }

    /// <summary>
    /// Builds the pack of foes waiting on a floor. Port of <c>packFor</c>.
    /// </summary>
    /// <remarks>
    /// Species are not random. One species owns each combat floor: floor <i>f</i> fields its own
    /// species as the boss, floor <i>f-1</i>'s as elites, and <i>f-2</i>'s and deeper as guards.
    /// The thing that nearly killed you as a boss comes back as an elite, then as trash — the
    /// clearest possible read on the player's own growth, at no cost in UI.
    ///
    /// Each rank samples its pool WITHOUT replacement, so one pack never fields the same species
    /// twice, and elites draw first because they are the read the player cares about.
    /// </remarks>
    public static class EnemyPackGenerator
    {
        /// <summary>The floor the Hoard Bazaar occupies. No fight happens there.</summary>
        public const int BazaarFloor = 7;

        public const int MaxFloor = 13;

        /// <summary>Bestiary index of the Hoard-King. Floor 13 only.</summary>
        public const int KingSpecies = 9;

        /// <summary>Bestiary index of the Ghoolem, the armoured bruiser.</summary>
        public const int GhoolemSpecies = 10;

        /// <summary>Which species owns each combat floor, in order.</summary>
        private static readonly int[] Ladder = { 1, 0, 3, 2, 5, 6, 4, 11, 12, 10, 8 };

        /// <summary>The twelfth species, held back as the King's honour guard.</summary>
        private const int Spare = 7;

        /// <summary>Sheet column per rank: guard bare, elite armed, boss armed and shielded.</summary>
        private static int VariantOf(EnemyRank rank)
        {
            switch (rank)
            {
                case EnemyRank.Elite: return 1;
                case EnemyRank.Boss:
                case EnemyRank.King: return 2;
                default: return 0;
            }
        }

        /// <summary>The pack for a floor, already scaled to its dungeon. Port of <c>tierPack</c>.</summary>
        public static List<EnemyState> Build(int floor, Mulberry32 rng, DungeonConfig dungeon)
        {
            dungeon = dungeon ?? new DungeonConfig();
            List<EnemyState> pack = BuildUnscaled(floor, rng, dungeon);

            if (dungeon.Multiplier > 1)
            {
                foreach (EnemyState foe in pack)
                {
                    foe.Hp = JsMath.RoundToInt(foe.Hp * dungeon.Multiplier);
                    foe.MaxHp = foe.Hp;
                    foe.Atk = JsMath.RoundToInt(foe.Atk * dungeon.Multiplier);
                    foe.Drop = JsMath.RoundToInt(foe.Drop * dungeon.Multiplier);
                }
            }

            return pack;
        }

        /// <summary>The pack before the dungeon multiplier. Port of <c>packFor</c>.</summary>
        public static List<EnemyState> BuildUnscaled(int floor, Mulberry32 rng, DungeonConfig dungeon)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            dungeon = dungeon ?? new DungeonConfig();

            // Every rank derives from the floor's guard baseline.
            int guardHp = JsMath.RoundToInt(6 + JsMath.Round(Math.Pow(floor, 1.4)));
            int guardAtk = JsMath.RoundToInt(1 + JsMath.Round(0.1 * Math.Pow(floor, 1.56)));
            int guardDef = JsMath.RoundToInt(Math.Floor(0.05 * Math.Pow(floor, 1.6)));
            const int guardSpd = 25;
            const int guardLck = 10;

            int guardDrop = GoldFor(guardHp, guardAtk, guardDef, guardSpd, guardLck);

            var pack = new List<EnemyState>();

            if (floor == MaxFloor)
            {
                // The King behind three distinct honour guards, the spare species included.
                List<int> pool = PoolOf(floor, 3, 1);
                pool.Add(Spare);
                List<int> chosen = TakeWithoutReplacement(pool, 3, new List<int>(), rng);

                for (int i = 0; i < 3; i++)
                {
                    pack.Add(MakeElite(chosen[i], floor, guardHp, guardAtk, guardDef, guardSpd, guardLck, guardDrop));
                }

                pack.Add(new EnemyState
                {
                    SpeciesIndex = KingSpecies,
                    Hp = JsMath.RoundToInt(guardHp * 3),
                    MaxHp = JsMath.RoundToInt(guardHp * 3),
                    Atk = guardAtk * 2 + 1,
                    Drop = 130,
                    Rank = EnemyRank.King,
                    Relics = new List<RelicId>(dungeon.BossRelics),
                    Armor = guardDef * 4,
                    Spd = Math.Max(10, guardSpd - 10),
                    Lck = guardLck + 10,
                    Variant = VariantOf(EnemyRank.King),
                });

                return pack;
            }

            if (floor == 1)
            {
                // Floor one is its boss alone: nothing has been demoted twice yet.
                pack.Add(MakeBoss(SpeciesAt(floor, 0), floor, dungeon, guardHp, guardAtk, guardDef, guardSpd, guardLck));
                return pack;
            }

            List<int> guardPool = PoolOf(floor, Ordinal(floor), 2);
            List<int> elitePool = PoolOf(floor, 3, 1);

            int normals;
            if (guardPool.Count == 0) normals = 0;
            else if (floor <= 3) normals = rng.NextInt(2);
            else if (floor <= 6) normals = 1 + rng.NextInt(2);
            else if (floor <= 9) normals = 2;
            else normals = rng.Next() < 0.5 ? 1 : 0;

            int elites = floor <= 9 ? 0 : (normals == 1 ? 1 : 2);

            // Elites claim first, so the species the player is watching for is never crowded out.
            var used = new List<int>();
            List<int> eliteIds = TakeWithoutReplacement(elitePool, elites, used, rng);
            List<int> guardIds = TakeWithoutReplacement(guardPool, normals, used, rng);

            for (int i = 0; i < normals; i++)
            {
                pack.Add(new EnemyState
                {
                    SpeciesIndex = guardIds[i],
                    Hp = guardHp,
                    MaxHp = guardHp,
                    Atk = guardAtk,
                    Armor = guardDef,
                    Spd = guardSpd,
                    Lck = guardLck,
                    Drop = guardDrop,
                    Rank = EnemyRank.Guard,
                    Variant = VariantOf(EnemyRank.Guard),
                });
            }

            for (int i = 0; i < elites; i++)
            {
                pack.Add(MakeElite(eliteIds[i], floor, guardHp, guardAtk, guardDef, guardSpd, guardLck, guardDrop));
            }

            pack.Add(MakeBoss(SpeciesAt(floor, 0), floor, dungeon, guardHp, guardAtk, guardDef, guardSpd, guardLck));
            return pack;
        }

        // ---------- the ladder ----------

        /// <summary>How far down the ladder a floor sits, skipping the bazaar.</summary>
        private static int Ordinal(int floor)
        {
            return floor < BazaarFloor ? floor - 1 : floor - 2;
        }

        /// <summary>The species owning <paramref name="back"/> floors above this one.</summary>
        private static int? SpeciesAt(int floor, int back)
        {
            int i = Ordinal(floor) - back;
            if (i < 0) return null;
            return Ladder[Math.Min(Ladder.Length - 1, i)];
        }

        /// <summary>Distinct species between two distances back, nearest first.</summary>
        private static List<int> PoolOf(int floor, int from, int to)
        {
            var pool = new List<int>();
            for (int i = from; i >= to; i--)
            {
                int? species = SpeciesAt(floor, i);
                if (species.HasValue && !pool.Contains(species.Value))
                {
                    pool.Add(species.Value);
                }
            }

            return pool;
        }

        /// <summary>
        /// Draws <paramref name="count"/> species without replacement, so a pack never fields the
        /// same species twice. If the pool runs dry it repeats rather than dropping a foe.
        /// </summary>
        private static List<int> TakeWithoutReplacement(List<int> pool, int count, List<int> used, Mulberry32 rng)
        {
            var available = new List<int>();
            foreach (int species in pool)
            {
                if (!used.Contains(species)) available.Add(species);
            }

            var taken = new List<int>();
            while (taken.Count < count)
            {
                if (available.Count == 0)
                {
                    taken.Add(pool.Count > 0 ? pool[rng.NextInt(pool.Count)] : -1);
                    continue;
                }

                int k = rng.NextInt(available.Count);
                taken.Add(available[k]);
                used.Add(available[k]);
                available.RemoveAt(k);
            }

            return taken;
        }

        // ---------- ranks ----------

        private static EnemyState MakeElite(int species, int floor, int guardHp, int guardAtk,
            int guardDef, int guardSpd, int guardLck, int guardDrop)
        {
            // The Ghoolem is the armoured bruiser: slower, tougher, heavily plated, and it runs
            // the same relic engine the hero does.
            bool ghoolem = species == GhoolemSpecies;

            var foe = new EnemyState
            {
                SpeciesIndex = species,
                Rank = EnemyRank.Elite,
                Hp = JsMath.RoundToInt(JsMath.RoundToInt(guardHp * 1.2) * (ghoolem ? 1.3 : 1.0)),
                Atk = guardAtk,
                Armor = guardDef + 2 + (ghoolem ? 6 : 0),
                Spd = ghoolem ? Math.Max(10, guardSpd + 5 - 8) : guardSpd + 5,
                Lck = guardLck,
                Drop = guardDrop + 4,
                Variant = VariantOf(EnemyRank.Elite),
            };

            foe.MaxHp = foe.Hp;
            if (ghoolem) foe.Relics = new List<RelicId> { RelicId.IronSkin };
            return foe;
        }

        private static EnemyState MakeBoss(int? species, int floor, DungeonConfig dungeon,
            int guardHp, int guardAtk, int guardDef, int guardSpd, int guardLck)
        {
            bool ghoolemHall = dungeon.GhoolemBoss && floor > 1;

            var relics = new List<RelicId>(dungeon.BossRelics);
            if (ghoolemHall) relics.Add(RelicId.IronSkin);

            var foe = new EnemyState
            {
                SpeciesIndex = ghoolemHall ? GhoolemSpecies : (species ?? 0),
                Hp = JsMath.RoundToInt(JsMath.RoundToInt(guardHp * 1.5) * (ghoolemHall ? 1.25 : 1.0)),
                Atk = guardAtk + 1,
                Rank = EnemyRank.Boss,
                Relics = relics,
                Armor = guardDef + 3 + (ghoolemHall ? 6 : 0),
                Spd = guardSpd,
                Lck = guardLck + 10,
                Variant = VariantOf(EnemyRank.Boss),
            };

            foe.MaxHp = foe.Hp;
            foe.Drop = JsMath.RoundToInt(GoldFor(foe.Hp, foe.Atk, foe.Armor, foe.Spd, foe.Lck) * 1.5);
            return foe;
        }

        /// <summary>
        /// What a foe is worth, priced from its own stats rather than from the floor. A tougher,
        /// faster, luckier foe pays more because it was harder to kill.
        /// </summary>
        private static int GoldFor(int hp, int atk, int def, int spd, int lck)
        {
            double gold = hp / 10.0 + def / 1.5 + atk + (spd - 25) / 1.5 + (lck - 10) / 3.5;
            return Math.Max(1, JsMath.RoundToInt(gold));
        }
    }
}
