using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Stats;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>Phase 1 gate: <c>Tools/corpus/statledger.json</c>.</summary>
    [TestFixture]
    public class StatLedgerTests
    {
        private static readonly Stat[] AllStats = { Stat.Atk, Stat.Def, Stat.Spd, Stat.Lck };

        private static Stat ParseStat(string s)
        {
            switch (s)
            {
                case "atk": return Stat.Atk;
                case "def": return Stat.Def;
                case "spd": return Stat.Spd;
                case "lck": return Stat.Lck;
                default: throw new AssertionException("unknown stat " + s);
            }
        }

        private static string StatKey(Stat s)
        {
            switch (s)
            {
                case Stat.Atk: return "atk";
                case Stat.Def: return "def";
                case Stat.Spd: return "spd";
                default: return "lck";
            }
        }

        private static EnemyRank ParseRank(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return EnemyRank.None;
            switch (token.Value<string>())
            {
                case "guard": return EnemyRank.Guard;
                case "elite": return EnemyRank.Elite;
                case "boss": return EnemyRank.Boss;
                case "king": return EnemyRank.King;
                default: return EnemyRank.None;
            }
        }

        private static StatContext BuildContext(JObject c)
        {
            var items = new List<RelicId>();
            foreach (JToken t in (JArray)c["items"])
            {
                Assert.That(RelicCatalog.TryParse(t.Value<string>(), out RelicId id), Is.True,
                    "corpus references a relic that is not in the catalog: " + t.Value<string>());
                items.Add(id);
            }

            var awakened = new List<RelicId>();
            foreach (JProperty p in ((JObject)c["awake"]).Properties())
            {
                if (p.Value.Value<int>() > 0 && RelicCatalog.TryParse(p.Name, out RelicId id))
                {
                    awakened.Add(id);
                }
            }

            var mods = new List<StatModifier>();
            foreach (JToken m in (JArray)c["mods"])
            {
                mods.Add(new StatModifier(ParseStat(m["stat"].Value<string>()),
                    m["src"].Value<string>(), m["amt"].Value<int>()));
            }

            JObject b = (JObject)c["base"];
            JObject run = (JObject)c["run"];

            return new StatContext
            {
                Items = items,
                Awakened = awakened,
                IsVersus = c["mode"].Value<string>() == "versus",
                BaseAtk = b["atk"].Value<int>(),
                BaseDef = b["def"].Value<int>(),
                BaseSpd = b["spd"].Value<int>(),
                BaseLck = b["lck"].Value<int>(),
                Adrenaline = run["adrenaline"].Value<int>(),
                MidasBonus = run["midasBonus"].Value<int>(),
                AtkBonus = run["atkB"].Value<int>(),
                DefBonus = run["defB"].Value<int>(),
                SpdBonus = run["spdB"].Value<int>(),
                LuckBonus = run["luckB"].Value<int>(),
                Php = c["php"].Value<int>(),
                Pmax = c["pmax"].Value<int>(),
                Gold = c["gold"].Value<int>(),
                Floor = c["floor"].Value<int>(),
                FoeRank = ParseRank(c["foeRank"]),
                Mods = mods,
            };
        }

        /// <summary>
        /// Rows must match label for label and value for value, in order. Labels are part of the
        /// contract: they are what the stat breakdown card shows the player, so a row credited to
        /// the wrong relic is a real bug even when the total happens to come out right.
        /// </summary>
        [Test]
        public void RowsMatchTheRecordedLedger()
        {
            JArray cases = Corpus.Array("statledger.json");
            Assert.That(cases.Count, Is.GreaterThan(0), "stat ledger corpus is empty");

            foreach (JToken token in cases)
            {
                string id = token["id"].Value<string>();
                StatContext ctx = BuildContext((JObject)token["ctx"]);
                JObject expectedRows = (JObject)token["rows"];

                foreach (Stat stat in AllStats)
                {
                    JArray want = (JArray)expectedRows[StatKey(stat)];
                    List<StatModifier> got = StatLedger.Rows(ctx, stat);

                    Assert.That(got.Count, Is.EqualTo(want.Count),
                        id + " " + StatKey(stat) + ": row count\nrecorded: " + want +
                        "\nreplayed: " + string.Join(", ", got));

                    for (int i = 0; i < want.Count; i++)
                    {
                        Assert.That(got[i].Source, Is.EqualTo(want[i]["src"].Value<string>()),
                            id + " " + StatKey(stat) + " row " + i + ": source");
                        Assert.That(got[i].Amount, Is.EqualTo(want[i]["amt"].Value<int>()),
                            id + " " + StatKey(stat) + " row " + i + " (" + got[i].Source + "): amount");
                    }
                }
            }
        }

        [Test]
        public void TotalsMatchTheRecordedLedger()
        {
            JArray cases = Corpus.Array("statledger.json");

            foreach (JToken token in cases)
            {
                string id = token["id"].Value<string>();
                StatContext ctx = BuildContext((JObject)token["ctx"]);
                JObject totals = (JObject)token["totals"];

                foreach (Stat stat in AllStats)
                {
                    Assert.That(StatLedger.Of(ctx, stat),
                        Is.EqualTo(totals[StatKey(stat)].Value<int>()),
                        id + " total " + StatKey(stat));
                }
            }
        }

        [Test]
        public void AttackNeverFallsBelowOne()
        {
            var ctx = new StatContext { BaseAtk = 0, Items = new List<RelicId> { RelicId.MillstonePendant } };
            Assert.That(StatLedger.Of(ctx, Stat.Atk), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void SpeedFloorDependsOnMode()
        {
            var items = new List<RelicId> { RelicId.MillstonePendant, RelicId.MillstonePendant };
            var delve = new StatContext { BaseSpd = 0, Items = items, IsVersus = false };
            var versus = new StatContext { BaseSpd = 0, Items = items, IsVersus = true };

            Assert.That(StatLedger.Of(delve, Stat.Spd), Is.EqualTo(15));
            Assert.That(StatLedger.Of(versus, Stat.Spd), Is.EqualTo(10));
        }

        /// <summary>Hollow Idol counts itself toward every set, which is why it is worth carrying.</summary>
        [Test]
        public void HollowIdolCountsTowardEverySet()
        {
            var without = new StatContext
            {
                Items = new List<RelicId> { RelicId.IronSkin, RelicId.MirrorScale },
            };
            var with = new StatContext
            {
                Items = new List<RelicId> { RelicId.IronSkin, RelicId.MirrorScale, RelicId.HollowIdol },
            };

            // Two GUARD relics is short of the tier; the idol makes the third.
            Assert.That(StatLedger.Rows(without, Stat.Def).Exists(r => r.Source == "Guard set (3)"), Is.False);
            Assert.That(StatLedger.Rows(with, Stat.Def).Exists(r => r.Source == "Guard set (3)"), Is.True);
        }

        /// <summary>Per-copy identity: a second copy must contribute, not be swallowed.</summary>
        [Test]
        public void DuplicateCopiesStack()
        {
            var one = new StatContext { Items = new List<RelicId> { RelicId.Whetstone } };
            var two = new StatContext { Items = new List<RelicId> { RelicId.Whetstone, RelicId.Whetstone } };

            Assert.That(StatLedger.Of(two, Stat.Atk) - StatLedger.Of(one, Stat.Atk), Is.EqualTo(2));
        }
    }

    [TestFixture]
    public class RelicCatalogTests
    {
        [Test]
        public void HoldsTheFiftyReachableRelics()
        {
            Assert.That(RelicCatalog.All.Count, Is.EqualTo(50));
            Assert.That(RelicCatalog.Count, Is.EqualTo(50));
        }

        [Test]
        public void KeysAreUniqueAndRoundTrip()
        {
            var seen = new HashSet<string>();
            foreach (RelicDef def in RelicCatalog.All)
            {
                Assert.That(seen.Add(def.Key), Is.True, "duplicate key " + def.Key);
                Assert.That(RelicCatalog.TryParse(def.Key, out RelicId id), Is.True);
                Assert.That(id, Is.EqualTo(def.Id));
                Assert.That(RelicCatalog.KeyOf(def.Id), Is.EqualTo(def.Key));
            }
        }

        [Test]
        public void IndicesMatchEnumValues()
        {
            for (int i = 0; i < RelicCatalog.All.Count; i++)
            {
                Assert.That((int)RelicCatalog.All[i].Id, Is.EqualTo(i));
            }
        }

        [Test]
        public void CutRelicsAreAbsent()
        {
            // A sample from docs/relics-cut.md. These have live code paths in the JS build but
            // no entry in its draft table, so nothing can ever offer them.
            foreach (string key in new[] { "titheshell", "fusecoil", "tinkerloop", "glassedge", "omen" })
            {
                Assert.That(RelicCatalog.TryParse(key, out _), Is.False, key + " should be cut");
            }
        }

        [Test]
        public void EveryRelicAppearsInAtLeastOneMode()
        {
            foreach (RelicDef def in RelicCatalog.All)
            {
                Assert.That(def.Modes, Is.Not.EqualTo(GameModes.None), def.Key);
            }

            Assert.That(RelicCatalog.PoolFor(GameModes.Delve).Count, Is.GreaterThan(0));
            Assert.That(RelicCatalog.PoolFor(GameModes.Versus).Count, Is.GreaterThan(0));
        }
    }
}
