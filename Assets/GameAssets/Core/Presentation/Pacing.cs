using RelicRun.Core.Determinism;

namespace RelicRun.Core.Presentation
{
    /// <summary>
    /// How long to wait between two events of a fight that has already been decided.
    /// </summary>
    /// <remarks>
    /// A fight is over before any of it is drawn: the engine resolves it, the events are
    /// recorded, and playback walks them. So none of this can change an outcome, and all of it
    /// changes how the outcome reads.
    ///
    /// The rule is worth stating plainly because it is not "one event, one beat". Every event
    /// carries the tick it happened on, and the wait between two events is how many ticks passed
    /// times a few hundred milliseconds — floored at a fraction of a beat so a burst still
    /// registers, capped at two and a half seconds so a slow exchange does not become an
    /// intermission. Playback follows the fight's own clock, which is what makes a flurry read
    /// as a flurry.
    /// </remarks>
    public sealed class Pacing
    {
        private readonly PacingRules _rules;
        private readonly int _base;
        private readonly bool _reduced;
        private readonly int _perTickAtFullSpeed;

        /// <summary>One beat at the current speed, in milliseconds.</summary>
        public readonly int StepMs;

        /// <summary>Milliseconds per tick of the fight's clock, at the current speed.</summary>
        public readonly int PerTickMs;

        /// <summary>How fast the delver has asked for it: 1, 2 or 4.</summary>
        public readonly int Speed;

        private Pacing(PacingRules rules, int step, int perTick, int fullBase, int perTickAtFullSpeed,
            bool reduced, int speed)
        {
            _rules = rules;
            _base = fullBase;
            _reduced = reduced;
            _perTickAtFullSpeed = perTickAtFullSpeed;

            StepMs = step;
            PerTickMs = perTick;
            Speed = speed;
        }

        /// <summary>
        /// The pacing a fight opens at.
        /// </summary>
        /// <param name="events">How many events the fight produced. A long one is shown faster.</param>
        /// <param name="reduced">Whether the delver's system asked for less motion.</param>
        /// <remarks>
        /// The event count decides the beat before anything is drawn, so a fight does not
        /// accelerate as it goes — it is fast or slow from its first blow, which is what stops
        /// the pace from feeling like a machine catching up with itself.
        /// </remarks>
        public static Pacing For(int events, bool reduced, int speed, PacingRules rules)
        {
            if (rules == null) rules = PacingRules.Shipped();
            if (speed < 1) speed = 1;

            bool busy = events > rules.BusyAfterEvents;

            int fullStep = reduced ? rules.ReducedStepMs : (busy ? rules.BusyStepMs : rules.StepMs);
            int fullPerTick = reduced ? 0 : (busy ? rules.BusyPerTickMs : rules.PerTickMs);

            return new Pacing(rules, Beat(fullStep, speed, rules), Divided(fullPerTick, speed),
                fullStep, fullPerTick, reduced, speed);
        }

        /// <summary>
        /// The same fight, shown at a different speed. What the speed control does.
        /// </summary>
        /// <remarks>
        /// Derived from the full-speed figures rather than from the current ones, so pressing
        /// the control three times lands exactly where starting at that speed would. Halving a
        /// number that has already been halved and rounded does not.
        /// </remarks>
        public Pacing At(int speed)
        {
            if (speed < 1) speed = 1;

            int perTick = _rules.SpeedShortensTheTickWait
                ? Divided(_perTickAtFullSpeed, speed)
                : PerTickMs;

            return new Pacing(_rules, Beat(_base, speed, _rules), perTick, _base,
                _perTickAtFullSpeed, _reduced, speed);
        }

        /// <summary>The next speed the control offers, wrapping at the end.</summary>
        public int NextSpeed()
        {
            int[] steps = _rules.SpeedSteps;

            for (int i = 0; i < steps.Length; i++)
            {
                if (steps[i] == Speed) return steps[(i + 1) % steps.Length];
            }

            return steps.Length > 0 ? steps[0] : 1;
        }

        /// <summary>
        /// The shortest gap between two events, whatever their ticks say.
        /// </summary>
        /// <remarks>
        /// Rounded JavaScript's way, halves upward. At the fastest speed on a short fight the
        /// beat is 250ms and this is 112.5 — 113 there and 112 under .NET's own rounding, which
        /// is the sort of one-millisecond difference that is invisible until it is a test.
        /// </remarks>
        public int ShortestMs
        {
            get { return JsMath.RoundToInt(StepMs * _rules.ShortestGapPercent / 100.0); }
        }

        /// <summary>
        /// How long to hold between an event on one tick and an event on another.
        /// </summary>
        /// <remarks>
        /// Under reduced motion this is a flat forty milliseconds and ignores the speed control
        /// entirely — a delver who asked for less motion is not asking to choose how much less.
        ///
        /// The floor does more work than it looks like. Two events on the same tick wait zero
        /// ticks and two events whose ticks run backwards wait fewer than none, and both come out
        /// of the arithmetic below the shortest gap — so the floor is what answers them, and no
        /// separate guard is needed. Backwards should not happen and nothing recorded does it;
        /// it is answered rather than asserted because a wrong answer here costs half a second
        /// of screen time and an exception costs a fight nobody can replay.
        /// </remarks>
        public int Between(int fromTick, int toTick)
        {
            if (_reduced) return _rules.ReducedStepMs;

            int shortest = ShortestMs;
            int waited = (toTick - fromTick) * PerTickMs;

            if (waited < shortest) return shortest;

            return waited > _rules.LongestWaitMs ? _rules.LongestWaitMs : waited;
        }

        /// <summary>How long to hold on the last event, which has nothing to wait for.</summary>
        public int Last() { return _reduced ? _rules.ReducedStepMs : ShortestMs; }

        /// <summary>
        /// The beat at a speed, never shorter than the floor.
        /// </summary>
        /// <remarks>
        /// The source floors the beat only when the speed CONTROL recomputes it, and not when a
        /// fight opens. That asymmetry reads as a floor added in one place and not backfilled to
        /// the other, and it is unreachable either way: a fight opens at a thousand or seven
        /// hundred, and a quarter of either is still six times the floor. Applied in both places
        /// here, because a rule that holds in one direction and not the other is a rule nobody
        /// can state.
        /// </remarks>
        private static int Beat(int ms, int speed, PacingRules rules)
        {
            int scaled = Divided(ms, speed);
            return scaled < rules.FloorStepMs ? rules.FloorStepMs : scaled;
        }

        /// <summary>
        /// A duration at a speed, rounded JavaScript's way.
        /// </summary>
        /// <remarks>
        /// The per-tick figure goes through this WITHOUT the floor, deliberately. A floor makes
        /// sense on a beat, which is waited on directly; the per-tick figure is multiplied by a
        /// tick count before anybody waits on it, and flooring it would make a one-tick gap and
        /// a thirty-tick gap disagree about what a tick is worth.
        /// </remarks>
        private static int Divided(int ms, int speed)
        {
            return ms == 0 ? 0 : JsMath.RoundToInt(ms / (double)speed);
        }
    }
}
