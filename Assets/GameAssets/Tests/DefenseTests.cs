using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Stats;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>Phase 1 gate: <c>Tools/corpus/defense.json</c>.</summary>
    [TestFixture]
    public class DefenseTests
    {
        /// <summary>
        /// Every cell of the recorded damage x defence grid, in both models. This is where a
        /// rounding mistake would show up first and most cheaply — the percentage model rounds
        /// on every single hit in the game.
        /// </summary>
        [Test]
        public void MatchesRecordedGrid()
        {
            JArray grid = Corpus.Array("defense.json");
            Assert.That(grid.Count, Is.GreaterThan(0), "defence corpus is empty");

            foreach (JToken cell in grid)
            {
                string modelName = cell["model"].Value<string>();
                int damage = cell["dmg"].Value<int>();
                int defense = cell["def"].Value<int>();
                int expected = cell["out"].Value<int>();

                DefenseModel model = modelName == "flat" ? DefenseModel.Flat : DefenseModel.Percentage;
                int actual = Defense.Apply(damage, defense, model);

                Assert.That(actual, Is.EqualTo(expected),
                    "model=" + modelName + " dmg=" + damage + " def=" + defense);
            }
        }

        [Test]
        public void ADefinedNumberOfPointsHalvesDamage()
        {
            Assert.That(Defense.Apply(100, Defense.K), Is.EqualTo(50));
        }

        [Test]
        public void ABlowAlwaysLandsForAtLeastOne()
        {
            Assert.That(Defense.Apply(1, 10000), Is.EqualTo(1));
            Assert.That(Defense.Apply(1, 10000, DefenseModel.Flat), Is.EqualTo(1));
        }

        [Test]
        public void NegativeDefenceIsTreatedAsZero()
        {
            Assert.That(Defense.Apply(20, -5), Is.EqualTo(Defense.Apply(20, 0)));
        }
    }
}
