using System.Collections.Generic;
using RelicRun.Core.Content;
using UnityEngine;

namespace RelicRun.Game.Data
{
    /// <summary>
    /// The relic icons, sliced from <c>relic-icons.png</c>.
    /// </summary>
    /// <remarks>
    /// Forty-nine of the fifty. Debt of Flesh was never given a cell and draws from its glyph
    /// instead, which is why <see cref="Excused"/> is not empty — and why it is read off the
    /// generated sheet table rather than typed here, so that drawing the missing icon retires
    /// the exception on its own.
    /// </remarks>
    [CreateAssetMenu(menuName = "Relic Run/Art/Relic Icons", fileName = "RelicIcons")]
    public sealed class RelicIconBook : SpriteBook
    {
        public override string What { get { return "relic icons"; } }

        public override IReadOnlyList<string> Needed { get { return ContentIds.Relics; } }

        public override IReadOnlyList<string> Excused { get { return ContentIds.RelicsWithoutIcons; } }

        /// <summary>The icon for a relic, or null when it draws from a glyph.</summary>
        public Sprite For(RelicId id) { return Get(RelicCatalog.KeyOf(id)); }
    }
}
