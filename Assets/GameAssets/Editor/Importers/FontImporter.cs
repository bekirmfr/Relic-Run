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
    /// Two pixel faces, baked as static atlases at the size they were drawn for. Press Start 2P
    /// is an eight-pixel face and Jacquard 12 a twelve-pixel one; sampled at anything else they
    /// are a blurry approximation of themselves, and sampled at those sizes they are exact at
    /// every integer multiple. That is the whole reason for RASTER over SDF: a signed distance
    /// field exists to make type scale smoothly to arbitrary sizes, which is the one thing pixel
    /// art must never do.
    ///
    /// The character set is not "Latin" or "ASCII" but the union of what the eight shipped
    /// languages actually need once <see cref="Locale.Clean"/> has taken the icons off. Anything
    /// a face cannot draw is reported rather than silently skipped — and it reports a great deal,
    /// because neither face has a single Japanese or Chinese glyph in it.
    /// </remarks>
    public static class FontImporter
    {
        /// <summary>A role, the file that fills it, and the size that file was drawn at.</summary>
        private struct Cut
        {
            public string Role;
            public string File;
            public int SamplingSize;
            public int Atlas;
        }

        private static readonly Cut[] Cuts =
        {
            new Cut { Role = FontBook.Ui, File = "PressStart2P.ttf", SamplingSize = 8, Atlas = 512 },
            new Cut { Role = FontBook.Display, File = "Jacquard12.ttf", SamplingSize = 12, Atlas = 512 },
        };

        /// <summary>
        /// No padding between glyphs.
        /// </summary>
        /// <remarks>
        /// Padding exists so a filtered atlas cannot bleed one glyph's edge into its neighbour.
        /// The atlas here is point sampled, so nothing bleeds and padding would only make the
        /// texture larger and the glyphs further apart than they were drawn.
        /// </remarks>
        private const int NoPadding = 0;

        /// <summary>Copies the faces in, bakes them, and binds them.</summary>
        public static List<FontBook.Face> Import()
        {
            ContentPaths.EnsureFolder(ContentPaths.Fonts);

            var bound = new List<FontBook.Face>(Cuts.Length);
            string wanted = Wanted();

            foreach (Cut cut in Cuts)
            {
                string ttf = ContentPaths.Fonts + "/" + cut.File;
                if (!Copy(cut.File, ttf)) continue;

                TMP_FontAsset face = Bake(cut, ttf, wanted);
                bound.Add(new FontBook.Face(cut.Role, face));
            }

            return bound;
        }

        /// <summary>
        /// Every character the game will ever ask a font to draw.
        /// </summary>
        /// <remarks>
        /// From the locale tables the importer has already copied in, so this measures the same
        /// strings the build ships rather than the ones in the source drop. Cleaned first: the
        /// arrows and pictograms in front of half the buttons never reach the screen, and asking
        /// a 1983 pixel face for an emoji would fill the report with noise nobody can act on.
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
                font, cut.SamplingSize, NoPadding, GlyphRenderMode.RASTER,
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

            foreach (Texture2D atlas in face.atlasTextures)
            {
                if (atlas == null) continue;

                atlas.name = cut.Role + " atlas";
                atlas.filterMode = FilterMode.Point;
                atlas.wrapMode = TextureWrapMode.Clamp;
                atlas.anisoLevel = 0;

                if (AssetDatabase.GetAssetPath(atlas) != path) AssetDatabase.AddObjectToAsset(atlas, face);
            }

            if (face.material != null)
            {
                face.material.name = cut.Role + " material";
                if (AssetDatabase.GetAssetPath(face.material) != path)
                {
                    AssetDatabase.AddObjectToAsset(face.material, face);
                }
            }

            EditorUtility.SetDirty(face);
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
