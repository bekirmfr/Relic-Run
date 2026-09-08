using UnityEngine;

namespace RelicRun.Game.Data
{
    /// <summary>
    /// The delver's wardrobe, as the text the compositor reads.
    /// </summary>
    /// <remarks>
    /// A hero is not a stack of sprites and cannot be one. Two of the role keys are not colours
    /// at all — a shadow pixel means "whatever is beneath this, one tone darker" — and the
    /// outline is drawn around a silhouette that nothing knows until every layer is down. So the
    /// parts stay as the pixel grids they were authored as, and <c>HeroCompositor</c> stacks them
    /// at runtime into a grid of indices for a shader to colour.
    ///
    /// Which is why this holds a <see cref="TextAsset"/> and not an atlas. There is no atlas to
    /// hold: the pack is ninety kilobytes of text, it slices into nothing, and pre-rendering it
    /// would mean pre-rendering every outfit against every palette.
    /// </remarks>
    [CreateAssetMenu(menuName = "Relic Run/Hero Pack", fileName = "HeroPack")]
    public sealed class HeroPackAsset : ScriptableObject
    {
        [SerializeField]
        [Tooltip("hero-pack.json, imported as text. Filled by Tools ▸ Relic Run ▸ Import Content.")]
        private TextAsset _pack;

        /// <summary>The pack, unparsed. Parsing it is the loader's job, once, at startup.</summary>
        public TextAsset Json { get { return _pack; } }

        public bool IsBound { get { return _pack != null; } }

        /// <summary>Points this at a pack. The importer's one way in.</summary>
        /// <remarks>
        /// Public on a runtime type for an Editor caller's sake, which is a small ugliness with
        /// a smaller alternative than it looks. Writing the field through a
        /// <c>SerializedObject</c> instead would mean naming it as a string, and a rename would
        /// then break the importer silently rather than at compile time.
        /// </remarks>
        public void Bind(TextAsset pack) { _pack = pack; }
    }
}
