namespace RelicRun.Core.Presentation
{
    /// <summary>
    /// How many screen pixels one authored pixel is worth.
    /// </summary>
    /// <remarks>
    /// A whole number, always, and that is the entire point. The ui face is a bitmap baked on an
    /// eight-pixel grid; drawn at a fractional multiple of it, some rows of a glyph get five
    /// screen pixels and the next gets six, and the text arrives looking doubled and smeared. It
    /// is not a font problem and no typeface fixes it — it is division.
    ///
    /// The source does the opposite and is right to. It scales its shell by
    /// <c>min(1, (innerHeight - 24) / 848)</c> — fractional, and only ever shrinking — because a
    /// browser rasterises Silkscreen's OUTLINES afresh at every size and can afford any number it
    /// likes. Magnifying a baked bitmap cannot, so the port takes the other road: whole numbers
    /// only, and the design area breathes instead of the glyphs.
    ///
    /// What is guaranteed is the AREA, not the size. <see cref="DesignWidth"/> by
    /// <see cref="DesignHeight"/> is the source's own shell, 390x844, and the scale is the
    /// largest whole number that still fits it on the screen. Everything beyond that is extra
    /// room the layout may use, so a taller phone shows more rather than the same thing letter-
    /// boxed. It follows that a canvas is a different number of units wide on different devices —
    /// 540 on a 1080-wide phone at 2x, 480 on a 1440-wide one at 3x — which is the price of
    /// whole numbers and is paid in layout, where it is cheap, rather than in glyphs, where it
    /// is not.
    /// </remarks>
    public static class PixelScale
    {
        /// <summary>The source's shell, which is the area every device is promised.</summary>
        public const int DesignWidth = 390;

        /// <summary>As above. Together these are an iPhone's logical viewport, which is what the
        /// source was drawn against.</summary>
        public const int DesignHeight = 844;

        /// <summary>Every authored size is a multiple of this, because the face is baked on it.</summary>
        /// <remarks>
        /// Not a style rule. A glyph baked at eight pixels and drawn at twelve is drawn at one and
        /// a half times, and half a pixel does not exist — so the row that has to round lands a
        /// pixel wide where its neighbour is two. Twelve is exactly the size that looks like a
        /// reasonable compromise and is the one that cannot work.
        /// </remarks>
        public const int Grid = 8;

        /// <summary>The scale for a screen, against the design area.</summary>
        public static int For(int screenWidth, int screenHeight)
        {
            return For(screenWidth, screenHeight, DesignWidth, DesignHeight);
        }

        /// <summary>
        /// The largest whole scale at which the design area still fits on the screen.
        /// </summary>
        /// <remarks>
        /// Never zero. A screen smaller than the design area — a tiny editor Game view, a window
        /// dragged narrow — has no whole scale that fits, and the honest answer there is one:
        /// the layout overflows and can be seen to overflow. Zero would multiply the whole
        /// interface away to nothing, which looks like a scene that failed to load.
        /// </remarks>
        public static int For(int screenWidth, int screenHeight, int designWidth, int designHeight)
        {
            // Only the design area is guarded, and only because dividing by it would throw. A
            // screen of no width needs no guard of its own: nothing over 390 is nothing, and the
            // floor below already turns nothing into one. A second check there would read like
            // caution and be dead code — mutation testing said so, having found it unkillable.
            if (designWidth <= 0 || designHeight <= 0) return 1;

            int across = screenWidth / designWidth;
            int down = screenHeight / designHeight;
            int fits = across < down ? across : down;

            return fits < 1 ? 1 : fits;
        }

        /// <summary>How many authored units a screen is across at a given scale.</summary>
        public static int Units(int screenPixels, int scale)
        {
            if (scale < 1) return screenPixels;

            return screenPixels / scale;
        }

        /// <summary>
        /// The nearest authored size the baked face can actually draw.
        /// </summary>
        /// <remarks>
        /// Rounds to the grid rather than rejecting, and rounds AWAY from nothing: the smallest
        /// answer is one whole cell, because a zero-point font size is a label that silently is
        /// not there. Halves round up, which is arbitrary and written down so it is not argued
        /// about twice.
        ///
        /// This exists so the sizes in the builder cannot drift. A size typed as twenty looks
        /// perfectly reasonable in a source file and is the exact thing that puts the smear
        /// back.
        /// </remarks>
        public static int Snap(int size)
        {
            if (size <= Grid) return Grid;

            int cells = (size + Grid / 2) / Grid;

            return cells * Grid;
        }
    }
}
