using NUnit.Framework;
using RelicRun.Core.Presentation;

namespace RelicRun.Tests
{
    /// <summary>
    /// The walk down the hall, which is the only thing that says how much floor is left.
    /// </summary>
    /// <remarks>
    /// A floor is one long room with a door at each end, drawn wider than the screen. The delver
    /// enters by the left door and leaves by the right, and every foe is a stride between them —
    /// so the pan is information, not decoration, and a fight that skips it loses the answer to
    /// "how far in am I".
    /// </remarks>
    [TestFixture]
    public class HallPanTests
    {
        /// <summary>
        /// The strides are equal, and there is one more of them than there are foes.
        /// </summary>
        /// <remarks>
        /// The plus one is the whole rule and the easiest thing to get wrong. Dividing by the
        /// number of FOES puts the delver at the right-hand door the moment the last one dies,
        /// with the walk to the exit still to come and nowhere left to walk. Three foes are four
        /// strides: one to each of them, and one to the door.
        /// </remarks>
        [Test]
        public void ThreeFoesAreFourEqualStrides()
        {
            Assert.That(HallPan.Of(0, 3), Is.EqualTo(0d), "the left door");
            Assert.That(HallPan.Of(1, 3), Is.EqualTo(0.25d).Within(1e-9));
            Assert.That(HallPan.Of(2, 3), Is.EqualTo(0.50d).Within(1e-9));
            Assert.That(HallPan.Of(3, 3), Is.EqualTo(0.75d).Within(1e-9),
                "the last foe is not the door: there is a walk left after it");
            Assert.That(HallPan.Of(4, 3), Is.EqualTo(1d), "and this is the door");
        }

        /// <summary>
        /// A lone foe still leaves half the hall to walk afterwards.
        /// </summary>
        /// <remarks>
        /// The smallest case, and the one where an off-by-one is invisible in the middle of a
        /// floor: with one foe the two strides are halves, so the delver meets it in the middle
        /// of the room rather than at the far door.
        /// </remarks>
        [Test]
        public void OneFoeIsMetInTheMiddle()
        {
            Assert.That(HallPan.Of(1, 1), Is.EqualTo(0.5d).Within(1e-9));
            Assert.That(HallPan.Of(2, 1), Is.EqualTo(1d));
        }

        /// <summary>
        /// A pan never leaves the hall, however many foes turn up.
        /// </summary>
        /// <remarks>
        /// Foes can arrive uncounted — the source has enemies that call for help — and a pan past
        /// the right door would slide the art off and show what is behind it, which is nothing.
        /// Below zero is the same problem at the other end.
        /// </remarks>
        [TestCase(9, 3)]
        [TestCase(1, 0)]
        [TestCase(400, 1)]
        // One past the door, which is the case that divides to more than one rather than
        // obviously too far. A pan of 1.25 slides the art clean off its own right edge.
        [TestCase(5, 3)]
        public void APanNeverLeavesTheHall(int stride, int foes)
        {
            Assert.That(HallPan.Of(stride, foes), Is.InRange(0d, 1d));
        }

        [TestCase(-1, 3)]
        [TestCase(0, 0)]
        [TestCase(-5, -5)]
        // No foes at all AND no walk taken: without the early return this divides zero strides
        // into zero and calls the delver already at the far door.
        [TestCase(0, -1)]
        public void ANonsenseWalkStartsAtTheDoor(int stride, int foes)
        {
            Assert.That(HallPan.Of(stride, foes), Is.EqualTo(0d));
        }

        /// <summary>
        /// The art slides left, and only ever left.
        /// </summary>
        /// <remarks>
        /// It is wider than the window, so the offset is negative: the room moves the other way
        /// from the delver. Art NARROWER than its window has nowhere to go, and sliding it right
        /// would walk the delver backwards down a hall they had not been down.
        /// </remarks>
        [Test]
        public void TheHallSlidesLeftAndNeverRight()
        {
            Assert.That(HallPan.Offset(390d, 1000d, 0d), Is.EqualTo(0d), "flush with the left door");
            Assert.That(HallPan.Offset(390d, 1000d, 1d), Is.EqualTo(-610d),
                "flush with the right door");
            Assert.That(HallPan.Offset(390d, 1000d, 0.5d), Is.EqualTo(-305d));

            Assert.That(HallPan.Offset(1000d, 390d, 1d), Is.EqualTo(0d),
                "art narrower than the window has nowhere to walk");
        }

        /// <summary>
        /// The hall is as wide as its own aspect makes it at the height it is given.
        /// </summary>
        /// <remarks>
        /// The source's rule, and the reason the doors land on the edges. Sized any other way —
        /// stretched to the window, say — the left door would sit somewhere in the middle of a
        /// wall and the walk would begin nowhere in particular.
        /// </remarks>
        [Test]
        public void TheHallIsAsWideAsItsOwnShape()
        {
            Assert.That(HallPan.Width(844d, 960d, 540d), Is.EqualTo(844d * (960d / 540d)).Within(1e-9));

            Assert.That(HallPan.Width(844d, 960d, 0d), Is.EqualTo(844d),
                "art of no height cannot give an aspect, and a square is better than a divide");
        }

        /// <summary>
        /// The hall settles before the delver stops walking.
        /// </summary>
        /// <remarks>
        /// Two thousand six hundred against three thousand three hundred and fifty. The gap is
        /// the source's and it is the difference between a foe appearing in a settled room and
        /// one sliding into place underneath a fight that has already started.
        /// </remarks>
        [Test]
        public void ThePanFinishesBeforeTheWalkDoes()
        {
            PacingRules shipped = PacingRules.Shipped();

            Assert.That(shipped.PanMs, Is.LessThan(shipped.WalkMs));
            Assert.That(shipped.PanMs, Is.GreaterThan(0));
        }
    }
}
