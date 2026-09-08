using System.Collections.Generic;
using RelicRun.Core.Content;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace RelicRun.Game.Data
{
    /// <summary>
    /// The twelve dungeon event illustrations, by event index, fetched when one is drawn.
    /// </summary>
    /// <remarks>
    /// Addressed for the same reason the halls are, and more so: twelve illustrations weigh two
    /// and a half megabytes between them, and a delver sees one at a time and often none at all.
    /// </remarks>
    [CreateAssetMenu(menuName = "Relic Run/Art/Events", fileName = "EventArt")]
    public sealed class EventBook : AddressBook
    {
        public override string What { get { return "events"; } }

        public override IReadOnlyList<string> Needed { get { return ContentIds.Events; } }

        public AssetReferenceSprite For(int index) { return Get(EventArt.Get(index)); }
    }
}
