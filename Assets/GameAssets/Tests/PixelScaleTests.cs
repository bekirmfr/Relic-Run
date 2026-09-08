using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Presentation;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// The scale that keeps a baked pixel face looking like pixels.
    /// </summary>
    /// <remarks>
    /// Arithmetic, and tested as arithmetic, because the failure it prevents is not visible in
    /// code at all — it is a screenshot where every letter looks doubled, which took a round
    /// trip through a phone to notice and was then blamed on the typeface.
    /// </remarks>
    [TestFixture]
    public class PixelScaleTests
    {
        /// <summary>
        /// The design area is the source's own shell, not a number somebody liked.
        /// </summary>
        /// <remarks>
        /// Read off the markup by <c>Tools/capture/shell.mjs</c> rather than typed here, because
        /// everything else in the port is a fraction of these two numbers — every font size,
        /// padding and offset in the source is relative to the box it is drawn in, and 14px is a
        /// heading in a 390-wide shell and a footnote in a 1080-wide one.
        ///
        /// Without this the constants agree with nothing. Mutation testing put 375 and 848 in
        /// their place — both entirely plausible, 848 being the source's OWN scale divisor — and
        /// every other test in this file passed, because every other test derives its
        /// expectations from the constants it is checking.
        /// </remarks>
        [Test]
        public void TheDesignAreaIsTheShellTheSourceDrawsIn()
        {
            JObject shell = Corpus.Object("shell.json");

            Assert.That(PixelScale.DesignWidth, Is.EqualTo(shell["width"].Value<int>()),
                "the port is laying out against a different box than the game was drawn in");
            Assert.That(PixelScale.DesignHeight, Is.EqualTo(shell["height"].Value<int>()));
        }

        /// <summary>
        /// The port does not use the source's scale, and that is the decision.
        /// </summary>
        /// <remarks>
        /// The source divides by 848 — the shell plus its borders — and keeps the fraction. Held
        /// here so the difference is visible: if the port ever agreed with that number it would
        /// be scaling a baked bitmap by 0.9670, which is the exact thing this class exists to
        /// stop.
        /// </remarks>
        [Test]
        public void ThePortFloorsWhereTheSourceTookTheFraction()
        {
            JObject shell = Corpus.Object("shell.json");
            int divisor = shell["scale"]["divisor"].Value<int>();

            Assert.That(divisor, Is.GreaterThan(PixelScale.DesignHeight),
                "the source divides by the shell plus its borders");

            // The source's answer for a common phone, and the port's. They are not close.
            Assert.That(848 * PixelScale.For(1080, 2400), Is.Not.EqualTo(2400 - 24),
                "the port has started agreeing with a fractional scale");
        }

        /// <summary>
        /// The phones people actually hold, and what each is worth.
        /// </summary>
        /// <remarks>
        /// Real resolutions rather than round numbers. The point of the table is that the answers
        /// are UNEVEN — a 1080 phone and a 1440 phone differ by a whole step, and a 2340-tall one
        /// and a 2400-tall one do not — and a rule that produced something tidy here would be a
        /// rule that had stopped dividing.
        /// </remarks>
        [TestCase(1080, 2400, 2, TestName = "a common 1080p phone")]
        [TestCase(1080, 2340, 2, TestName = "a shorter 1080p phone, same scale")]
        [TestCase(1440, 3200, 3, TestName = "a 1440p phone gets a whole step more")]
        [TestCase(750, 1334, 1, TestName = "an older 4.7 inch phone barely fits the design area")]
        [TestCase(2048, 2732, 3, TestName = "a tablet is capped by its height, not its width")]
        public void EveryScreenGetsAWholeNumber(int width, int height, int expected)
        {
            Assert.That(PixelScale.For(width, height), Is.EqualTo(expected));
        }

        /// <summary>
        /// The design area fits at the scale chosen, and would not fit at one more.
        /// </summary>
        /// <remarks>
        /// Asked as a property rather than a table, because it is the actual promise: whatever
        /// the arithmetic, the source's own shell has to be on the screen. The second half is
        /// what stops the promise being kept by returning 1 forever.
        /// </remarks>
        [Test]
        public void TheDesignAreaFitsAndOneStepMoreWouldNot()
        {
            for (int width = 320; width <= 2560; width += 7)
            {
                for (int height = 560; height <= 3200; height += 53)
                {
                    int scale = PixelScale.For(width, height);

                    Assert.That(scale, Is.GreaterThanOrEqualTo(1), width + "x" + height);

                    if (scale == 1 && (width < PixelScale.DesignWidth || height < PixelScale.DesignHeight))
                    {
                        // Smaller than the design area. One is the floor, and the overflow shows.
                        continue;
                    }

                    Assert.That(PixelScale.DesignWidth * scale, Is.LessThanOrEqualTo(width),
                        "the design area is wider than the screen at " + width + "x" + height);
                    Assert.That(PixelScale.DesignHeight * scale, Is.LessThanOrEqualTo(height),
                        "the design area is taller than the screen at " + width + "x" + height);

                    bool roomForMore = PixelScale.DesignWidth * (scale + 1) <= width
                        && PixelScale.DesignHeight * (scale + 1) <= height;

                    Assert.That(roomForMore, Is.False,
                        width + "x" + height + " had room for " + (scale + 1) + " and got " + scale);
                }
            }
        }

        /// <summary>
        /// A screen too small for the design area still gets a scale of one.
        /// </summary>
        /// <remarks>
        /// Zero is the answer the arithmetic wants to give and it is the wrong one: the whole
        /// interface would be multiplied away, which does not look like a layout that overflowed,
        /// it looks like a scene that failed to load.
        /// </remarks>
        [TestCase(320, 480)]
        [TestCase(1, 1)]
        [TestCase(0, 0)]
        [TestCase(-100, -100)]
        public void ATinyScreenStillDrawsSomething(int width, int height)
        {
            Assert.That(PixelScale.For(width, height), Is.EqualTo(1));
        }

        [Test]
        public void ADesignAreaOfNothingDoesNotDivideByZero()
        {
            Assert.That(PixelScale.For(1080, 2400, 0, 0), Is.EqualTo(1));
            Assert.That(PixelScale.For(1080, 2400, 390, 0), Is.EqualTo(1));
        }

        /// <summary>The canvas is the screen divided down, so the extra room is real.</summary>
        [Test]
        public void TheCanvasIsAsWideAsTheScreenDividesInto()
        {
            int scale = PixelScale.For(1080, 2400);

            Assert.That(scale, Is.EqualTo(2));
            Assert.That(PixelScale.Units(1080, scale), Is.EqualTo(540));
            Assert.That(PixelScale.Units(2400, scale), Is.EqualTo(1200));

            Assert.That(PixelScale.Units(1080, scale), Is.GreaterThan(PixelScale.DesignWidth),
                "the design area is a floor, not a frame — the rest is room the layout may use");
        }

        /// <summary>
        /// Sizes land on the grid the face was baked on.
        /// </summary>
        /// <remarks>
        /// Twelve is the case worth staring at. It is exactly between two cells, reads as an
        /// entirely sensible font size, and is the one that puts back the smear this whole file
        /// exists to prevent.
        /// </remarks>
        [TestCase(0, 8)]
        [TestCase(1, 8)]
        [TestCase(8, 8)]
        [TestCase(9, 8)]
        [TestCase(11, 8)]
        [TestCase(12, 16)]
        [TestCase(16, 16)]
        [TestCase(20, 24)]
        [TestCase(24, 24)]
        [TestCase(40, 40)]
        public void EverySizeLandsOnTheGrid(int asked, int given)
        {
            Assert.That(PixelScale.Snap(asked), Is.EqualTo(given));
        }

        [Test]
        public void NothingSnapsToNothing()
        {
            for (int size = -20; size < 400; size++)
            {
                int snapped = PixelScale.Snap(size);

                Assert.That(snapped % PixelScale.Grid, Is.Zero, size + " landed off the grid");
                Assert.That(snapped, Is.GreaterThanOrEqualTo(PixelScale.Grid),
                    size + " snapped to a label that is not there");
            }
        }
    }
}
