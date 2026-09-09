namespace RelicRun.Core.Presentation
{
    /// <summary>
    /// How far down the hall the delver has walked.
    /// </summary>
    /// <remarks>
    /// A floor is one long room drawn wider than the screen, with a door at each end. The delver
    /// starts at the left door and leaves by the right one, and every foe met on the way is a
    /// stride between them. So the walk is not scenery between fights — it is the only thing on
    /// screen that says how much of the floor is left, which is why a fight that skips it loses
    /// information rather than just an animation.
    ///
    /// The source states the geometry in a comment beside it, and it is worth keeping verbatim:
    /// pan 0 is flush left, pan 1 is flush right, with equal strides between. That equality is
    /// the whole rule. The strides are equal because the DENOMINATOR is the number of foes plus
    /// one, not the number of foes — the last stride is the walk to the door after the last one
    /// is dead, and without it a delver would arrive at the exit before the floor was over.
    /// </remarks>
    public static class HallPan
    {
        /// <summary>
        /// How far along, from nothing at the left door to one at the right.
        /// </summary>
        /// <param name="stride">How many walks have been taken, counting from none.</param>
        /// <param name="foes">How many foes this floor holds.</param>
        /// <remarks>
        /// Clamped rather than trusted. A floor can gain a foe it did not announce — the source
        /// has enemies that call for help — and a pan past the right door would show the art's
        /// edge and whatever is behind it, which is nothing.
        /// </remarks>
        public static double Of(int stride, int foes)
        {
            if (stride <= 0) return 0d;

            int strides = foes + 1;

            // No guard against a floor of no foes, and none needed: any stride at or past the
            // last one is already the door, so a strides of zero or less is answered before
            // anything divides by it. A check here would read as caution and be unreachable —
            // mutation testing said so, having found it unkillable.
            if (stride >= strides) return 1d;

            return (double)stride / strides;
        }

        /// <summary>
        /// Where the art sits, given how wide it is and how wide the window on it is.
        /// </summary>
        /// <remarks>
        /// Negative or zero, always: the art is wider than the window, so it slides LEFT to
        /// reveal what is further down the hall. Art narrower than its window has nowhere to go
        /// and stays put rather than sliding right, which would walk the delver backwards.
        /// </remarks>
        public static double Offset(double windowWidth, double artWidth, double pan)
        {
            double room = windowWidth - artWidth;

            return room >= 0d ? 0d : room * pan;
        }

        /// <summary>
        /// How wide the art is when it is drawn as tall as its window.
        /// </summary>
        /// <remarks>
        /// From the art's OWN aspect, which is the source's rule and the reason the doors land
        /// where they should: the hall is scaled to the height available and is then however wide
        /// that makes it. Sizing it any other way would put the left door somewhere other than
        /// the left edge, and the walk would start in the middle of a wall.
        /// </remarks>
        public static double Width(double windowHeight, double artWidth, double artHeight)
        {
            if (artHeight <= 0d) return windowHeight;

            return windowHeight * (artWidth / artHeight);
        }
    }
}
