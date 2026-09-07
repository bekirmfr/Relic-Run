using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// Phase 8 gate: <c>Tools/corpus/palette.json</c>.
    /// </summary>
    /// <remarks>
    /// A hero's pixels are role KEYS rather than colours, and the palette turns keys into
    /// colours at draw time — which is what lets the Changing Room recolour a delver without
    /// touching a sprite. The derivation runs a colour through HSL and back, which is float
    /// arithmetic landing on a byte: nearly always the rounding absorbs any difference between
    /// two languages, and occasionally it does not.
    ///
    /// The multipliers are recorded as THOUSANDTHS so the corpus holds no float literals. Both
    /// sides divide by a thousand, which is exact, and the answers are compared as the hex
    /// strings the source itself writes.
    /// </remarks>
    [TestFixture]
    public class PaletteTests
    {
        [Test]
        public void RecordedShadesMatchExactly()
        {
            JObject corpus = Corpus.Object("palette.json");
            var cases = (JArray)corpus["shades"];
            Assert.That(cases.Count, Is.GreaterThan(0), "palette corpus is empty");

            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                string hex = token["hex"].Value<string>();
                int mul = token["mul"].Value<int>();

                string got = HeroColour.Shade(Rgb.Parse(hex), mul / 1000.0).ToString();
                string want = token["out"].Value<string>();

                if (got != want)
                {
                    failures.Add(hex + " x " + mul + "/1000: recorded " + want + ", replayed " + got);
                }
            }

            Report(failures, cases.Count, "shades");
        }

        [Test]
        public void RecordedShiftsMatchExactly()
        {
            JObject corpus = Corpus.Object("palette.json");
            var cases = (JArray)corpus["shifts"];
            Assert.That(cases.Count, Is.GreaterThan(0), "palette corpus is empty");

            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                string hex = token["hex"].Value<string>();
                int mult = token["mult"].Value<int>();
                int hue = token["hue"].Value<int>();
                int sat = token["sat"].Value<int>();

                string got = HeroColour
                    .ApplyShade(Rgb.Parse(hex), mult / 1000.0, hue, sat / 1000.0).ToString();
                string want = token["out"].Value<string>();

                if (got != want)
                {
                    failures.Add(hex + " lightness " + mult + "/1000, hue " + hue + ", saturation " +
                                 sat + "/1000: recorded " + want + ", replayed " + got);
                }
            }

            Report(failures, cases.Count, "shifts");
        }

        /// <summary>
        /// The palette a delver gets before they have chosen anything.
        /// </summary>
        /// <remarks>
        /// This is the one that pins the whole thing together: the family table, the fixed
        /// colours, the shade defaults and the maths all have to agree at once for eighty-five
        /// keys to come out right.
        /// </remarks>
        [Test]
        public void TheDefaultPaletteIsBuiltExactly()
        {
            JObject corpus = Corpus.Object("palette.json");
            var cases = (JArray)corpus["roles"];

            Dictionary<char, Rgb> built = HeroPalette.Build();
            var failures = new List<string>();

            Assert.That(built.Count, Is.EqualTo(cases.Count),
                "the palette holds " + built.Count + " keys and the recording holds " + cases.Count);

            foreach (JToken token in cases)
            {
                string key = token["key"].Value<string>();
                char role = key[0];

                Rgb got;
                if (!built.TryGetValue(role, out got))
                {
                    failures.Add(token["family"] + " " + token["kind"] + ": no key '" + key + "'");
                    continue;
                }

                string want = token["hex"].Value<string>();
                if (!string.Equals(got.ToString(), want, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add(token["family"] + " " + token["kind"] + " ('" + key +
                                 "'): recorded " + want + ", built " + got);
                }
            }

            Report(failures, cases.Count, "roles");
        }

        /// <summary>
        /// A delver's own colour replaces a family's default, and takes its shades with it.
        /// </summary>
        /// <remarks>
        /// The corpus can only hold the default palette — what a player picks is not a rule — so
        /// the thing that makes the palette worth having is asked directly: choosing a colour
        /// has to move all three of that family's keys and none of anybody else's.
        /// </remarks>
        [Test]
        public void ChoosingAColourMovesThatFamilyAndNoOther()
        {
            ColourFamily hair = HeroPalette.Families[0];
            Assert.That(hair.Name, Is.EqualTo("Hair"));

            Dictionary<char, Rgb> before = HeroPalette.Build();
            Dictionary<char, Rgb> after = HeroPalette.Build(
                new Dictionary<char, string> { { hair.Base, "#2E7ED9" } });

            Assert.That(after[hair.Base].ToString(), Is.EqualTo("#2e7ed9"));
            Assert.That(after[hair.Dark].ToString(), Is.Not.EqualTo(before[hair.Dark].ToString()),
                "the shaded side follows the colour it is derived from");
            Assert.That(after[hair.Light].ToString(), Is.Not.EqualTo(before[hair.Light].ToString()));

            foreach (KeyValuePair<char, Rgb> role in before)
            {
                if (role.Key == hair.Base || role.Key == hair.Dark || role.Key == hair.Light) continue;

                Assert.That(after[role.Key].ToString(), Is.EqualTo(role.Value.ToString()),
                    "'" + role.Key + "' moved, and only the hair was chosen");
            }
        }

        /// <summary>
        /// The fixed colours are fixed: nothing a delver picks can move them.
        /// </summary>
        /// <remarks>
        /// White, black and the greys are what outlines and eyes are drawn in. A wardrobe that
        /// could recolour them would let a delver erase their own silhouette.
        /// </remarks>
        [Test]
        public void TheFixedColoursCannotBeChosenAway()
        {
            var everything = new Dictionary<char, string>();
            foreach (ColourFamily family in HeroPalette.Families) everything[family.Base] = "#FF00FF";
            foreach (KeyValuePair<char, string> solo in HeroPalette.Fixed) everything[solo.Key] = "#FF00FF";

            Dictionary<char, Rgb> built = HeroPalette.Build(everything);

            foreach (KeyValuePair<char, string> solo in HeroPalette.Fixed)
            {
                Assert.That(built[solo.Key].ToString(),
                    Is.EqualTo(Rgb.Parse(solo.Value).ToString()),
                    "'" + solo.Key + "' is meant to be fixed");
            }
        }

        /// <summary>A colour survives being written down and read back.</summary>
        [Test]
        public void AColourRoundTripsThroughItsHex()
        {
            foreach (string hex in new[] { "#000000", "#ffffff", "#8a5a2b", "#010203", "#ABCDEF" })
            {
                Assert.That(Rgb.Parse(hex).ToString(), Is.EqualTo(hex.ToLowerInvariant()));
            }

            Assert.That(Rgb.Parse("8A5A2B").ToString(), Is.EqualTo("#8a5a2b"), "the hash is optional");
            Assert.Throws<FormatException>(() => Rgb.Parse("#12345"));
            Assert.Throws<FormatException>(() => Rgb.Parse("#12345g"));
        }

        private static void Report(List<string> failures, int total, string what)
        {
            Assert.That(failures, Is.Empty,
                failures.Count + " of " + total + " " + what + " diverge:\n  " +
                string.Join("\n  ", failures.GetRange(0, Math.Min(8, failures.Count))));
        }
    }
}
