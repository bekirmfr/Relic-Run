using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Editor.Importers;
using RelicRun.Game.Data;
using UnityEditor;
using UnityEngine;

namespace RelicRun.Tests.Editor
{
    /// <summary>
    /// The other half of the content gate: the assets themselves.
    /// </summary>
    /// <remarks>
    /// <see cref="BindingAudit"/> states the rule and is tested by <c>dotnet test</c> in a
    /// second, because it is arithmetic over two lists of strings. What it cannot do is look at
    /// the project — only Unity can say whether a <c>Sprite</c> reference resolves, which cell
    /// of a sheet it points at, or how a texture was imported. That is what runs here.
    ///
    /// The pixel checks matter more than the audit does. An audit can only say a relic is bound
    /// to SOMETHING; it would pass just as happily with every icon off by one row, and the port
    /// would ship with the wrong picture on every relic in the game. So a handful of cells are
    /// pinned to coordinates worked out by hand from the source's own table, which is the one
    /// thing here that was not derived from the same place the importer derives from.
    /// </remarks>
    [TestFixture]
    public class ContentAssetTests
    {
        private GameContent _content;

        [OneTimeSetUp]
        public void LoadTheContent()
        {
            _content = AssetDatabase.LoadAssetAtPath<GameContent>(ContentPaths.GameContentAsset);
        }

        [Test]
        public void TheContentAssetIsWhereEverythingWillLookForIt()
        {
            Assert.That(_content, Is.Not.Null,
                ContentPaths.GameContentAsset + " is missing — run Tools > Relic Run > Import Content");

            Assert.That(_content.Unbound(), Is.Empty, "references nobody filled in");
        }

        /// <summary>
        /// Every id the catalogs will ask for is bound exactly once, to something.
        /// </summary>
        /// <remarks>
        /// The whole rule, applied to the real assets. A failure here names what broke and what
        /// to do about it; the five ways to break it are documented on <see cref="BindingAudit"/>.
        /// </remarks>
        [Test]
        public void EverythingTheCatalogsAskForIsBound()
        {
            string trouble = _content.Audit();
            Assert.That(trouble, Is.Empty, trouble);
        }

        /// <summary>
        /// Every book holds as many rows as the catalogs have ids, less the excused.
        /// </summary>
        /// <remarks>
        /// Stated as counts as well as by the audit, because the audit passes when a book is
        /// empty and nothing is needed — and a book that lost its contents to a bad re-run looks
        /// exactly like that.
        /// </remarks>
        [Test]
        public void EveryBookIsAsLongAsItsCatalog()
        {
            Assert.That(_content.RelicIcons.Entries.Count, Is.EqualTo(RelicCatalog.All.Count - 1),
                "fifty relics, one of them drawn from a glyph");
            Assert.That(_content.Halls.Entries.Count, Is.EqualTo(DungeonCatalog.All.Count));
            Assert.That(_content.Events.Entries.Count, Is.EqualTo(EventArt.All.Count));
            Assert.That(_content.Enemies.Entries.Count, Is.EqualTo(ContentIds.Enemies.Count));

            Assert.That(_content.Locales.Languages.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(_content.HeroPack.IsBound, Is.True);
        }

        /// <summary>
        /// The cells are cut from the pixels they are supposed to be cut from.
        /// </summary>
        /// <remarks>
        /// Worked out by hand from the source's <c>RELIC_ICON</c> table and written down as
        /// numbers, on purpose. Recomputing them from <see cref="RelicArt"/> would only prove the
        /// importer agrees with itself: it flips the row the same way, so a flip in the wrong
        /// direction would satisfy both. Row 0 belongs at the TOP of the texture and Unity counts
        /// from the bottom, which is the whole of what can go wrong here and the whole of what
        /// these four numbers pin down.
        /// </remarks>
        [Test]
        public void TheRelicCellsAreCutFromTheRightPixels()
        {
            // id, column, row, and the pixel corner that follows from a 480x384 sheet of 48s.
            Cell("iron", 0, 336);        // [0, 0] — the top-left cell
            Cell("whetstone", 0, 240);   // [0, 2]
            Cell("duelist", 336, 288);   // [7, 1] — furthest right of the drawn cells
            Cell("hexthread", 288, 0);   // [6, 7] — the bottom row
        }

        [Test]
        public void TheEnemyCellsAreCutFromTheRightPixels()
        {
            // The rat is drawn on row 1, not row 0. Its index says otherwise and its index is wrong.
            Foe("rat/guard", 0, 704);
            Foe("rat/boss", 128, 704);
            Foe("bat/guard", 0, 768);    // row 0 — the top of an 832-tall sheet
            Foe("crown/boss", 128, 0);   // row 12 — the bottom
        }

        private void Cell(string id, int x, int y)
        {
            Sprite sprite = _content.RelicIcons.Get(id);
            Assert.That(sprite, Is.Not.Null, id + " is not bound");

            Assert.That(sprite.rect, Is.EqualTo(new Rect(x, y, RelicArt.SheetCell, RelicArt.SheetCell)),
                id + " is cut from the wrong cell");
        }

        private void Foe(string id, int x, int y)
        {
            Sprite sprite = _content.Enemies.Get(id);
            Assert.That(sprite, Is.Not.Null, id + " is not bound");

            Assert.That(sprite.rect,
                Is.EqualTo(new Rect(x, y, EnemyCatalog.SheetCell, EnemyCatalog.SheetCell)),
                id + " is cut from the wrong cell");
        }

        /// <summary>No two ids share a cell, and every cell is on the grid.</summary>
        /// <remarks>
        /// The four pinned cells above catch a flip or a transposition, which move everything at
        /// once. This catches the one that moves a single icon: a cell nudged off the grid, or
        /// two relics quietly pointed at the same picture.
        /// </remarks>
        [Test]
        public void NoTwoCellsOverlapAndNoneIsOffTheGrid()
        {
            OnTheGrid(_content.RelicIcons, RelicArt.SheetCell, RelicArt.SheetColumns, RelicArt.SheetRows);
            OnTheGrid(_content.Enemies, EnemyCatalog.SheetCell, EnemyCatalog.SheetColumns,
                EnemyCatalog.SheetRows);
        }

        private static void OnTheGrid(SpriteBook book, int cell, int columns, int rows)
        {
            var taken = new Dictionary<string, string>();

            foreach (SpriteBook.Entry entry in book.Entries)
            {
                Assert.That(entry.Sprite, Is.Not.Null, entry.Id + " is not bound");

                Rect rect = entry.Sprite.rect;

                // Compared as whole pixels. A cell that landed on a fraction of one would fail
                // the width check below long before the rounding here could hide anything.
                int x = Mathf.RoundToInt(rect.x);
                int y = Mathf.RoundToInt(rect.y);
                string where = x + "," + y;

                Assert.That(Mathf.RoundToInt(rect.width), Is.EqualTo(cell),
                    entry.Id + " is not a whole cell wide");
                Assert.That(Mathf.RoundToInt(rect.height), Is.EqualTo(cell),
                    entry.Id + " is not a whole cell tall");
                Assert.That(x % cell, Is.Zero, entry.Id + " is off the grid horizontally");
                Assert.That(y % cell, Is.Zero, entry.Id + " is off the grid vertically");
                Assert.That(x, Is.InRange(0, (columns - 1) * cell), entry.Id + " is off the sheet");
                Assert.That(y, Is.InRange(0, (rows - 1) * cell), entry.Id + " is off the sheet");

                string already;
                Assert.That(taken.TryGetValue(where, out already), Is.False,
                    entry.Id + " draws the same cell as " + already);

                taken[where] = entry.Id;
            }
        }

        /// <summary>
        /// The textures are imported the way pixel art has to be.
        /// </summary>
        /// <remarks>
        /// Every one of these is a setting somebody can change in the Inspector in two seconds
        /// and not notice for a week. Bilinear filtering turns a 48-pixel icon into porridge;
        /// compression puts block artefacts in flat colour; a tight sprite mesh crops to the
        /// visible pixels and so moves the centre of every icon by a different amount.
        /// </remarks>
        [Test]
        public void EveryTextureIsImportedForPixelArt()
        {
            var sheets = new[] { ContentPaths.RelicIconSheet, ContentPaths.EnemySheet };
            foreach (string path in sheets) Imported(path, SpriteImportMode.Multiple);

            foreach (string hall in ContentIds.Halls)
            {
                Imported(ContentPaths.Halls + "/" + hall + ".png", SpriteImportMode.Single);
            }

            foreach (string art in ContentIds.Events)
            {
                Imported(ContentPaths.Events + "/" + art + ".png", SpriteImportMode.Single);
            }
        }

        private static void Imported(string path, SpriteImportMode mode)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.That(importer, Is.Not.Null, path + " is not imported as a texture");

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            Assert.That(settings.textureType, Is.EqualTo(TextureImporterType.Sprite), path);
            Assert.That(settings.spriteMode, Is.EqualTo((int)mode), path);
            Assert.That(settings.filterMode, Is.EqualTo(FilterMode.Point), path + " is not point filtered");
            Assert.That(settings.mipmapEnabled, Is.False, path + " has mipmaps");
            Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect), path);
            Assert.That(settings.spriteExtrude, Is.Zero, path + " is extruded");
            Assert.That(settings.alphaIsTransparency, Is.True, path);

            Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed),
                path + " is compressed");
        }

        /// <summary>
        /// The text assets are the languages they claim to be, and English is among them.
        /// </summary>
        /// <remarks>
        /// English is not a preference: <see cref="Locale"/> falls back to it for every key a
        /// translation is missing, so a build without it shows raw keys the moment a translator
        /// falls behind.
        /// </remarks>
        [Test]
        public void EveryLanguageIsTheFileItSaysItIs()
        {
            bool english = false;

            foreach (LocaleBook.Translation translation in _content.Locales.Languages)
            {
                Assert.That(translation.Strings, Is.Not.Null, translation.Language + " has no strings");
                Assert.That(translation.Strings.name, Is.EqualTo(translation.Language),
                    "declared as " + translation.Language + " but the file is " + translation.Strings.name);
                Assert.That(translation.Strings.text, Is.Not.Empty, translation.Language + " is empty");

                if (translation.Language == LocaleBook.Fallback) english = true;
            }

            Assert.That(english, Is.True, "no English to fall back to");
        }

        [Test]
        public void TheHeroPackCameAcrossWhole()
        {
            TextAsset pack = _content.HeroPack.Json;
            Assert.That(pack, Is.Not.Null);

            // Not parsed here — that is the loader's job, and HeroCompositorTests already
            // composes the shipped pack. This only asks whether the file arrived.
            Assert.That(pack.text, Does.Contain("\"stack\""), "this is not the hero pack");
            Assert.That(pack.text.Length, Is.GreaterThan(10000), "the pack came across truncated");
        }
    }
}
