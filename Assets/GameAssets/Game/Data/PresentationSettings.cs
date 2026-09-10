using RelicRun.Core.Presentation;
using UnityEngine;

namespace RelicRun.Game.Data
{
    /// <summary>
    /// How fast a fight is shown. The one thing in the content layer that is authored.
    /// </summary>
    /// <remarks>
    /// Everything else the game knows is extracted from the source and gated — change a relic's
    /// numbers and a hundred recorded fights disagree with you. None of that applies here. A
    /// fight is already decided by the time it is shown; these numbers govern only how long the
    /// showing takes, so this is the one place a person is meant to sit with the Inspector open
    /// and change a value until it feels right.
    ///
    /// The fields mirror <see cref="PacingRules"/> one for one and hand it over through
    /// <see cref="ToPacing"/>. The arithmetic lives in Core, where <c>dotnet test</c> can reach
    /// it in a second; this is only where a person changes the numbers.
    /// </remarks>
    [CreateAssetMenu(menuName = "Relic Run/Presentation", fileName = "Presentation")]
    public sealed class PresentationSettings : ScriptableObject
    {
        [Header("Beats")]
        [Tooltip("One beat, in milliseconds.")]
        [Min(1)] public int StepMs = 1000;

        [Tooltip("A beat in a long fight, where a second each would outstay its welcome.")]
        [Min(1)] public int BusyStepMs = 700;

        [Tooltip("How many events make a fight long. The count decides the beat before the " +
                 "first blow is drawn, so a fight does not accelerate as it goes.")]
        [Min(1)] public int BusyAfterEvents = 34;

        [Tooltip("Everything, for a delver whose system asked for less motion. The speed " +
                 "control does nothing at all in that case.")]
        [Min(1)] public int ReducedStepMs = 40;

        [Tooltip("The shortest a beat can be however far the speed control is pushed.")]
        [Min(1)] public int FloorStepMs = 30;

        [Header("The fight's own clock")]
        [Tooltip("Milliseconds per tick of the ATB clock. Playback follows the fight's own " +
                 "pace: two events on one tick land together, a slow exchange takes its time.")]
        [Min(0)] public int PerTickMs = 500;

        [Tooltip("The same, in a long fight.")]
        [Min(0)] public int BusyPerTickMs = 350;

        [Tooltip("The longest a single wait may be. Twenty ticks between blows would be ten " +
                 "seconds of an empty screen, which reads as the game having hung.")]
        [Min(1)] public int LongestWaitMs = 2600;

        [Tooltip("The shortest gap between two events, as hundredths of a beat.")]
        [Range(1, 100)] public int ShortestGapPercent = 45;

        [Header("The speed control")]
        [Tooltip("What the control cycles through. The last one wraps back to the first.")]
        public int[] SpeedSteps = { 1, 2, 4 };

        [Header("The hall")]
        [Tooltip("How long the walk to a new foe takes. Not divided by the speed — this is an " +
                 "animation with a length of its own, and cutting the timer would cut the walk. " +
                 "Skipped entirely under reduced motion.")]
        [Min(0)] public int WalkMs = 3350;

        [Tooltip("How long the hall itself takes to slide. Shorter than the walk on purpose, so " +
                 "the room has settled by the time the next foe is standing in it.")]
        [Min(0)] public int PanMs = 2600;

        [Tooltip("How long the card a fight opens on is held, AFTER the walk rather than during " +
                 "it. The card is opaque, so raising it as the walk set off would play the whole " +
                 "approach behind it. Skipped by whatever skips the walk.")]
        [Min(0)] public int IntroMs = 3000;

        [Tooltip("Whether pressing the control also shortens the long waits. The source only " +
                 "shortened the gaps between events on the same tick, which was most likely a " +
                 "slip: it changed the least of what a delver pressing it wanted skipped.")]
        public bool SpeedShortensTheTickWait = true;

        /// <summary>These numbers, as Core understands them.</summary>
        public PacingRules ToPacing()
        {
            return new PacingRules
            {
                StepMs = StepMs,
                BusyStepMs = BusyStepMs,
                BusyAfterEvents = BusyAfterEvents,
                ReducedStepMs = ReducedStepMs,
                FloorStepMs = FloorStepMs,
                PerTickMs = PerTickMs,
                BusyPerTickMs = BusyPerTickMs,
                LongestWaitMs = LongestWaitMs,
                ShortestGapPercent = ShortestGapPercent,
                SpeedSteps = SpeedSteps,
                SpeedShortensTheTickWait = SpeedShortensTheTickWait,
                WalkMs = WalkMs,
                PanMs = PanMs,
                IntroMs = IntroMs,
            };
        }
    }
}
