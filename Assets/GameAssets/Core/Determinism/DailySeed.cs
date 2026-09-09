using System;

namespace RelicRun.Core.Determinism
{
    /// <summary>
    /// The seed every delver shares on a given day.
    /// </summary>
    /// <remarks>
    /// It is the UTC date read as a number — 7 September 2026 is 20260907 — so the Daily Delve
    /// is the same run for everybody, and the day rolls over at the same instant everywhere
    /// rather than at each player's midnight. Being readable is the point: the run screen shows
    /// the last four digits as the day's label.
    ///
    /// The instant is taken as a <see cref="DateTimeOffset"/> rather than a
    /// <see cref="DateTime"/>, because a DateTime that does not say which zone it is in would
    /// have to be guessed at, and guessing here means two delvers on the same day getting
    /// different runs. A DateTime is accepted only when it says it is UTC.
    /// </remarks>
    public static class DailySeed
    {
        public static uint For(DateTimeOffset instant)
        {
            DateTime utc = instant.UtcDateTime;
            return (uint)(utc.Year * 10000 + utc.Month * 100 + utc.Day);
        }

        /// <summary>The day a UTC instant falls on.</summary>
        /// <exception cref="ArgumentException">
        /// If the value does not say it is UTC. Local and unspecified times are refused rather
        /// than converted, because converting one silently is how a delver ends up on the wrong
        /// day.
        /// </exception>
        public static uint For(DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException(
                    "the daily seed is a UTC date; pass a UTC DateTime or a DateTimeOffset",
                    nameof(utc));
            }

            return (uint)(utc.Year * 10000 + utc.Month * 100 + utc.Day);
        }

        /// <summary>Today's, in UTC.</summary>
        public static uint Today()
        {
            return For(DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// How long until the day rolls over and a new Daily exists.
        /// </summary>
        /// <remarks>
        /// The next UTC midnight, which is the same instant for everybody — the whole point of
        /// the Daily being a UTC date. A delver in Istanbul and one in Chicago watch the same
        /// clock reach zero, and both get the new run at once.
        ///
        /// It lives here rather than beside the screen that shows it because it is the same
        /// boundary <see cref="For(DateTimeOffset)"/> uses. Two places computing when the day
        /// ends is two answers, and the wrong one is a countdown that reaches zero while the
        /// seed is still yesterday's.
        /// </remarks>
        public static TimeSpan ResetsIn(DateTimeOffset instant)
        {
            DateTime utc = instant.UtcDateTime;
            DateTime midnight = utc.Date.AddDays(1);

            return midnight - utc;
        }

        /// <summary>The label the run screen shows: the day inside the month's number.</summary>
        public static string Label(uint seed)
        {
            string text = seed.ToString();
            return text.Length > 4 ? text.Substring(4) : text;
        }
    }
}
