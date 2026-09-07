namespace RelicRun.Core.Content
{
    /// <summary>
    /// What the hero's appearance costs a seeded generator.
    /// </summary>
    /// <remarks>
    /// A versus lobby rolls every rival a face — an outfit slot by slot, a colour per family,
    /// an accent and a motto — and it rolls them from the MATCH's generator, the same one that
    /// then decides their statline and their hall. So the wardrobe is not optional to a port
    /// that has no wardrobe: skipping those draws would silently shift every number after them.
    ///
    /// Only the COUNTS are here. What each draw picks out is appearance and lands with the
    /// wardrobe itself; nothing downstream reads it, which is why a lobby can be generated
    /// exactly without a single sprite. The recorded rosters are what hold these honest — get a
    /// count wrong and every rival after the first is a different delver.
    /// </remarks>
    public static class HeroWardrobe
    {
        /// <summary>
        /// Outfit slots that take a draw when a face is rolled.
        /// </summary>
        /// <remarks>
        /// The full paint order is eleven slots deep, back to front: the backdrop, then the
        /// body's own layers and the things it carries. The backdrop is skipped rather than
        /// rolled — a rival is drawn against the hall they are fought in — so ten are drawn.
        /// </remarks>
        public const int DrawnSlots = 10;

        /// <summary>
        /// Colour families that take a draw.
        /// </summary>
        /// <remarks>
        /// Of the twenty-six families the palette carries, the nine named hues and six named
        /// materials are canonical and never re-rolled: a Gold stays gold. The remaining eleven
        /// — hair, skin, outfit, cape, face, accent, eye, eyebrow, backdrop, legs and boots —
        /// each draw one swatch, and every one of them has more than a single option, which is
        /// the condition the source checks before spending the draw.
        /// </remarks>
        public const int DrawnFamilies = 11;

        /// <summary>The accent colour, then the motto: one draw each, after the families.</summary>
        public const int DrawnFlourishes = 2;

        /// <summary>Everything one rival's face costs.</summary>
        public const int DrawsPerFace = DrawnSlots + DrawnFamilies + DrawnFlourishes;
    }
}
