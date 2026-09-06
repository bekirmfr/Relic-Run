using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Run;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>Phase 5 gate: <c>Tools/corpus/progression.json</c>.</summary>
    [TestFixture]
    public class ProgressionTests
    {
        private static JObject Recorded()
        {
            return Corpus.Object("progression.json");
        }

        [Test]
        public void XpCostPerLevelMatches()
        {
            foreach (JToken t in (JArray)Recorded()["need"])
            {
                int level = t["level"].Value<int>();
                Assert.That(Progression.Need(level), Is.EqualTo(t["need"].Value<int>()),
                    "xp needed for level " + level);
            }
        }

        /// <summary>
        /// The probes sit either side of every level boundary, so an off-by-one in the
        /// accumulate-then-compare loop cannot slip through.
        /// </summary>
        [Test]
        public void LevelAndProgressMatchAtEveryBoundary()
        {
            foreach (JToken t in (JArray)Recorded()["xp"])
            {
                int xp = t["xp"].Value<int>();
                Assert.That(Progression.LevelFor(xp), Is.EqualTo(t["level"].Value<int>()),
                    "level at " + xp + " xp");
                Assert.That(Progression.Progress(xp), Is.EqualTo(t["progress"].Value<double>()).Within(1e-12),
                    "progress at " + xp + " xp");
            }
        }

        [Test]
        public void LevelBonusesMatch()
        {
            foreach (JToken t in (JArray)Recorded()["levels"])
            {
                int level = t["level"].Value<int>();
                JObject want = (JObject)t["bonuses"];
                LevelBonuses got = Progression.Bonuses(level);

                Assert.That(got.Hp, Is.EqualTo(want["hp"].Value<int>()), "level " + level + " hp");
                Assert.That(got.Atk, Is.EqualTo(want["atk"].Value<int>()), "level " + level + " atk");
                Assert.That(got.Def, Is.EqualTo(want["def"].Value<int>()), "level " + level + " def");
                Assert.That(got.Spd, Is.EqualTo(want["spd"].Value<int>()), "level " + level + " spd");
                Assert.That(got.Lck, Is.EqualTo(want["lck"].Value<int>()), "level " + level + " lck");
                Assert.That(got.Breath, Is.EqualTo(want["breath"].Value<int>()), "level " + level + " breath");
                Assert.That(got.Gold, Is.EqualTo(want["gold"].Value<int>()), "level " + level + " gold");
                Assert.That(got.DraftChoices, Is.EqualTo(want["draft"].Value<int>()), "level " + level + " draft");
                Assert.That(got.BazaarDeals, Is.EqualTo(want["deal"].Value<int>()), "level " + level + " deal");
            }
        }

        /// <summary>
        /// The perk text is what the level-up screen shows, so a perk credited to the wrong
        /// level is a real bug even when the stat totals happen to agree.
        /// </summary>
        [Test]
        public void PerkTextMatches()
        {
            foreach (JToken t in (JArray)Recorded()["levels"])
            {
                int level = t["level"].Value<int>();
                Assert.That(Progression.PerkDescription(level),
                    Is.EqualTo(t["perkDesc"].Value<string>()), "perk text for level " + level);
            }
        }

        [Test]
        public void FrontierRelativeRewardTableMatches()
        {
            foreach (JToken t in (JArray)Recorded()["rewardMult"])
            {
                int frontier = t["unlocked"].Value<int>();
                int tier = t["tier"].Value<int>();
                Assert.That(Progression.RewardMultiplier(tier, frontier),
                    Is.EqualTo(t["mult"].Value<double>()).Within(1e-12),
                    "reward at frontier " + frontier + " running tier " + tier);
            }
        }

        [Test]
        public void TierMultiplierMatches()
        {
            foreach (JToken t in (JArray)Recorded()["tierMult"])
            {
                Assert.That(Progression.TierMultiplier(t["tier"].Value<int>()),
                    Is.EqualTo(t["mult"].Value<double>()).Within(1e-12));
            }
        }

        /// <summary>
        /// The design intent behind the reward table, stated as behaviour: the frontier always
        /// pays full rate whatever its depth, so a new delver's first hall is worth as much to
        /// them as a veteran's tenth is to the veteran.
        /// </summary>
        [Test]
        public void TheFrontierAlwaysPaysFullRate()
        {
            for (int frontier = 1; frontier <= 10; frontier++)
            {
                Assert.That(Progression.RewardMultiplier(frontier, frontier), Is.EqualTo(1.0));
            }
        }

        [Test]
        public void FarmingBehindTheFrontierPaysProgressivelyLess()
        {
            const int frontier = 7;
            double previous = double.MaxValue;
            for (int tier = frontier; tier >= 1; tier--)
            {
                double rate = Progression.RewardMultiplier(tier, frontier);
                Assert.That(rate, Is.LessThanOrEqualTo(previous), "rate should not rise as halls get shallower");
                previous = rate;
            }

            Assert.That(Progression.RewardMultiplier(1, frontier), Is.EqualTo(Progression.RewardFloor));
        }

        [Test]
        public void LevellingStopsAtTheCap()
        {
            Assert.That(Progression.LevelFor(int.MaxValue), Is.EqualTo(Progression.MaxLevel));
            Assert.That(Progression.Progress(int.MaxValue), Is.EqualTo(1.0));
        }
    }
}
