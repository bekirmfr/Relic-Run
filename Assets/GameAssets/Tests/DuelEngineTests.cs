using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>Phase 6 gate: <c>Tools/corpus/duel.json</c>.</summary>
    [TestFixture]
    public class DuelEngineTests
    {
        private static DuelSide ReadSide(JObject s, bool isHero)
        {
            var items = new List<RelicId>();
            foreach (JToken t in (JArray)s["items"])
            {
                if (RelicCatalog.TryParse(t.Value<string>(), out RelicId id)) items.Add(id);
            }

            var awakened = new HashSet<RelicId>();
            foreach (JProperty p in ((JObject)s["awake"]).Properties())
            {
                if (p.Value.Value<int>() > 0 && RelicCatalog.TryParse(p.Name, out RelicId id))
                {
                    awakened.Add(id);
                }
            }

            var triggers = new Dictionary<int, SocketTrigger>();
            var emitters = new Dictionary<int, SocketEmitter>();
            if (s["sockets"] != null && s["sockets"].Type != JTokenType.Null)
            {
                foreach (JProperty p in ((JObject)s["sockets"]).Properties())
                {
                    int slot = int.Parse(p.Name);
                    string component = p.Value.Value<string>();
                    if (component == null) continue;
                    if (component.StartsWith("t_")) triggers[slot] = CorpusFight.ParseTrigger(component);
                    else if (component.StartsWith("e_")) emitters[slot] = CorpusFight.ParseEmitter(component);
                }
            }

            JObject b = (JObject)s["base"];

            return new DuelSide
            {
                Name = s["name"].Value<string>(),
                IsHero = isHero,
                Items = items,
                Awakened = awakened,
                SocketTriggers = triggers,
                SocketEmitters = emitters,
                BaseAtk = b["atk"].Value<int>(),
                BaseDef = b["def"].Value<int>(),
                BaseSpd = b["spd"].Value<int>(),
                BaseLck = b["lck"].Value<int>(),
                Php = s["php"].Value<int>(),
                Pmax = s["pmax"].Value<int>(),
                Gold = s["gold"].Value<int>(),
                Kills = s["kills"].Value<int>(),
                Drop = s["drop"] != null ? s["drop"].Value<int>() : 0,
                Adrenaline = s["adrenaline"].Value<int>(),
                MidasBonus = s["midasBonus"].Value<int>(),
                AtkBonus = s["atkB"].Value<int>(),
                DefBonus = s["defB"].Value<int>(),
                SpdBonus = s["spdB"].Value<int>(),
                LuckBonus = s["luckB"].Value<int>(),
                SpeciesIndex = s["idx"] != null ? s["idx"].Value<int>() : 0,
                Variant = s["variant"] != null ? s["variant"].Value<int>() : 0,
                AnvilBonus = s["_anvilB"].Value<int>(),
                DebtLeft = s["_debtLeft"].Value<int>(),
                GlassBroken = s["_glassBroken"].Value<bool>(),
                SoilUsed = s["_soilUsed"].Value<bool>(),
                FleshSetApplied = s["_flesh3"] != null && s["_flesh3"].Value<bool>(),
                StrikeTotal = s["_strikeTot"].Value<int>(),
            };
        }

        [Test]
        public void RecordedDuelsReplayExactly()
        {
            JArray cases = Corpus.Array("duel.json");
            Assert.That(cases.Count, Is.GreaterThan(0), "duel corpus is empty");

            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                string id = token["id"].Value<string>();
                DuelSide hero = ReadSide((JObject)token["input"]["a"], true);
                DuelSide rival = ReadSide((JObject)token["input"]["b"], false);
                uint seed = token["fightSeed"].Value<uint>();
                JArray want = (JArray)token["events"];

                CombatResult got = new DuelEngine().Resolve(hero, rival, new Mulberry32(seed));

                int n = System.Math.Min(want.Count, got.Events.Count);
                string diff = null;
                for (int i = 0; i < n && diff == null; i++)
                {
                    diff = CorpusFight.Diff((JObject)want[i], got.Events[i], i);
                }

                if (diff == null && want.Count != got.Events.Count)
                {
                    diff = "recorded " + want.Count + " events, replayed " + got.Events.Count;
                }

                if (diff != null) failures.Add(id + ": " + diff);
            }

            Assert.That(failures, Is.Empty,
                failures.Count + " of " + cases.Count + " duels diverge:\n  " +
                string.Join("\n  ", failures.GetRange(0, System.Math.Min(8, failures.Count))));
        }
    }
}
