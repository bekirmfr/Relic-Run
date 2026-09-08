using System;
using System.Collections.Generic;

namespace RelicRun.Core.Content
{
    /// <summary>
    /// A composed hero, re-encoded as indices into a small table of its own colours.
    /// </summary>
    /// <remarks>
    /// The composition is the expensive part and the palette is the part that changes. A delver
    /// dragging a swatch in the Changing Room is not restacking twelve layers, resolving shadows
    /// and re-outlining a silhouette forty times a second — they are changing what one of ninety
    /// colours means. So the two are separated here: the grid says which colour each pixel is,
    /// the table says what those colours currently are, and recolouring rewrites the table alone.
    ///
    /// A pixel indexes on its MEANING, which is its role and the tones laid over it — not on its
    /// role alone. A shadow does not replace what is beneath it but darkens it, so "the outfit's
    /// dark side" and "the outfit's dark side, one tone darker again" are two different colours
    /// that a single role key cannot tell apart.
    ///
    /// Index zero is nothing, always, whether or not the hero has a transparent pixel in it. A
    /// renderer that reads a grid it did not build still knows what zero means, and that is worth
    /// one wasted entry out of two hundred and fifty-six.
    /// </remarks>
    public sealed class HeroIndex
    {
        /// <summary>The index that means no pixel at all.</summary>
        public const int Nothing = 0;

        /// <summary>
        /// How many colours a grid may hold.
        /// </summary>
        /// <remarks>
        /// A byte each, because the grid is going to a texture and a single channel is the
        /// cheapest thing to sample. The busiest delver the shipped wardrobe can assemble uses
        /// fifty-five, so this is not a ceiling anybody is near — but it IS a ceiling, and one
        /// that would otherwise be discovered by an outfit rendering as noise.
        /// </remarks>
        public const int Limit = 256;

        /// <summary>Side of one frame, in pixels.</summary>
        public readonly int Size;

        /// <summary>How long each frame is held, in milliseconds.</summary>
        public readonly int Ms;

        /// <summary>Whether the state loops or plays once.</summary>
        public readonly string Mode;

        /// <summary>The colours this hero uses. <see cref="Nothing"/> is first and is empty.</summary>
        public readonly IReadOnlyList<ComposedPixel> Entries;

        /// <summary>One byte per pixel, row by row, top row first.</summary>
        public readonly IReadOnlyList<byte[]> Frames;

        private HeroIndex(int size, int ms, string mode, IReadOnlyList<ComposedPixel> entries,
            IReadOnlyList<byte[]> frames)
        {
            Size = size;
            Ms = ms;
            Mode = mode;
            Entries = entries;
            Frames = frames;
        }

        /// <summary>
        /// Indexes a composed hero.
        /// </summary>
        /// <remarks>
        /// Entries are numbered in the order they are first met, scanning frames in order and
        /// each frame from its top-left. That is arbitrary but it has to be SOMETHING, and being
        /// arbitrary is not the same as being unstable: the same hero indexes the same way every
        /// time, so a grid written to disk and a table built later still agree.
        /// </remarks>
        public static HeroIndex Of(ComposedHero hero)
        {
            if (hero == null) throw new ArgumentNullException("hero");

            // Nothing is claimed before anything is looked at, which is the whole of what makes
            // index zero mean the same thing in every hero. An empty pixel then indexes as
            // nothing by the ordinary path rather than by a special case.
            var entries = new List<ComposedPixel> { new ComposedPixel(HeroCompositor.Empty) };
            var seen = new Dictionary<string, byte> { { Key(entries[0]), Nothing } };

            var frames = new List<byte[]>(hero.Frames.Count);

            for (int f = 0; f < hero.Frames.Count; f++)
            {
                ComposedPixel[] source = hero.Frames[f];
                var grid = new byte[source.Length];

                for (int i = 0; i < source.Length; i++)
                {
                    string key = Key(source[i]);

                    byte at;
                    if (!seen.TryGetValue(key, out at))
                    {
                        if (entries.Count >= Limit)
                        {
                            throw new InvalidOperationException(
                                "a hero cannot hold more than " + Limit + " colours");
                        }

                        at = (byte)entries.Count;
                        entries.Add(source[i]);
                        seen[key] = at;
                    }

                    grid[i] = at;
                }

                frames.Add(grid);
            }

            return new HeroIndex(hero.Size, hero.Ms, hero.Mode, entries, frames);
        }

        /// <summary>What is drawn at one pixel.</summary>
        public ComposedPixel At(int frame, int x, int y)
        {
            return Entries[IndexAt(frame, x, y)];
        }

        /// <summary>The index at one pixel.</summary>
        public byte IndexAt(int frame, int x, int y)
        {
            return Frames[frame][y * Size + x];
        }

        /// <summary>
        /// This hero's colours, resolved against a palette.
        /// </summary>
        /// <remarks>
        /// The whole of what changes when a delver picks a different shirt colour, which is why
        /// it is a separate call rather than something baked into the grid. Entry zero resolves
        /// to whatever the palette says about nothing, and is meant to be drawn as transparent
        /// rather than as that colour — the caller supplies the alpha, because Core has no notion
        /// of one.
        /// </remarks>
        public Rgb[] Colours(IReadOnlyDictionary<char, Rgb> palette)
        {
            var colours = new Rgb[Entries.Count];
            for (int i = 0; i < colours.Length; i++)
            {
                colours[i] = HeroCompositor.Colour(Entries[i], palette);
            }

            return colours;
        }

        /// <summary>
        /// A pixel's identity: its role, then the tones over it in the order they were applied.
        /// </summary>
        /// <remarks>
        /// A role is one character, so a role followed by its tones can only be read one way and
        /// needs no separator. The ORDER of the tones is part of the identity: paint is laid over
        /// paint, so a highlight under a shadow is not the same colour as a shadow under a
        /// highlight.
        /// </remarks>
        private static string Key(ComposedPixel pixel)
        {
            return pixel.Role + pixel.Tones;
        }
    }
}
