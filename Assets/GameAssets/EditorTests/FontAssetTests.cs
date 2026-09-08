using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Editor.Importers;
using RelicRun.Game.Data;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace RelicRun.Tests.Editor
{
    /// <summary>
    /// The faces as they were actually baked, rather than as the TTFs could have been.
    /// </summary>
    /// <remarks>
    /// <c>LegibilityTests</c> asks what the font FILES contain, from the recorded character
    /// maps, under <c>dotnet test</c>. This asks what came out of the oven — which is a
    /// different question with its own ways of going wrong: an atlas that overflowed and
    /// silently dropped the last glyphs, a face left Dynamic and so rasterising at runtime from
    /// font data the build may not carry, a filtered atlas that turns an eight-pixel face into
    /// grey mush at every size but one.
    /// </remarks>
    [TestFixture]
    public class FontAssetTests
    {
        private GameContent _content;

        [OneTimeSetUp]
        public void LoadTheContent()
        {
            _content = AssetDatabase.LoadAssetAtPath<GameContent>(ContentPaths.GameContentAsset);
            Assert.That(_content, Is.Not.Null, "run Tools > Relic Run > Import Content first");
        }

        /// <summary>
        /// The font book, or a failure that says what to do about it.
        /// </summary>
        /// <remarks>
        /// Every test here goes through this rather than touching the field, because a book that
        /// is not bound at all is the one failure with an obvious remedy — the content was
        /// imported before the fonts existed — and a null reference exception is the worst
        /// possible way to say so.
        /// </remarks>
        private FontBook Book()
        {
            Assert.That(_content.Fonts, Is.Not.Null,
                "no font book is bound — run Tools > Relic Run > Import Content");

            return _content.Fonts;
        }

        private TMP_FontAsset Face(string role)
        {
            TMP_FontAsset face = Book().For(role);
            Assert.That(face, Is.Not.Null, "nothing fills the " + role + " role");

            return face;
        }

        [Test]
        public void EveryRoleIsFilledExactlyOnce()
        {
            BindingAudit audit = Book().Audit();
            Assert.That(audit.Passed, Is.True, audit.Report());

            Assert.That(Book().Faces.Count, Is.EqualTo(FontBook.Roles.Count));
        }

        /// <summary>
        /// Both atlases are sealed, unfiltered and rasterised rather than distance-fielded.
        /// </summary>
        /// <remarks>
        /// Each of these is the difference between pixel art and a blurry approximation of it,
        /// and none of them is visible in a small screenshot. A signed distance field exists to
        /// make type scale smoothly to any size, which is precisely what a face drawn on a
        /// five-by-seven grid must never do.
        /// </remarks>
        [Test]
        public void BothFacesAreBakedForPixelArt()
        {
            foreach (string role in FontBook.Roles)
            {
                TMP_FontAsset face = Face(role);

                Assert.That(face.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static),
                    role + " still rasterises at runtime");
                Assert.That(face.atlasRenderMode, Is.EqualTo(GlyphRenderMode.RASTER),
                    role + " is not a bitmap face");
                Assert.That(face.atlasPadding, Is.Zero, role + " has padding between its glyphs");

                Assert.That(face.atlasTextures, Is.Not.Empty, role + " has no atlas");
                Assert.That(face.atlasTextures.Length, Is.EqualTo(1),
                    role + " spilled into a second atlas — raise the size rather than allowing it");

                foreach (Texture2D atlas in face.atlasTextures)
                {
                    Assert.That(atlas.filterMode, Is.EqualTo(FilterMode.Point),
                        role + "'s atlas is filtered");
                }
            }
        }

        /// <summary>The atlases and materials travel with the asset, not beside it.</summary>
        /// <remarks>
        /// A texture left outside the font asset is a texture somebody can delete, move or fail
        /// to include in a build, and the face then draws nothing at all.
        /// </remarks>
        [Test]
        public void EachFaceCarriesItsOwnAtlasAndMaterial()
        {
            foreach (string role in FontBook.Roles)
            {
                TMP_FontAsset face = Face(role);
                string path = AssetDatabase.GetAssetPath(face);

                Assert.That(face.material, Is.Not.Null, role + " has no material");
                Assert.That(AssetDatabase.GetAssetPath(face.material), Is.EqualTo(path),
                    role + "'s material lives somewhere else");

                foreach (Texture2D atlas in face.atlasTextures)
                {
                    Assert.That(AssetDatabase.GetAssetPath(atlas), Is.EqualTo(path),
                        role + "'s atlas lives somewhere else");
                }
            }
        }

        /// <summary>
        /// The baked faces draw every language they are supposed to draw.
        /// </summary>
        /// <remarks>
        /// The end of the chain: the TTF has the glyph, the importer asked for it, the atlas had
        /// room, and the face reports it. Anything that broke in between shows up here as a
        /// language rather than as a number.
        /// </remarks>
        [Test]
        public void TheUiFaceDrawsEveryLanguageItsTypefaceCan()
        {
            foreach (string language in new[] { "en", "es", "fr", "tr", "ru" })
            {
                Missing(Face(FontBook.Ui), language, "the ui face");
            }
        }

        [Test]
        public void TheDisplayFaceDrawsTheLatinLanguages()
        {
            foreach (string language in new[] { "en", "es", "fr", "tr" })
            {
                Missing(Face(FontBook.Display), language, "the display face");
            }
        }

        /// <summary>
        /// Three languages have nothing to draw them, and nothing is pretending otherwise.
        /// </summary>
        /// <remarks>
        /// The same tracked gap <c>LegibilityTests</c> records against the font files, asserted
        /// here against the built assets and the fallback list. It fails the day somebody adds a
        /// fallback — which is the point. Nobody should be able to fix this quietly, and nobody
        /// should be able to ship without having decided.
        /// </remarks>
        [Test]
        public void JapaneseChineseAndArabicStillHaveNothingToDrawThem()
        {
            Assert.That(Book().Fallbacks, Is.Empty,
                "a fallback face has appeared — say which languages it fixes, here and in " +
                "LegibilityTests");

            foreach (string language in new[] { "ja", "zh", "ar" })
            {
                foreach (string role in FontBook.Roles)
                {
                    Assert.That(Gap(Face(role), language), Is.GreaterThan(0),
                        role + " can now draw " + language);
                }
            }
        }

        /// <summary>Nothing was baked that no language asks for.</summary>
        /// <remarks>
        /// An atlas holds what it was told to hold. A face carrying the whole of Latin Extended
        /// when the game uses eighty-three characters is wasting most of a texture on glyphs
        /// nobody will ever see, and the first sign of it is a build that is larger than it
        /// should be for a reason nobody can name.
        /// </remarks>
        [Test]
        public void NoFaceCarriesGlyphsNoLanguageAsksFor()
        {
            var asked = new HashSet<int>();
            foreach (string language in Languages()) asked.UnionWith(Needed(language));

            foreach (string role in FontBook.Roles)
            {
                TMP_FontAsset face = Face(role);
                var extra = new List<string>();

                foreach (TMP_Character character in face.characterTable)
                {
                    int point = (int)character.unicode;
                    if (asked.Contains(point)) continue;

                    // TextMeshPro keeps bookkeeping of its own in the low control range — the
                    // missing-glyph stand-in among it. That is the library's business; what
                    // matters here is that no PRINTABLE glyph was baked for nothing.
                    if (point < 0x20) continue;

                    extra.Add("U+" + character.unicode.ToString("X4"));
                }

                Assert.That(extra, Is.Empty,
                    role + " carries " + extra.Count + " glyphs nothing asks for: " +
                    string.Join(" ", extra.GetRange(0, Mathf.Min(12, extra.Count))));
            }
        }

        /* ---------- what the languages need ---------- */

        private void Missing(TMP_FontAsset face, string language, string what)
        {
            int gap = Gap(face, language);
            Assert.That(gap, Is.Zero, what + " is missing " + gap + " characters of " + language);
        }

        private int Gap(TMP_FontAsset face, string language)
        {
            int gap = 0;
            foreach (int point in Needed(language))
            {
                if (!face.HasCharacter(point)) gap++;
            }

            return gap;
        }

        /// <summary>What a language needs, from the tables the build actually ships.</summary>
        private IReadOnlyList<int> Needed(string language)
        {
            TextAsset strings = _content.Locales.For(language);
            Assert.That(strings, Is.Not.Null, "no strings for " + language);

            var lines = new List<string>();
            foreach (JProperty line in JObject.Parse(strings.text).Properties())
            {
                lines.Add(line.Value.ToString());
            }

            return Legibility.Needed(lines);
        }

        private IEnumerable<string> Languages()
        {
            foreach (LocaleBook.Translation translation in _content.Locales.Languages)
            {
                yield return translation.Language;
            }
        }
    }
}
