namespace RelicRun.Core.Presentation
{
    /// <summary>Which of the three layouts a screen is being drawn in.</summary>
    public enum View
    {
        /// <summary>The shape the game was drawn for. Everything else is a widening of it.</summary>
        Phone = 0,

        Tablet = 1,

        Desktop = 2,
    }

    /// <summary>
    /// Which layout a screen gets, from how much room there is.
    /// </summary>
    /// <remarks>
    /// The thresholds are the source's, read off the one line that decides them. What they are
    /// measured IN is not, and the difference is worth stating plainly rather than discovering.
    ///
    /// The source measures the CSS viewport, which is device pixels divided by the browser's
    /// device-pixel-ratio — a fractional number the browser picks. This port has no such number:
    /// it scales by a whole factor so a bitmap face stays sharp, and its canvas units are what
    /// that leaves. Canvas units are therefore this port's CSS pixels, and the thresholds are
    /// measured in them.
    ///
    /// The consequence is real and is not a bug. A device the browser would call a tablet can
    /// land here as a phone, because a whole scale factor divides the screen more coarsely than a
    /// fractional one — a 2048-wide tablet at 3x has 682 units of room where a browser at 2x
    /// would report 1024. The port answers "how much room is there", the browser answers "how
    /// many CSS pixels does this device claim", and on a device that scales cleanly they agree.
    /// </remarks>
    public static class ViewModes
    {
        /// <summary>A desktop needs this much width, this much height, AND to be landscape.</summary>
        /// <remarks>
        /// All three, because the desktop layout puts the fight between two side rails. Given
        /// width without height it would be a letterbox; given a portrait window of any size it
        /// would be two rails with a slot between them.
        /// </remarks>
        public const int DesktopWide = 1100;

        public const int DesktopTall = 700;

        /// <summary>A tablet needs only width. It is the phone layout with room around it.</summary>
        public const int TabletWide = 760;

        /// <summary>
        /// What the room is, given a screen and the whole factor it is drawn at.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Of"/> so the two questions stay apart: how much room, and
        /// what to do with it. A screen that asked for a view mode and got a size would end up
        /// doing its own arithmetic on the way back.
        /// </remarks>
        public static int Room(int screenPixels, int scale)
        {
            return PixelScale.Units(screenPixels, scale);
        }

        /// <summary>The layout for a window of this many units.</summary>
        /// <remarks>
        /// The source also tests <c>w &gt; 430</c> alongside the tablet's 760, which cannot fail
        /// when the first has passed. Not ported: a condition that can only ever be true is not a
        /// rule, and carrying it across would mean carrying an explanation for it too.
        /// </remarks>
        public static View Of(int wide, int tall)
        {
            if (wide >= DesktopWide && tall >= DesktopTall && wide > tall) return View.Desktop;

            return wide >= TabletWide ? View.Tablet : View.Phone;
        }

        /// <summary>The layout for a screen, working the room out on the way.</summary>
        public static View Of(int screenWidth, int screenHeight, int scale)
        {
            return Of(Room(screenWidth, scale), Room(screenHeight, scale));
        }

        /// <summary>
        /// The design area a layout is built against.
        /// </summary>
        /// <remarks>
        /// Every one of them is as TALL as the phone's, and only the width changes. That is the
        /// source's arrangement and it is what makes three layouts affordable: a screen is a
        /// column of things, and a wider view puts columns beside each other rather than
        /// redrawing any of them.
        ///
        /// The numbers are the thresholds themselves, which is deliberate — a layout is
        /// guaranteed exactly the room that qualified it, and never more, so nothing can be built
        /// against a width the smallest qualifying device does not have.
        /// </remarks>
        public static int DesignWidth(View view)
        {
            switch (view)
            {
                case View.Desktop: return DesktopWide;
                case View.Tablet: return TabletWide;
                default: return PixelScale.DesignWidth;
            }
        }
    }
}
