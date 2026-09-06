using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Determinism;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>Phase 1 gate: <c>Tools/corpus/rng.json</c>.</summary>
    [TestFixture]
    public class Mulberry32Tests
    {
        /// <summary>
        /// Every draw of every recorded seed must match the JS generator exactly. A single
        /// diverging bit here would silently reshape every seeded run in the game.
        /// </summary>
        /// <remarks>
        /// Compares the generator's raw 32-bit output, never the double. An earlier version of
        /// this test compared doubles parsed out of the corpus and failed under Unity while
        /// passing under dotnet — not because the port was wrong, but because Unity's JSON
        /// parser read 0.1853655439335853 back one ULP high. Integers remove the text
        /// round-trip from the contract entirely.
        /// </remarks>
        [Test]
        public void MatchesRecordedDrawsExactly()
        {
            JArray cases = Corpus.Array("rng.json");
            Assert.That(cases.Count, Is.GreaterThan(0), "rng corpus is empty");

            int totalDraws = 0;

            foreach (JToken token in cases)
            {
                uint seed = token["seed"].Value<uint>();
                JArray expected = (JArray)token["raw"];
                Mulberry32 rng = new Mulberry32(seed);

                for (int i = 0; i < expected.Count; i++)
                {
                    uint want = expected[i].Value<uint>();
                    uint got = rng.NextRaw();

                    Assert.That(got, Is.EqualTo(want),
                        "seed " + seed + " diverges at draw " + i);
                    totalDraws++;
                }
            }

            Assert.That(totalDraws, Is.GreaterThanOrEqualTo(5000),
                "expected at least 5000 recorded draws, got " + totalDraws);
        }

        /// <summary>
        /// A draw is exactly raw / 2^32. Dividing by a power of two is exact in IEEE 754, so
        /// this holds on every runtime — no parsing involved, so it cannot be fooled the way
        /// the corpus comparison was.
        /// </summary>
        [Test]
        public void DoubleDrawIsExactlyTheRawValueOverTwoToThe32()
        {
            Mulberry32 a = new Mulberry32(2);
            Mulberry32 b = new Mulberry32(2);
            for (int i = 0; i < 2000; i++)
            {
                uint raw = a.NextRaw();
                Assert.That(b.Next(), Is.EqualTo(raw / 4294967296.0));
            }
        }

        [Test]
        public void ProducesValuesInUnitInterval()
        {
            Mulberry32 rng = new Mulberry32(0xDEADBEEF);
            for (int i = 0; i < 10000; i++)
            {
                double v = rng.Next();
                Assert.That(v, Is.GreaterThanOrEqualTo(0.0).And.LessThan(1.0));
            }
        }

        [Test]
        public void SameSeedProducesSameSequence()
        {
            Mulberry32 a = new Mulberry32(12345);
            Mulberry32 b = new Mulberry32(12345);
            for (int i = 0; i < 1000; i++)
            {
                Assert.That(b.Next(), Is.EqualTo(a.Next()));
            }
        }
    }

    [TestFixture]
    public class JsMathTests
    {
        /// <summary>
        /// The cases that separate JS rounding from both C# built-ins. Banker's rounding fails
        /// the .5 cases; AwayFromZero fails the negative ones.
        /// </summary>
        [TestCase(0.5, 1.0)]
        [TestCase(1.5, 2.0)]
        [TestCase(2.5, 3.0)]     // banker's rounding would give 2
        [TestCase(3.5, 4.0)]
        [TestCase(-0.5, 0.0)]    // toward +infinity, not away from zero
        [TestCase(-1.5, -1.0)]
        [TestCase(-2.5, -2.0)]   // AwayFromZero would give -3
        [TestCase(2.4, 2.0)]
        [TestCase(2.6, 3.0)]
        [TestCase(0.0, 0.0)]
        public void RoundMatchesJavaScript(double input, double expected)
        {
            Assert.That(JsMath.Round(input), Is.EqualTo(expected));
        }

        /// <summary>
        /// The largest double below 0.5. JS returns 0; the naive Math.Floor(x + 0.5) returns 1,
        /// because the addition itself rounds up to exactly 1.0.
        /// </summary>
        [Test]
        public void RoundHandlesTheFloatingPointMidpointTrap()
        {
            const double justUnderHalf = 0.49999999999999994;
            Assert.That(JsMath.Round(justUnderHalf), Is.EqualTo(0.0));
            Assert.That(Math.Floor(justUnderHalf + 0.5), Is.EqualTo(1.0),
                "sanity: the naive form really is wrong here");
        }

        [Test]
        public void DiffersFromBothBuiltInModes()
        {
            Assert.That(JsMath.Round(2.5), Is.Not.EqualTo(Math.Round(2.5)));
            Assert.That(JsMath.Round(-2.5), Is.Not.EqualTo(Math.Round(-2.5, MidpointRounding.AwayFromZero)));
        }
    }
}
