using System.Collections.Generic;
using RelicRun.Core.Content;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace RelicRun.Game.Data
{
    /// <summary>
    /// The ten hall backdrops, one per dungeon tier, fetched as the delver reaches them.
    /// </summary>
    /// <remarks>
    /// Addressed rather than referenced because of what they weigh: 650 by 181 uncompressed is
    /// four hundred and seventy kilobytes each, four and a half megabytes for the set, and a run
    /// descends ONE hall. Holding them directly would put the whole dungeon in memory to draw a
    /// single corridor.
    /// </remarks>
    [CreateAssetMenu(menuName = "Relic Run/Art/Halls", fileName = "HallArt")]
    public sealed class HallBook : AddressBook
    {
        public override string What { get { return "halls"; } }

        public override IReadOnlyList<string> Needed { get { return ContentIds.Halls; } }

        /// <summary>Where to find a tier's backdrop, counting from one as the catalog does.</summary>
        public AssetReferenceSprite For(int tier)
        {
            return Get(ContentIds.Stem(DungeonCatalog.Get(tier).Art));
        }
    }
}
