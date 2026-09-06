using System;

namespace RelicRun.Core.Combat
{
    /// <summary>One side's slot in the turn order.</summary>
    public sealed class AtbSlot
    {
        /// <summary>Points still to drain before this side acts.</summary>
        public int Gauge;

        /// <summary>Whether this side is still standing.</summary>
        public Func<bool> Alive;

        /// <summary>Take this side's turn.</summary>
        public Action Act;

        /// <summary>Points drained per tick. Read fresh, since speed changes mid-fight.</summary>
        public Func<int> Speed;

        /// <summary>
        /// An awakened Swift Boots skipping the wait. Checked while the clock would otherwise
        /// advance, and returning true empties this side's gauge without time passing.
        /// </summary>
        public Func<bool> WantsFreeAction;

        /// <summary>
        /// A Hare's Drum answering a dodge. Checked after the OTHER side acts, and returning
        /// true empties this side's gauge so the riposte lands immediately.
        /// </summary>
        public Func<bool> WantsRiposte;
    }

    /// <summary>
    /// The turn order both modes run on.
    /// </summary>
    /// <remarks>
    /// Time is integer ticks, not seconds. Each side drains a hundred-point gauge at its own
    /// speed, and when neither gauge is empty the clock jumps straight to whichever empties
    /// next — so a fight costs a handful of iterations rather than a simulated second per
    /// second, and the result depends only on the seed.
    ///
    /// The two arguments are ordered, not symmetric: on a tie <paramref name="first"/> acts
    /// before <paramref name="second"/>, which is why a killing blow still costs the hero the
    /// hit they were already taking. Free actions are checked the other way round, second
    /// first, matching the source in both modes.
    /// </remarks>
    public static class AtbScheduler
    {
        /// <summary>Points a side must drain before acting.</summary>
        public const int Gauge = 100;

        /// <summary>Stops a pathological loop from hanging the harness.</summary>
        public const int MaxIterations = 600;

        /// <param name="onTick">Advances the clock by the given number of ticks.</param>
        public static void Run(AtbSlot first, AtbSlot second, Action<int> onTick)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));

            int guard = 0;
            while (first.Alive() && second.Alive() && guard++ < MaxIterations)
            {
                if (first.Gauge <= 0 && second.Gauge > 0)
                {
                    first.Act();
                    if (!second.Alive()) break;
                    first.Gauge += Gauge;

                    if (second.WantsRiposte != null && second.WantsRiposte()) second.Gauge = 0;
                }
                else if (second.Gauge <= 0 && first.Gauge > 0)
                {
                    second.Act();
                    if (!first.Alive() || !second.Alive()) break;
                    second.Gauge += Gauge;

                    if (first.WantsRiposte != null && first.WantsRiposte()) first.Gauge = 0;
                }
                else if (first.Gauge <= 0 && second.Gauge <= 0)
                {
                    first.Act();
                    if (!second.Alive()) break;
                    first.Gauge += Gauge;

                    second.Act();
                    if (!first.Alive() || !second.Alive()) break;
                    second.Gauge += Gauge;
                }
                else
                {
                    if (second.WantsFreeAction != null && second.WantsFreeAction())
                    {
                        second.Gauge = 0;
                        continue;
                    }

                    if (first.WantsFreeAction != null && first.WantsFreeAction())
                    {
                        first.Gauge = 0;
                        continue;
                    }

                    int firstSpeed = first.Speed();
                    int secondSpeed = second.Speed();
                    int step = Math.Min(CeilDiv(first.Gauge, firstSpeed), CeilDiv(second.Gauge, secondSpeed));

                    first.Gauge -= firstSpeed * step;
                    second.Gauge -= secondSpeed * step;
                    onTick(step);
                }
            }
        }

        private static int CeilDiv(int numerator, int denominator)
        {
            return (numerator + denominator - 1) / denominator;
        }
    }
}
