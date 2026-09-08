using System.Collections.Generic;
using RelicRun.Core.Content;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace RelicRun.Editor.Importers
{
    /// <summary>One cell of a sheet: what it is called, and where it is.</summary>
    public struct SheetCell
    {
        /// <summary>The content id this cell draws — <c>whetstone</c>, <c>rat/guard</c>.</summary>
        public string Id;

        /// <summary>The sub-sprite's name, which is the id made safe for an asset name.</summary>
        public string Name;

        public int Column;
        public int Row;
    }

    /// <summary>
    /// Slicing the two sprite sheets into the cells the catalogs name.
    /// </summary>
    /// <remarks>
    /// Grid slicing, but not Unity's grid slicing. The built-in one cuts every cell of the grid
    /// and names them <c>sheet_0</c> upward, which would give the relic sheet thirty-one empty
    /// sprites and leave the join between a cell and the relic that draws it to a person
    /// counting rows. This cuts only the cells something asks for and names each one after what
    /// asks for it, so the binding afterwards is a lookup rather than a guess.
    /// </remarks>
    public static class SpriteSheet
    {
        /// <summary>The forty-nine relic icons that were drawn. Debt of Flesh has no cell.</summary>
        public static List<SheetCell> RelicCells()
        {
            var cells = new List<SheetCell>();

            foreach (RelicArtDef art in RelicArt.All)
            {
                if (!art.OnTheSheet) continue;

                string id = RelicCatalog.KeyOf(art.Id);
                cells.Add(new SheetCell { Id = id, Name = SafeName(id), Column = art.Column, Row = art.Row });
            }

            return cells;
        }

        /// <summary>Thirteen species across three ranks, every cell of the sheet.</summary>
        public static List<SheetCell> EnemyCells()
        {
            var cells = new List<SheetCell>();

            for (int species = 0; species < EnemyCatalog.All.Count; species++)
            {
                for (int column = 0; column < EnemyCatalog.Ranks.Count; column++)
                {
                    string id = EnemyCatalog.ArtId(species, column);

                    // The row is the species' own, which is NOT its bestiary index.
                    cells.Add(new SheetCell
                    {
                        Id = id,
                        Name = SafeName(id),
                        Column = column,
                        Row = EnemyCatalog.Get(species).SheetRow,
                    });
                }
            }

            return cells;
        }

        /// <summary>An id as a sub-sprite name: <c>rat/guard</c> becomes <c>rat_guard</c>.</summary>
        /// <remarks>
        /// A sub-asset name with a slash in it is a path separator to half of Unity's tooling.
        /// Nothing binds by this name — the book stores the id and a reference to the object —
        /// so it only has to be unique and readable.
        /// </remarks>
        public static string SafeName(string id) { return id.Replace('/', '_'); }

        /// <summary>
        /// Cuts a sheet into named cells and reimports it.
        /// </summary>
        /// <param name="cell">Side of one cell, in pixels.</param>
        /// <param name="rows">How many rows the sheet has, which fixes where the top is.</param>
        /// <remarks>
        /// The height is taken from the tables rather than from the texture, because a cell's row
        /// has to be flipped — the tables count rows from the top and Unity's texture space
        /// counts from the bottom. Doing that arithmetic against a number the tables also supply
        /// would make the flip agree with itself even if both were wrong; it is safe here only
        /// because <c>TheSheetsAreTheSizeTheTablesSayTheyAre</c> checks the tables against the
        /// PNG's own header first.
        /// </remarks>
        public static void Cut(string assetPath, IList<SheetCell> cells, int cell, int rows)
        {
            var factory = new SpriteDataProviderFactories();
            factory.Init();

            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            ISpriteEditorDataProvider provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null)
            {
                Debug.LogError("no sprite data provider for " + assetPath);
                return;
            }

            provider.InitSpriteEditorDataProvider();

            // Cells that already exist keep their id, so a re-run does not orphan every
            // reference made by the run before it.
            var known = new Dictionary<string, SpriteRect>();
            foreach (SpriteRect existing in provider.GetSpriteRects())
            {
                if (!known.ContainsKey(existing.name)) known[existing.name] = existing;
            }

            int height = rows * cell;
            var rects = new SpriteRect[cells.Count];
            var names = new List<SpriteNameFileIdPair>(cells.Count);

            for (int i = 0; i < cells.Count; i++)
            {
                SheetCell wanted = cells[i];

                SpriteRect rect;
                if (!known.TryGetValue(wanted.Name, out rect)) rect = new SpriteRect();

                rect.name = wanted.Name;
                rect.rect = new Rect(wanted.Column * cell, height - (wanted.Row + 1) * cell, cell, cell);
                rect.alignment = SpriteAlignment.Center;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.border = Vector4.zero;

                rects[i] = rect;
                names.Add(new SpriteNameFileIdPair(rect.name, rect.spriteID));
            }

            provider.SetSpriteRects(rects);

            // Without the name-to-id map the sub-assets are recreated on every reimport under
            // fresh file ids, and every book pointing at them goes null overnight.
            var ids = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (ids != null) ids.SetNameFileIdPairs(names);

            provider.Apply();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }
    }
}
