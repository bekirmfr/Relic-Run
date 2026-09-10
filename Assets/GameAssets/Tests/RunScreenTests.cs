using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;

namespace RelicRun.Tests
{
    /// <summary>
    /// The two pieces the run screen draws that no other screen does.
    /// </summary>
    /// <remarks>
    /// The rail across the top says where the delver IS, and the draft says what the next floor
    /// might cost. Everything else on a run screen is about the floor underfoot; these two are
    /// about the descent, which is what makes walking out at the ninth gate a decision rather
    /// than a guess.
    /// </remarks>
    [TestFixture]
    public class RunScreenTests
    {
        /// <summary>
        /// The rail is thirteen floors, with the bazaar and the crown marked.
        /// </summary>
        /// <remarks>
        /// A delver plans a run off this. The bazaar is where gold becomes relics and the crown
        /// is where it ends, and a rail that did not mark them would be thirteen identical dots.
        /// </remarks>
        [Test]
        public void TheRailMarksTheBazaarAndTheCrown()
        {
            FloorRailCard rail = FloorRails.Of(1, null);

            Assert.That(rail.Floors.Count, Is.EqualTo(DelveRun.MaxFloor));
            Assert.That(rail.Deepest, Is.EqualTo(DelveRun.MaxFloor));

            var bazaars = 0;
            var crowns = 0;

            foreach (FloorNode node in rail.Floors)
            {
                if (node.Mark == FloorMark.Bazaar) bazaars++;
                if (node.Mark == FloorMark.Crown) crowns++;
            }

            Assert.That(bazaars, Is.EqualTo(1));
            Assert.That(crowns, Is.EqualTo(1));

            Assert.That(rail.Floors[DelveRun.BazaarFloor - 1].Mark, Is.EqualTo(FloorMark.Bazaar));
            Assert.That(rail.Floors[DelveRun.MaxFloor - 1].Mark, Is.EqualTo(FloorMark.Crown));
        }

        /// <summary>
        /// Exactly one floor is underfoot, and everything shallower is behind.
        /// </summary>
        /// <remarks>
        /// Swept over every floor, because "where am I" is the one question this widget exists to
        /// answer and it is answered by three flags that could each be off by one.
        /// </remarks>
        [Test]
        public void OneFloorIsUnderfootAndTheRestAreNot()
        {
            for (var floor = 1; floor <= DelveRun.MaxFloor; floor++)
            {
                FloorRailCard rail = FloorRails.Of(floor, null);

                var here = 0;

                foreach (FloorNode node in rail.Floors)
                {
                    if (node.Here) here++;

                    Assert.That(node.Walked, Is.EqualTo(node.Floor < floor),
                        "floor " + node.Floor + " of a run standing on " + floor);

                    Assert.That(node.Here && node.Walked, Is.False,
                        "a floor was both underfoot and behind");
                }

                Assert.That(here, Is.EqualTo(1), "standing on " + floor);
                Assert.That(rail.Floors[floor - 1].Here, Is.True);
            }
        }

        /// <summary>
        /// A delver can see one floor ahead and no further.
        /// </summary>
        /// <remarks>
        /// The whole reason the rail can be pressed. Looking one step down is how a delver
        /// decides whether to walk out at this gate; looking further would hand them the rest of
        /// the run and there would be nothing to decide.
        /// </remarks>
        [Test]
        public void TheDarkIsOneFloorAhead()
        {
            FloorRailCard rail = FloorRails.Of(5, null);

            foreach (FloorNode node in rail.Floors)
            {
                Assert.That(node.Known, Is.EqualTo(node.Floor <= 6),
                    "floor " + node.Floor + " was " + (node.Known ? "shown" : "hidden") +
                    " to a delver on the fifth");
            }
        }

        /// <summary>
        /// Size says what a floor is and where the delver is standing, together.
        /// </summary>
        /// <remarks>
        /// Two facts in one number, which is why it is worked out beside the flags rather than
        /// by the screen: a bazaar drawn at a plain floor's size, or the floor underfoot drawn at
        /// everything else's, loses the only thing this widget communicates without words.
        /// </remarks>
        [Test]
        public void ABazaarIsBiggerThanAFloorAndTheFloorUnderfootIsBiggerStill()
        {
            Assert.That(FloorRails.Side(FloorMark.Crown, true),
                Is.GreaterThan(FloorRails.Side(FloorMark.Crown, false)));

            Assert.That(FloorRails.Side(FloorMark.Bazaar, true),
                Is.GreaterThan(FloorRails.Side(FloorMark.Bazaar, false)));

            Assert.That(FloorRails.Side(FloorMark.Plain, true),
                Is.GreaterThan(FloorRails.Side(FloorMark.Plain, false)));

            Assert.That(FloorRails.Side(FloorMark.Crown, false),
                Is.GreaterThan(FloorRails.Side(FloorMark.Bazaar, false)));

            Assert.That(FloorRails.Side(FloorMark.Bazaar, false),
                Is.GreaterThan(FloorRails.Side(FloorMark.Plain, false)));

            // And what the rail hands out agrees with what the table says.
            FloorRailCard rail = FloorRails.Of(DelveRun.BazaarFloor, null);

            foreach (FloorNode node in rail.Floors)
            {
                Assert.That(node.Side, Is.EqualTo(FloorRails.Side(node.Mark, node.Here)));
            }
        }

        /// <summary>
        /// An event sits in the gap BEFORE a floor, so the first floor never has one.
        /// </summary>
        /// <remarks>
        /// There is no gap in front of the first step of a run. The dot on the rail marks the
        /// thread between two nodes rather than a node, which is why it is drawn on the floor
        /// AFTER the gap the run placed it in — and why an off-by-one here would show a delver
        /// an event one floor from where they will meet it.
        /// </remarks>
        [Test]
        public void AnEventSitsInTheGapBeforeAFloor()
        {
            var placed = new Dictionary<int, int> { { 1, 0 }, { 4, 2 }, { 9, 7 } };

            FloorRailCard rail = FloorRails.Of(6, placed);

            Assert.That(rail.Floors[0].Event, Is.False, "the first floor was given a gap");
            Assert.That(rail.Floors[1].Event, Is.True, "the gap after floor one was lost");
            Assert.That(rail.Floors[4].Event, Is.True);
            Assert.That(rail.Floors[9].Event, Is.True);

            Assert.That(rail.Floors[2].Event, Is.False);

            // Behind the delver is met; ahead of them is not.
            Assert.That(rail.Floors[1].EventPassed, Is.True);
            Assert.That(rail.Floors[4].EventPassed, Is.True);
            Assert.That(rail.Floors[9].EventPassed, Is.False);
        }

        /// <summary>
        /// The draft says which held relics an offer would chain with.
        /// </summary>
        /// <remarks>
        /// The single most useful thing on the card. A chain runs both ways — this relic reacting
        /// to something held, or something held reacting to this — and the source counts both, so
        /// a Blood Altar reads as chaining with a Vampire Tooth from either side of the table.
        /// </remarks>
        [Test]
        public void TheDraftSaysWhatWouldChain()
        {
            RelicId reacts = Reacting();
            RelicId emits = EmittingTo(reacts);

            var held = new List<RelicId> { emits };

            Offered card = DraftCards.One(reacts, held);

            Assert.That(card.Chains.Count, Is.EqualTo(1),
                RelicCatalog.KeyOf(reacts) + " did not notice " + RelicCatalog.KeyOf(emits));

            Assert.That(card.Chains[0], Is.EqualTo(emits));

            // And the other way round, which is the same chain seen from the other side.
            Offered mirrored = DraftCards.One(emits, new List<RelicId> { reacts });

            Assert.That(mirrored.Chains.Count, Is.EqualTo(1));
            Assert.That(mirrored.Chains[0], Is.EqualTo(reacts));
        }

        /// <summary>
        /// A relic never chains with itself, however many copies are on the shelf.
        /// </summary>
        /// <remarks>
        /// A second Thorn Vest is a bigger Thorn Vest, not a combination. Listing it would make
        /// every duplicate look like a synergy, which is the opposite of what the row is for.
        /// </remarks>
        [Test]
        public void ARelicNeverChainsWithItself()
        {
            RelicId reacts = Reacting();

            Offered card = DraftCards.One(reacts, new List<RelicId> { reacts, reacts });

            Assert.That(card.Chains.Count, Is.Zero, "a relic chained with its own copies");
            Assert.That(card.Owned, Is.EqualTo(2), "the copies were not counted");
        }

        /// <summary>
        /// A reroll is offered only when it can be paid for.
        /// </summary>
        /// <remarks>
        /// The run itself never asks otherwise — a delve skips straight past the question with an
        /// empty purse — so a screen drawing the button anyway would be drawing one the run will
        /// not stop for, and pressing it would do nothing at all.
        /// </remarks>
        [Test]
        public void ARerollIsOfferedOnlyWhenItCanBePaidFor()
        {
            var offer = new List<RelicId> { RelicId.Whetstone, RelicId.IronSkin };

            Assert.That(DraftCards.Of(offer, null, 9, 10, 0).CanReroll, Is.False);
            Assert.That(DraftCards.Of(offer, null, 10, 10, 0).CanReroll, Is.True);
            Assert.That(DraftCards.Of(offer, null, 40, 10, 0).CanReroll, Is.True);

            DraftCard card = DraftCards.Of(offer, null, 40, 20, 1);

            Assert.That(card.Offer.Count, Is.EqualTo(2));
            Assert.That(card.Price, Is.EqualTo(20));
            Assert.That(card.Rerolls, Is.EqualTo(1));
        }

        /// <summary>The draft and the relic book describe a relic the same way.</summary>
        /// <remarks>
        /// Two screens a delver moves between in one press, both carrying the translated/English
        /// split by hand. The draft is the one a decision is made on, so a disagreement would be
        /// invisible and expensive.
        /// </remarks>
        [Test]
        public void TheDraftAndTheBookAgree()
        {
            foreach (BookEntry entry in RelicBookCards.Of().Relics)
            {
                Offered card = DraftCards.One(entry.Relic, null);

                Assert.That(card.Name, Is.EqualTo(entry.Name));
                Assert.That(card.NameKey, Is.EqualTo(entry.NameKey));
                Assert.That(card.What, Is.EqualTo(entry.What));
                Assert.That(card.WhatKey, Is.EqualTo(entry.WhatKey));
                Assert.That(card.Family, Is.EqualTo(entry.Kind));
                Assert.That(card.Wheel, Is.EqualTo(entry.Wheel));
            }
        }

        /// <summary>A relic that reacts to something.</summary>
        private static RelicId Reacting()
        {
            foreach (RelicDef relic in RelicCatalog.All)
            {
                if (relic.Reacts != null && relic.Reacts.Length > 0) return relic.Id;
            }

            Assert.Fail("no relic reacts to anything, so nothing can chain");
            return RelicId.Whetstone;
        }

        /// <summary>And one that emits what it is listening for.</summary>
        private static RelicId EmittingTo(RelicId reacting)
        {
            RelicDef listener = RelicCatalog.Get(reacting);

            foreach (RelicDef relic in RelicCatalog.All)
            {
                if (relic.Id == reacting || relic.Emits == null) continue;

                foreach (Channel mine in listener.Reacts)
                {
                    foreach (Channel theirs in relic.Emits)
                    {
                        if (mine == theirs) return relic.Id;
                    }
                }
            }

            Assert.Fail("nothing emits what " + RelicCatalog.KeyOf(reacting) + " listens for");
            return RelicId.Whetstone;
        }
    }
}
