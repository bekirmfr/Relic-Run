using System.Collections.Generic;
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
