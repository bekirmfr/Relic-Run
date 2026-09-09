using System;
using System.Globalization;

namespace RelicRun.Core.Presentation
{
    /// <summary>
    /// A clock counting down, as a delver reads it.
    /// </summary>
    /// <remarks>
    /// Three fields, two digits each, zero-padded, and every part of that is load-bearing on a
    /// screen where the number changes once a second: a field that narrows from two digits to
    /// one makes the whole line jump sideways, and a countdown that jitters reads as broken even
    /// when it is right.
    /// </remarks>
    public static class Countdown
    {
        /// <summary>What is shown before the first tick has been counted.</summary>
        /// <remarks>
        /// The source's, and it is dashes rather than zeroes on purpose: zeroes would say the
        /// day is over, which is a different thing from not knowing yet.
        /// </remarks>
        public const string Unknown = "--:--:--";

        /// <summary>
        /// A span, as hours, minutes and seconds.
        /// </summary>
        /// <remarks>
        /// Hours are NOT capped at twenty-four and are not carried into days. This one only ever
        /// counts a day down, so the question never arises for it — but a formatter that dropped
        /// whole days would be wrong the first time anything else used it, and would be wrong
        /// silently.
        ///
        /// A span at or below zero is all zeroes rather than a negative, because a countdown that
        /// has run out has run out. Nothing shows a delver how far past the end they are.
        /// </remarks>
        public static string Text(TimeSpan left)
        {
            if (left <= TimeSpan.Zero) return "00:00:00";

            var total = (long)left.TotalSeconds;

            long hours = total / 3600;
            long minutes = total / 60 % 60;
            long seconds = total % 60;

            return Two(hours) + ":" + Two(minutes) + ":" + Two(seconds);
        }

        private static string Two(long value)
        {
            string digits = value.ToString(CultureInfo.InvariantCulture);

            return digits.Length >= 2 ? digits : "0" + digits;
        }
    }
}
