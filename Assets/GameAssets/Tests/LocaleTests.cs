using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// Phase 8 gate: <c>Tools/corpus/strings.json</c> — every string, in every language.
    /// </summary>
    /// <remarks>
    /// Resolving a string is a lookup, a fallback to English, a fallback to the key itself,
    /// placeholder substitution, and then a clean-up that strips decoration and tidies spacing.
    /// The last step is the one worth a corpus: a hundred and forty-four of the twelve hundred
    /// shipped strings change on their way to the screen, because the game writes its buttons
    /// with icons in front of them and none of those icons is ever drawn.
    /// </remarks>
    [TestFixture]
    public class LocaleTests
    {
        private static JObject _corpus;
        private static Dictionary<string, Locale> _locales;

        [OneTimeSetUp]
        public void LoadTheLocales()
        {
            _corpus = Corpus.Object("strings.json");
            _locales = new Dictionary<string, Locale>();

            var tables = new Dictionary<string, Dictionary<string, string>>();
            foreach (JToken lang in (JArray)_corpus["languages"])
            {
                string code = lang.Value<string>();
                tables[code] = Corpus.LocaleTable(code);
            }

            foreach (KeyValuePair<string, Dictionary<string, string>> table in tables)
            {
                _locales[table.Key] = new Locale(table.Key, table.Value, tables["en"]);
            }
        }

        private Locale Of(string language)
        {
            Locale locale;
            if (_locales.TryGetValue(language, out locale)) return locale;

            // A language nobody speaks still has to answer, out of English.
            return new Locale(language, new Dictionary<string, string>(),
                Corpus.LocaleTable("en"));
        }

        [Test]
        public void EveryRecordedStringResolvesExactly()
        {
            var cases = (JArray)_corpus["resolved"];
            Assert.That(cases.Count, Is.GreaterThan(0), "string corpus is empty");

            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                string lang = token["lang"].Value<string>();
                string key = token["key"].Value<string>();
                string want = token["out"].Value<string>();

                string got = Of(lang).Get(key);
                if (got != want)
                {
                    failures.Add(lang + "/" + key + ": recorded " + Show(want) + ", got " + Show(got));
                }
            }

            Report(failures, cases.Count, "strings");
        }

        [Test]
        public void EveryRecordedPlaceholderFillsExactly()
        {
            var cases = (JArray)_corpus["filled"];
            Assert.That(cases.Count, Is.GreaterThan(0), "string corpus is empty");

            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                string lang = token["lang"].Value<string>();
                string key = token["key"].Value<string>();
                string want = token["out"].Value<string>();

                string got = Of(lang).Get(key, Values((JObject)token["vars"]));
                if (got != want)
                {
                    failures.Add(lang + "/" + key + ": recorded " + Show(want) + ", got " + Show(got));
                }
            }

            Report(failures, cases.Count, "filled strings");
        }

        /// <summary>The awkward cases: a key nobody has, a language nobody speaks, an empty value.</summary>
        [Test]
        public void TheAwkwardCasesResolveExactly()
        {
            var cases = (JArray)_corpus["edges"];
            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                string lang = token["lang"].Value<string>();
                string key = token["key"].Value<string>();
                string want = token["out"].Value<string>();

                JToken vars = token["vars"];
                string got = vars == null || vars.Type == JTokenType.Null
                    ? Of(lang).Get(key)
                    : Of(lang).Get(key, Values((JObject)vars));

                if (got != want)
                {
                    failures.Add(lang + "/" + key + ": recorded " + Show(want) + ", got " + Show(got));
                }
            }

            Report(failures, cases.Count, "awkward cases");
        }

        /// <summary>
        /// The stripped ranges, probed either side of every boundary.
        /// </summary>
        /// <remarks>
        /// These strings never appear in the game. They are here so the ranges are pinned rather
        /// than inferred from whichever emoji the translators happened to reach for — a range
        /// one code point wide in the wrong place is invisible until the day somebody writes a
        /// button with a different arrow.
        /// </remarks>
        [Test]
        public void TheStrippedRangesAreExact()
        {
            var cases = (JArray)_corpus["stripped"];
            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                string shape = token["shape"].Value<string>();
                string want = token["out"].Value<string>();
                string got = Locale.Clean(shape);

                if (got != want)
                {
                    failures.Add("U+" + token["codepoint"].Value<int>().ToString("X4") + " in " +
                                 Show(shape) + ": recorded " + Show(want) + ", got " + Show(got));
                }
            }

            Report(failures, cases.Count, "stripping probes");
        }

        /// <summary>
        /// A translation that is missing a key falls through to English, and English to the key.
        /// </summary>
        /// <remarks>
        /// The shipped locales are complete, so the corpus never walks the first fallback. It is
        /// the one that matters when a translator is mid-way through a language, which is most
        /// of the time a language exists at all.
        /// </remarks>
        [Test]
        public void AMissingTranslationFallsThroughToEnglish()
        {
            var english = new Dictionary<string, string>
            {
                { "hello", "Hello" }, { "bye", "Goodbye" },
            };

            var partial = new Locale("xx", new Dictionary<string, string> { { "hello", "Bonjour" } },
                english);

            Assert.That(partial.Get("hello"), Is.EqualTo("Bonjour"), "its own where it has one");
            Assert.That(partial.Get("bye"), Is.EqualTo("Goodbye"), "English where it does not");
            Assert.That(partial.Get("missing"), Is.EqualTo("missing"),
                "and the key itself where nobody does, so it shows up rather than going blank");

            Assert.That(partial.Has("hello"), Is.True);
            Assert.That(partial.Has("bye"), Is.False, "borrowed, not had");
        }

        /// <summary>Every shipped language has every key, so nothing falls back in practice.</summary>
        [Test]
        public void TheShippedLanguagesAreComplete()
        {
            var keys = new List<string>();
            foreach (JToken key in (JArray)_corpus["keys"]) keys.Add(key.Value<string>());

            Assert.That(keys.Count, Is.GreaterThan(0));

            foreach (JToken lang in (JArray)_corpus["languages"])
            {
                string code = lang.Value<string>();
                Locale locale = Of(code);

                foreach (string key in keys)
                {
                    Assert.That(locale.Has(key), Is.True, code + " is missing " + key);
                }
            }
        }

        /// <summary>
        /// The strings the PORT adds are written in all eight languages, placeholders and all.
        /// </summary>
        /// <remarks>
        /// The source writes a handful of its labels straight into its markup instead of through
        /// its string table, so they are English on a Japanese screen. Where the port draws one
        /// of those it adds a key of its own — <c>ADDED</c> in <c>Tools/extract/extract.mjs</c> —
        /// and a key nobody translated shows up as itself, which is worse than the English it
        /// was meant to replace.
        ///
        /// Found by DIFFERENCE rather than by a list kept here: anything English has that the
        /// recorded corpus does not is something the port added, so this gate covers the next
        /// one too without anybody remembering to come back.
        /// </remarks>
        [Test]
        public void TheStringsThePortAddsAreWrittenInEveryLanguage()
        {
            var recorded = new HashSet<string>();
            foreach (JToken key in (JArray)_corpus["keys"]) recorded.Add(key.Value<string>());

            Dictionary<string, string> english = Corpus.LocaleTable("en");

            var added = new List<string>();
            foreach (KeyValuePair<string, string> one in english)
            {
                if (!recorded.Contains(one.Key)) added.Add(one.Key);
            }

            Assert.That(added.Count, Is.GreaterThan(0),
                "the port adds no strings of its own — if that is now true, delete this test");

            foreach (JToken lang in (JArray)_corpus["languages"])
            {
                string code = lang.Value<string>();
                Dictionary<string, string> table = Corpus.LocaleTable(code);

                foreach (string key in added)
                {
                    string said;

                    Assert.That(table.TryGetValue(key, out said), Is.True,
                        code + " has nothing for the added string " + key);

                    // And it still says the number. A price is what the reroll button is FOR,
                    // and a translation that dropped the placeholder would read as a button
                    // offering something free.
                    foreach (string hole in Holes(english[key]))
                    {
                        Assert.That(said.Contains(hole), Is.True,
                            code + " lost " + hole + " out of " + key);
                    }
                }
            }
        }

        /// <summary>Every placeholder in a string, as it is written.</summary>
        private static IEnumerable<string> Holes(string text)
        {
            var found = new List<string>();

            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '{') continue;

                int end = text.IndexOf('}', i);

                if (end < 0) break;

                found.Add(text.Substring(i, end - i + 1));
                i = end;
            }

            return found;
        }

        /// <summary>
        /// A placeholder is filled everywhere it appears, not just the first time.
        /// </summary>
        /// <remarks>
        /// No shipped string repeats one, in any of the eight languages, so the corpus cannot
        /// say. A translator writing "{n} of {n}" is doing nothing unusual, and a line that
        /// filled only the first would read as half-finished.
        /// </remarks>
        [Test]
        public void APlaceholderIsFilledEverywhereItAppears()
        {
            var locale = new Locale("en", new Dictionary<string, string>
            {
                { "twice", "{n} and {n} again, {m} apart" },
            });

            string filled = locale.Get("twice", new Dictionary<string, string>
            {
                { "n", "7" }, { "m", "3" },
            });

            Assert.That(filled, Is.EqualTo("7 and 7 again, 3 apart"));
        }

        /// <summary>An empty value fills the placeholder rather than leaving it standing.</summary>
        [Test]
        public void AnEmptyValueStillFillsItsPlaceholder()
        {
            var locale = new Locale("en", new Dictionary<string, string>
            {
                { "line", "before{n}after" },
            });

            Assert.That(locale.Get("line", new Dictionary<string, string> { { "n", "" } }),
                Is.EqualTo("beforeafter"));

            Assert.That(locale.Get("line", new Dictionary<string, string> { { "n", null } }),
                Is.EqualTo("beforeafter"), "and a value nobody supplied is not a crash");
        }

        /// <summary>
        /// Tabs are tidied exactly as spaces are: one is kept, a run becomes a single space.
        /// </summary>
        /// <remarks>
        /// Nothing shipped has a tab in it. The rule is the source's and the format allows one,
        /// so it is asked rather than assumed — a lone tab surviving as a tab is the half that
        /// is easy to lose when the run-collapsing is written the obvious way.
        /// </remarks>
        [Test]
        public void TabsAreTidiedAsSpacesAre()
        {
            Assert.That(Locale.Clean("a	b"), Is.EqualTo("a	b"), "a lone tab stays a tab");
            Assert.That(Locale.Clean("a		b"), Is.EqualTo("a b"), "a run of them becomes a space");
            Assert.That(Locale.Clean("a 	b"), Is.EqualTo("a b"), "and so does a mixed run");
            Assert.That(Locale.Clean("a  b"), Is.EqualTo("a b"));
            Assert.That(Locale.Clean("a b"), Is.EqualTo("a b"), "a lone space stays a space");
            Assert.That(Locale.Clean("	 a 	"), Is.EqualTo("a"), "and the ends come off");
        }

        private static Dictionary<string, string> Values(JObject vars)
        {
            var values = new Dictionary<string, string>();
            foreach (JProperty var in vars.Properties())
            {
                values[var.Name] = var.Value.Type == JTokenType.Null
                    ? ""
                    : var.Value.ToString();
            }

            return values;
        }

        /// <summary>Shows a string with its invisible characters spelled out.</summary>
        private static string Show(string text)
        {
            var shown = new System.Text.StringBuilder("\"");
            foreach (char c in text)
            {
                if (c < 32 || c > 126) shown.Append("\\u").Append(((int)c).ToString("X4"));
                else shown.Append(c);
            }

            return shown.Append('"').ToString();
        }

        private static void Report(List<string> failures, int total, string what)
        {
            Assert.That(failures, Is.Empty,
                failures.Count + " of " + total + " " + what + " diverge:\n  " +
                string.Join("\n  ", failures.GetRange(0, Math.Min(8, failures.Count))));
        }
    }
}
