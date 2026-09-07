using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// Phase 6 gate: <c>Tools/corpus/duel.json</c>.
    /// </summary>
    /// <remarks>
    /// The recorded duels replay under <see cref="CombatRules.DuelAsRecorded"/> rather than the
    /// shipped rules, because seven rules have deliberately been settled on the delve's answer
    /// and versus no longer matches the source. That gate still proves the ENGINE is exact.
    /// <see cref="EveryAdoptedRuleChangesADuel"/> is what proves the seven are actually in
    /// force in the game, since nothing else would notice if one quietly reverted.
    /// </remarks>
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

                CombatResult got = new DuelEngine(CombatRules.DuelAsRecorded())
                    .Resolve(hero, rival, new Mulberry32(seed));

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

        /// <summary>Every rule versus adopted from the delve, and how to put it back.</summary>
        private static readonly (string Name, System.Action<CombatRules> Revert)[] Adopted =
        {
            ("ArmorMeetsRelicDamage", r => r.ArmorMeetsRelicDamage = false),
            ("StaggerTripsOnBeingHit", r => r.StaggerTripsOnBeingHit = true),
            ("WhetstoneSundersOnlyPlainStrikes", r => r.WhetstoneSundersOnlyPlainStrikes = false),
            ("ReturnedBlowUsesTheStrikersAttack", r => r.ReturnedBlowUsesTheStrikersAttack = false),
            ("ReturnedBlowDepth", r => r.ReturnedBlowDepth = 1),
            ("IronSkinGlancesOneBlow", r => r.IronSkinGlancesOneBlow = false),
            ("RiposteStrikesWhenAwakened", r => r.RiposteStrikesWhenAwakened = false),
        };

        /// <summary>
        /// Each of the seven rules versus took from the delve has to actually change a duel.
        /// </summary>
        /// <remarks>
        /// The recorded corpus replays under <see cref="CombatRules.DuelAsRecorded"/>, so it
        /// says nothing about the shipped rules — put one of the seven back and every gate
        /// would still be green. This is what notices. Reverting a rule on its own must make
        /// at least one recorded duel play differently; one that can be put back with no
        /// visible effect is dead or unreachable, and adopting it was not a real change.
        /// </remarks>
        [Test]
        public void EveryAdoptedRuleChangesADuel()
        {
            JArray cases = Corpus.Array("duel.json");
            Assert.That(cases.Count, Is.GreaterThan(0), "duel corpus is empty");

            var inert = new List<string>();
            foreach ((string name, System.Action<CombatRules> revert) in Adopted)
            {
                CombatRules reverted = CombatRules.Duel();
                revert(reverted);
                if (!AnyDuelDiffers(cases, reverted)) inert.Add(name);
            }

            Assert.That(inert, Is.Empty,
                "these rules can be put back with no effect on any recorded duel, so versus " +
                "adopting the delve's answer did nothing: " + string.Join(", ", inert));
        }

        /// <summary>
        /// A duel has to end in a death, not by running out of the scheduler's patience.
        /// </summary>
        /// <remarks>
        /// Versus adopting the delve's armor rule means relic damage is now reduced too, and
        /// the percentage curve floors at 1. A chip-damage build against a heavily armored
        /// rival is the shape that could stall, so this asserts it does not.
        /// </remarks>
        [Test]
        public void EveryShippedDuelEndsInADeath()
        {
            JArray cases = Corpus.Array("duel.json");
            var stalled = new List<string>();

            foreach (JToken token in cases)
            {
                DuelSide hero = ReadSide((JObject)token["input"]["a"], true);
                DuelSide rival = ReadSide((JObject)token["input"]["b"], false);
                uint seed = token["fightSeed"].Value<uint>();

                new DuelEngine(CombatRules.Duel()).Resolve(hero, rival, new Mulberry32(seed));

                if (hero.Php > 0 && rival.Php > 0)
                {
                    stalled.Add(token["id"].Value<string>() +
                                " (you " + hero.Php + ", rival " + rival.Php + ")");
                }
            }

            Assert.That(stalled, Is.Empty,
                stalled.Count + " of " + cases.Count + " duels ran to the iteration cap with " +
                "both sides alive:\n  " +
                string.Join("\n  ", stalled.GetRange(0, System.Math.Min(8, stalled.Count))));
        }

        /// <summary>Whether any recorded duel plays differently under a second set of rules.</summary>
        private static bool AnyDuelDiffers(JArray cases, CombatRules reverted)
        {
            foreach (JToken token in cases)
            {
                uint seed = token["fightSeed"].Value<uint>();
                var input = (JObject)token["input"];

                CombatResult shipped = new DuelEngine(CombatRules.Duel()).Resolve(
                    ReadSide((JObject)input["a"], true),
                    ReadSide((JObject)input["b"], false),
                    new Mulberry32(seed));

                CombatResult other = new DuelEngine(reverted).Resolve(
                    ReadSide((JObject)input["a"], true),
                    ReadSide((JObject)input["b"], false),
                    new Mulberry32(seed));

                if (shipped.Events.Count != other.Events.Count) return true;

                for (int i = 0; i < shipped.Events.Count; i++)
                {
                    if (CorpusFight.Differs(shipped.Events[i], other.Events[i])) return true;
                }
            }

            return false;
        }
    }
}
