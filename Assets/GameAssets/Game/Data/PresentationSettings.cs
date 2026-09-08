using UnityEngine;

namespace RelicRun.Game.Data
{
    /// <summary>
    /// How fast a fight is shown. The one thing in the content layer that is authored.
    /// </summary>
    /// <remarks>
    /// Everything else the game knows is extracted from the source and gated — change a relic's
    /// numbers and a hundred recorded fights disagree with you. None of that applies here. A
    /// fight is already decided by the time it is shown; these numbers only govern how long the
    /// showing takes, so they are the one place a person is meant to sit with the Inspector open
    /// and change a value until it feels right.
    ///
    /// The defaults are the source's, so the port starts where the game was rather than where
    /// somebody guessed. A beat is a second, or seven tenths once a fight runs long enough that
    /// a second each would outstay its welcome, or as near to nothing as makes no difference for
    /// a delver who has asked their system for less motion.
    /// </remarks>
    [CreateAssetMenu(menuName = "Relic Run/Presentation", fileName = "Presentation")]
    public sealed class PresentationSettings : ScriptableObject
    {
        [Header("Beats")]
        [Tooltip("One combat beat, in milliseconds.")]
        [Min(1)] public int StepMs = 1000;

        [Tooltip("A beat in a long fight, where a full second each would outstay its welcome.")]
        [Min(1)] public int BusyStepMs = 700;

        [Tooltip("How many events make a fight long.")]
        [Min(1)] public int BusyAfterEvents = 34;

        [Tooltip("A beat for a delver who asked their system for reduced motion.")]
        [Min(1)] public int ReducedStepMs = 40;

        [Tooltip("The shortest a beat can be however far the speed control is pushed.")]
        [Min(1)] public int FloorStepMs = 30;

        [Header("Holds")]
        [Tooltip("How long a struck figure holds its flinch.")]
        [Min(0)] public int HoldMs = 500;

        [Tooltip("The same, in a long fight.")]
        [Min(0)] public int BusyHoldMs = 350;

        [Tooltip("The same, under reduced motion. Zero means no hold at all.")]
        [Min(0)] public int ReducedHoldMs = 0;

        [Header("Flourishes")]
        [Tooltip("How much of a beat a number takes to fly off a struck figure.")]
        [Range(0f, 1f)] public float FlyFraction = 0.45f;

        [Tooltip("What the speed control cycles through. The last one wraps back to the first.")]
        public int[] SpeedSteps = { 1, 2, 4 };
    }
}
