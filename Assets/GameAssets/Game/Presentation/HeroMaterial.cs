using UnityEngine;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The material a delver is drawn with.
    /// </summary>
    /// <remarks>
    /// One per delver, not one shared. The palette is a texture and a texture is a material
    /// property, so two delvers in different colours cannot share a material — and a
    /// <c>MaterialPropertyBlock</c>, which is how this would normally be avoided, does not
    /// reach a <c>CanvasRenderer</c>. There are never more than a handful on screen.
    /// </remarks>
    public static class HeroMaterial
    {
        public const string ShaderName = "RelicRun/Hero Palette Swap";

        private static readonly int PaletteId = Shader.PropertyToID("_Palette");
        private static readonly int PaletteSizeId = Shader.PropertyToID("_PaletteSize");

        /// <summary>A material for one delver, pointed at their own palette.</summary>
        public static Material For(Texture2D palette)
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError(ShaderName + " is missing — delvers will draw as their raw indices");
                return null;
            }

            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            Repoint(material, palette);

            return material;
        }

        /// <summary>
        /// Points a material at a palette, and tells the shader how wide it is.
        /// </summary>
        /// <remarks>
        /// The width travels with the texture because the lookup samples the middle of a texel:
        /// index <i>n</i> is at <c>(n + 0.5) / width</c>. A material left holding the previous
        /// delver's width would read every colour but the first from between two texels, which
        /// with point filtering means the wrong one — a delver in somebody else's clothes rather
        /// than an obvious mistake.
        /// </remarks>
        public static void Repoint(Material material, Texture2D palette)
        {
            if (material == null || palette == null) return;

            material.SetTexture(PaletteId, palette);
            material.SetFloat(PaletteSizeId, palette.width);
        }
    }
}
