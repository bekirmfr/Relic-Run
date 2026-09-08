using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// Which of the eight languages the two shipped faces can actually draw.
    /// </summary>
    /// <remarks>
    /// The corpus records the faces' own character maps, straight out of the TTF, and nothing
    /// else. What a language NEEDS is decided here, from the locale tables and
    /// <see cref="Locale.Clean"/> — recording the intersection would have made the answer agree
    /// with the question, which is a mistake this project has already made once.
    ///
    /// The answer is uncomfortable and stated out loud rather than left for Phase 10. Both faces
    /// draw English, Spanish and French completely. Space Grotesk also draws Turkish; Silkscreen
    /// reaches 94% of it, having no dotless i and no breve. NEITHER draws Russian, and neither
    /// draws Japanese, Chinese or Arabic — a tenth of Chinese, an eighth of Japanese, half of
    /// Arabic. All of those are asserted as unreadable on purpose. A face that fixed one would
    /// fail this test, which is the point: it should be impossible to add a fallback font and
    /// not notice that it worked.
    ///
    /// The Turkish and Russian holes arrived WITH the correct faces, not despite them. The first
    /// pass bundled Press Start 2P and Jacquard 12, which are in the source nowhere and which
    /// did draw both; swapping to the faces the source actually asks for looked like a
    /// regression and is not one. The source has the same hole and falls through to
    /// <c>monospace</c>, and the borrow chain in <c>FontBook</c> is that decision made explicit.
    /// </remarks>
    [TestFixture]
    public class LegibilityTests
    {
        private static JObject _corpus;
        private static Dictionary<string, List<int>> _faces;

        private const string Pixel = "silkscreen";
        private const string Prose = "space-grotesk";

        [OneTimeSetUp]
        public void LoadTheFaces()
        {
            _corpus = Corpus.Object("fonts.json");
            _faces = new Dictionary<string, List<int>>();

            foreach (JToken face in (JArray)_corpus["faces"])
            {
                var covers = new List<int>();
                foreach (JToken point in (JArray)face["covers"]) covers.Add(point.Value<int>());

                _faces[face["id"].Value<string>()] = covers;
            }
        }

        private static IEnumerable<string> Strings(string language)
        {
            return Corpus.LocaleTable(language).Values;
        }

        private Legibility Read(string face, string language)
        {
            return Legibility.Of(language, face, Strings(language), _faces[face]);
        }

        [Test]
        public void TheCorpusHoldsBothShippedFaces()
        {
            Assert.That(_faces.ContainsKey(Pixel), Is.True);
            Assert.That(_faces.ContainsKey(Prose), Is.True);

            Assert.That(_faces[Pixel].Count, Is.GreaterThan(200), "the cmap read as nearly empty");
            Assert.That(_faces[Prose].Count, Is.GreaterThan(200));
        }

        /// <summary>
        /// English, Spanish and French are drawn completely by both faces.
        /// </summary>
        /// <remarks>
        /// Including the accents. Spanish and French are the reason this is asked rather than
        /// assumed — a face can hold every letter of the alphabet and still be missing an é or
        /// an ñ, and the word it belongs to then arrives with a hole in it.
        /// </remarks>
        [Test]
        public void BothFacesDrawTheAccentedLatinLanguagesCompletely()
        {
            foreach (string language in new[] { "en", "es", "fr" })
            {
                foreach (string face in new[] { Pixel, Prose })
                {
                    Legibility read = Read(face, language);

                    Assert.That(read.Needs, Is.GreaterThan(50), language + " reads as nearly empty");
                    Assert.That(read.Readable, Is.True, read.Report());
                }
            }
        }

        /// <summary>
        /// Turkish stops just short of readable in the pixel face.
        /// </summary>
        /// <remarks>
        /// Ninety-four per cent, which is the worst a coverage figure can be: high enough to look
        /// fine in a spot check, low enough that a Turkish delver meets a hole in a word. What is
        /// absent is the dotless ı and the breve — a handful of characters that happen to be the
        /// ones Turkish uses constantly.
        ///
        /// Asserted as NOT readable rather than quietly tolerated, so the borrow chain covering
        /// it cannot be taken away without this saying what it was for.
        /// </remarks>
        [Test]
        public void OnlyTheProseFaceFinishesTurkish()
        {
            Assert.That(Read(Prose, "tr").Readable, Is.True, Read(Prose, "tr").Report());

            Legibility pixel = Read(Pixel, "tr");

            Assert.That(pixel.Readable, Is.False,
                "Silkscreen has grown the Turkish letters — say so where the fonts are bound");
            Assert.That(pixel.Percent, Is.InRange(90, 99),
                "Turkish coverage has moved; the borrow chain was sized for a near miss");
        }

        /// <summary>
        /// Neither face has any Cyrillic, so Russian borrows as the CJK languages do.
        /// </summary>
        /// <remarks>
        /// A change from the first pass and not a regression. The faces bundled then were not the
        /// ones the source uses, and one of them happened to carry Cyrillic; using the real faces
        /// gives the real answer, which is that the shipped game falls through to
        /// <c>monospace</c> for Russian and so does this.
        /// </remarks>
        [Test]
        public void NeitherFaceDrawsRussian()
        {
            foreach (string face in new[] { Pixel, Prose })
            {
                Legibility read = Read(face, "ru");

                Assert.That(read.Readable, Is.False,
                    face + " has grown Cyrillic — say so where the fonts are bound");
                Assert.That(read.Percent, Is.LessThan(60), read.Report());
            }
        }

        /// <summary>
        /// Three languages cannot be drawn by either shipped face, and borrow instead.
        /// </summary>
        /// <remarks>
        /// This is a fact about the two TTFs and it does not change: a face drawn on a
        /// five-by-seven grid has no Japanese in it and never will. What changed is what is done
        /// about it — <c>FontBook</c> now carries a chain of system faces that the reader's own
        /// device resolves, so those three languages render in whatever the platform has rather
        /// than in boxes. That decision is asserted next door, in <c>FontAssetTests</c>, where
        /// there are real assets to ask.
        ///
        /// Kept as an assertion rather than deleted, because it is the reason the chain exists.
        /// A face that grew Japanese would make the borrowing pointless, and the test that
        /// noticed should be this one.
        /// </remarks>
        [Test]
        public void JapaneseChineseAndArabicCannotBeDrawnByEitherFace()
        {
            var stillBroken = new List<string>();

            foreach (string language in new[] { "ja", "zh", "ar" })
            {
                foreach (string face in new[] { Pixel, Prose })
                {
                    Legibility read = Read(face, language);

                    Assert.That(read.Readable, Is.False,
                        face + " can now read " + language + " — the fallback question is settled, " +
                        "so settle it here too");

                    stillBroken.Add(language + "/" + face + " " + read.Percent + "%");
                }
            }

            Assert.That(stillBroken.Count, Is.EqualTo(6), string.Join(", ", stillBroken));
        }

        /// <summary>The decoration never reaches a font, so no face is asked for emoji.</summary>
        /// <remarks>
        /// English uses thirteen characters that no pixel font of that vintage has ever had, and all
        /// thirteen are arrows and pictograms that <see cref="Locale.Clean"/> removes on the way
        /// to the screen. Counting them would make every face fail for a reason nobody could act
        /// on.
        /// </remarks>
        [Test]
        public void TheStrippedDecorationIsNotAFontsProblem()
        {
            var raw = new HashSet<int>();
            foreach (string line in Strings("en"))
            {
                foreach (char c in line) raw.Add(c);
            }

            IReadOnlyList<int> needed = Legibility.Needed(Strings("en"));

            Assert.That(needed.Count, Is.LessThan(raw.Count),
                "English has decoration in it and none of it was stripped");

            foreach (int point in needed)
            {
                Assert.That(point, Is.Not.InRange(0x2190, 0x21FF), "an arrow survived");
                Assert.That(point, Is.Not.InRange(0x1F000, 0x1FAFF), "an emoji survived");
            }
        }

        /// <summary>What is needed is what survives cleaning, in order, once each.</summary>
        [Test]
        public void TheNeededCharactersAreDistinctAndSorted()
        {
            IReadOnlyList<int> needed = Legibility.Needed(new[] { "banana", "  ← Back  ", "banana" });

            Assert.That(needed, Is.EqualTo(new[] { 'B', 'a', 'b', 'c', 'k', 'n' }),
                "the arrow and the padding go, and every letter is listed once");
        }

        /// <summary>
        /// A character above the basic plane is one character, not two halves.
        /// </summary>
        /// <remarks>
        /// Nothing shipped reaches this. The tables hold five characters above the basic plane
        /// and every one of them is an emoji that <see cref="Locale.Clean"/> removes, so a
        /// version of this that read surrogate pairs as two separate characters passes every
        /// other test here — mutation found exactly that, and nothing else would have.
        ///
        /// It matters anyway, because the halves of a surrogate pair are not characters. A face
        /// asked for U+D835 and U+DD38 would be asked for two things no font has ever had a
        /// glyph for, and would report a language as unreadable for a reason that does not
        /// exist.
        /// </remarks>
        [Test]
        public void ACharacterAboveTheBasicPlaneIsOneCharacterNotTwo()
        {
            IReadOnlyList<int> needed = Legibility.Needed(new[] { "A𝔸B" });

            Assert.That(needed, Is.EqualTo(new[] { 'A', 'B', 0x1D538 }),
                "the pair came apart into two halves of nothing");

            Legibility read = Legibility.Of("xx", "face", new[] { "𝔸" },
                new[] { 0x1D538 });

            Assert.That(read.Readable, Is.True, "a face that HAS the glyph was told it does not");
            Assert.That(Legibility.Of("xx", "face", new[] { "𝔸" },
                new[] { 0xD835, 0xDD38 }).Readable, Is.False,
                "having both halves is not having the character");
        }

        /// <summary>A stripped character above the basic plane leaves nothing behind.</summary>
        [Test]
        public void AStrippedEmojiTakesBothOfItsHalvesWithIt()
        {
            Assert.That(Legibility.Needed(new[] { "a😀b" }), Is.EqualTo(new[] { 'a', 'b' }));
        }

        [Test]
        public void AFaceThatDrawsNothingIsMissingEverything()
        {
            Legibility nothing = Legibility.Of("en", "blank", new[] { "abc" }, new int[0]);

            Assert.That(nothing.Needs, Is.EqualTo(3));
            Assert.That(nothing.Missing.Count, Is.EqualTo(3));
            Assert.That(nothing.Percent, Is.Zero);
            Assert.That(nothing.Report(), Does.Contain("U+0061"));
        }

        [Test]
        public void AnEmptyLanguageIsReadableByAnybody()
        {
            Legibility empty = Legibility.Of("xx", "blank", new string[0], new int[0]);

            Assert.That(empty.Readable, Is.True);
            Assert.That(empty.Percent, Is.EqualTo(100), "nothing to read is not a failure");
            Assert.That(empty.Report(), Is.Empty);
        }

        /// <summary>The report names what is missing and counts what it leaves out.</summary>
        [Test]
        public void TheReportNamesTheFirstFewAndCountsTheRest()
        {
            Legibility read = Read(Pixel, "zh");
            string report = read.Report();

            Assert.That(report, Does.StartWith("silkscreen cannot read zh"));
            Assert.That(report, Does.Contain(" of " + read.Needs + " characters"));
            Assert.That(report, Does.Contain(" more"), "it names eight and counts the rest");
        }

        /// <summary>
        /// A hole in a face is a hole even when the face is otherwise complete.
        /// </summary>
        /// <remarks>
        /// The shipped faces are complete for the Latin languages, so nothing above walks the
        /// case of ONE missing character — which is the case that matters, because it is the one
        /// a person would never notice while scrolling a percentage.
        /// </remarks>
        [Test]
        public void OneMissingCharacterIsEnoughToFail()
        {
            IReadOnlyList<int> needed = Legibility.Needed(Strings("en"));
            Assert.That(needed.Count, Is.EqualTo(83), "English needs a different number of characters now");

            // Taken from what English actually uses rather than chosen. English has no lowercase
            // q in it anywhere, which is the sort of thing that makes a hand-picked letter prove
            // nothing at all.
            int dropped = needed[needed.Count / 2];

            var almost = new List<int>(_faces[Pixel]);
            almost.Remove(dropped);

            Legibility read = Legibility.Of("en", Pixel, Strings("en"), almost);

            Assert.That(read.Readable, Is.False,
                "a missing U+" + dropped.ToString("X4") + " went unnoticed");
            Assert.That(read.Missing, Is.EqualTo(new[] { dropped }));
            Assert.That(read.Percent, Is.EqualTo(98), "and it still reads as very nearly fine");
        }
    }
}
