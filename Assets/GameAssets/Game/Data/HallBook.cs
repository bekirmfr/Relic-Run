using System.Collections.Generic;
using RelicRun.Core.Content;
using UnityEngine;

namespace RelicRun.Game.Data
{
    /// <summary>The ten hall backdrops, one per dungeon tier.</summary>
    [CreateAssetMenu(menuName = "Relic Run/Art/Halls", fileName = "HallArt")]
    public sealed class HallBook : SpriteBook
    {
        public override string What { get { return "halls"; } }

        public override IReadOnlyList<string> Needed { get { return ContentIds.Halls; } }

        /// <summary>The backdrop for a dungeon tier, counting from one as the catalog does.</summary>
        public Sprite For(int tier) { return Get(ContentIds.Stem(DungeonCatalog.Get(tier).Art)); }
    }
}
