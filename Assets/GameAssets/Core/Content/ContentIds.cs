using System.Collections.Generic;

namespace RelicRun.Core.Content
{
    /// <summary>
    /// Every id the game will ask a binding for.
    /// </summary>
    /// <remarks>
    /// One place, derived from the catalogs, so that the importer which fills a binding and the
    /// audit which checks it are reading the same list. Two lists written separately would agree
    /// on the day they were written and drift on the day a relic was added, which is exactly the
    /// day nobody would look.
    ///
    /// The ids are strings rather than the typed ids the engine uses, because a binding is
    /// authored in the Editor and an Inspector shows a string. What the string means is settled
    /// here — a relic is its source key, a hall and an event are the stem of their art file, and
    /// an enemy is a species and the rank it is drawn at.
    /// </remarks>
    public static class ContentIds
    {
        private static IReadOnlyList<string> _relics;
        private static IReadOnlyList<string> _withoutIcons;
        private static IReadOnlyList<string> _halls;
        private static IReadOnlyList<string> _enemies;

        /// <summary>All fifty relics, by source key, in catalog order.</summary>
        public static IReadOnlyList<string> Relics
        {
            get
            {
                if (_relics != null) return _relics;

                var ids = new List<string>(RelicCatalog.All.Count);
                for (int i = 0; i < RelicCatalog.All.Count; i++) ids.Add(RelicCatalog.All[i].Key);

                _relics = ids;
                return _relics;
            }
        }

        /// <summary>
        /// The relics no icon was ever drawn for, which are drawn from a glyph instead.
        /// </summary>
        /// <remarks>
        /// Read straight off the generated sheet table rather than listed by hand. That is the
        /// point: this is the excuse list a relic binding is audited against, and an excuse
        /// nobody can type is an excuse nobody can abuse. Draw the icon and the exception
        /// disappears on the next generation.
        /// </remarks>
        public static IReadOnlyList<string> RelicsWithoutIcons
        {
            get
            {
                if (_withoutIcons != null) return _withoutIcons;

                var ids = new List<string>();
                for (int i = 0; i < RelicArt.All.Count; i++)
                {
                    if (!RelicArt.All[i].OnTheSheet) ids.Add(RelicCatalog.KeyOf(RelicArt.All[i].Id));
                }

                _withoutIcons = ids;
                return _withoutIcons;
            }
        }

        /// <summary>The ten halls, by the stem of their backdrop, in tier order.</summary>
        public static IReadOnlyList<string> Halls
        {
            get
            {
                if (_halls != null) return _halls;

                var ids = new List<string>(DungeonCatalog.All.Count);
                for (int i = 0; i < DungeonCatalog.All.Count; i++)
                {
                    ids.Add(Stem(DungeonCatalog.All[i].Art));
                }

                _halls = ids;
                return _halls;
            }
        }

        /// <summary>
        /// The bazaar floor's own scenery: its hall, and the merchant who stands in it.
        /// </summary>
        /// <remarks>
        /// Apart from <see cref="Halls"/> on purpose, and the separation is the point: a hall
        /// belongs to a DUNGEON and there are exactly as many of them as there are dungeons,
        /// which is a thing worth a test. The bazaar is a FLOOR — floor seven of every run,
        /// whichever dungeon it is in — so its art is neither one of the ten nor an eleventh.
        ///
        /// Addressed with the halls all the same, because they weigh what halls weigh and a run
        /// meets them once.
        /// </remarks>
        public static readonly IReadOnlyList<string> Bazaar = new[] { "hall-bazaar", "merchant" };

        /// <summary>The twelve dungeon events, by the stem of their illustration, in index order.</summary>
        public static IReadOnlyList<string> Events { get { return EventArt.All; } }

        /// <summary>
        /// Every species at every rank it is drawn at: thirteen species, three columns.
        /// </summary>
        /// <remarks>
        /// The King is not a fourteenth row — he is the Hoard-King species drawn in the boss
        /// column, which is why this is thirty-nine and not forty.
        /// </remarks>
        public static IReadOnlyList<string> Enemies
        {
            get
            {
                if (_enemies != null) return _enemies;

                var ids = new List<string>(EnemyCatalog.All.Count * EnemyCatalog.Ranks.Count);
                for (int species = 0; species < EnemyCatalog.All.Count; species++)
                {
                    for (int column = 0; column < EnemyCatalog.Ranks.Count; column++)
                    {
                        ids.Add(EnemyCatalog.ArtId(species, column));
                    }
                }

                _enemies = ids;
                return _enemies;
            }
        }

        /// <summary>
        /// The name an art path is bound under: no folder, no extension.
        /// </summary>
        /// <remarks>
        /// <c>assets/hall-hoard.png</c> is bound as <c>hall-hoard</c>. The path in the catalog is
        /// where the browser game found the file; where Unity keeps it is Unity's business, and
        /// baking the source's folder into the binding would tie one to the other for no reason.
        /// </remarks>
        public static string Stem(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";

            int start = path.LastIndexOf('/') + 1;
            int back = path.LastIndexOf('\\') + 1;
            if (back > start) start = back;

            int dot = path.LastIndexOf('.');
            int end = dot > start ? dot : path.Length;

            return path.Substring(start, end - start);
        }
    }
}
