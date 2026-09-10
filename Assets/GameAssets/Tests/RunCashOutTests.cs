using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Run;

namespace RelicRun.Tests
{
    /// <summary>
    /// Walking out of a delve: the one branch of a run nothing was driving.
    /// </summary>
    /// <remarks>
    /// The corpus holds 516 runs and 13,472 steps, and not one of them cashes out — every chooser
    /// that made them, and every other chooser in this suite, answers <c>false</c>. So the branch
    /// that ENDS runs and banks gold has been carried this whole time by nothing at all:
    /// <c>RunEnding.CashedOut</c> is tested where it is scored and settled, and nowhere where it
    /// is decided.
    ///
    /// That is fine until somebody restructures <see cref="DelveRun.Resolve"/>, which is exactly
    /// what the run layer needs. A gate that covers five branches out of six is a gate that says
    /// PASS while the sixth quietly stops working.
    /// </remarks>
    [TestFixture]
    public class RunCashOutTests
    {
        /// <summary>
        /// Walking out ends the run where it stood.
        /// </summary>
        /// <remarks>
        /// Where it STOOD, not where it was going: the floor does not advance, because a delver
        /// who leaves at the fifth gate walked five floors. A run that counted the floor it
        /// declined to enter would pay out one floor too deep, every time.
        /// </remarks>
        [Test]
        public void WalkingOutEndsTheRunWhereItStood()
        {
            // Every one of these is a floor that is actually FOUGHT. The seventh is not: see
            // TheQuestionComesAtEveryGateAndNowhereElse.
            foreach (int leaving in new[] { 1, 2, 5, 9, 12 })
            {
                var walker = new Walker { LeavesAfter = leaving };

                RunState run = DelveRun.Resolve(4242u, Strong(), walker, walker);

                Assert.That(walker.Ending, Is.EqualTo(RunEnding.CashedOut),
                    "leaving after floor " + leaving);

                Assert.That(walker.EndedOn, Is.EqualTo(leaving));
                Assert.That(run.Floor, Is.EqualTo(leaving));

                // One fight per floor walked, less the bazaar floor if it was passed.
                int fights = leaving > DelveRun.BazaarFloor ? leaving - 1 : leaving;

                Assert.That(walker.Fought.Count, Is.EqualTo(fights),
                    "a floor was fought after the delver had left");
            }
        }

        /// <summary>
        /// The question is asked after every fight, and only after a fight.
        /// </summary>
        /// <remarks>
        /// Which is not the same as "at every gate", and the difference is the seventh floor.
        /// THE BAZAAR FLOOR IS NEVER FOUGHT: the gate loop walks through it and out the other
        /// side without a pack, so floor seven has no fight, no cash-out question, and a delver
        /// who says they will leave "after seven" leaves after eight.
        ///
        /// That is not written down anywhere else in the port, and it is the sort of ordering a
        /// restructure gets wrong in a way that still looks like a game.
        ///
        /// Three places the question must not appear, each its own kind of wrong: before the
        /// first fight, which is offering to leave with nothing; after a death, which is offering
        /// a corpse a choice; and at the bottom, where the run has already CLEARED and leaving
        /// would take a lesser ending than the one earned.
        /// </remarks>
        [Test]
        public void TheQuestionComesAtEveryGateAndNowhereElse()
        {
            var cleared = new Walker { LeavesAfter = 0 };

            DelveRun.Resolve(1717u, Strong(), cleared, cleared);

            Assert.That(cleared.Ending, Is.EqualTo(RunEnding.Cleared), "the fixture did not clear");

            var expected = new List<int>();

            for (var floor = 1; floor < DelveRun.MaxFloor; floor++)
            {
                if (floor == DelveRun.BazaarFloor) continue;

                expected.Add(floor);
            }

            Assert.That(cleared.Asked, Is.EqualTo(expected),
                "a cleared run was asked about leaving on the wrong floors");

            Assert.That(cleared.Fought.Contains(DelveRun.BazaarFloor), Is.False,
                "something was fought on the bazaar floor");

            Assert.That(cleared.Asked.Contains(DelveRun.MaxFloor), Is.False,
                "the delver was offered a way out of a run they had already cleared");

            var died = new Walker { LeavesAfter = 0 };

            DelveRun.Resolve(99u, Frail(), died, died);

            Assert.That(died.Ending, Is.EqualTo(RunEnding.Died), "the fixture did not die");

            Assert.That(died.Asked.Count, Is.EqualTo(died.Fought.Count - 1),
                "a dead delver was asked whether to walk out");
        }

        /// <summary>
        /// Leaving skips the gate it is standing at.
        /// </summary>
        /// <remarks>
        /// The gate is where the breather is taken and the event in the gap is walked through,
        /// and a delver who has left takes neither. Checked against the same run that stayed:
        /// one leaves at the fifth gate and one walks through it, and the difference is exactly
        /// the breather and whatever was waiting in the gap.
        /// </remarks>
        [Test]
        public void LeavingSkipsTheGateItStandsAt()
        {
            var left = new Walker { LeavesAfter = 5 };
            var stayed = new Walker { LeavesAfter = 6 };

            RunState walking = DelveRun.Resolve(808u, Strong(), left, left);
            RunState onward = DelveRun.Resolve(808u, Strong(), stayed, stayed);

            Assert.That(left.Ending, Is.EqualTo(RunEnding.CashedOut));
            Assert.That(stayed.Ending, Is.EqualTo(RunEnding.CashedOut));

            // The one that stayed went through the gate, so it healed and moved on. The one that
            // left is still standing on floor five with whatever the fight left it.
            Assert.That(walking.Floor, Is.EqualTo(5));
            Assert.That(onward.Floor, Is.EqualTo(6));

            Assert.That(left.Events.Count, Is.LessThanOrEqualTo(stayed.Events.Count),
                "a delver who left walked through an event on the way out");

            Assert.That(left.Deals, Is.Zero, "a delver who left at five visited the bazaar at seven");
        }

        /// <summary>
        /// The bazaar is behind the seventh gate, and leaving at the seventh gate misses it.
        /// </summary>
        /// <remarks>
        /// The floor the bazaar sits on is the one place cashing out costs something other than
        /// depth, and it is the sort of ordering a restructure gets wrong without any test
        /// noticing — the deal happens INSIDE the gate loop, after the question.
        /// </remarks>
        [Test]
        public void TheBazaarIsBehindTheGate()
        {
            // Six is the last floor fought before the bazaar, and eight is the first after it.
            // Seven itself cannot be left at, because it is never fought.
            var left = new Walker { LeavesAfter = DelveRun.BazaarFloor - 1 };
            var stayed = new Walker { LeavesAfter = DelveRun.BazaarFloor + 1 };

            DelveRun.Resolve(31337u, Strong(), left, left);
            DelveRun.Resolve(31337u, Strong(), stayed, stayed);

            Assert.That(left.Deals, Is.Zero, "the bazaar opened for somebody who had walked out");
            Assert.That(stayed.Deals, Is.GreaterThan(0), "the fixture never reached the bazaar");
        }

        /// <summary>
        /// A Piggy Bank pays out on the way out, and only on the way out.
        /// </summary>
        /// <remarks>
        /// The one rule in the game that exists solely for this branch: a quarter more gold for
        /// leaving, half again if it has been awakened. Dying pays nothing at all and clearing
        /// pays face value, so the relic is a bet on knowing when to stop — and until now nothing
        /// carried a Piggy Bank out of a real run to check it.
        /// </remarks>
        [Test]
        public void ThePiggyBankPaysOnTheWayOut()
        {
            RunSetup setup = Strong();

            setup.StartKit = new List<RelicId> { RelicId.PiggyBank };

            var walker = new Walker { LeavesAfter = 8 };

            RunState run = DelveRun.Resolve(5150u, setup, walker, walker);

            Assert.That(walker.Ending, Is.EqualTo(RunEnding.CashedOut));
            Assert.That(run.Has(RelicId.PiggyBank), Is.True, "the delver lost the bank on the way");
            Assert.That(run.Gold, Is.GreaterThan(0), "the fixture banked nothing");

            int carried = RunOutcome.Bank(RunEnding.CashedOut, run.Gold, run);

            Assert.That(carried, Is.EqualTo((int)System.Math.Floor(run.Gold * 1.25)));
            Assert.That(carried, Is.GreaterThan(run.Gold), "the bank paid nothing for leaving");

            // The same purse, the other two ways a run can end.
            Assert.That(RunOutcome.Bank(RunEnding.Cleared, run.Gold, run), Is.EqualTo(run.Gold));
            Assert.That(RunOutcome.Bank(RunEnding.Died, run.Gold, run), Is.Zero);
        }

        /// <summary>A delver who will survive anything, so a run ends when it is told to.</summary>
        private static RunSetup Strong()
        {
            return new RunSetup
            {
                Hp = 5000,
                Atk = 400,
                Def = 40,
                Spd = 60,
                Lck = 10,
                Breath = 5,
                DraftChoices = 2,
                BazaarDeals = 1,
                Dungeon = DungeonConfig.ForTier(1),
                StartKit = new List<RelicId>(),
            };
        }

        /// <summary>And one who will not, so the dead can be asked what they are not asked.</summary>
        private static RunSetup Frail()
        {
            return new RunSetup
            {
                Hp = 30,
                Atk = 1,
                Def = 0,
                Spd = 10,
                Lck = 0,
                Breath = 0,
                DraftChoices = 2,
                BazaarDeals = 1,
                Dungeon = DungeonConfig.ForTier(10),
                StartKit = new List<RelicId>(),
            };
        }

        /// <summary>
        /// A delver who walks out at a chosen gate, and writes down what they were asked.
        /// </summary>
        /// <remarks>
        /// <c>LeavesAfter</c> of zero never leaves, which is how the cleared and died fixtures
        /// are made: the question is still asked and still answered, and what is recorded is
        /// WHEN it was asked.
        /// </remarks>
        private sealed class Walker : IRunChoices, IRunObserver
        {
            public int LeavesAfter;

            public readonly List<int> Asked = new List<int>();
            public readonly List<int> Fought = new List<int>();
            public readonly List<int> Events = new List<int>();

            public int Deals;
            public RunEnding Ending;
            public int EndedOn;

            public bool CashOut(RunState run, int floor)
            {
                Asked.Add(floor);

                return LeavesAfter > 0 && floor >= LeavesAfter;
            }

            public int Event(RunState run, DungeonEvent ev) { return 0; }

            public bool Reroll(RunState run, IReadOnlyList<RelicId> offer, int price) { return false; }

            public RelicId Draft(RunState run, IReadOnlyList<RelicId> offer) { return offer[0]; }

            public BazaarDeal Bazaar(RunState run, IReadOnlyList<RelicId> offer,
                IReadOnlyList<int> awakenable)
            {
                return BazaarDeal.Walk;
            }

            public bool Revive(RunState run, int floor) { return false; }

            void IRunObserver.Event(RunState run, int floor, int index, int choice)
            {
                Events.Add(floor);
            }

            void IRunObserver.Deal(RunState run, int floor, IReadOnlyList<RelicId> offer,
                IReadOnlyList<int> awakenable, BazaarDeal deal)
            {
                Deals++;
            }

            void IRunObserver.Draft(RunState run, int floor, IReadOnlyList<RelicId> offer,
                int rerolls, RelicId pick)
            {
            }

            void IRunObserver.Fight(RunState run, int floor, IReadOnlyList<EnemyState> pack,
                CombatResult result)
            {
                Fought.Add(floor);
            }

            void IRunObserver.Revived(RunState run, int floor) { }

            void IRunObserver.End(RunState run, RunEnding ending, int floor)
            {
                Ending = ending;
                EndedOn = floor;
            }
        }
    }
}
