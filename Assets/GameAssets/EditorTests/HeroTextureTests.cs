using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Game.Presentation;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace RelicRun.Tests.Editor
{
    /// <summary>
    /// A delver, from a composed grid to two textures and a shader.
    /// </summary>
    /// <remarks>
    /// <c>HeroIndexTests</c> proves the re-encoding is lossless, under <c>dotnet test</c>, over
    /// all ninety-three recorded compositions, in a second. What it cannot prove is that the
    /// bytes reach the GPU as written — that needs a real <see cref="Texture2D"/>, which needs
    /// Unity. So this composes nothing: it takes a small hero built by hand, whose shape is
    /// known, and asks what happened to it on the way into a texture.
    ///
    /// The two mistakes worth catching are both invisible in a screenshot to anybody who does
    /// not already know what a delver looks like. The row flip, because Core counts rows from
    /// the top as the art was drawn and a texture counts from the bottom. And the colour space,
    /// because an sRGB grid would gamma-convert its own indices and hand back a different colour
    /// for every pixel that is not transparent.
    /// </remarks>
    [TestFixture]
    public class HeroTextureTests
    {
        private HeroIndex _hero;
        private Dictionary<char, Rgb> _palette;

        /// <summary>How many of the top rows are left empty, which is what makes it lopsided.</summary>
        private const int BareRows = 2;

        private const int Side = 8;
        private const int FrameCount = 3;

        [OneTimeSetUp]
        public void BuildADelver()
        {
            _palette = HeroPalette.Build();
            _hero = HeroIndex.Of(Lopsided(_palette));
        }

        /* ---------- the grid ---------- */

        [Test]
        public void TheGridHoldsOneByteAPixelWithTheFramesSideBySide()
        {
            Texture2D grid = HeroTextures.Grid(_hero);

            try
            {
                Assert.That(grid.width, Is.EqualTo(_hero.Size * _hero.Frames.Count));
                Assert.That(grid.height, Is.EqualTo(_hero.Size));
                Assert.That(grid.format, Is.EqualTo(TextureFormat.R8), "one channel, one byte");
                Assert.That(grid.filterMode, Is.EqualTo(FilterMode.Point),
                    "a filtered index is an index nobody wrote");
                Assert.That(grid.mipmapCount, Is.EqualTo(1), "a mip of indices is meaningless");
            }
            finally
            {
                Object.DestroyImmediate(grid);
            }
        }

        /// <summary>Every index arrives where it was written.</summary>
        [Test]
        public void EveryIndexArrivesIntact()
        {
            Texture2D grid = HeroTextures.Grid(_hero);

            try
            {
                byte[] raw = grid.GetRawTextureData();
                Assert.That(raw.Length, Is.EqualTo(grid.width * grid.height));

                int wrong = 0;
                string first = null;

                for (int frame = 0; frame < _hero.Frames.Count; frame++)
                {
                    for (int y = 0; y < _hero.Size; y++)
                    {
                        for (int x = 0; x < _hero.Size; x++)
                        {
                            byte want = _hero.IndexAt(frame, x, y);
                            byte got = raw[(grid.height - 1 - y) * grid.width + frame * _hero.Size + x];

                            if (want == got) continue;

                            wrong++;
                            if (first == null)
                            {
                                first = "frame " + frame + " at " + x + "," + y + ": wrote " +
                                        want + ", read " + got;
                            }
                        }
                    }
                }

                Assert.That(wrong, Is.Zero, wrong + " indices moved — " + first);
            }
            finally
            {
                Object.DestroyImmediate(grid);
            }
        }

        /// <summary>
        /// The delver is not upside down, said without borrowing the writer's own arithmetic.
        /// </summary>
        /// <remarks>
        /// The test above walks the same flip the writer does, so a flip in the wrong direction
        /// would satisfy both and prove only that the code agrees with itself. This hero is built
        /// with its top two rows empty and everything below them filled, which is a fact about
        /// the hero and not about the texture. In a texture the first rows of data are the
        /// BOTTOM ones, so the empty rows have to come out at the end.
        /// </remarks>
        [Test]
        public void TheTopOfTheArtIsTheTopOfTheTexture()
        {
            Texture2D grid = HeroTextures.Grid(_hero);

            try
            {
                byte[] raw = grid.GetRawTextureData();

                for (int row = 0; row < grid.height; row++)
                {
                    bool empty = true;
                    for (int x = 0; x < grid.width; x++)
                    {
                        if (raw[row * grid.width + x] != HeroIndex.Nothing) empty = false;
                    }

                    // The last BareRows rows of the data are the top of the art, and only those.
                    bool shouldBeEmpty = row >= grid.height - BareRows;

                    Assert.That(empty, Is.EqualTo(shouldBeEmpty),
                        shouldBeEmpty
                            ? "the delver has been flipped: row " + row + " should be the bare top"
                            : "row " + row + " came out empty and should not have");
                }
            }
            finally
            {
                Object.DestroyImmediate(grid);
            }
        }

        /* ---------- the palette ---------- */

        [Test]
        public void ThePaletteIsOneTexelPerColourAndNothingIsTransparent()
        {
            Texture2D palette = HeroTextures.Palette(_hero, _palette);

            try
            {
                Assert.That(palette.width, Is.EqualTo(_hero.Entries.Count));
                Assert.That(palette.height, Is.EqualTo(1));
                Assert.That(palette.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(_hero.Entries.Count, Is.GreaterThan(4),
                    "a delver with three colours would not exercise much");

                Color32[] texels = palette.GetPixels32();
                Rgb[] want = _hero.Colours(_palette);

                Assert.That(texels[HeroIndex.Nothing].a, Is.Zero, "index zero has to be invisible");

                for (int i = 1; i < texels.Length; i++)
                {
                    Assert.That(texels[i].a, Is.EqualTo(255), _hero.Entries[i] + " is see-through");
                    Assert.That(texels[i].r, Is.EqualTo(want[i].R), _hero.Entries[i].ToString());
                    Assert.That(texels[i].g, Is.EqualTo(want[i].G), _hero.Entries[i].ToString());
                    Assert.That(texels[i].b, Is.EqualTo(want[i].B), _hero.Entries[i].ToString());
                }
            }
            finally
            {
                Object.DestroyImmediate(palette);
            }
        }

        /// <summary>A recolour changes the same texture rather than making a new one.</summary>
        /// <remarks>
        /// What a swatch drag actually does. If it allocated instead, every material pointing at
        /// the old texture would go on drawing the old colours and nobody would see the change.
        /// </remarks>
        [Test]
        public void ARecolourRewritesThePaletteInPlace()
        {
            Texture2D palette = HeroTextures.Palette(_hero, _palette);

            try
            {
                Color32 before = palette.GetPixels32()[1];

                var repainted = new Dictionary<char, Rgb>(_palette);
                foreach (ComposedPixel entry in _hero.Entries) repainted[entry.Role] = new Rgb(1, 2, 3);

                HeroTextures.Recolour(palette, _hero, repainted);

                Assert.That(palette.GetPixels32()[1], Is.Not.EqualTo(before), "the drag did nothing");
                Assert.That(palette.width, Is.EqualTo(_hero.Entries.Count), "still the same texture");
            }
            finally
            {
                Object.DestroyImmediate(palette);
            }
        }

        [Test]
        public void EachFrameHasItsOwnSliceOfTheGrid()
        {
            var seen = new HashSet<float>();

            for (int frame = 0; frame < _hero.Frames.Count; frame++)
            {
                Rect uv = HeroTextures.FrameUv(_hero, frame);

                Assert.That(uv.width, Is.EqualTo(1f / _hero.Frames.Count).Within(0.0001f));
                Assert.That(uv.height, Is.EqualTo(1f));
                Assert.That(seen.Add(uv.x), Is.True, "frame " + frame + " overlaps another");
            }

            Rect last = HeroTextures.FrameUv(_hero, _hero.Frames.Count - 1);
            Assert.That(last.xMax, Is.EqualTo(1f).Within(0.0001f), "the last frame runs to the edge");
        }

        /* ---------- the shader ---------- */

        /// <summary>
        /// The shader compiles.
        /// </summary>
        /// <remarks>
        /// A shader that fails to compile does not throw. It falls back, draws something
        /// plausible, and puts one line in a console nobody is reading — which is why this is
        /// asked out loud rather than left to somebody noticing a delver looks wrong.
        /// </remarks>
        [Test]
        public void TheShaderCompiles()
        {
            Shader shader = Shader.Find(HeroMaterial.ShaderName);

            Assert.That(shader, Is.Not.Null, HeroMaterial.ShaderName + " is missing");
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, Errors(shader));
            Assert.That(shader.isSupported, Is.True, "this shader will not run on this machine");
        }

        [Test]
        public void AMaterialCarriesThePaletteAndItsWidth()
        {
            Texture2D palette = HeroTextures.Palette(_hero, _palette);
            Material material = HeroMaterial.For(palette);

            try
            {
                Assert.That(material, Is.Not.Null);
                Assert.That(material.GetTexture("_Palette"), Is.SameAs(palette));
                Assert.That(material.GetFloat("_PaletteSize"), Is.EqualTo(_hero.Entries.Count));
            }
            finally
            {
                if (material != null) Object.DestroyImmediate(material);
                Object.DestroyImmediate(palette);
            }
        }

        private static string Errors(Shader shader)
        {
            ShaderMessage[] messages = ShaderUtil.GetShaderMessages(shader);
            var said = new List<string>();

            foreach (ShaderMessage message in messages)
            {
                said.Add(message.file + "(" + message.line + "): " + message.message);
            }

            return said.Count == 0 ? "" : string.Join("\n", said);
        }

        /* ---------- the delver ---------- */

        /// <summary>
        /// A small hero with its top rows bare, so which way up it is can be asked.
        /// </summary>
        /// <remarks>
        /// Roles are taken from the palette itself rather than invented, so every entry resolves
        /// to a real colour and the palette test is comparing something. Tones are laid on every
        /// third column, which is what makes the table longer than the list of roles — a shadow
        /// over a colour is a different colour, and that is the whole reason a role key alone
        /// cannot be the index.
        /// </remarks>
        private static ComposedHero Lopsided(IReadOnlyDictionary<char, Rgb> palette)
        {
            var roles = new List<char>(palette.Keys);
            roles.Sort();

            var hero = new ComposedHero { Size = Side, Ms = 120, Mode = "loop" };

            for (int frame = 0; frame < FrameCount; frame++)
            {
                var pixels = new ComposedPixel[Side * Side];

                for (int y = 0; y < Side; y++)
                {
                    for (int x = 0; x < Side; x++)
                    {
                        if (y < BareRows)
                        {
                            pixels[y * Side + x] = new ComposedPixel(HeroCompositor.Empty);
                            continue;
                        }

                        char role = roles[(x + y + frame) % 6];
                        string tones = x % 3 == 0 ? "D" : "";
                        pixels[y * Side + x] = new ComposedPixel(role, tones);
                    }
                }

                hero.Frames.Add(pixels);
            }

            return hero;
        }
    }
}
