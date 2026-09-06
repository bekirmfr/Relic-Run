using System;

namespace RelicRun.Core.Determinism
{
    /// <summary>
    /// JavaScript numeric semantics. Use these instead of <see cref="Math"/> anywhere a ported
    /// formula rounds, or the balance silently drifts.
    /// </summary>
    /// <remarks>
    /// The engine rounds on nearly every damage, heal and gold line, and JS and C# disagree at
    /// exactly the midpoint:
    ///
    /// <list type="bullet">
    /// <item>JS <c>Math.round</c> rounds halves toward +infinity: 2.5 → 3, −2.5 → −2.</item>
    /// <item>C# <see cref="Math.Round(double)"/> uses banker's rounding: 2.5 → 2.</item>
    /// <item>C# <c>MidpointRounding.AwayFromZero</c> is also wrong: −2.5 → −3.</item>
    /// </list>
    ///
    /// Neither built-in matches, so every ported formula goes through <see cref="Round"/>.
    /// </remarks>
    public static class JsMath
    {
        /// <summary>JavaScript <c>Math.round</c>: halves go toward +infinity.</summary>
        public static double Round(double value)
        {
            // Deliberately not Math.Floor(value + 0.5). That form is wrong for the largest
            // double below 0.5, where value + 0.5 rounds up to exactly 1.0 in floating point
            // and yields 1 where JS yields 0. Comparing the fraction avoids the trap.
            double floor = Math.Floor(value);
            return (value - floor >= 0.5) ? floor + 1.0 : floor;
        }

        /// <summary>JavaScript <c>Math.round</c>, as an int.</summary>
        public static int RoundToInt(double value)
        {
            return (int)Round(value);
        }

        /// <summary>JavaScript <c>Math.floor</c>, as an int.</summary>
        public static int FloorToInt(double value)
        {
            return (int)Math.Floor(value);
        }

        /// <summary>JavaScript <c>Math.ceil</c>, as an int.</summary>
        public static int CeilToInt(double value)
        {
            return (int)Math.Ceiling(value);
        }
    }
}
