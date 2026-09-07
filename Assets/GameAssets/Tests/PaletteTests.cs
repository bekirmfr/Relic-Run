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

        [Test]
        public void RecordedCompositesMatchExactly()
        {
            JObject corpus = Corpus.Object("palette.json");
            var cases = (JArray)corpus["overs"];
            Assert.That(cases.Count, Is.GreaterThan(0), "palette corpus is empty");

            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                string below = token["below"].Value<string>();
                string top = token["top"].Value<string>();
                int alpha = token["alpha"].Value<int>();

                string got = HeroColour
                    .Over(Rgb.Parse(below), Rgb.Parse(top), alpha / 1000.0).ToString();
                string want = token["out"].Value<string>();

                if (!string.Equals(got, want, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add(top + " over " + below + " at " + alpha + "/1000: recorded " +
                                 want + ", replayed " + got);
                }
            }

            Report(failures, cases.Count, "composites");
        }

        /// <summary>
        /// The palettes the source builds, key for key.
        /// </summary>
        /// <remarks>
        /// This is the one that pins the whole thing together: the family table, the fixed
        /// colours, the shade defaults, the maths and the odds and ends all have to agree at
        /// once for ninety-four keys to come out right.
        ///
        /// Recorded from the source's own builder rather than re-derived, which is not a detail.
        /// The first version of this gate walked the family table and applied the shades in the
        /// recorder — a corpus the port agreed with by construction, and which said nothing
        /// about the nine keys the real builder adds on top. It passed, and the port was wrong.
        /// </remarks>
        [Test]
        public void RecordedPalettesAreBuiltExactly()
        {
            JObject corpus = Corpus.Object("palette.json");
            var cases = (JArray)corpus["palettes"];
            Assert.That(cases.Count, Is.GreaterThan(0), "palette corpus is empty");

            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                string id = token["id"].Value<string>();

                var chosen = new Dictionary<char, string>();
                foreach (JProperty pick in ((JObject)token["famHex"]).Properties())
                {
                    chosen[pick.Name[0]] = pick.Value.Value<string>();
                }

                Dictionary<char, Rgb> built = HeroPalette.Build(chosen);
                var roles = (JArray)token["roles"];

                if (built.Count != roles.Count)
                {
                    failures.Add(id + ": the palette holds " + built.Count +
                                 " keys and the recording holds " + roles.Count);
                    continue;
                }

                foreach (JToken role in roles)
                {
                    char key = role["key"].Value<string>()[0];
                    string want = role["hex"].Value<string>();

                    Rgb got;
                    if (!built.TryGetValue(key, out got))
                    {
                        failures.Add(id + ": no key U+" + ((int)key).ToString("X4"));
                        continue;
                    }

                    if (!string.Equals(got.ToString(), want, StringComparison.OrdinalIgnoreCase))
                    {
                        failures.Add(id + " U+" + ((int)key).ToString("X4") + ": recorded " +
                                     want + ", built " + got);
                    }
                }
            }

            Report(failures, cases.Count, "palettes");
        }

        /// <summary>
        /// An alias answers with whatever the role it points at is currently painted.
        /// </summary>
        /// <remarks>
        /// Art drawn before a role existed still carries the old letter. Resolving an alias
        /// against the FINISHED palette rather than against the family default is what makes a
        /// delver's own colour reach those pixels too — otherwise choosing a hair colour would
        /// leave the old eyebrow token painted in the default.
        /// </remarks>
        [Test]
        public void AnAliasFollowsWhateverItPointsAt()
        {
            Assert.That(HeroPalette.Aliases, Is.Not.Empty);

            foreach (KeyValuePair<char, char> alias in HeroPalette.Aliases)
            {
                Dictionary<char, Rgb> plain = HeroPalette.Build();
                Assert.That(plain[alias.Key].ToString(), Is.EqualTo(plain[alias.Value].ToString()),
                    "'" + alias.Key + "' is meant to mean '" + alias.Value + "'");
            }

            // The eye family, whose base key two aliases point at.
            Dictionary<char, Rgb> chosen = HeroPalette.Build(
                new Dictionary<char, string> { { 'E', "#2E7ED9" } });

            Assert.That(chosen['i'].ToString(), Is.EqualTo("#2e7ed9"));
            Assert.That(chosen['j'].ToString(), Is.EqualTo("#2e7ed9"));
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

                // An alias of a hair key follows it, which is the point of the aliases.
                char points;
                if (HeroPalette.Aliases.TryGetValue(role.Key, out points) &&
                    (points == hair.Base || points == hair.Dark || points == hair.Light))
                {
                    continue;
                }

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
