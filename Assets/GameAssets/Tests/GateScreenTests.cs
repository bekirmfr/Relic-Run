using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;
using RelicRun.Core.Stats;

namespace RelicRun.Tests
{
    /// <summary>
    /// The gate: the only decision in this game that is about the whole run.
    /// </summary>
    /// <remarks>
    /// Every other choice a delver makes is between two things they find out about in a minute.
    /// This one asks them to bet everything the run has earned on a fight they have been shown
    /// one card of — so what is on that card, and what is not, is the whole mechanism.
    /// </remarks>
    [TestFixture]
    public class GateScreenTests
    {
        /// <summary>
        /// The purse is what is at risk and what would be kept, and they are one number.
        /// </summary>
        /// <remarks>
        /// The screen says it three times on purpose. The one thing that must never happen is the
        /// three disagreeing — a delver told they risk 40 and keep 38 would reasonably conclude
        /// walking away costs something, and walking away is the option this game is about.
        /// </remarks>
        [Test]
        public void WhatIsRiskedIsWhatWouldBeKept()
        {
            GateCard gate = GateCards.Of(4, 137, null);

            Assert.That(gate.Gold, Is.EqualTo(137));
            Assert.That(gate.Floor, Is.EqualTo(4));
            Assert.That(gate.Next, Is.EqualTo(5));
        }

        /// <summary>
        /// The last gate knows the crown is below it.
        /// </summary>
        /// <remarks>
        /// Thirteen floors, and the twelfth gate is the one where the bet stops being ordinary.
        /// A delver who has carried a purse that far is entitled to be told what they are about
        /// to walk into.
        /// </remarks>
        [Test]
        public void TheLastGateKnowsWhatIsUnderIt()
        {
            for (var floor = 1; floor < DelveRun.MaxFloor; floor++)
            {
                GateCard gate = GateCards.Of(floor, 0, null);

                Assert.That(gate.Crowned, Is.EqualTo(floor + 1 >= DelveRun.MaxFloor),
                    "the gate after floor " + floor);
            }

            Assert.That(GateCards.Of(DelveRun.MaxFloor - 1, 0, null).Crowned, Is.True);
        }

        /// <summary>
        /// The peek shows the foe at the BACK, which is the one worth knowing about.
        /// </summary>
        /// <remarks>
        /// The opposite of what it sounds like it should be. A pack is fought front to back with
        /// the worst of it at the end, so the first foe says only that a delver survives the next
        /// thirty seconds — which they could already guess. What the purse is being bet against
        /// is whatever is standing at the back, and that is what the source shows.
        /// </remarks>
        [Test]
        public void ThePeekShowsTheWorstOfWhatIsDownThere()
        {
            var pack = new List<EnemyState>
            {
                new EnemyState { SpeciesIndex = 2, Variant = 0, MaxHp = 20, Atk = 4, Drop = 5 },
                new EnemyState { SpeciesIndex = 9, Variant = 2, MaxHp = 90, Atk = 18, Drop = 30,
                                 Rank = EnemyRank.Boss },
            };

            WaitingBelow peek = GateCards.Peek(pack);

            Assert.That(peek.Known, Is.True);
            Assert.That(peek.Species, Is.EqualTo(9), "the guard at the front was shown instead");
            Assert.That(peek.Rank, Is.EqualTo(EnemyRank.Boss));
            Assert.That(peek.Behind, Is.EqualTo(1), "one guard stands in front of it");
            Assert.That(peek.Gold, Is.EqualTo(35), "the whole pack's drop is what a floor pays");

            Assert.That(peek.Line, Does.Contain("90 HP"));
            Assert.That(peek.Line, Does.StartWith("BEHIND 1 GUARD"));
        }

        /// <summary>
        /// A foe standing alone is not described as having nobody behind it.
        /// </summary>
        /// <remarks>
        /// "BEHIND 0 GUARDS" is a sentence that makes an empty hall sound crowded. Most floors
        /// of a run are exactly one foe, so this is the common case rather than the edge.
        /// </remarks>
        [Test]
        public void AFoeAloneIsNotSaidToHaveNobodyBehindIt()
        {
            var alone = new List<EnemyState>
            {
                new EnemyState { SpeciesIndex = 1, MaxHp = 30, Atk = 6, Drop = 7 },
            };

            WaitingBelow peek = GateCards.Peek(alone);

            Assert.That(peek.Behind, Is.Zero);
            Assert.That(peek.Line, Does.Not.Contain("BEHIND"));
            Assert.That(peek.Line, Is.EqualTo("30 HP" + GateCards.Between + "ATK 6" +
                                              GateCards.Between + "~7 GOLD"));

            // One in front is a guard; two are guards. The one peeked at stays the last, so the
            // new foes go in FRONT of it.
            var two = new List<EnemyState> { new EnemyState { Drop = 3 } };
            two.AddRange(alone);

            var three = new List<EnemyState> { new EnemyState { Drop = 3 } };
            three.AddRange(two);

            Assert.That(GateCards.Peek(two).Line, Does.StartWith("BEHIND 1 GUARD" + GateCards.Between));
            Assert.That(GateCards.Peek(three).Line, Does.StartWith("BEHIND 2 GUARDS"));
        }

        /// <summary>An empty floor below is a gate with nothing to peek at.</summary>
        /// <remarks>
        /// Never happens in a delve — every floor rolls a pack — but the gate is also what the
        /// versus arena will ask, and there the run below is a person rather than a pack. A card
        /// that assumed a foe would draw a species-zero guard with no health.
        /// </remarks>
        [Test]
        public void AGateWithNothingBelowShowsNothing()
        {
            Assert.That(GateCards.Peek(null).Known, Is.False);
            Assert.That(GateCards.Peek(new List<EnemyState>()).Known, Is.False);
            Assert.That(GateCards.Peek(new List<EnemyState> { null }).Known, Is.False);

            Assert.That(GateCards.Of(3, 40, null).Below.Known, Is.False);
        }

        /// <summary>
        /// The gate is asked with the pack the next floor is actually fought with — except one.
        /// </summary>
        /// <remarks>
        /// The peek is a promise, and it is kept at every gate but the one before the bazaar.
        /// There the run rolls a pack for a floor that is never fought, walks the delver through
        /// a shop instead, and rolls again on the way out — so the gate above the bazaar shows
        /// foes nobody ever meets.
        ///
        /// That is the SOURCE's behaviour, not a slip in the port: the bazaar's own pack is built
        /// at the gate like any floor's, and <c>BazaarRollsItsOwnPack</c> is the recorded rule
        /// that replaces it afterwards. It is pinned here rather than quietly smoothed over,
        /// because the fix — peeking past the bazaar — would change which numbers the RNG draws
        /// and every one of the recorded runs with it.
        /// </remarks>
        [Test]
        public void TheGatePeeksAtThePackTheNextFloorFights()
        {
            var delve = new Delve(20260910u, new RunSetup());

            IReadOnlyList<EnemyState> promised = null;
            var promisedAt = 0;
            var gates = 0;
            var broken = 0;

            while (!delve.Finished)
            {
                Ask stop = delve.Pending;

                if (stop.Kind == AskKind.CashOut)
                {
                    gates++;

                    Assert.That(stop.Pack, Is.Not.Null, "a gate showed nothing below it");
                    Assert.That(stop.Pack.Count, Is.GreaterThan(0));

                    promised = stop.Pack;
                    promisedAt = stop.Floor;
                }
                else if (stop.Kind == AskKind.Fought && promised != null)
                {
                    bool overTheBazaar = promisedAt + 1 == DelveRun.BazaarFloor;

                    if (overTheBazaar) broken++;
                    else
                    {
                        Assert.That(stop.Pack, Is.SameAs(promised),
                            "the gate after floor " + promisedAt + " showed a pack that was " +
                            "never fought");
                    }

                    promised = null;
                }

                delve.Answer(Plainly(stop));
            }

            Assert.That(gates, Is.GreaterThan(1), "the run never reached a gate");

            // And the exception is exactly one gate, not a habit.
            Assert.That(broken, Is.LessThanOrEqualTo(1),
                "more than one gate promised a pack that was never fought");
        }

        /// <summary>Walking out at a gate ends the run and keeps the purse.</summary>
        /// <remarks>
        /// The whole point of the screen. Gated here as well as in <c>RunCashOutTests</c> because
        /// this is the answer a BUTTON produces — <c>Yes</c> on a cash-out stop — and the button
        /// and the engine agreeing about which field means "leave" is the kind of thing that is
        /// obviously right until somebody adds a second boolean.
        /// </remarks>
        [Test]
        public void SayingYesAtAGateWalksOutWithTheGold()
        {
            var delve = new Delve(20260910u, new RunSetup());

            var purse = 0;

            while (!delve.Finished)
            {
                Ask stop = delve.Pending;

                if (stop.Kind == AskKind.CashOut)
                {
                    purse = delve.State.Gold;

                    delve.Answer(new Answer { Yes = true });
                    break;
                }

                delve.Answer(Plainly(stop));
            }

            Assert.That(delve.Finished, Is.True);
            Assert.That(delve.Ending, Is.EqualTo(RunEnding.CashedOut));
            Assert.That(delve.State.Gold, Is.EqualTo(purse), "the purse changed on the way out");
        }

        /// <summary>What a delver with no screen would answer: take the first, keep going.</summary>
        private static Answer Plainly(Ask stop)
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
