using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Presentation;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// Which of the three layouts a screen is drawn in.
    /// </summary>
    /// <remarks>
    /// The first thing Phase 10 needs, because every screen in it asks the question. The
    /// thresholds are the source's; what they are measured in is this port's, and the two answer
    /// slightly different questions on a device that does not scale cleanly.
    /// </remarks>
    [TestFixture]
    public class ViewModeTests
    {
        /// <summary>
        /// The thresholds are the source's, read rather than typed.
        /// </summary>
        /// <remarks>
        /// Without this every other test in this file agrees only with the constants it is
        /// checking — 1100 could be 1200 throughout and the whole fixture would still pass, and
        /// the game would use the phone layout on a band of laptops nobody here owns. The same
        /// hole PixelScale had, closed the same way.
        ///
        /// The source's fourth number is recorded too, and this is where its absence is stated:
        /// <c>w &gt; 430</c> alongside the tablet's 760 cannot fail once the 760 has passed.
        /// </remarks>
        [Test]
        public void TheBreakpointsAreTheSourcesOwn()
        {
            JObject view = (JObject)Corpus.Object("shell.json")["view"];

            Assert.That(ViewModes.DesktopWide, Is.EqualTo(view["desktopWide"].Value<int>()));
            Assert.That(ViewModes.DesktopTall, Is.EqualTo(view["desktopTall"].Value<int>()));
            Assert.That(ViewModes.TabletWide, Is.EqualTo(view["tabletWide"].Value<int>()));

            Assert.That(view["tabletFloor"].Value<int>(),
                Is.LessThan(view["tabletWide"].Value<int>()),
                "the source's second tablet test is not a rule: it cannot fail once the first has passed");
        }

        /// <summary>
        /// A desktop needs width, height and landscape — all three.
        /// </summary>
        /// <remarks>
        /// The desktop layout puts the fight between two side rails. Width without height is a
        /// letterbox with rails down it; a portrait window of any size is two rails with a slot
        /// between them. So each of the three is asked for on its own, and each is denied on its
        /// own.
        /// </remarks>
        [Test]
        public void ADesktopNeedsRoomInBothDirectionsAndToBeLandscape()
        {
            Assert.That(ViewModes.Of(1280, 800), Is.EqualTo(View.Desktop));

            Assert.That(ViewModes.Of(1099, 800), Is.Not.EqualTo(View.Desktop), "not wide enough");
            Assert.That(ViewModes.Of(1280, 699), Is.Not.EqualTo(View.Desktop), "not tall enough");

            // Both thresholds cleared and still refused, which is what makes this a test of the
            // landscape clause. An earlier 800x1280 here passed for the wrong reason — it was
            // too narrow — and a mutant that dropped the clause entirely survived it.
            Assert.That(ViewModes.Of(1200, 1600), Is.Not.EqualTo(View.Desktop),
                "portrait, however much room it has");
        }

        /// <summary>A window at exactly the threshold qualifies.</summary>
        /// <remarks>
        /// The source uses >=, and an off-by-one here is a device that sits on the boundary
        /// getting the wrong layout for its whole life while every other device is fine.
        /// </remarks>
        [Test]
        public void TheThresholdsAreInclusive()
        {
            Assert.That(ViewModes.Of(ViewModes.DesktopWide, ViewModes.DesktopTall),
                Is.EqualTo(View.Desktop));

            Assert.That(ViewModes.Of(ViewModes.TabletWide, 1200), Is.EqualTo(View.Tablet));
            Assert.That(ViewModes.Of(ViewModes.TabletWide - 1, 1200), Is.EqualTo(View.Phone));
        }

        /// <summary>
        /// A tablet is a matter of width alone.
        /// </summary>
        /// <remarks>
        /// Unlike the desktop, and deliberately: a tablet layout is the phone's with room around
        /// it, so it needs somewhere to put the room and nothing else. A tall narrow window is a
        /// phone whatever its height.
        /// </remarks>
        [Test]
        public void ATabletIsAboutWidthAlone()
        {
            Assert.That(ViewModes.Of(900, 1400), Is.EqualTo(View.Tablet), "portrait, and still a tablet");
            Assert.That(ViewModes.Of(900, 500), Is.EqualTo(View.Tablet), "short, and still a tablet");

            Assert.That(ViewModes.Of(420, 3000), Is.EqualTo(View.Phone),
                "a very tall narrow window is a phone, not a tablet stood on end");
        }

        /// <summary>The room is the screen divided by the whole factor it is drawn at.</summary>
        /// <remarks>
        /// This is where the port and the browser part company, and the case is worth spelling
        /// out because it looks like a bug: a 1080-wide phone at 2x has 540 units of room, which
        /// is a phone — and so does the browser call it. A 1440-wide phone at 3x has 480, which
        /// is a SMALLER number for a BIGGER screen, and still a phone. Both answers are right,
        /// because the question is how much room the layout has rather than how many pixels the
        /// device claims.
        /// </remarks>
        [Test]
        public void TheRoomIsWhatTheScaleLeaves()
        {
            int common = PixelScale.For(1080, 2400);
            int sharper = PixelScale.For(1440, 3088);

            Assert.That(ViewModes.Room(1080, common), Is.EqualTo(540));
            Assert.That(ViewModes.Room(1440, sharper), Is.EqualTo(480));

            Assert.That(ViewModes.Of(1080, 2400, common), Is.EqualTo(View.Phone));
            Assert.That(ViewModes.Of(1440, 3088, sharper), Is.EqualTo(View.Phone));
        }

        /// <summary>A desktop window really does resolve to the desktop layout.</summary>
        /// <remarks>
        /// At a scale of one, which is what a wide short window gets: 1920x1080 divides by 390
        /// four times across and by 844 once down, and the smaller wins.
        /// </remarks>
        [Test]
        public void ADesktopWindowGetsTheDesktopLayout()
        {
            int scale = PixelScale.For(1920, 1080);

            Assert.That(scale, Is.EqualTo(1), "a short window has room for one whole design area");
            Assert.That(ViewModes.Of(1920, 1080, scale), Is.EqualTo(View.Desktop));
        }

        /// <summary>
        /// Every layout is promised exactly the room that qualified it.
        /// </summary>
        /// <remarks>
        /// The design widths ARE the thresholds, which is what stops a layout being built against
        /// room the smallest device that qualifies does not have. A tablet design of 900 would
        /// look right everywhere except on the 760-wide tablets that are the reason the layout
        /// exists.
        /// </remarks>
        [Test]
        public void ALayoutIsBuiltAgainstTheRoomItIsGuaranteed()
        {
            Assert.That(ViewModes.DesignWidth(View.Phone), Is.EqualTo(PixelScale.DesignWidth));
            Assert.That(ViewModes.DesignWidth(View.Tablet), Is.EqualTo(ViewModes.TabletWide));
            Assert.That(ViewModes.DesignWidth(View.Desktop), Is.EqualTo(ViewModes.DesktopWide));

            Assert.That(ViewModes.DesignWidth(View.Phone),
                Is.LessThan(ViewModes.DesignWidth(View.Tablet)));
            Assert.That(ViewModes.DesignWidth(View.Tablet),
                Is.LessThan(ViewModes.DesignWidth(View.Desktop)));
        }

        /// <summary>
        /// The layout a window qualifies for always fits in it.
        /// </summary>
        /// <remarks>
        /// Asked as a property over a sweep rather than at the two boundaries, because the
        /// failure it guards is a design width creeping past its threshold — which shows up on
        /// exactly one band of devices and nowhere else.
        /// </remarks>
        [Test]
        public void WhateverLayoutIsChosenActuallyFits()
        {
            for (int wide = PixelScale.DesignWidth; wide <= 2600; wide += 13)
            {
                for (int tall = 400; tall <= 1600; tall += 37)
                {
                    View got = ViewModes.Of(wide, tall);

                    Assert.That(ViewModes.DesignWidth(got), Is.LessThanOrEqualTo(wide),
                        "a " + got + " layout was chosen for a window " + wide + " wide");
                }
            }
        }

        /// <summary>
        /// A window narrower than the phone itself still gets the phone, and overflows.
        /// </summary>
        /// <remarks>
        /// The one case where the layout does NOT fit, and it is deliberate rather than missed —
        /// found by the sweep above, which started at 320 and failed. There is no fourth layout
        /// below the phone's, so the choice is between showing the phone layout overflowing and
        /// showing nothing. It overflows, visibly, which is the same answer PixelScale gives when
        /// it floors its factor at one: a layout that spills can be seen to spill, and one
        /// multiplied away to nothing looks like a screen that failed to load.
        /// </remarks>
        [Test]
        public void ATinyWindowGetsThePhoneLayoutAnyway()
        {
            Assert.That(ViewModes.Of(320, 480), Is.EqualTo(View.Phone));

            Assert.That(ViewModes.DesignWidth(View.Phone), Is.GreaterThan(320),
                "and it does not fit, which is the honest answer rather than a hidden one");
        }
    }
}
