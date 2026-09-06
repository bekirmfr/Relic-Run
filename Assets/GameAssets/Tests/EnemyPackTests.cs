using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Run;
using RelicRun.Core.Stats;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>Phase 5 gate: <c>Tools/corpus/packs.json</c>.</summary>
    [TestFixture]
    public class EnemyPackTests
    {
        private static DungeonConfig ConfigFor(int dungeon)
        {
            JArray dungeons = Corpus.ArrayFromContent("dungeons.json");
            JObject d = (JObject)dungeons[dungeon - 1];

            var bossRelics = new List<RelicId>();
            if (d["bossRelics"] != null)
            {
                foreach (JToken t in (JArray)d["bossRelics"])
                {
                    if (RelicCatalog.TryParse(t.Value<string>(), out RelicId id)) bossRelics.Add(id);
                }
            }

            return new DungeonConfig
            {
                // The corpus records packs BEFORE the dungeon multiplier, so combat and pack
                // generation stay independently falsifiable.
                Multiplier = 1.0,
                BossRelics = bossRelics,
                GhoolemBoss = d["ghoolemBoss"] != null && d["ghoolemBoss"].Value<bool>(),
            };
        }

        private static string RankKey(EnemyRank rank)
        {
            switch (rank)
            {
                case EnemyRank.Elite: return "elite";
                case EnemyRank.Boss: return "boss";
                case EnemyRank.King: return "king";
                default: return "guard";
            }
        }

        [Test]
        public void RecordedPacksRegenerateExactly()
        {
            JArray cases = Corpus.Array("packs.json");
            Assert.That(cases.Count, Is.GreaterThan(0), "pack corpus is empty");

            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                int dungeon = token["dungeon"].Value<int>();
                int floor = token["floor"].Value<int>();
                uint seed = token["seed"].Value<uint>();
                JArray want = (JArray)token["pack"];
                string id = "d" + dungeon + "/f" + floor + "/" + seed;

                List<EnemyState> got = EnemyPackGenerator.BuildUnscaled(
                    floor, new Mulberry32(seed), ConfigFor(dungeon));

                if (want.Count != got.Count)
                {
                    failures.Add(id + ": recorded " + want.Count + " foes, generated " + got.Count);
                    continue;
                }

                for (int i = 0; i < want.Count; i++)
                {
                    JObject w = (JObject)want[i];
                    EnemyState g = got[i];
                    string at = id + " foe " + i + " ";

                    if (w["idx"].Value<int>() != g.SpeciesIndex)
                    { failures.Add(at + "species: " + w["idx"] + " vs " + g.SpeciesIndex); break; }
                    if (w["hp"].Value<int>() != g.Hp)
                    { failures.Add(at + "hp: " + w["hp"] + " vs " + g.Hp); break; }
                    if (w["atk"].Value<int>() != g.Atk)
                    { failures.Add(at + "atk: " + w["atk"] + " vs " + g.Atk); break; }
                    if (w["armor"].Value<int>() != g.Armor)
                    { failures.Add(at + "armor: " + w["armor"] + " vs " + g.Armor); break; }
                    if (w["spd"].Value<int>() != g.Spd)
                    { failures.Add(at + "spd: " + w["spd"] + " vs " + g.Spd); break; }
                    if (w["lck"].Value<int>() != g.Lck)
                    { failures.Add(at + "lck: " + w["lck"] + " vs " + g.Lck); break; }
                    if (w["drop"].Value<int>() != g.Drop)
                    { failures.Add(at + "drop: " + w["drop"] + " vs " + g.Drop); break; }
                    if (w["rank"].Value<string>() != RankKey(g.Rank))
                    { failures.Add(at + "rank: " + w["rank"] + " vs " + RankKey(g.Rank)); break; }
                    if (w["variant"].Value<int>() != g.Variant)
                    { failures.Add(at + "variant: " + w["variant"] + " vs " + g.Variant); break; }

                    int wantRelics = w["relics"] != null && w["relics"].Type != JTokenType.Null
                        ? ((JArray)w["relics"]).Count : -1;
                    int gotRelics = g.Relics != null ? g.Relics.Count : -1;
                    if (wantRelics != gotRelics)
                    { failures.Add(at + "relics: " + wantRelics + " vs " + gotRelics); break; }
                }
            }

            Assert.That(failures, Is.Empty,
                failures.Count + " of " + cases.Count + " packs diverge:\n  " +
                string.Join("\n  ", failures.GetRange(0, System.Math.Min(8, failures.Count))));
        }

        /// <summary>Combat floors in order. Floor 7 is the bazaar and is never fought.</summary>
        private static readonly int[] CombatFloors = { 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13 };

        /// <summary>The floor <paramref name="steps"/> fights deeper, stepping over the bazaar.</summary>
        private static int Deeper(int floor, int steps)
        {
            int at = System.Array.IndexOf(CombatFloors, floor);
            return CombatFloors[at + steps];
        }

        /// <summary>
        /// The demotion ladder is the point of the whole generator: what nearly killed you as a
        /// boss must come back as an elite, then as trash.
        /// </summary>
        [Test]
        public void EachFloorsBossReturnsAsAnEliteAndThenAGuard()
        {
            var cfg = new DungeonConfig();
            for (int floor = 4; floor <= 6; floor++)
            {
                int nextFloor = Deeper(floor, 1);
                int twoDeeper = Deeper(floor, 2);
                int boss = EnemyPackGenerator.BuildUnscaled(floor, new Mulberry32(1), cfg)[^1].SpeciesIndex;

                var laterElites = new HashSet<int>();
                var laterGuards = new HashSet<int>();
                for (uint seed = 1; seed <= 40; seed++)
                {
                    foreach (EnemyState foe in EnemyPackGenerator.BuildUnscaled(nextFloor, new Mulberry32(seed), cfg))
                    {
                        if (foe.Rank == EnemyRank.Elite) laterElites.Add(foe.SpeciesIndex);
                    }

                    foreach (EnemyState foe in EnemyPackGenerator.BuildUnscaled(twoDeeper, new Mulberry32(seed), cfg))
                    {
                        if (foe.Rank == EnemyRank.Guard) laterGuards.Add(foe.SpeciesIndex);
                    }
                }

                // Floors 8 and 9 field no elites, so only assert where the rank exists.
                if (laterElites.Count > 0)
                {
                    Assert.That(laterElites, Contains.Item(boss),
                        "floor " + floor + " boss should return as an elite on floor " + nextFloor);
                }

                Assert.That(laterGuards, Contains.Item(boss),
                    "floor " + floor + " boss should return as a guard on floor " + twoDeeper);
            }
        }

        [Test]
        public void APackNeverFieldsTheSameSpeciesTwice()
        {
            var cfg = new DungeonConfig();
            for (uint seed = 1; seed <= 200; seed++)
            {
                foreach (int floor in new[] { 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13 })
                {
                    var seen = new HashSet<int>();
                    foreach (EnemyState foe in EnemyPackGenerator.BuildUnscaled(floor, new Mulberry32(seed), cfg))
                    {
                        Assert.That(seen.Add(foe.SpeciesIndex), Is.True,
                            "floor " + floor + " seed " + seed + " repeats species " + foe.SpeciesIndex);
                    }
                }
            }
        }

        [Test]
        public void TheKingOnlyAppearsOnTheFinalFloor()
        {
            var cfg = new DungeonConfig();
            for (uint seed = 1; seed <= 50; seed++)
            {
                for (int floor = 1; floor <= 12; floor++)
                {
                    if (floor == EnemyPackGenerator.BazaarFloor) continue;
                    foreach (EnemyState foe in EnemyPackGenerator.BuildUnscaled(floor, new Mulberry32(seed), cfg))
                    {
                        Assert.That(foe.SpeciesIndex, Is.Not.EqualTo(EnemyPackGenerator.KingSpecies));
                        Assert.That(foe.Rank, Is.Not.EqualTo(EnemyRank.King));
                    }
                }
            }
        }

        [Test]
        public void TheDungeonMultiplierScalesHealthAttackAndLoot()
        {
            var plain = new DungeonConfig { Multiplier = 1.0 };
            var deep = new DungeonConfig { Multiplier = 2.0 };

            List<EnemyState> a = EnemyPackGenerator.Build(5, new Mulberry32(99), plain);
            List<EnemyState> b = EnemyPackGenerator.Build(5, new Mulberry32(99), deep);

            Assert.That(b.Count, Is.EqualTo(a.Count));
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(b[i].Hp, Is.EqualTo(a[i].Hp * 2));
                Assert.That(b[i].Atk, Is.EqualTo(a[i].Atk * 2));
                Assert.That(b[i].Drop, Is.EqualTo(a[i].Drop * 2));

                // Defence and speed are deliberately untouched: depth makes foes fatter and
                // richer, not harder to hit.
                Assert.That(b[i].Armor, Is.EqualTo(a[i].Armor));
                Assert.That(b[i].Spd, Is.EqualTo(a[i].Spd));
            }
        }
    }
}
