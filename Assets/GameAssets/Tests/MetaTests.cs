using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Determinism;
using RelicRun.Core.Meta;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// Gate: <c>Tools/corpus/meta.json</c> — what the save is asked once a run is banked in it.
    /// </summary>
    /// <remarks>
    /// Achievements are shipped rules, not presentation: what a delver has earned is a fact
    /// about their save, and a screen that worked it out would be a second copy of the rule.
    /// Each of the fourteen is asked of a spread of saves, including saves sitting exactly on a
    /// threshold — which is where a port that reads a greater-than for a greater-or-equal
    /// disagrees, and nowhere else.
    /// </remarks>
    [TestFixture]
    public class MetaTests
    {
        [Test]
        public void RecordedSavesEarnExactly()
        {
            JObject corpus = Corpus.Object("meta.json");
            var cases = (JArray)corpus["achievements"];
            Assert.That(cases.Count, Is.GreaterThan(0), "meta corpus is empty");

            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                string id = token["id"].Value<string>();
                var store = (JObject)token["save"];

                var save = new SaveState
                {
                    Runs = store["dd.runs"].Value<int>(),
                    Clears = store["dd.clears"].Value<int>(),
                    Best = store["dd.best"].Value<int>(),
                    VsCrowns = store["dd.vsCrowns"].Value<int>(),
                    GoldLife = store["dd.goldLife"].Value<int>(),
                    Xp = store["dd.xp"].Value<int>(),
                    Unlocked = store["dd.unlocked"].Value<int>(),
                };

                int level = token["level"].Value<int>();
                if (save.Level != level)
                {
                    failures.Add(id + ": level recorded " + level + ", replayed " + save.Level);
                    continue;
                }

                var want = new List<string>();
                foreach (JToken t in (JArray)token["earned"]) want.Add(t.Value<string>());

                List<string> got = Achievements.EarnedBy(save);

                if (string.Join(",", want) != string.Join(",", got))
                {
                    failures.Add(id + ": earned [" + string.Join(",", want) + "], replayed [" +
                                 string.Join(",", got) + "]");
                }
            }

            Assert.That(failures, Is.Empty,
                failures.Count + " of " + cases.Count + " saves earn differently:\n  " +
                string.Join("\n  ", failures.GetRange(0, Math.Min(8, failures.Count))));
        }

        /// <summary>
        /// The Daily Delve's seed is the UTC date read as a number.
        /// </summary>
        /// <remarks>
        /// Recorded across month ends, year ends and a leap day, which is where a date routine
        /// written by hand goes wrong. The hour is deliberately noon in the recording: the seed
        /// must not depend on the time of day, only on which day it is in UTC.
        /// </remarks>
        [Test]
        public void RecordedDaysSeedExactly()
        {
            JObject corpus = Corpus.Object("meta.json");
            var days = (JArray)corpus["seeds"];
            var failures = new List<string>();

            foreach (JToken token in days)
            {
                int y = token["y"].Value<int>(), m = token["m"].Value<int>(), d = token["d"].Value<int>();
                uint want = token["seed"].Value<uint>();

                uint got = DailySeed.For(new DateTime(y, m, d, 12, 0, 0, DateTimeKind.Utc));
                if (got != want) failures.Add(y + "-" + m + "-" + d + ": recorded " + want + ", replayed " + got);

                // The same day at either edge is the same day.
                uint dawn = DailySeed.For(new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc));
                uint dusk = DailySeed.For(new DateTime(y, m, d, 23, 59, 59, DateTimeKind.Utc));
                if (dawn != want || dusk != want)
                {
                    failures.Add(y + "-" + m + "-" + d + ": the seed moved within the day");
                }

                // Midday anywhere from Tokyo to Chicago is still the same day in UTC.
                foreach (int offset in new[] { -6, -1, 0, 5, 9 })
                {
                    uint elsewhere = DailySeed.For(
                        new DateTimeOffset(y, m, d, 12, 0, 0, TimeSpan.FromHours(offset)));
                    if (elsewhere != want)
                    {
                        failures.Add(y + "-" + m + "-" + d + ": a clock " + offset +
                                     " hours off landed on " + elsewhere);
                    }
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n  ", failures));
        }

        /// <summary>
        /// The day is the one the INSTANT falls on in UTC, not the one on the local calendar.
        /// </summary>
        /// <remarks>
        /// A delver in Tokyo opening the game at two in the morning is still on yesterday's
        /// Daily, and one in Chicago at nine in the evening is already on tomorrow's. Both are
        /// the point of anchoring on UTC: everybody is running the same dungeon at the same
        /// moment, whatever their calendar says.
        /// </remarks>
        [Test]
        public void TheDayIsTheOneTheInstantFallsOnInUtc()
        {
            Assert.That(DailySeed.For(new DateTimeOffset(2026, 9, 8, 2, 0, 0, TimeSpan.FromHours(9))),
                Is.EqualTo(20260907u), "02:00 in Tokyo is 17:00 the previous day in UTC");

            Assert.That(DailySeed.For(new DateTimeOffset(2026, 9, 7, 21, 0, 0, TimeSpan.FromHours(-5))),
                Is.EqualTo(20260908u), "21:00 in Chicago is 02:00 the next day in UTC");
        }

        /// <summary>
        /// A DateTime that does not say which zone it is in is refused, not guessed at.
        /// </summary>
        /// <remarks>
        /// Guessing would mean converting from whatever zone the machine happens to be in, and
        /// two delvers on the same day would get different runs. There is no test that could
        /// catch that without itself depending on the machine's zone, which is the reason the
        /// ambiguity is refused at the door rather than handled.
        /// </remarks>
        [Test]
        public void ADayWithoutAZoneIsRefused()
        {
            Assert.Throws<ArgumentException>(
                () => DailySeed.For(new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Unspecified)));
            Assert.Throws<ArgumentException>(
                () => DailySeed.For(new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Local)));
            Assert.DoesNotThrow(
                () => DailySeed.For(new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc)));
        }

        /// <summary>A day's label is the last four digits, which is the day inside the month.</summary>
        [Test]
        public void ADaysLabelIsItsMonthAndDay()
        {
            Assert.That(DailySeed.Label(20260907u), Is.EqualTo("0907"));
            Assert.That(DailySeed.Label(19991231u), Is.EqualTo("1231"));
        }
    }
}
