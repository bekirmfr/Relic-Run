using System.Collections.Generic;
using System.IO;
using RelicRun.Core.Content;
using RelicRun.Game.Data;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace RelicRun.Editor.Importers
{
    /// <summary>
    /// Brings the source drop into the project and binds it to the catalogs.
    /// </summary>
    /// <remarks>
    /// Run from <b>Tools ▸ Relic Run ▸ Import Content</b>. It copies across only the art the
    /// catalogs ask for, sets every texture to the settings pixel art needs, cuts the two sheets
    /// into cells named after what draws them, fills the books, and then audits its own work.
    ///
    /// It is written to be run again. Every step overwrites rather than appends, cells keep the
    /// file ids they were given the first time, and nothing is deleted — so a second run after
    /// a new relic is added does the right thing, and a second run after nothing changed does
    /// nothing at all.
    ///
    /// The audit at the end is the point. An importer that guesses from filenames is exactly the
    /// kind of thing that half-works quietly: one hall misnamed and the ninth floor draws the
    /// eighth's backdrop, in a game where the delver has never seen the ninth floor before.
    /// </remarks>
    public static class ContentImporter
    {
        [MenuItem("Tools/Relic Run/Import Content", priority = 100)]
        public static void Import()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Relic Run", "copying the source drop", 0f);
                int copied = Copy();

                EditorUtility.DisplayProgressBar("Relic Run", "importing", 0.3f);
                AssetDatabase.Refresh();

                EditorUtility.DisplayProgressBar("Relic Run", "setting texture import", 0.4f);
                Configure();

                EditorUtility.DisplayProgressBar("Relic Run", "cutting the sheets", 0.6f);
                SpriteSheet.Cut(ContentPaths.RelicIconSheet, SpriteSheet.RelicCells(),
                    RelicArt.SheetCell, RelicArt.SheetRows);
                SpriteSheet.Cut(ContentPaths.EnemySheet, SpriteSheet.EnemyCells(),
                    EnemyCatalog.SheetCell, EnemyCatalog.SheetRows);

                EditorUtility.DisplayProgressBar("Relic Run", "baking the fonts", 0.75f);
                var fallbacks = new List<TMP_FontAsset>();
                List<FontBook.Face> faces = FontImporter.Import(fallbacks);

                EditorUtility.DisplayProgressBar("Relic Run", "binding", 0.9f);
                GameContent content = Bind(faces, fallbacks);

                AssetDatabase.SaveAssets();
                Report(content, copied);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>Runs the audit on its own, without reimporting anything.</summary>
        [MenuItem("Tools/Relic Run/Audit Content", priority = 101)]
        public static void AuditOnly()
        {
            var content = AssetDatabase.LoadAssetAtPath<GameContent>(ContentPaths.GameContentAsset);
            if (content == null)
            {
                Debug.LogWarning("no content to audit yet — run Tools ▸ Relic Run ▸ Import Content");
                return;
            }

            Report(content, 0);
        }

        /* ---------- copying ---------- */

        private static int Copy()
        {
            ContentPaths.EnsureFolder(ContentPaths.Relics);
            ContentPaths.EnsureFolder(ContentPaths.Enemies);
            ContentPaths.EnsureFolder(ContentPaths.UI);
            ContentPaths.EnsureFolder(ContentPaths.Halls);
            ContentPaths.EnsureFolder(ContentPaths.Events);
            ContentPaths.EnsureFolder(ContentPaths.Locales);

            int copied = 0;

            copied += Bring(ContentPaths.SourceArt + "/relic-icons.png", ContentPaths.RelicIconSheet);
            copied += Bring(ContentPaths.SourceArt + "/enemies-hoard.png", ContentPaths.EnemySheet);

            foreach (string hall in ContentIds.Halls)
            {
                copied += Bring(ContentPaths.SourceArt + "/" + hall + ".png",
                    ContentPaths.Halls + "/" + hall + ".png");
            }

            // The bazaar's two, each to the folder its KIND of art lives in. They are addressed
            // together — both are fetched once, on one floor — but a hall is scenery and a
            // merchant is somebody, and the folders say so.
            copied += Bring(ContentPaths.SourceArt + "/hall-bazaar.png", ContentPaths.BazaarHall);
            copied += Bring(ContentPaths.SourceArt + "/merchant.png", ContentPaths.MerchantArt);

            foreach (string art in ContentIds.Events)
            {
                copied += Bring(ContentPaths.SourceArt + "/" + art + ".png",
                    ContentPaths.Events + "/" + art + ".png");
            }

            copied += Bring(ContentPaths.SourceHeroPack, ContentPaths.HeroPackText);

            string locales = ContentPaths.Source(ContentPaths.SourceLocales);
            if (Directory.Exists(locales))
            {
                foreach (string file in Directory.GetFiles(locales, "*.json"))
                {
                    copied += Bring(ContentPaths.SourceLocales + "/" + Path.GetFileName(file),
                        ContentPaths.Locales + "/" + Path.GetFileName(file));
                }
            }
            else
            {
                Debug.LogWarning("no locales to import — run node Tools/extract/extract.mjs first");
            }

            return copied;
        }

        /// <summary>
        /// Copies one file in, and says whether it had to.
        /// </summary>
        /// <remarks>
        /// Compared byte for byte first. Rewriting a file Unity is watching costs a reimport of
        /// it and everything downstream, and for a sliced sheet that means the cells too — so a
        /// re-run that changes nothing has to actually write nothing.
        /// </remarks>
        private static int Bring(string from, string to)
        {
            string source = ContentPaths.Source(from);
            if (!File.Exists(source))
            {
                Debug.LogError("missing from the source drop: " + from);
                return 0;
            }

            string destination = Path.Combine(ContentPaths.ProjectRoot,
                to.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(destination) && Same(source, destination)) return 0;

            File.Copy(source, destination, true);
            return 1;
        }

        private static bool Same(string a, string b)
        {
            var one = new FileInfo(a);
            var two = new FileInfo(b);
            if (one.Length != two.Length) return false;

            byte[] first = File.ReadAllBytes(a);
            byte[] second = File.ReadAllBytes(b);

            for (int i = 0; i < first.Length; i++)
            {
                if (first[i] != second[i]) return false;
            }

            return true;
        }

        /* ---------- texture import ---------- */

        private static void Configure()
        {
            Pixelate(ContentPaths.RelicIconSheet, true);
            Pixelate(ContentPaths.EnemySheet, true);

            foreach (string hall in ContentIds.Halls)
            {
                Pixelate(ContentPaths.Halls + "/" + hall + ".png", false);
            }

            // The bazaar's two. Copied without this they import as plain TEXTURES — and an
            // address that resolves to a texture when a sprite was asked for throws
            // InvalidKeyException at the moment the delver walks onto floor seven, with nothing
            // on screen to say why.
            Pixelate(ContentPaths.BazaarHall, false);
            Pixelate(ContentPaths.MerchantArt, false);

            foreach (string art in ContentIds.Events)
            {
                Pixelate(ContentPaths.Events + "/" + art + ".png", false);
            }
        }

        /// <summary>
        /// The settings pixel art needs, and the reasons each one is not the default.
        /// </summary>
        /// <remarks>
        /// Point filtering because bilinear turns a 48-pixel icon into porridge. Uncompressed
        /// because DXT's block artefacts are visible at this size and the whole atlas is a
        /// quarter of a megabyte anyway. No mipmaps because nothing here is ever drawn smaller
        /// than it was authored. Full-rect meshes because the tight mesh crops to the visible
        /// pixels, which moves a sprite's centre and quietly misaligns everything in a grid. No
        /// extrude, because padding a cell in a point-filtered atlas bleeds a neighbour's edge
        /// into it rather than preventing the bleed.
        /// </remarks>
        private static void Pixelate(string assetPath, bool sheet)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError("not imported as a texture: " + assetPath);
                return;
            }

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);

            settings.textureType = TextureImporterType.Sprite;
            settings.spriteMode = (int)(sheet ? SpriteImportMode.Multiple : SpriteImportMode.Single);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 0;
            settings.spriteAlignment = (int)SpriteAlignment.Center;

            // One pixel to one canvas unit, so a 48-pixel icon lays out as 48 and a hall as 650.
            settings.spritePixelsPerUnit = 100;

            settings.filterMode = FilterMode.Point;
            settings.mipmapEnabled = false;
            settings.alphaIsTransparency = true;
            settings.npotScale = TextureImporterNPOTScale.None;
            settings.wrapMode = TextureWrapMode.Clamp;
            settings.readable = false;
            settings.sRGBTexture = true;

            importer.SetTextureSettings(settings);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;

            importer.SaveAndReimport();
        }

        /* ---------- binding ---------- */

        private static GameContent Bind(IList<FontBook.Face> faces,
            IList<TMP_FontAsset> fallbacks)
        {
            ContentPaths.EnsureFolder(ContentPaths.Content);

            RelicIconBook relicIcons = Asset<RelicIconBook>(ContentPaths.RelicIconBookAsset);
            HallBook halls = Asset<HallBook>(ContentPaths.HallBookAsset);
            EventBook events = Asset<EventBook>(ContentPaths.EventBookAsset);
            EnemyBook enemies = Asset<EnemyBook>(ContentPaths.EnemyBookAsset);
            HeroPackAsset heroPack = Asset<HeroPackAsset>(ContentPaths.HeroPackAssetPath);
            LocaleBook locales = Asset<LocaleBook>(ContentPaths.LocaleBookAsset);
            FontBook fonts = Asset<FontBook>(ContentPaths.FontBookAsset);
            PresentationSettings presentation = Asset<PresentationSettings>(ContentPaths.PresentationAsset);
            GameContent content = Asset<GameContent>(ContentPaths.GameContentAsset);

            relicIcons.Rebind(FromSheet(ContentPaths.RelicIconSheet, SpriteSheet.RelicCells()));
            enemies.Rebind(FromSheet(ContentPaths.EnemySheet, SpriteSheet.EnemyCells()));
            // The book's own list: the ten dungeons' halls AND the bazaar floor's two. Asking
            // ContentIds.Halls here would address the dungeons and leave the bazaar unbound,
            // which is exactly what it did — the files copied and the book stayed at ten.
            halls.Rebind(Addressed(halls.Needed, Scenery(halls.Needed)));
            events.Rebind(Addressed(ContentIds.Events,
                Addressing.Address(Addressing.EventGroup, ContentPaths.Events, ContentIds.Events, ".png")));

            heroPack.Bind(AssetDatabase.LoadAssetAtPath<TextAsset>(ContentPaths.HeroPackText));
            locales.Rebind(Translations());
            fonts.Rebind(faces, fallbacks);

            content.Bind(relicIcons, halls, events, enemies, heroPack, locales, fonts, presentation);

            foreach (Object asset in new Object[]
                     { relicIcons, halls, events, enemies, heroPack, locales, fonts, presentation, content })
            {
                EditorUtility.SetDirty(asset);
            }

            return content;
        }

        private static T Asset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            // Created rather than replaced, so an asset somebody has already wired into a scene
            // keeps its GUID and every reference to it survives a re-run.
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);

            return asset;
        }

        /// <summary>Binds cells of a cut sheet, matched by the name the cutting gave them.</summary>
        private static List<SpriteBook.Entry> FromSheet(string assetPath, IList<SheetCell> cells)
        {
            var sprites = new Dictionary<string, Sprite>();
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                var sprite = sub as Sprite;
                if (sprite != null) sprites[sprite.name] = sprite;
            }

            var bound = new List<SpriteBook.Entry>(cells.Count);
            foreach (SheetCell cell in cells)
            {
                Sprite sprite;
                sprites.TryGetValue(cell.Name, out sprite);
                bound.Add(new SpriteBook.Entry(cell.Id, sprite));
            }

            return bound;
        }

        /// <summary>Binds whole images, one per id, named after the id.</summary>
        /// <summary>
        /// Binds ids to the addresses the same pass just handed out.
        /// </summary>
        /// <remarks>
        /// The GUIDs come from the addressing rather than from a second lookup, so there is one
        /// place that decides what an id points at. Two passes would agree until the day one of
        /// them changed.
        ///
        /// An id with no GUID is bound to nothing rather than skipped. A missing row and a row
        /// pointing nowhere are different failures and the audit says which.
        /// </remarks>
        /// <summary>
        /// Addresses the halls, and the bazaar's two wherever they each live.
        /// </summary>
        /// <remarks>
        /// One addressable GROUP over two folders, which is the shape the art ended up in: the
        /// halls and the bazaar's hall are scenery and sit together, and the merchant is filed
        /// with the enemies. Addressing is about WHEN a thing is fetched, and all of these are
        /// fetched once when a delver reaches the floor that wants them — so the group is right
        /// even where the folders differ.
        /// </remarks>
        private static IDictionary<string, string> Scenery(IReadOnlyList<string> ids)
        {
            var halls = new List<string>();

            foreach (string id in ids)
            {
                if (id != "merchant") halls.Add(id);
            }

            IDictionary<string, string> found = Addressing.Address(
                Addressing.HallGroup, ContentPaths.Halls, halls, ".png");

            IDictionary<string, string> merchant = Addressing.Address(
                Addressing.HallGroup, ContentPaths.Enemies, new[] { "merchant" }, ".png");

            foreach (KeyValuePair<string, string> one in merchant) found[one.Key] = one.Value;

            return found;
        }

        private static List<AddressBook.Entry> Addressed(IReadOnlyList<string> ids,
            IDictionary<string, string> guids)
        {
            var bound = new List<AddressBook.Entry>(ids.Count);

            foreach (string id in ids)
            {
                string guid;
                bound.Add(new AddressBook.Entry(id,
                    guids.TryGetValue(id, out guid) ? new AssetReferenceSprite(guid) : null));
            }

            return bound;
        }

        private static List<LocaleBook.Translation> Translations()
        {
            var found = new List<LocaleBook.Translation>();

            string folder = Path.Combine(ContentPaths.ProjectRoot,
                ContentPaths.Locales.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(folder)) return found;

            var languages = new List<string>();
            foreach (string file in Directory.GetFiles(folder, "*.json"))
            {
                languages.Add(Path.GetFileNameWithoutExtension(file));
            }

            languages.Sort();

            IDictionary<string, string> guids = Addressing.Address(
                Addressing.LocaleGroup, ContentPaths.Locales, languages, ".json");

            foreach (string language in languages)
            {
                string guid;
                found.Add(new LocaleBook.Translation(language,
                    guids.TryGetValue(language, out guid)
                        ? new AssetReferenceT<TextAsset>(guid)
                        : null));
            }

            return found;
        }

        /* ---------- the report ---------- */

        private static void Report(GameContent content, int copied)
        {
            string trouble = content.Audit();

            var said = new System.Text.StringBuilder("Relic Run content");
            if (copied > 0) said.Append(" — ").Append(copied).Append(" file(s) copied in");

            foreach (SpriteBook book in content.Resident)
            {
                said.Append("\n  ").Append(book.Entries.Count).Append(' ').Append(book.What);
            }

            foreach (AddressBook book in content.Fetched)
            {
                said.Append("\n  ").Append(book.Entries.Count).Append(' ').Append(book.What)
                    .Append(", addressed");
            }

            if (content.Locales != null)
            {
                said.Append("\n  ").Append(content.Locales.Languages.Count).Append(" languages");
            }

            if (content.Fonts != null)
            {
                said.Append("\n  ").Append(content.Fonts.Faces.Count).Append(" faces");

                if (content.Fonts.Fallbacks.Count > 0)
                {
                    said.Append(", and ").Append(content.Fonts.Fallbacks.Count)
                        .Append(" borrowed from the system for ja, zh and ar");
                }
            }

            if (trouble.Length == 0)
            {
                said.Append("\n\neverything the catalogs ask for is bound exactly once.");
                Debug.Log(said.ToString(), content);
                return;
            }

            said.Append("\n\n").Append(trouble);
            Debug.LogError(said.ToString(), content);
        }
    }
}
