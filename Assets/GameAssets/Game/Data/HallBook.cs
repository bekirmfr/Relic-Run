using System.Collections.Generic;
using RelicRun.Core.Content;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace RelicRun.Game.Data
{
    /// <summary>
    /// The hall backdrops — one per dungeon tier — and the bazaar floor's own scenery.
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

        /// <summary>
        /// The ten dungeons' halls, and the two the bazaar floor needs.
        /// </summary>
        /// <remarks>
        /// Two lists rather than one, joined here. <c>ContentIds.Halls</c> is exactly as long as
        /// the dungeon catalog and a test says so; the bazaar is a floor rather than a dungeon,
        /// so folding its art into that list would cost the test its meaning.
        /// </remarks>
        public override IReadOnlyList<string> Needed
        {
            get
            {
                if (_needed != null) return _needed;

                var all = new List<string>(ContentIds.Halls.Count + ContentIds.Bazaar.Count);

                all.AddRange(ContentIds.Halls);
                all.AddRange(ContentIds.Bazaar);

                _needed = all;
                return _needed;
            }
        }

        private IReadOnlyList<string> _needed;

        /// <summary>Where to find a tier's backdrop, counting from one as the catalog does.</summary>
        public AssetReferenceSprite For(int tier)
        {
            return Get(ContentIds.Stem(DungeonCatalog.Get(tier).Art));
        }

        /// <summary>
        /// The bazaar's own hall, which every run descends into on floor seven.
        /// </summary>
        /// <remarks>
        /// Not the dungeon's. The source swaps the backdrop for the shop floor whatever hall the
        /// run is in, and it is the one place in a delve where the walls change without the
        /// delver having chosen a different dungeon.
        /// </remarks>
        public AssetReferenceSprite Bazaar
        {
            get { return Get(ContentIds.Bazaar[0]); }
        }

        /// <summary>And whoever is standing in it.</summary>
        public AssetReferenceSprite Merchant
        {
            get { return Get(ContentIds.Bazaar[1]); }
        }
    }
}
