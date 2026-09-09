using System;
using NUnit.Framework;
using RelicRun.Core.Determinism;
using RelicRun.Core.Presentation;

namespace RelicRun.Tests
{
    /// <summary>
    /// The clock on the title, and the day boundary it is counting to.
    /// </summary>
    /// <remarks>
    /// Two things that look trivial and are the sort that go wrong once a day, for one second,
    /// on somebody else's machine. The day rolls over in UTC so that every delver in the world
    /// gets the new Daily at the same instant — which means the boundary has to be right in a
    /// timezone nobody here is sitting in.
    /// </remarks>
    [TestFixture]
    public class CountdownTests
    {
        /// <summary>A whole day, half a day, and nothing left.</summary>
        [Test]
        public void ASpanIsShownAsHoursMinutesAndSeconds()
        {
            Assert.That(Countdown.Text(TimeSpan.FromHours(12)), Is.EqualTo("12:00:00"));
            Assert.That(Countdown.Text(new TimeSpan(1, 2, 3)), Is.EqualTo("01:02:03"));
            Assert.That(Countdown.Text(TimeSpan.FromSeconds(59)), Is.EqualTo("00:00:59"));
        }

        /// <summary>
        /// Every field keeps two digits.
        /// </summary>
        /// <remarks>
        /// On a line that changes once a second, a field that narrows from two digits to one
        /// makes the whole row jump sideways — and a countdown that jitters reads as broken even
        /// while it is telling the truth.
        /// </remarks>
        [Test]
        public void EveryFieldStaysTwoDigitsWide()
        {
            for (var seconds = 0; seconds < 86400; seconds += 61)
            {
                string shown = Countdown.Text(TimeSpan.FromSeconds(seconds));

                Assert.That(shown.Length, Is.EqualTo(8), "at " + seconds + "s");
                Assert.That(shown[2], Is.EqualTo(':'), "at " + seconds + "s");
                Assert.That(shown[5], Is.EqualTo(':'), "at " + seconds + "s");
            }
        }

        /// <summary>A clock that has run out shows zero, not a negative.</summary>
        /// <remarks>
        /// The second between the day ending and the screen noticing. Nothing shows a delver how
        /// far past the end they are, and a minus sign would break the width besides.
        /// </remarks>
        [Test]
        public void ARunOutClockIsZeroRatherThanNegative()
        {
            Assert.That(Countdown.Text(TimeSpan.Zero), Is.EqualTo("00:00:00"));
            Assert.That(Countdown.Text(TimeSpan.FromSeconds(-5)), Is.EqualTo("00:00:00"));
            Assert.That(Countdown.Text(TimeSpan.FromHours(-30)), Is.EqualTo("00:00:00"));
        }

        /// <summary>
        /// Hours are not capped and not carried into days.
        /// </summary>
        /// <remarks>
        /// This one only ever counts a single day down, so the case never arises for it. It is
        /// asked anyway because a formatter that silently dropped whole days would be wrong the
        /// first time anything else used it, and would be wrong without saying so.
        /// </remarks>
        [Test]
        public void HoursRunPastADayRatherThanWrapping()
        {
            Assert.That(Countdown.Text(TimeSpan.FromHours(25)), Is.EqualTo("25:00:00"));
            Assert.That(Countdown.Text(TimeSpan.FromHours(100)), Is.EqualTo("100:00:00"));
        }

        /// <summary>Before the first tick, dashes rather than zeroes.</summary>
        /// <remarks>
        /// Zeroes would say the day is over, which is a different claim from not knowing yet.
        /// </remarks>
        [Test]
        public void NotKnowingYetIsNotTheSameAsRunOut()
        {
            Assert.That(Countdown.Unknown, Is.Not.EqualTo(Countdown.Text(TimeSpan.Zero)));
            Assert.That(Countdown.Unknown.Length, Is.EqualTo(8), "and it is the same width");
        }

        /// <summary>The day ends at the next UTC midnight.</summary>
        [Test]
        public void TheDayEndsAtTheNextMidnightInUtc()
        {
            var noon = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

            Assert.That(DailySeed.ResetsIn(noon), Is.EqualTo(TimeSpan.FromHours(12)));

            var nearly = new DateTimeOffset(2026, 9, 9, 23, 59, 59, TimeSpan.Zero);

            Assert.That(DailySeed.ResetsIn(nearly), Is.EqualTo(TimeSpan.FromSeconds(1)));
        }

        /// <summary>
        /// The instant the clock reaches zero is the instant the seed changes.
        /// </summary>
        /// <remarks>
        /// The failure this exists for: a countdown that hits zero while the seed is still
        /// yesterday's, or a seed that rolls while the clock still reads eight hours. Either is a
        /// delver watching a timer reach zero and being handed the run they just finished. The
        /// two answers come from the same boundary, and this is what says so.
        /// </remarks>
        [Test]
        public void TheClockReachesZeroExactlyWhenTheSeedTurnsOver()
        {
            var day = new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);

            for (var minute = 0; minute < 24 * 60; minute += 7)
            {
                DateTimeOffset at = day.AddMinutes(minute);

                Assert.That(DailySeed.For(at), Is.EqualTo(DailySeed.For(day)),
                    "the seed changed before the day did, at minute " + minute);
                Assert.That(DailySeed.ResetsIn(at), Is.GreaterThan(TimeSpan.Zero),
                    "the clock ran out before the day did, at minute " + minute);
            }

            DateTimeOffset over = day.AddDays(1);

            Assert.That(DailySeed.For(over), Is.Not.EqualTo(DailySeed.For(day)));
            Assert.That(DailySeed.ResetsIn(over), Is.EqualTo(TimeSpan.FromHours(24)),
                "a fresh day has a whole day left, not none");
        }

        /// <summary>
        /// The boundary is UTC, not the delver's own midnight.
        /// </summary>
        /// <remarks>
        /// Two delvers, one in Istanbul and one in Chicago, looking at the clock at the same
        /// moment. They must see the same number: the Daily is one run shared by everybody, and a
        /// countdown keyed to local midnight would hand it over nine hours apart.
        /// </remarks>
        [Test]
        public void EverybodySeesTheSameClockAtTheSameMoment()
        {
            var utc = new DateTimeOffset(2026, 9, 9, 21, 30, 0, TimeSpan.Zero);

            var istanbul = new DateTimeOffset(2026, 9, 10, 0, 30, 0, TimeSpan.FromHours(3));
            var chicago = new DateTimeOffset(2026, 9, 9, 16, 30, 0, TimeSpan.FromHours(-5));

            Assert.That(istanbul.UtcDateTime, Is.EqualTo(utc.UtcDateTime), "not the same instant");
            Assert.That(chicago.UtcDateTime, Is.EqualTo(utc.UtcDateTime));

            Assert.That(Countdown.Text(DailySeed.ResetsIn(istanbul)), Is.EqualTo("02:30:00"));
            Assert.That(Countdown.Text(DailySeed.ResetsIn(chicago)), Is.EqualTo("02:30:00"));

            Assert.That(DailySeed.For(istanbul), Is.EqualTo(DailySeed.For(chicago)),
                "past midnight at home and still the same Daily");
        }
    }
}
