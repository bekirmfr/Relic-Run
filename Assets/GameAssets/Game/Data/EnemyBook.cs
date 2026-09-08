using System.Collections.Generic;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using UnityEngine;

namespace RelicRun.Game.Data
{
    /// <summary>
    /// The bestiary, sliced from <c>enemies-hoard.png</c>: thirteen species in three ranks.
    /// </summary>
    /// <remarks>
    /// A foe is drawn by the row its species owns and the column its rank owns, and neither is
    /// its bestiary index — the artist drew the species in an order of their own, and the King
    /// has no column, being a boss with a bigger hat. Both facts are in
    /// <see cref="EnemyCatalog"/>, so nothing here has to know them.
    /// </remarks>
    [CreateAssetMenu(menuName = "Relic Run/Art/Enemies", fileName = "EnemyArt")]
    public sealed class EnemyBook : SpriteBook
    {
        public override string What { get { return "enemies"; } }

        public override IReadOnlyList<string> Needed { get { return ContentIds.Enemies; } }

        /// <summary>The art for a foe, at the rank it is fielded at.</summary>
        public Sprite For(EnemyState foe)
        {
            return foe == null ? null : Get(EnemyCatalog.ArtId(foe.SpeciesIndex, foe.Variant));
        }

        public Sprite For(int species, int variant) { return Get(EnemyCatalog.ArtId(species, variant)); }
    }
}
