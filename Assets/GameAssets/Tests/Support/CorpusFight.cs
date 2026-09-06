using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Stats;

namespace RelicRun.Tests.Support
{
    /// <summary>
    /// Reads recorded fight cases and diffs replayed event streams against them.
    /// </summary>
    /// <remarks>
    /// Corpus mode: the recording was made with <c>itemName(id) => id</c> and
    /// <c>t(key) => key</c>, so <c>src</c> holds relic ids and string keys rather than display
    /// names. That keeps the comparison locale-independent.
    ///
    /// Two shape details of the source are normalized away rather than reproduced, because
    /// neither carries meaning: a missing <c>rid</c> and a null one both mean "no relic caused
    /// this", and a missing optional field and a null one are likewise the same thing.
    /// Everything else is compared exactly, including tick numbers and the full snapshot.
    /// </remarks>
    public static class CorpusFight
    {
        public sealed class Case
        {
            public string Id;
            public int Floor;
            public int Dungeon;
            public uint FightSeed;
            public HeroState Hero;
            public List<EnemyState> Pack;
            public JArray Events;
            public JObject Final;
            public JObject Carry;
        }

        public static List<Case> Load(string fileName)
        {
            var cases = new List<Case>();
            foreach (JToken token in Corpus.Array(fileName))
            {
                JObject hero = (JObject)token["input"]["hero"];
                cases.Add(new Case
                {
                    Id = token["id"].Value<string>(),
                    Floor = token["floor"].Value<int>(),
                    Dungeon = token["dungeon"].Value<int>(),
                    FightSeed = token["fightSeed"].Value<uint>(),
                    Hero = ReadHero(hero),
                    Pack = ReadPack((JArray)token["input"]["pack"]),
                    Events = (JArray)token["events"],
                    Final = (JObject)token["final"],
                    Carry = (JObject)token["carry"],
                });
            }

            return cases;
        }

        private static HeroState ReadHero(JObject h)
        {
            var items = new List<RelicId>();
            foreach (JToken t in (JArray)h["items"])
            {
                if (RelicCatalog.TryParse(t.Value<string>(), out RelicId id))
                {
                    items.Add(id);
                }
            }

            var awakened = new List<RelicId>();
            foreach (JProperty p in ((JObject)h["awake"]).Properties())
            {
                if (p.Value.Value<int>() > 0 && RelicCatalog.TryParse(p.Name, out RelicId id))
                {
                    awakened.Add(id);
                }
            }

            // Sockets are keyed by INVENTORY INDEX, not by relic, because a socket belongs to
            // one copy. The source keeps triggers and emitters in a single map; here they are
            // split by prefix, and a slot can hold at most one of them.
            var triggers = new Dictionary<int, SocketTrigger>();
            var emitters = new Dictionary<int, SocketEmitter>();
            if (h["sockets"] != null && h["sockets"].Type != JTokenType.Null)
            {
                foreach (JProperty p in ((JObject)h["sockets"]).Properties())
                {
                    int slot = int.Parse(p.Name);
                    string component = p.Value.Value<string>();
                    if (component == null) continue;

                    if (component.StartsWith("t_"))
                    {
                        triggers[slot] = ParseTrigger(component);
                    }
                    else if (component.StartsWith("e_"))
                    {
                        emitters[slot] = ParseEmitter(component);
                    }
                }
            }

            return new HeroState
            {
                Items = items,
                Awakened = awakened,
                SocketTriggers = triggers,
                SocketEmitters = emitters,
                IsVersus = h["mode"].Value<string>() == "versus",
                Floor = h["floor"].Value<int>(),
                Php = h["php"].Value<int>(),
                Pmax = h["pmax"].Value<int>(),
                Gold = h["gold"].Value<int>(),
                Kills = h["kills"].Value<int>(),
                Adrenaline = h["adrenaline"].Value<int>(),
                MidasBonus = h["midasBonus"].Value<int>(),
                AtkBonus = h["atkB"].Value<int>(),
                DefBonus = h["defB"].Value<int>(),
                SpdBonus = h["spdB"].Value<int>(),
                LuckBonus = h["luckB"].Value<int>(),
                BaseAtk = h["baseAtk"].Value<int>(),
                BaseDef = h["baseDef"].Value<int>(),
                BaseSpd = h["baseSpd"].Value<int>(),
                BaseLck = h["baseLck"].Value<int>(),
                StrikeTotal = h["_strikeTot"].Value<int>(),
                AnvilBonus = h["_anvilB"].Value<int>(),
                ChaliceGain = h["_chaliceG"].Value<int>(),
                DebtLeft = h["_debtLeft"].Value<int>(),
                GlassBroken = h["_glassBroken"].Value<bool>(),
                SoilUsed = h["_soilUsed"].Value<bool>(),
            };
        }

        internal static SocketTrigger ParseTrigger(string key)
        {
            switch (key)
            {
                case "t_attack": return SocketTrigger.Attack;
                case "t_hit": return SocketTrigger.Hit;
                case "t_gold": return SocketTrigger.Gold;
                case "t_kill": return SocketTrigger.Kill;
                case "t_dodge": return SocketTrigger.Dodge;
                case "t_luck": return SocketTrigger.Luck;
                case "t_floor": return SocketTrigger.Floor;
                case "t_fight": return SocketTrigger.Fight;
                default: throw new System.ArgumentException("unknown trigger " + key);
            }
        }

        internal static SocketEmitter ParseEmitter(string key)
        {
            switch (key)
            {
                case "e_dmg": return SocketEmitter.Dmg;
                case "e_heal": return SocketEmitter.Heal;
                case "e_gold": return SocketEmitter.Gold;
                case "e_atk": return SocketEmitter.Atk;
                case "e_def": return SocketEmitter.Def;
                case "e_spd": return SocketEmitter.Spd;
                case "e_luck": return SocketEmitter.Luck;
                default: throw new System.ArgumentException("unknown emitter " + key);
            }
        }

        private static List<EnemyState> ReadPack(JArray pack)
        {
            var foes = new List<EnemyState>();
            foreach (JToken t in pack)
            {
                JObject e = (JObject)t;
                int hp = e["hp"].Value<int>();

                List<RelicId> relics = null;
                if (e["relics"] != null && e["relics"].Type != JTokenType.Null)
                {
                    relics = new List<RelicId>();
                    foreach (JToken r in (JArray)e["relics"])
                    {
                        // Boss kits may name relics this engine never reads; keep them so the
                        // relic list still reports, and so counts stay honest.
                        if (RelicCatalog.TryParse(r.Value<string>(), out RelicId id))
                        {
                            relics.Add(id);
                        }
                    }
                }

                foes.Add(new EnemyState
                {
                    SpeciesIndex = e["idx"].Value<int>(),
                    Hp = hp,
                    MaxHp = hp,
                    Atk = e["atk"].Value<int>(),
                    Armor = e["armor"] != null ? e["armor"].Value<int>() : 0,
                    Spd = e["spd"] != null ? e["spd"].Value<int>() : 25,
                    Lck = e["lck"] != null ? e["lck"].Value<int>() : 10,
                    Drop = e["drop"].Value<int>(),
                    Rank = ParseRank(e["rank"]),
                    Variant = e["variant"] != null ? e["variant"].Value<int>() : 0,
                    Relics = relics,
                });
            }

            return foes;
        }

        private static EnemyRank ParseRank(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return EnemyRank.Guard;
            switch (token.Value<string>())
            {
                case "elite": return EnemyRank.Elite;
                case "boss": return EnemyRank.Boss;
                case "king": return EnemyRank.King;
                default: return EnemyRank.Guard;
            }
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

        private static readonly Dictionary<CombatEventType, string> TypeKeys =
            new Dictionary<CombatEventType, string>
            {
                { CombatEventType.Enter, "enter" },
                { CombatEventType.PlayerDamage, "pdmg" },
                { CombatEventType.EnemyDamage, "edmg" },
                { CombatEventType.Heal, "heal" },
                { CombatEventType.HealFull, "healfull" },
                { CombatEventType.EnemyHeal, "eheal" },
                { CombatEventType.Gold, "gold" },
                { CombatEventType.Luck, "luck" },
                { CombatEventType.Miss, "miss" },
                { CombatEventType.EnemyMiss, "emiss" },
                { CombatEventType.Fizzle, "fizzle" },
                { CombatEventType.Adrenaline, "adren" },
                { CombatEventType.Momentum, "mom" },
                { CombatEventType.First, "first" },
                { CombatEventType.EnemyFury, "efury" },
                { CombatEventType.EnemySlow, "eslow" },
                { CombatEventType.DashOpen, "dashopen" },
                { CombatEventType.EnemyDashOpen, "edashopen" },
                { CombatEventType.Kill, "kill" },
                { CombatEventType.Death, "death" },
            };

        /// <summary>
        /// Compares one replayed event against its recording, returning null when they agree or
        /// a description of the first field that differs.
        /// </summary>
        public static string Diff(JObject want, CombatEvent got, int index)
        {
            string prefix = "event " + index + " (" + want["t"] + ") ";

            string gotType = TypeKeys[got.Type];
            if (want["t"].Value<string>() != gotType)
            {
                return prefix + "type: recorded " + want["t"] + ", replayed " + gotType;
            }

            string mismatch =
                Int(prefix, want, "depth", got.Depth) ??
                Int(prefix, want, "tk", got.State.Tick) ??
                Int(prefix, want, "php", got.State.HeroHp) ??
                Int(prefix, want, "ehp", got.State.EnemyHp) ??
                Int(prefix, want, "gold", got.State.Gold) ??
                Int(prefix, want, "emax", got.State.EnemyMaxHp) ??
                Int(prefix, want, "eatk", got.State.EnemyAtk) ??
                Int(prefix, want, "earm", got.State.EnemyArmor) ??
                Int(prefix, want, "espd", got.State.EnemySpd) ??
                Int(prefix, want, "elck", got.State.EnemyLck) ??
                Int(prefix, want, "evar", got.State.EnemyVariant) ??
                Int(prefix, want, "eidx", got.State.EnemyIndex) ??
                Int(prefix, want, "padr", got.State.HeroAdrenaline) ??
                Int(prefix, want, "pfury", got.State.HeroFury) ??
                OptionalInt(prefix, want, "amt", got.Amount) ??
                OptionalString(prefix, want, "src", got.Source) ??
                Rank(prefix, want, got) ??
                Counters(prefix, want, got) ??
                Relics(prefix, want, got) ??
                Mods(prefix, want, got);

            if (mismatch != null) return mismatch;

            if (want["ecrit"] != null)
            {
                bool wantCrit = want["ecrit"].Value<int>() != 0;
                bool gotCrit = got.EnemyCrit.GetValueOrDefault();
                if (wantCrit != gotCrit) return prefix + "ecrit: recorded " + wantCrit + ", replayed " + gotCrit;
            }

            if (want["another"] != null)
            {
                bool wantAnother = want["another"].Value<bool>();
                bool gotAnother = got.Another.GetValueOrDefault();
                if (wantAnother != gotAnother) return prefix + "another: recorded " + wantAnother + ", replayed " + gotAnother;
            }

            // Relic provenance: absent and null both mean "no relic caused this".
            string wantRid = want["rid"] != null && want["rid"].Type != JTokenType.Null
                ? want["rid"].Value<string>() : null;
            string gotRid = got.Relic != RelicId.None ? RelicCatalog.KeyOf(got.Relic) : null;
            if (wantRid != gotRid)
            {
                return prefix + "rid: recorded " + (wantRid ?? "none") + ", replayed " + (gotRid ?? "none");
            }

            return null;
        }

        private static string Int(string prefix, JObject want, string key, int got)
        {
            if (want[key] == null) return null;
            int expected = want[key].Value<int>();
            return expected == got ? null : prefix + key + ": recorded " + expected + ", replayed " + got;
        }

        private static string OptionalInt(string prefix, JObject want, string key, int? got)
        {
            bool present = want[key] != null && want[key].Type != JTokenType.Null;
            if (!present && !got.HasValue) return null;
            if (!present) return prefix + key + ": recorded absent, replayed " + got.Value;
            if (!got.HasValue) return prefix + key + ": recorded " + want[key] + ", replayed absent";
            return want[key].Value<int>() == got.Value ? null
                : prefix + key + ": recorded " + want[key] + ", replayed " + got.Value;
        }

        private static string OptionalString(string prefix, JObject want, string key, string got)
        {
            string expected = want[key] != null && want[key].Type != JTokenType.Null
                ? want[key].Value<string>() : null;
            return expected == got ? null
                : prefix + key + ": recorded " + (expected ?? "absent") + ", replayed " + (got ?? "absent");
        }

        private static string Rank(string prefix, JObject want, CombatEvent got)
        {
            if (want["erank"] == null) return null;
            string expected = want["erank"].Value<string>();
            string actual = RankKey(got.State.EnemyRank);
            return expected == actual ? null : prefix + "erank: recorded " + expected + ", replayed " + actual;
        }

        private static string Counters(string prefix, JObject want, CombatEvent got)
        {
            JObject c = (JObject)want["pcnt"];
            if (c == null) return null;
            CombatCounters n = got.State.Counters;

            return Int(prefix + "pcnt.", c, "a", n.Strikes) ??
                   Int(prefix + "pcnt.", c, "h", n.Pain) ??
                   Int(prefix + "pcnt.", c, "g", n.Gold) ??
                   Int(prefix + "pcnt.", c, "st", n.StoneCount) ??
                   Int(prefix + "pcnt.", c, "f", n.FightStrikes) ??
                   Int(prefix + "pcnt.", c, "q", n.QuenchCount) ??
                   Int(prefix + "pcnt.", c, "m", n.MomentumCount) ??
                   Int(prefix + "pcnt.", c, "r", n.RabbitCount) ??
                   Int(prefix + "pcnt.", c, "at", n.StrikeTotal) ??
                   Int(prefix + "pcnt.", c, "sb", n.SentinelBonus);
        }

        private static string Relics(string prefix, JObject want, CombatEvent got)
        {
            bool wantPresent = want["erel"] != null && want["erel"].Type != JTokenType.Null;
            bool gotPresent = got.State.EnemyRelics != null;

            if (wantPresent != gotPresent)
            {
                return prefix + "erel: recorded " + (wantPresent ? "present" : "absent") +
                       ", replayed " + (gotPresent ? "present" : "absent");
            }

            if (!wantPresent) return null;

            JArray expected = (JArray)want["erel"];
            if (expected.Count != got.State.EnemyRelics.Count)
            {
                return prefix + "erel: recorded " + expected.Count + " relics, replayed " +
                       got.State.EnemyRelics.Count;
            }

            return null;
        }

        private static string Mods(string prefix, JObject want, CombatEvent got)
        {
            JArray expected = (JArray)want["pl"];
            if (expected == null) return null;
            int actual = got.State.HeroMods != null ? got.State.HeroMods.Count : 0;
            if (expected.Count != actual)
            {
                return prefix + "pl: recorded " + expected.Count + " modifiers (" + expected +
                       "), replayed " + actual;
            }

            for (int i = 0; i < expected.Count; i++)
            {
                StatModifier m = got.State.HeroMods[i];
                if (expected[i]["src"].Value<string>() != m.Source)
                {
                    return prefix + "pl[" + i + "].src: recorded " + expected[i]["src"] + ", replayed " + m.Source;
                }

                if (expected[i]["amt"].Value<int>() != m.Amount)
                {
                    return prefix + "pl[" + i + "].amt: recorded " + expected[i]["amt"] + ", replayed " + m.Amount;
                }
            }

            return null;
        }

        /// <summary>Replays a case and returns the first divergence, or null if it matches.</summary>
        public static string Replay(Case c)
        {
            var engine = new CombatEngine();
            CombatResult result = engine.ResolveFloor(c.Hero, c.Pack, new Mulberry32(c.FightSeed));

            int n = System.Math.Min(c.Events.Count, result.Events.Count);
            for (int i = 0; i < n; i++)
            {
                string diff = Diff((JObject)c.Events[i], result.Events[i], i);
                if (diff != null) return c.Id + ": " + diff;
            }

            if (c.Events.Count != result.Events.Count)
            {
                return c.Id + ": recorded " + c.Events.Count + " events, replayed " + result.Events.Count;
            }

            string finalDiff =
                Int(c.Id + " final ", c.Final, "php", c.Hero.Php) ??
                Int(c.Id + " final ", c.Final, "gold", c.Hero.Gold) ??
                Int(c.Id + " final ", c.Final, "kills", c.Hero.Kills) ??
                Int(c.Id + " final ", c.Final, "strikeTot", c.Hero.StrikeTotal);
            if (finalDiff != null) return finalDiff;

            return Int(c.Id + " carry ", c.Carry, "strikeN", result.Carry.StrikeCount) ??
                   Int(c.Id + " carry ", c.Carry, "painN", result.Carry.PainCount) ??
                   Int(c.Id + " carry ", c.Carry, "goldN", result.Carry.GoldCount);
        }
    }
}
