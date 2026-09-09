using System;
using System.Collections.Generic;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;
using RelicRun.Core.Stats;
using UnityEngine;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// One relic on the harness's shelf, as it is typed into an inspector.
    /// </summary>
    /// <remarks>
    /// The serialized twin of <see cref="RelicCopy"/>. It exists because Unity cannot serialize a
    /// readonly struct or a dictionary, and the shelf is two dictionaries keyed by slot — so what
    /// a person edits is a flat list and what the game reads is the folded thing, with
    /// <see cref="Shelf.Dress"/> doing the folding.
    ///
    /// Sockets are edited PER ROW rather than as a separate "which slot is socketed" number,
    /// which is what this replaced. That number was the bug waiting to happen: it addressed a
    /// slot by index from somewhere else, so inserting a relic above it silently moved the socket
    /// onto a different relic.
    /// </remarks>
    [Serializable]
    public struct ShelfEntry
    {
        public RelicId Relic;

        [Tooltip("A socket bolted to THIS copy. Attack, Hit and Gold count every third.")]
        public SocketTrigger Trigger;

        [Tooltip("A socket's output. Only Def has a gauge; the rest fire without counting.")]
        public SocketEmitter Emitter;

        [Tooltip("Woken at the bazaar. Wakes every copy of this relic, which is the source's rule.")]
        public bool Awakened;

        public RelicCopy ToCopy()
        {
            return new RelicCopy(Relic, Trigger, Emitter, Awakened);
        }
    }

    /// <summary>
    /// One foe, as it is typed into an inspector.
    /// </summary>
    /// <remarks>
    /// There is deliberately no Variant field. The sheet column follows the RANK, and a column
    /// somebody could type is a boss that gets drawn as a guard — wrong art, with nothing
    /// anywhere to say so. <see cref="EnemyPackGenerator.Authored"/> decides it, along with the
    /// HP ceiling and the difference between an empty relic list and no relic list.
    /// </remarks>
    [Serializable]
    public struct FoeEntry
    {
        [Tooltip("Bestiary row. Thirteen species, and it picks the sprite.")]
        [Range(0, 12)] public int Species;

        [Tooltip("Decides the sheet column as well as the behaviour.")]
        public EnemyRank Rank;

        [Min(1)] public int Hp;
        [Min(0)] public int Atk;
        [Min(0)] public int Armor;
        [Min(1)] public int Spd;
        [Min(0)] public int Lck;

        [Tooltip("Gold dropped on death.")]
        [Min(0)] public int Drop;

        [Tooltip("Only six enemy relics do anything in a delve: Thorn Vest, Berserker Charm, " +
                 "Weighted Dice, Vampire Tooth, Battle Dash, Lucky Clover.")]
        public RelicId[] Relics;

        public EnemyState ToFoe()
        {
            // A row added in the inspector starts at zero, because a struct has no constructor to
            // run. Zero HP is a foe that dies before the first tick and zero speed is one that
            // never acts — both look like the engine misbehaving rather than like a form that was
            // never filled in, so they are corrected and said out loud.
            int hp = Hp;
            int spd = Spd;
            EnemyRank rank = Rank;

            if (hp < 1 || spd < 1 || rank == EnemyRank.None)
            {
                Debug.LogWarning("a foe was authored as a " + Rank + " with " + Hp + " hp and " +
                                 Spd + " speed, which is a row nobody finished filling in; " +
                                 "treating it as a guard with at least one of each");

                if (hp < 1) hp = 1;
                if (spd < 1) spd = 1;

                // None is what a rank field holds before anybody touches it, and it is not a
                // rank the game has — it would pick the guard's art by falling off the end of
                // the switch rather than by being one.
                if (rank == EnemyRank.None) rank = EnemyRank.Guard;
            }

            return EnemyPackGenerator.Authored(Species, rank, hp, Atk, Armor, spd, Lck, Drop,
                Relics);
        }

        /// <summary>A plain guard, which is what a new row in the inspector should be.</summary>
        public static FoeEntry Guard()
        {
            return new FoeEntry
            {
                Species = 0,
                Rank = EnemyRank.Guard,
                Hp = 20,
                Atk = 4,
                Armor = 0,
                Spd = 25,
                Lck = 10,
                Drop = 6,
                Relics = new RelicId[0],
            };
        }
    }

    /// <summary>
    /// The delver, either as a level or as numbers somebody chose.
    /// </summary>
    /// <remarks>
    /// A level by default, because that is what the game does and it keeps the harness honest:
    /// the fight it shows is one a delver could actually have. The override is for the times when
    /// the point is not realism — a delver with 999 attack kills everything in one blow, which is
    /// a terrible fight and a very quick way to see what a kill looks like.
    /// </remarks>
    [Serializable]
    public sealed class DelverSetup
    {
        [Tooltip("Sets the opening stats, the way the run layer would.")]
        [Min(1)] public int Level = 1;

        [Tooltip("Ignore the level and use the numbers below.")]
        public bool OverrideStats;

        [Min(1)] public int Hp = 100;
        [Min(0)] public int Gold;
        [Min(0)] public int Atk = 5;
        [Min(0)] public int Def;
        [Min(1)] public int Spd = 25;
        [Min(0)] public int Lck = 10;

        [Tooltip("One row per COPY. Duplicates are the point: sockets belong to a slot.")]
        public ShelfEntry[] Shelf =
        {
            new ShelfEntry { Relic = RelicId.AnvilHeart },
            new ShelfEntry { Relic = RelicId.Whetstone },
            new ShelfEntry { Relic = RelicId.Whetstone, Trigger = SocketTrigger.Attack },
            new ShelfEntry { Relic = RelicId.QuenchedBlade },
            new ShelfEntry { Relic = RelicId.SentinelBell },
        };

        /// <summary>The hero this describes, dressed and ready to fight.</summary>
        public HeroState Build(int floor)
        {
            RunSetup setup = RunSetup.ForLevel(Level);

            var hero = new HeroState
            {
                Floor = floor,
                Php = OverrideStats ? Hp : setup.Hp,
                Pmax = OverrideStats ? Hp : setup.Hp,
                Gold = OverrideStats ? Gold : setup.Gold,
                BaseAtk = OverrideStats ? Atk : setup.Atk,
                BaseDef = OverrideStats ? Def : setup.Def,
                BaseSpd = OverrideStats ? Spd : setup.Spd,
                BaseLck = OverrideStats ? Lck : setup.Lck,
            };

            var copies = new List<RelicCopy>();

            if (Shelf != null)
            {
                foreach (ShelfEntry entry in Shelf) copies.Add(entry.ToCopy());
            }

            RelicRun.Core.Presentation.Shelf.Dress(hero, copies);

            return hero;
        }
    }

    /// <summary>
    /// The opposition, either generated for the floor or typed in.
    /// </summary>
    /// <remarks>
    /// Generated by default, and that default matters more than it looks. A floor's pack is drawn
    /// from the same random stream the fight then runs on, so REPLACING it does not merely change
    /// who turns up — it changes every roll afterwards. The same seed therefore describes a
    /// different fight with this switched on, which is fine as long as nobody compares the two
    /// and concludes the engine changed.
    /// </remarks>
    [Serializable]
    public sealed class FoeSetup
    {
        [Tooltip("Ignore the floor's pack and fight the foes below.")]
        public bool Override;

        public FoeEntry[] Pack = { FoeEntry.Guard() };

        /// <summary>The pack to fight: the floor's, or the one somebody typed.</summary>
        public List<EnemyState> Build(int floor, Mulberry32 rng, DungeonConfig dungeon)
        {
            if (!Override) return EnemyPackGenerator.Build(floor, rng, dungeon);

            var pack = new List<EnemyState>();

            if (Pack != null)
            {
                foreach (FoeEntry entry in Pack) pack.Add(entry.ToFoe());
            }

            // A floor with nobody on it is not a fight, and an engine handed an empty pack has
            // nothing to resolve. Better to say so than to show an empty room.
            if (pack.Count == 0)
            {
                Debug.LogWarning("no foes are authored, so the floor's own pack is used instead");

                return EnemyPackGenerator.Build(floor, rng, dungeon);
            }

            return pack;
        }
    }
}
