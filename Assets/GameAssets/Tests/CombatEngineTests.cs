using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>Phase 2 gate: <c>Tools/corpus/bare.json</c>.</summary>
    [TestFixture]
    public class CombatEngineTests
    {
        /// <summary>
        /// Every relic-less fight must replay event for event, including tick numbers and the
        /// full snapshot on each event. Failures name the first diverging field, since a single
        /// mismatched draw cascades into everything after it.
        /// </summary>
        [Test]
        public void BareFightsReplayExactly()
        {
            List<CorpusFight.Case> cases = CorpusFight.Load("bare.json");
            Assert.That(cases.Count, Is.GreaterThan(0), "bare corpus is empty");

            var failures = new List<string>();
            foreach (CorpusFight.Case c in cases)
            {
                string diff = CorpusFight.Replay(c);
                if (diff != null) failures.Add(diff);
            }

            Assert.That(failures, Is.Empty,
                failures.Count + " of " + cases.Count + " bare fights diverge:\n  " +
                string.Join("\n  ", failures.GetRange(0, System.Math.Min(8, failures.Count))));
        }

        /// <summary>
        /// Phase 3 gate. Chain primitives only - Alchemist's Vial, Blood Altar, Thorn Vest,
        /// Vampire Tooth, Coin Magnet and Cutpurse's Hook - so a failure is in the bus itself
        /// rather than in some relic's own rules.
        /// </summary>
        [Test]
        public void PrimitiveChainFightsReplayExactly()
        {
            List<CorpusFight.Case> cases = CorpusFight.Load("primitives.json");
            Assert.That(cases.Count, Is.GreaterThan(0), "primitives corpus is empty");

            var failures = new List<string>();
            foreach (CorpusFight.Case c in cases)
            {
                string diff = CorpusFight.Replay(c);
                if (diff != null) failures.Add(diff);
            }

            Assert.That(failures, Is.Empty,
                failures.Count + " of " + cases.Count + " primitive fights diverge:\n  " +
                string.Join("\n  ", failures.GetRange(0, System.Math.Min(8, failures.Count))));
        }

        /// <summary>
        /// Guards the gate itself: these fights must actually build chains, waste heals on a
        /// full pool, and reach the softened CHAIN-set decay. Without that the test above could
        /// pass on an engine that never chains at all.
        /// </summary>
        [Test]
        public void PrimitiveCorpusActuallyExercisesChains()
        {
            JArray events = Corpus.Array("primitives.json");
            int maxDepth = 0, heals = 0, healFull = 0, luck = 0;

            foreach (JToken token in events)
            {
                foreach (JToken e in (JArray)token["events"])
                {
                    int depth = e["depth"] != null ? e["depth"].Value<int>() : 0;
                    if (depth > maxDepth) maxDepth = depth;
                    switch (e["t"].Value<string>())
                    {
                        case "heal": heals++; break;
                        case "healfull": healFull++; break;
                        case "luck": luck++; break;
                    }
                }
            }

            Assert.That(maxDepth, Is.GreaterThanOrEqualTo(3), "chains never deepen");
            Assert.That(heals, Is.GreaterThan(0), "nothing ever heals");
            Assert.That(healFull, Is.GreaterThan(0), "a heal is never wasted on a full pool");
            Assert.That(luck, Is.GreaterThan(0), "no luck signal is ever emitted");
        }

        /// <summary>
        /// Phase 4 gate, part one. Every relic alone, three floors each, so a failure names
        /// exactly one relic instead of an interaction.
        /// </summary>
        [Test]
        public void SoloRelicFightsReplayExactly()
        {
            List<CorpusFight.Case> cases = CorpusFight.Load("solo.json");
            Assert.That(cases.Count, Is.GreaterThan(0), "solo corpus is empty");

            var brokenRelics = new SortedDictionary<string, string>();
            foreach (CorpusFight.Case c in cases)
            {
                string diff = CorpusFight.Replay(c);
                if (diff == null) continue;
                string relic = c.Id.Split('/')[1];
                if (!brokenRelics.ContainsKey(relic)) brokenRelics[relic] = diff;
            }

            var lines = new List<string>();
            foreach (var kv in brokenRelics) lines.Add(kv.Key + " -- " + kv.Value);

            Assert.That(brokenRelics.Count, Is.EqualTo(0),
                brokenRelics.Count + " relics diverge:\n  " + string.Join("\n  ", lines));
        }

        /// <summary>Phase 4 gate, part two: duplicates, sockets and awakenings together.</summary>
        [Test]
        public void MixedLoadoutFightsReplayExactly()
        {
            List<CorpusFight.Case> cases = CorpusFight.Load("mixed.json");
            Assert.That(cases.Count, Is.GreaterThan(0), "mixed corpus is empty");

            var failures = new List<string>();
            foreach (CorpusFight.Case c in cases)
            {
                string diff = CorpusFight.Replay(c);
                if (diff != null) failures.Add(diff);
            }

            Assert.That(failures, Is.Empty,
                failures.Count + " of " + cases.Count + " mixed fights diverge:\n  " +
                string.Join("\n  ", failures.GetRange(0, System.Math.Min(6, failures.Count))));
        }

        /// <summary>
        /// The corpus spans four dungeons, and bosses from Dungeon 2 onward carry relic kits, so
        /// these cases exercise the enemy-relic paths — enrage, crit, lifesteal, thorns — even
        /// though the hero carries nothing.
        /// </summary>
        [Test]
        public void BareCorpusCoversEnemyRelicsAndEveryCombatFloor()
        {
            List<CorpusFight.Case> cases = CorpusFight.Load("bare.json");

            var floors = new HashSet<int>();
            var dungeons = new HashSet<int>();
            int withEnemyRelics = 0;

            foreach (CorpusFight.Case c in cases)
            {
                floors.Add(c.Floor);
                dungeons.Add(c.Dungeon);
                foreach (var foe in c.Pack)
                {
                    if (foe.Relics != null && foe.Relics.Count > 0)
                    {
                        withEnemyRelics++;
                        break;
                    }
                }
            }

            Assert.That(floors.Count, Is.EqualTo(12), "expected all 12 combat floors");
            Assert.That(floors.Contains(7), Is.False, "floor 7 is the bazaar, not a fight");
            Assert.That(dungeons.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(withEnemyRelics, Is.GreaterThan(0), "no case exercises enemy relics");
        }
    }
}
