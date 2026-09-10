using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Meta;
using RelicRun.Core.Presentation;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// The one field in the game a person types.
    /// </summary>
    /// <remarks>
    /// Everything else a delver's save holds is a number the game put there. This is a string
    /// somebody chose, and it is shown on the board, written to disk and carried on every run —
    /// so what gets past the cleaner is what the rest of the port has to survive.
    /// </remarks>
    [TestFixture]
    public class NamingTests
    {
        /// <summary>An ordinary name is left exactly alone.</summary>
        /// <remarks>
        /// Said first because it is the case a cleaner breaks by being thorough. A delver called
        /// Bekir should be called Bekir, and one called Ünlü Kâşif should keep every mark.
        /// </remarks>
        [Test]
        public void AnOrdinaryNameIsUntouched()
        {
            foreach (string name in new[]
            {
                "Bekir", "Ünlü Kâşif", "Ada", "delver_9", "Zoë", "Ali-Baba", "日本",
                "a b c", "O'Neill", "Ω",
            })
            {
                Assert.That(Naming.Clean(name), Is.EqualTo(name), "[" + name + "] was changed");
            }
        }

        /// <summary>The control range and the two brackets do not survive.</summary>
        /// <remarks>
        /// The brackets are there because the SOURCE draws a name into a web page. This port draws
        /// it into a text mesh, where they are harmless — and they are stripped anyway, because a
        /// save is portable between the two builds and a name that is safe in one and dangerous
        /// in the other is a name that stops being safe the day somebody writes an exporter.
        /// </remarks>
        [Test]
        public void NothingHostileGetsThrough()
        {
            Assert.That(Naming.Clean("<script>"), Is.EqualTo("script"));
            Assert.That(Naming.Clean("a\u0000b"), Is.EqualTo("ab"));
            Assert.That(Naming.Clean("line\nbreak"), Is.EqualTo("linebreak"));
            Assert.That(Naming.Clean("tab\there"), Is.EqualTo("tabhere"));
            Assert.That(Naming.Clean("bell\u0007"), Is.EqualTo("bell"));
            Assert.That(Naming.Clean("\u001funit"), Is.EqualTo("unit"));

            // U+0020 is one past the stripped range and stays — it is an ordinary space, and
            // a delver may put one in the middle of their name. Spelled out because the line
            // above it differs by one invisible character and nothing else.
            Assert.That(Naming.Clean("a" + (char)0x20 + "b"), Is.EqualTo("a b"));
        }

        /// <summary>Ends are trimmed, and a name of only spaces is no name.</summary>
        [Test]
        public void SpaceAtTheEndsIsNotPartOfAName()
        {
            Assert.That(Naming.Clean("  Bekir  "), Is.EqualTo("Bekir"));
            Assert.That(Naming.Clean("\t Bekir \n"), Is.EqualTo("Bekir"));

            Assert.That(Naming.Clean("   "), Is.Empty);
            Assert.That(Naming.Clean(""), Is.Empty);
            Assert.That(Naming.Clean(null), Is.Empty);
        }

        /// <summary>Sixteen characters, and not seventeen.</summary>
        [Test]
        public void ANameIsCutAtSixteen()
        {
            Assert.That(Naming.Clean(new string('a', 40)).Length, Is.EqualTo(Naming.Longest));
            Assert.That(Naming.Clean(new string('a', Naming.Longest)).Length,
                Is.EqualTo(Naming.Longest), "exactly sixteen is not too many");

            Assert.That(Naming.Clean("0123456789abcdefGHIJ"), Is.EqualTo("0123456789abcdef"));
        }

        /// <summary>
        /// The cut never leaves half a character behind.
        /// </summary>
        /// <remarks>
        /// Sixteen CHARS is the source's count, and a char is not a character: an emoji is two,
        /// so the cut can land between the halves of one. A lone surrogate is not text — it does
        /// not draw, it does not compare, and it survives a save round trip looking like it might.
        /// The orphan goes.
        /// </remarks>
        [Test]
        public void TheCutNeverSplitsACharacter()
        {
            // Fifteen plain letters and then a two-char emoji: the sixteenth char is its first
            // half, and there is no room for its second.
            string split = new string('a', 15) + "🗡";

            string cleaned = Naming.Clean(split);

            Assert.That(cleaned, Is.EqualTo(new string('a', 15)));

            foreach (char letter in cleaned)
            {
                Assert.That(char.IsSurrogate(letter), Is.False, "half a character survived");
            }

            // With room for both halves it is kept whole.
            Assert.That(Naming.Clean(new string('a', 14) + "🗡"),
                Is.EqualTo(new string('a', 14) + "🗡"));
        }

        /// <summary>
        /// Half a character never survives, however it arrived.
        /// </summary>
        /// <remarks>
        /// The cut is not the only way to get one — a delver can paste a lone surrogate straight
        /// into the box, and a short name never reaches the cut at all. The cleaner used to drop
        /// orphans it MADE and keep orphans it was GIVEN, which is a promise that held for some
        /// names and not others.
        ///
        /// Mutation testing is what said so. Widening the length check pushed a sixteen-character
        /// name down the other path, the two paths disagreed about a pasted orphan, and the
        /// mutant lived. Neither path leaves one now.
        /// </remarks>
        [Test]
        public void HalfACharacterNeverSurvivesHoweverItArrived()
        {
            const char High = (char)0xD83D;
            const char Low = (char)0xDDE1;

            Assert.That(Naming.Clean("a" + High + "b"), Is.EqualTo("ab"), "a high half alone");
            Assert.That(Naming.Clean("a" + Low + "b"), Is.EqualTo("ab"), "a low half alone");
            Assert.That(Naming.Clean(High.ToString()), Is.Empty, "nothing but a half");
            Assert.That(Naming.Clean("end" + High), Is.EqualTo("end"), "a half at the end");

            // The pair itself is untouched, which is the half of this that matters to a delver
            // who put an emoji in their name on purpose.
            Assert.That(Naming.Clean("a" + High + Low + "b"), Is.EqualTo("a" + High + Low + "b"));

            // And a name short enough never to be cut is held to the same promise.
            foreach (char letter in Naming.Clean("hi" + High))
            {
                Assert.That(char.IsSurrogate(letter), Is.False, "an orphan survived a short name");
            }
        }

        /// <summary>An empty name is a name that has to be made up.</summary>
        [Test]
        public void AnEmptyNameAsksForOne()
        {
            Assert.That(Naming.Missing(Naming.Clean("   ")), Is.True);
            Assert.That(Naming.Missing(Naming.Clean("<>")), Is.True);
            Assert.That(Naming.Missing(Naming.Clean("Bekir")), Is.False);
        }

        /// <summary>A made-up name is the local word and four digits.</summary>
        [Test]
        public void AMadeUpNameIsAWordAndFourDigits()
        {
            Assert.That(Naming.Auto("Delver", 42), Is.EqualTo("Delver#0042"));
            Assert.That(Naming.Auto("Delver", 0), Is.EqualTo("Delver#0000"));
            Assert.That(Naming.Auto("Delver", 9999), Is.EqualTo("Delver#9999"));

            // A roll from outside the range is folded rather than refused: the caller owns the
            // random source, and a name is not worth throwing over.
            Assert.That(Naming.Auto("Delver", 10042), Is.EqualTo("Delver#0042"));
            Assert.That(Naming.Auto("Delver", -7), Is.EqualTo("Delver#0007"));

            Assert.That(Naming.Auto("Kâşif", 1), Is.EqualTo("Kâşif#0001"),
                "the word is translated, so it is whatever the language says");
        }

        /// <summary>
        /// A made-up name fits in the box it came from.
        /// </summary>
        /// <remarks>
        /// The word is translated, so its length is a translator's decision — and a generated
        /// name longer than the limit would be one a delver could never retype. Every shipped
        /// language is checked rather than English alone.
        /// </remarks>
        [Test]
        public void AMadeUpNameIsShortEnoughToBeANameAgain()
        {
            foreach (JToken language in (JArray)Corpus.Object("strings.json")["languages"])
            {
                string code = language.Value<string>();
                Dictionary<string, string> table = Corpus.LocaleTable(code);

                string word;
                if (!table.TryGetValue("autoNameWord", out word)) continue;

                string made = Naming.Auto(word, 1234);

                Assert.That(Naming.Clean(made), Is.EqualTo(made),
                    code + " generates \"" + made + "\", which the cleaner would change");
            }
        }

        /// <summary>
        /// A name from a build whose translation was missing is recognised and cleared.
        /// </summary>
        /// <remarks>
        /// Not a scheme the source abandoned — a bug it survived. Its <c>t()</c> returns the KEY
        /// when a string is absent, so a build missing <c>autoNameWord</c> generated names that
        /// literally began "autoNameWord". The check exists to clean those up, and it is asked in
        /// two places because a board row keeps whatever name was current when a run was banked.
        /// </remarks>
        [Test]
        public void ANameLeftByAMissingTranslationIsRecognised()
        {
            string broken = Naming.Auto(PreferenceCodec.AutoNamedPrefix, 42);

            Assert.That(broken, Does.StartWith(PreferenceCodec.AutoNamedPrefix));
            Assert.That(broken, Is.EqualTo(PreferenceCodec.AutoNamed + "0042"),
                "the two spellings of the marker have drifted apart");

            Assert.That(PreferenceCodec.Read("name " + broken + "\n").Name, Is.Null,
                "a delver carrying one should be asked again");
        }
    }
}
