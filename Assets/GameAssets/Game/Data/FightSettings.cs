using RelicRun.Game.Presentation;
using UnityEngine;

namespace RelicRun.Game.Data
{
    /// <summary>
    /// The fight the harness shows: who, against what, at what pace.
    /// </summary>
    /// <remarks>
    /// An ASSET rather than fields on the harness, and the reason is a mistake that had already
    /// happened by the time this existed. The harness lives inside a generated hierarchy —
    /// <c>Build Fight Scene</c> deletes the whole <c>Fight</c> child and adds the component back
    /// from scratch — so every setting typed into its inspector was thrown away by the next
    /// rebuild. Authored data inside generated output has exactly one lifetime, and it is shorter
    /// than the person editing it expects.
    ///
    /// So the generator wires a reference and never touches what is behind it, the same
    /// arrangement <see cref="PresentationSettings"/> already has. Two things follow that are
    /// worth having anyway: the settings can be committed, so "the fight that shows the bug" is a
    /// thing you can hand somebody; and the asset can be duplicated, so a set of interesting
    /// fights can sit side by side and be swapped in a single field.
    ///
    /// Everything here is a scaffold's business — none of it decides anything about the game. A
    /// fight is resolved by the engine from a seed and a shelf, and these are the seed and the
    /// shelf.
    /// </remarks>
    [CreateAssetMenu(menuName = "Relic Run/Fight", fileName = "Fight")]
    public sealed class FightSettings : ScriptableObject
    {
        [Header("The fight")]
        [Tooltip("Fixed, so the same fight can be watched twice and talked about.")]
        public uint Seed = 0x5E1F00D;

        [Tooltip("Which floor of the first hall. Seven is the bazaar and has no fight.")]
        [Range(1, 13)] public int Floor = 1;

        [Header("The delver")]
        public DelverSetup Delver = new DelverSetup();

        [Header("The opposition")]
        public FoeSetup Foes = new FoeSetup();

        [Header("Watching")]
        [Tooltip("Skips the walk down the hall, which is three and a half seconds of scenery.")]
        public bool SkipIntro;

        [Tooltip("How fast the fight is read out. The pacing decides the beat; this multiplies it.")]
        [Range(1, 4)] public int Speed = 1;

        [Tooltip("What a delver who asked their system for less motion would see.")]
        public bool ReducedMotion;

        /// <summary>What this fight is, in one line, for the log.</summary>
        /// <remarks>
        /// Printed when a fight starts, because the first question about a strange-looking fight
        /// is what it was — and with the settings in an asset, the answer is no longer visible on
        /// the object being watched.
        /// </remarks>
        public string Describe()
        {
            return name + ": seed " + Seed + ", floor " + Floor + ", " +
                   (Delver.Shelf == null ? 0 : Delver.Shelf.Length) + " relics" +
                   (Delver.OverrideStats ? " (stats overridden)" : " (level " + Delver.Level + ")") +
                   " against " + (Foes.Override ? "an authored pack" : "the floor's own pack");
        }
    }
}
