using System.Collections.Generic;
using RelicRun.Core.Content;
using UnityEngine;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// A composed delver, as two textures: which colour each pixel is, and what those are.
    /// </summary>
    /// <remarks>
    /// The split is the point. The grid is expensive to build and never changes — a delver in
    /// the same clothes is the same shape whatever colour they are. The palette is ninety-odd
    /// bytes and changes every time somebody drags a swatch. Keeping them apart means a
    /// recolour rewrites a one-pixel-tall texture instead of restacking twelve layers,
    /// re-resolving every shadow against what is under it and re-outlining the silhouette.
    ///
    /// Frames are laid out left to right in one texture rather than kept as several. An
    /// animation is then a UV offset — which a <c>RawImage</c> already knows how to do through
    /// <c>uvRect</c> — instead of a texture swap and the material rebind that comes with it.
    ///
    /// Two colour-space details that are easy to get wrong and silent when wrong. The grid holds
    /// INDICES, not colour, so it is created linear: an sRGB texture would have its bytes
    /// gamma-converted on the way to the shader and every index after the first would come back
    /// as a different one. The palette holds real colours, authored as sRGB hex, so it is
    /// created sRGB and converts as any other colour texture does.
    /// </remarks>
    public static class HeroTextures
    {
        /// <summary>
        /// The index grid: one byte per pixel, every frame side by side.
        /// </summary>
        /// <remarks>
        /// Rows are flipped on the way in. Core counts rows from the top, as the pack's own art
        /// is written, and a texture's first row is its bottom one.
        /// </remarks>
        public static Texture2D Grid(HeroIndex hero)
        {
            int width = hero.Size * hero.Frames.Count;
            int height = hero.Size;

            var pixels = new byte[width * height];

            for (int frame = 0; frame < hero.Frames.Count; frame++)
            {
                byte[] source = hero.Frames[frame];
                int left = frame * hero.Size;

                for (int y = 0; y < hero.Size; y++)
                {
                    int row = (height - 1 - y) * width + left;
                    for (int x = 0; x < hero.Size; x++)
                    {
                        pixels[row + x] = source[y * hero.Size + x];
                    }
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.R8, false, true)
            {
                name = "hero grid",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave,
            };

            texture.SetPixelData(pixels, 0);
            texture.Apply(false, false);

            return texture;
        }

        /// <summary>The colours this delver is currently wearing, one texel each.</summary>
        public static Texture2D Palette(HeroIndex hero, IReadOnlyDictionary<char, Rgb> palette)
        {
            var texture = new Texture2D(hero.Entries.Count, 1, TextureFormat.RGBA32, false, false)
            {
                name = "hero palette",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave,
            };

            Recolour(texture, hero, palette);
            return texture;
        }

        /// <summary>
        /// Rewrites an existing palette in place. What a swatch drag actually does.
        /// </summary>
        /// <remarks>
        /// The same texture, so every material already pointing at it picks the change up with
        /// no rebinding and no allocation. Refuses a texture of the wrong width rather than
        /// writing past it or silently painting half a delver.
        /// </remarks>
        public static void Recolour(Texture2D texture, HeroIndex hero,
            IReadOnlyDictionary<char, Rgb> palette)
        {
            if (texture.width != hero.Entries.Count)
            {
                Debug.LogError("this palette is " + texture.width + " wide and the hero needs " +
                               hero.Entries.Count);
                return;
            }

            Rgb[] colours = hero.Colours(palette);
            var texels = new Color32[colours.Length];

            for (int i = 0; i < colours.Length; i++)
            {
                // Index zero is nothing at all, and nothing is transparent rather than a colour.
                byte alpha = (byte)(hero.Entries[i].IsEmpty ? 0 : 255);
                texels[i] = new Color32(colours[i].R, colours[i].G, colours[i].B, alpha);
            }

            texture.SetPixels32(texels);
            texture.Apply(false, false);
        }

        /// <summary>
        /// Where one frame sits in the grid, as a <c>RawImage.uvRect</c>.
        /// </summary>
        public static Rect FrameUv(HeroIndex hero, int frame)
        {
            float slice = 1f / hero.Frames.Count;
            return new Rect(frame * slice, 0f, slice, 1f);
        }
    }
}
