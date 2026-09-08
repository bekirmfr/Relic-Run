using System.Collections.Generic;
using RelicRun.Core.Content;
using UnityEngine;

namespace RelicRun.Game.Data
{
    /// <summary>The twelve dungeon event illustrations, by event index.</summary>
    [CreateAssetMenu(menuName = "Relic Run/Art/Events", fileName = "EventArt")]
    public sealed class EventBook : SpriteBook
    {
        public override string What { get { return "events"; } }

        public override IReadOnlyList<string> Needed { get { return ContentIds.Events; } }

        public Sprite For(int index) { return Get(EventArt.Get(index)); }
    }
}
