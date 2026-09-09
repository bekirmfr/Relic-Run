using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using RelicRun.Core.Content;
using RelicRun.Game.Data;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace RelicRun.Editor.Importers
{
    /// <summary>
    /// Builds the TextMeshPro font assets, with exactly the characters the game says.
    /// </summary>
    /// <remarks>
    /// Two faces wanting opposite things, baked as static atlases. Silkscreen is drawn on an
    /// eight-pixel grid and is baked as a bitmap at exactly eight: sampled at anything else it is
    /// a blurry approximation of itself, and at every whole multiple it is exact. That is the
    /// reason for RASTER over SDF here — a distance field exists to make type scale smoothly to
    /// arbitrary sizes, which is the one thing pixel art must never do. Space Grotesk is the
    /// opposite: an outline face for prose, baked as a distance field, because a bitmap of it
    /// would be sharp at one size and wrong at every other.
    ///
    /// The character set is not "Latin" or "ASCII" but the union of what the eight shipped
    /// languages actually need once <see cref="Locale.Clean"/> has taken the icons off. Anything
    /// a face cannot draw is reported rather than silently skipped — and it reports a great deal,
    /// because neither face has a single Japanese or Chinese glyph in it, and Silkscreen has no
    /// Cyrillic either.
    /// </remarks>
    public static class FontImporter
    {
        /// <summary>A role, the file that fills it, and how that file wants to be drawn.</summary>
        private struct Cut
        {
            public string Role;
            public string File;
            public int SamplingSize;
            public int Atlas;

            /// <summary>Pixel art, which is baked as pixels and never filtered.</summary>
            /// <remarks>Asked of the role rather than stored, so one place decides.</remarks>
            public bool Pixels { get { return FontBook.IsPixelArt(Role); } }
        }

        /// <summary>
        /// The two faces, read off the source's own stylesheet link.
        /// </summary>
        /// <remarks>
        /// Not chosen. The source asks Google Fonts for three families and uses Silkscreen for
        /// 240 of its 249 font-family declarations, Space Grotesk for the six on text inputs,
        /// and Baloo 2 for three containers whose children override it anyway. So Silkscreen
        /// fills the ui role and Space Grotesk the display one, and Baloo 2 is left out on the
        /// grounds that nothing visible is set in it.
        ///
        /// Press Start 2P and Jacquard 12 were here first and appear in the source NOWHERE. They
        /// arrived as uploads, were bound because they were to hand, and the game was set in a
        /// gothic face nobody had asked for until a screenshot showed it. Worth remembering as
        /// the failure mode it is: nothing was broken, everything was wired, and the answer was
        /// simply not the one the source gives.
        ///
        /// The two want opposite treatment, and which is which comes from
        /// <see cref="FontBook.IsPixelArt"/> rather than being repeated here — it is a fact about
        /// what the role is, and it was briefly written down in two places, which is how a test
        /// came to assert that both faces were bitmaps after one of them had stopped being one.
        /// </remarks>
        private static readonly Cut[] Cuts =
        {
            new Cut { Role = FontBook.Ui, File = "Silkscreen-Regular.ttf",
                      SamplingSize = 8, Atlas = 512 },
            new Cut { Role = FontBook.Display, File = "SpaceGrotesk.ttf",
                      SamplingSize = 48, Atlas = 1024 },
        };

        /// <summary>
        /// One texel of nothing around each glyph in a bitmap atlas.
        /// </summary>
        /// <remarks>
        /// This was zero, and the reasoning was wrong in a way worth keeping: "a pixel atlas is
        /// point sampled, so nothing bleeds". Point sampling does not mean no bleed — it means
        /// each sample takes exactly one texel, and WHICH texel depends on where the quad lands.
        /// The glyphs are packed edge to edge, so a quad half a texel out samples the neighbouring
        /// glyph rather than empty space, and at four screen pixels per texel that neighbour
        /// arrives two pixels wide.
        ///
        /// Whole quads were the assumption underneath, and layout does not honour it: a
        /// <c>VerticalLayoutGroup</c> distributing 310 units across a variable number of lines
        /// puts children on half units without asking. So the atlas stops relying on alignment it
        /// cannot enforce, at a cost of one texel per glyph in a texture that is 226 glyphs in a
        /// 512-square.
        ///
        /// The symptom was a doubled, blobby letterform that read as a different typeface
        /// entirely — it survived swapping the face, fixing a double playback and moving to a
        /// whole-number canvas scale, which in hindsight was the clue: the only thing that had
        /// not changed was the packing.
        /// </remarks>
        private const int PixelPadding = 1;

        /// <summary>
        /// Room around each glyph in a distance field, which is where the field lives.
        /// </summary>
        /// <remarks>
        /// Not decoration and not the same thing as the padding above. A distance field encodes
        /// how far each texel is from the glyph's edge, and it can only encode as far out as it
        /// has room for — with none, the face has no field to read and renders as hard-edged
        /// mush at every size except the one it was baked at, which is the whole problem a
        /// distance field is there to solve.
        /// </remarks>
        private const int FieldPadding = 5;

        /// <summary>
        /// The scripts no shipped face can draw, and the families that can.
        /// </summary>
        /// <remarks>
        /// Tried in order; every one the machine running the importer can see becomes a fallback,
        /// and the rest are reported. The order is deliberate — the platform's own UI face first,
        /// then the Noto families, which is what most Linux and Android images carry.
        ///
        /// The FIRST chain is Latin and Cyrillic, and it is first because it is the one most
        /// readers will actually hit. Silkscreen is a small face: it draws English, Spanish and
        /// French whole, gets Turkish to 94% — it has no dotless i and no breve — and has no
        /// Cyrillic at all, so Russian sits at 48%. Press Start 2P did both of those, which made
        /// this look like a regression when the faces were swapped. It is not: the source has
        /// exactly the same hole and falls through to <c>monospace</c> for it, and this chain is
        /// the same decision written where it can be read.
        ///
        /// Which means a Turkish word may arrive with two letters in a different face. That is
        /// worse than uniform and better than a word with holes in it, and it is what the
        /// shipped game already does.
        ///
        /// A device resolves these by family name at run time, so a build made on Windows and
        /// played on a phone will find whichever of these that phone has. It may find none, and
        /// then the text is boxes again. That is the honest limit of borrowing somebody else's
        /// fonts and the reason the book's list is serialized rather than computed: a platform
        /// this machine has never seen can have its families added by hand.
        /// </remarks>
        private static readonly string[][] Borrowed =
        {
            new[] { "Segoe UI", "Noto Sans", "DejaVu Sans", "Roboto", "Arial" },
            new[] { "Yu Gothic", "Meiryo", "MS Gothic", "Hiragino Sans", "Noto Sans CJK JP", "Noto Sans JP" },
            new[] { "Microsoft YaHei", "SimSun", "PingFang SC", "Noto Sans CJK SC", "Noto Sans SC" },
            new[] { "Segoe UI", "Geeza Pro", "Noto Sans Arabic", "Arial" },
        };

        /// <summary>
        /// A borrowed face is not pixel art and is not sampled like one.
        /// </summary>
        /// <remarks>
        /// System fonts are outline faces meant to scale, and a delver reading Japanese is
        /// reading a system font whatever is drawn around it. Rasterising one at eight pixels
        /// would make it illegible rather than making it match.
        /// </remarks>
        private const int BorrowedSize = 48;

        /// <summary>Copies the faces in, bakes them, and binds them.</summary>
        public static List<FontBook.Face> Import(List<TMP_FontAsset> fallbacks)
        {
            ContentPaths.EnsureFolder(ContentPaths.Fonts);

            fallbacks.Clear();
            fallbacks.AddRange(Borrow());

            var bound = new List<FontBook.Face>(Cuts.Length);
            string wanted = Wanted();

            foreach (Cut cut in Cuts)
            {
                string ttf = ContentPaths.Fonts + "/" + cut.File;
                if (!Copy(cut.File, ttf)) continue;

                TMP_FontAsset face = Bake(cut, ttf, wanted);
                if (face != null) face.fallbackFontAssetTable = new List<TMP_FontAsset>(fallbacks);

                bound.Add(new FontBook.Face(cut.Role, face));
            }

            return bound;
        }

        /// <summary>
        /// Makes a font asset for each system family that can stand in for a script.
        /// </summary>
        /// <remarks>
        /// These are DynamicOS assets: they carry no glyphs at all, only a family name, and the
        /// device rasterises what it needs when it needs it. That is what makes borrowing free —
        /// nothing is bundled and nothing is baked — and it is also what makes it uncertain,
        /// since a family that is not installed simply fails to load and the chain moves on.
        /// </remarks>
        private static List<TMP_FontAsset> Borrow()
        {
            ContentPaths.EnsureFolder(ContentPaths.Fallbacks);

            var borrowed = new List<TMP_FontAsset>();
            var absent = new List<string>();

            foreach (string[] script in Borrowed)
            {
                foreach (string family in script)
                {
                    string path = ContentPaths.Fallbacks + "/" + family.Replace(' ', '-') + ".asset";

                    var already = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                    if (already != null)
                    {
                        borrowed.Add(already);
                        continue;
                    }

                    TMP_FontAsset face = TMP_FontAsset.CreateFontAsset(family, "Regular", BorrowedSize);
                    if (face == null)
                    {
                        absent.Add(family);
                        continue;
                    }

                    face.name = family;
                    AssetDatabase.CreateAsset(face, path);
                    Seal(face, path, family, false);

                    borrowed.Add(face);
                }
            }

            if (absent.Count > 0)
            {
                Debug.Log("not installed on this machine, so not borrowed: " +
                          string.Join(", ", absent) +
                          "\n  a device that has one will still not use it — a fallback is made " +
                          "from a family the importer can see. Add them by hand on a machine " +
                          "that has them, or accept that those platforms fall through.");
            }

            return borrowed;
        }

        /// <summary>
        /// Every character the game will ever ask a font to draw.
        /// </summary>
        /// <remarks>
        /// From the locale tables the importer has already copied in, so this measures the same
        /// strings the build ships rather than the ones in the source drop. Cleaned first: the
        /// arrows and pictograms in front of half the buttons never reach the screen, and asking
        /// a 2001 pixel face for an emoji would fill the report with noise nobody can act on.
        /// </remarks>
        private static string Wanted()
        {
            var lines = new List<string>();

            foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { ContentPaths.Locales }))
            {
                var strings = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (strings == null) continue;

                // Parsed rather than scanned. The shipped tables happen to hold every accent as
                // itself, so scanning the raw file would work today — and would stop working
                // silently the day the extractor wrote one as an escape, because a face is
                // missing a character in exactly the same way whether it was never asked for or
                // asked for wrongly. Parsing also leaves the keys and the punctuation out.
                foreach (JProperty line in JObject.Parse(strings.text).Properties())
                {
                    lines.Add(line.Value.ToString());
                }
            }

            var said = new StringBuilder();
            foreach (int point in Legibility.Needed(lines)) said.Append(char.ConvertFromUtf32(point));

            return said.ToString();
        }

        private static bool Copy(string file, string into)
        {
            if (AssetDatabase.LoadAssetAtPath<Font>(into) != null) return true;

            string source = ContentPaths.Source(ContentPaths.SourceArt + "/" + file);
            if (!System.IO.File.Exists(source))
            {
                Debug.LogError("missing from the source drop: " + file);
                return false;
            }

            System.IO.File.Copy(source,
                System.IO.Path.Combine(ContentPaths.ProjectRoot,
                    into.Replace('/', System.IO.Path.DirectorySeparatorChar)), true);

            AssetDatabase.ImportAsset(into, ImportAssetOptions.ForceUpdate);
            return true;
        }

        /// <summary>
        /// Puts a font asset's atlas and material inside it, where they cannot be lost.
        /// </summary>
        /// <remarks>
        /// <c>CreateFontAsset</c> hands back a font asset holding a loose texture and a loose
        /// material, and neither belongs to any file. Save only the font asset and both are gone
        /// on the next domain reload — the face keeps its glyph table and draws nothing, which
        /// reads as a broken shader rather than as a missing sub-asset.
        ///
        /// <paramref name="pixels"/> says whether the atlas holds pixel art. A borrowed system
        /// face does not: it is a distance field meant to scale, and point filtering one would
        /// make it worse rather than sharper.
        /// </remarks>
        private static void Seal(TMP_FontAsset face, string path, string name, bool pixels)
        {
            foreach (Texture2D atlas in face.atlasTextures)
            {
                if (atlas == null) continue;

                atlas.name = name + " atlas";
                if (pixels)
                {
                    atlas.filterMode = FilterMode.Point;
                    atlas.wrapMode = TextureWrapMode.Clamp;
                    atlas.anisoLevel = 0;
                }

                if (AssetDatabase.GetAssetPath(atlas) != path) AssetDatabase.AddObjectToAsset(atlas, face);
            }

            if (face.material != null)
            {
                face.material.name = name + " material";
                if (AssetDatabase.GetAssetPath(face.material) != path)
                {
                    AssetDatabase.AddObjectToAsset(face.material, face);
                }
            }

            EditorUtility.SetDirty(face);
        }

        /// <summary>
        /// Bakes one face.
        /// </summary>
        /// <remarks>
        /// TMP's own recipe, in TMP's own order, and the order is not negotiable: a font asset
        /// refuses to add characters once its atlas is Static, so it is created Dynamic, filled,
        /// and only then sealed. Sealing matters — a Dynamic asset in a build rasterises glyphs
        /// at runtime from font data that has to ship alongside it.
        /// </remarks>
        private static TMP_FontAsset Bake(Cut cut, string ttf, string wanted)
        {
            var importer = AssetImporter.GetAtPath(ttf) as TrueTypeFontImporter;
            if (importer != null && !importer.includeFontData)
            {
                // Without the font data there is nothing to rasterise from, and TMP says so in a
                // warning rather than a failure.
                importer.includeFontData = true;
                importer.SaveAndReimport();
            }

            var font = AssetDatabase.LoadAssetAtPath<Font>(ttf);
            if (font == null)
            {
                Debug.LogError(ttf + " did not import as a font");
                return null;
            }

            string path = ContentPaths.Fonts + "/" + cut.Role + ".asset";
            AssetDatabase.DeleteAsset(path);

            TMP_FontAsset face = TMP_FontAsset.CreateFontAsset(
                font, cut.SamplingSize,
                cut.Pixels ? PixelPadding : FieldPadding,
                cut.Pixels ? GlyphRenderMode.RASTER : GlyphRenderMode.SDFAA,
                cut.Atlas, cut.Atlas, AtlasPopulationMode.Dynamic, false);

            if (face == null)
            {
                Debug.LogError("could not create a font asset from " + ttf);
                return null;
            }

            face.name = cut.Role;
            AssetDatabase.CreateAsset(face, path);

            string missing;
            face.TryAddCharacters(wanted, out missing);

            face.atlasPopulationMode = AtlasPopulationMode.Static;
            Seal(face, path, cut.Role, cut.Pixels);
            Report(cut, face, wanted, missing);

            return face;
        }

        /// <summary>
        /// Says what the face could and could not draw.
        /// </summary>
        /// <remarks>
        /// A warning rather than an error. What is missing is Japanese, Chinese and Arabic, and
        /// that is a decision nobody has taken yet rather than a mistake somebody made — the
        /// same three languages <c>LegibilityTests</c> asserts as unreadable, so this cannot
        /// quietly become true or quietly stop being true.
        /// </remarks>
        private static void Report(Cut cut, TMP_FontAsset face, string wanted, string missing)
        {
            int asked = Legibility.Needed(new[] { wanted }).Count;
            int absent = missing == null ? 0 : Legibility.Needed(new[] { missing }).Count;

            string said = cut.Role + " — " + cut.File + " at " + cut.SamplingSize + "px, " +
                          face.characterTable.Count + " glyphs baked into a " +
                          cut.Atlas + "x" + cut.Atlas + " atlas";

            if (absent == 0)
            {
                Debug.Log(said + "\n  every character the game uses is drawn.", face);
                return;
            }

            Debug.LogWarning(said + "\n  " + absent + " of " + asked +
                             " characters have no glyph in this face — see LegibilityTests for " +
                             "which languages that leaves unreadable.", face);
        }
    }
}
