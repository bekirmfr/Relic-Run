using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;

namespace RelicRun.Tests
{
    /// <summary>
    /// The bazaar: the only floor with no fight, and the only place gold becomes anything.
    /// </summary>
    /// <remarks>
    /// Everything a delver has hoarded since floor one is spent here or carried home unspent. So
    /// the two things this screen must never get wrong are what a deal costs and whether it can
    /// be afforded — a shelf that offers what the purse cannot reach is a screen that does
    /// nothing when pressed.
    /// </remarks>
    [TestFixture]
    public class BazaarScreenTests
    {
        /// <summary>
        /// A Merchant's Thumb shaves a fifth off both prices, and the shelf says so.
        /// </summary>
        /// <remarks>
        /// The screen shows a number a delver decides on, so it cannot be the list price while
        /// the engine charges something else. Both come from <c>PriceOf</c> for exactly that
        /// reason.
        /// </remarks>
        [Test]
        public void TheThumbShavesWhatTheShelfCharges()
        {
            var plain = new RunState();
            plain.Gold = 500;

            BazaarCard full = BazaarCards.Of(7, Shelf(), null, plain);

            Assert.That(full.BuyPrice, Is.EqualTo(DelveRun.BuyPrice));
            Assert.That(full.WakePrice, Is.EqualTo(DelveRun.AwakenPrice));

            var thumbed = new RunState();
            thumbed.Gold = 500;
            Pickup.Take(thumbed, RelicId.MerchantsThumb);

            BazaarCard cut = BazaarCards.Of(7, Shelf(), null, thumbed);

            Assert.That(cut.BuyPrice, Is.LessThan(full.BuyPrice), "the thumb charged full price");
            Assert.That(cut.BuyPrice, Is.EqualTo(DelveRun.PriceOf(thumbed, DelveRun.BuyPrice)));
            Assert.That(cut.WakePrice, Is.EqualTo(DelveRun.PriceOf(thumbed, DelveRun.AwakenPrice)));
        }

        /// <summary>
        /// A delver who cannot afford anything is told so, rather than shown a live shelf.
        /// </summary>
        /// <remarks>
        /// A real way to arrive: the bazaar is floor seven and nothing makes a delver save for
        /// it. Five relics in full colour that do nothing when pressed is worse than an empty
        /// shelf, because the delver spends the visit wondering what they did wrong.
        /// </remarks>
        [Test]
        public void AnEmptyPurseBuysNothingAndTheShelfKnows()
        {
            var broke = new RunState();
            broke.Gold = DelveRun.BuyPrice - 1;

            BazaarCard card = BazaarCards.Of(7, Shelf(), null, broke);

            Assert.That(card.Wares.Count, Is.EqualTo(3), "the shelf was not drawn at all");
            Assert.That(card.Anything, Is.False);

            foreach (Ware ware in card.Wares) Assert.That(ware.Afford, Is.False);

            broke.Gold = DelveRun.BuyPrice;

            BazaarCard afford = BazaarCards.Of(7, Shelf(), null, broke);

            Assert.That(afford.Anything, Is.True);

            foreach (Ware ware in afford.Wares) Assert.That(ware.Afford, Is.True);
        }

        /// <summary>
        /// Waking is priced apart from buying, and a purse can reach one without the other.
        /// </summary>
        /// <remarks>
        /// Sixty against forty. A delver with fifty gold can buy and cannot wake, and a shelf
        /// that ran one affordability check for both would tell them the opposite of the truth
        /// on one of the two rows.
        /// </remarks>
        [Test]
        public void BuyingAndWakingArePricedApart()
        {
            var run = new RunState();

            Pickup.Take(run, RelicId.Whetstone);
            Pickup.Take(run, RelicId.Whetstone);

            run.Gold = DelveRun.BuyPrice;

            List<int> shelf = run.Awakenable();

            Assert.That(shelf.Count, Is.GreaterThan(0), "two copies could not be woken");

            BazaarCard card = BazaarCards.Of(7, Shelf(), shelf, run);

            Assert.That(card.Wares[0].Afford, Is.True, "forty gold could not buy a forty relic");
            Assert.That(card.Wakings[0].Afford, Is.False, "forty gold woke a sixty relic");
            Assert.That(card.Anything, Is.True, "something was affordable");

            run.Gold = DelveRun.AwakenPrice;

            BazaarCard richer = BazaarCards.Of(7, Shelf(), shelf, run);

            Assert.That(richer.Wakings[0].Afford, Is.True);
        }

        /// <summary>
        /// The waking shelf names the COPY, not the relic.
        /// </summary>
        /// <remarks>
        /// A delver carrying three Thorn Vests wakes one of them and the engine is told which by
        /// its slot. A row that carried the relic instead would wake whichever copy the engine
        /// found first — usually the right answer, and silently the wrong one the moment two
        /// copies of different relics sit next to each other.
        /// </remarks>
        [Test]
        public void TheWakingShelfNamesTheCopy()
        {
            var run = new RunState();

            Pickup.Take(run, RelicId.Whetstone);
            Pickup.Take(run, RelicId.Whetstone);
            Pickup.Take(run, RelicId.IronSkin);
            Pickup.Take(run, RelicId.IronSkin);

            run.Gold = 500;

            List<int> shelf = run.Awakenable();

            BazaarCard card = BazaarCards.Of(7, Shelf(), shelf, run);

            Assert.That(card.Wakings.Count, Is.EqualTo(shelf.Count));

            foreach (Waking waking in card.Wakings)
            {
                Assert.That(waking.Slot, Is.InRange(0, run.Items.Count - 1));

                Assert.That(waking.Relic, Is.EqualTo(run.Items[waking.Slot]),
                    "slot " + waking.Slot + " named the wrong relic");
            }
        }

        /// <summary>The shelf and the draft describe a relic the same way.</summary>
        /// <remarks>
        /// True by construction — a ware IS the draft's own <c>Offered</c> — and asserted anyway,
        /// because the cheapest way to break it later is to give the shelf its own copy of the
        /// same six fields.
        /// </remarks>
        [Test]
        public void TheShelfAndTheDraftAgree()
        {
            var run = new RunState();
            run.Gold = 500;

            Pickup.Take(run, RelicId.Whetstone);

            BazaarCard card = BazaarCards.Of(7, Shelf(), null, run);

            foreach (Ware ware in card.Wares)
            {
                Offered drafted = DraftCards.One(ware.Shown.Relic, run.Items);

                Assert.That(ware.Shown.Name, Is.EqualTo(drafted.Name));
                Assert.That(ware.Shown.NameKey, Is.EqualTo(drafted.NameKey));
                Assert.That(ware.Shown.What, Is.EqualTo(drafted.What));
                Assert.That(ware.Shown.Family, Is.EqualTo(drafted.Family));
                Assert.That(ware.Shown.Owned, Is.EqualTo(drafted.Owned));
                Assert.That(ware.Shown.Chains.Count, Is.EqualTo(drafted.Chains.Count));
            }
        }

        /// <summary>
        /// A run walked to the bazaar is asked with a shelf the screen can draw.
        /// </summary>
        /// <remarks>
        /// The shape of the real stop rather than one built by hand: five wares, a floor that is
        /// the bazaar's, and an awakening shelf that closes after one deal. The last is the part
        /// worth walking a whole run for — the engine empties it rather than re-offering, and a
        /// screen that cached the first shelf would let a delver wake twice.
        /// </remarks>
        [Test]
        public void TheRealBazaarHandsTheScreenAShelf()
        {
            var delve = new Delve(20260911u, new RunSetup());

            var visits = 0;

            while (!delve.Finished)
            {
                Ask stop = delve.Pending;

                if (stop.Kind == AskKind.Bazaar)
                {
                    visits++;

                    Assert.That(stop.Floor, Is.EqualTo(DelveRun.BazaarFloor));

                    BazaarCard card = BazaarCards.Of(stop.Floor, stop.Offer, stop.Awakenable,
                        delve.State);

                    Assert.That(card.Wares.Count, Is.EqualTo(DelveRun.BazaarChoices));
                    Assert.That(card.BuyPrice, Is.GreaterThan(0));

                    // Buy the first thing the purse can reach, or walk.
                    if (card.Anything && card.Wares[0].Afford)
                    {
                        delve.Answer(new Answer { Deal = BazaarDeal.Buy(card.Wares[0].Shown.Relic) });
                        continue;
                    }

                    delve.Answer(new Answer { Deal = BazaarDeal.Walk });
                    continue;
                }

                delve.Answer(Dully(stop));
            }

            Assert.That(visits, Is.GreaterThan(0), "the run never reached the bazaar");
        }

        /// <summary>Three relics, which is enough shelf for anything asked here.</summary>
        private static List<RelicId> Shelf()
        {
            return new List<RelicId> { RelicId.Whetstone, RelicId.IronSkin, RelicId.ThornVest };
        }

        /// <summary>What a delver with no screen would answer.</summary>
        private static Answer Dully(Ask stop)
        {
            switch (stop.Kind)
            {
                case AskKind.Draft:
                case AskKind.Reroll:
                    return new Answer { Pick = stop.Offer[0] };

                case AskKind.Bazaar:
                    return new Answer { Deal = BazaarDeal.Walk };

                default:
                    return new Answer();
            }
        }
    }
}
