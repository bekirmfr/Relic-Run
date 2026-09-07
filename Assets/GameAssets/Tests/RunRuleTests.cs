using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Run;

namespace RelicRun.Tests
{
    /// <summary>
    /// Rules the recorded runs cannot pin, asked directly instead.
    /// </summary>
    /// <remarks>
    /// A corpus can only catch what its own runs happen to do. Three rules sat outside that:
    /// the breather is invisible on the first floor because a run starts whole, an event grant's
    /// pool is invisible while no relic is versus-only, and the Debt of Flesh's awakened rate
    /// was unreachable in the source. Each is asked here rather than waited for.
    /// </remarks>
    [TestFixture]
    public class RunRuleTests
    {
        private static RunState Holding(params RelicId[] relics)
        {
            var run = new RunState();
            run.Items.AddRange(relics);
            return run;
        }

        // ---- the breather ----

        /// <summary>
        /// A breather is what happens BETWEEN floors, so the first floor gets none. The corpus
        /// cannot see this: a run starts at full health, so healing there would change nothing.
        /// </summary>
        [Test]
        public void ThereIsNoBreatherBeforeTheFirstFloor()
        {
            var setup = new RunSetup { Breath = 5 };
            RunState run = Holding();
            run.Pmax = 100;
            run.Php = 40;

            Assert.That(DelveRun.Breather(setup, run, 1), Is.Zero, "floor 1 has no floor before it");

            for (int floor = 2; floor <= DelveRun.MaxFloor; floor++)
            {
                Assert.That(DelveRun.Breather(setup, run, floor), Is.EqualTo(5), "floor " + floor);
            }
        }

        [Test]
        public void ASecondStomachDoublesTheBreather()
        {
            var setup = new RunSetup { Breath = 5 };

            Assert.That(DelveRun.Breather(setup, Holding(), 4), Is.EqualTo(5));
            Assert.That(DelveRun.Breather(setup, Holding(RelicId.SecondStomach), 4), Is.EqualTo(10));

            // Still nothing on the first floor, however many stomachs are in hand.
            Assert.That(DelveRun.Breather(setup, Holding(RelicId.SecondStomach), 1), Is.Zero);
        }

        // ---- the mode's pool ----

        /// <summary>
        /// An event that hands over a relic draws from the MODE's pool, not the whole table.
        /// </summary>
        /// <remarks>
        /// Today only a delve has relics of its own, so a delve grant cannot tell the two apart.
        /// A versus grant can, and does: it must never produce a Greedy Curse, a Second Stomach
        /// or a Merchant's Thumb. Written from both sides so that the day a versus-only relic
        /// is added, the delve half starts biting too.
        /// </remarks>
        [Test]
        public void AnEventGrantDrawsFromTheModesPoolOnly()
        {
            AssertGrantsStayInPool(versus: true);
            AssertGrantsStayInPool(versus: false);
        }

        private static void AssertGrantsStayInPool(bool versus)
        {
            GameModes mode = versus ? GameModes.Versus : GameModes.Delve;
            var seen = new HashSet<RelicId>();

            for (uint seed = 1; seed <= 200; seed++)
            {
                var run = new RunState();
                run.Hero.IsVersus = versus;
                seen.Add(DungeonEvents.GrantRelic(run, new Mulberry32(seed)));
            }

            Assert.That(seen.Count, Is.GreaterThan(1), "the grant never varied, so nothing was sampled");

            foreach (RelicId id in seen)
            {
                Assert.That((RelicCatalog.Get(id).Modes & mode) != 0, Is.True,
                    (versus ? "a duel" : "a delve") + " was handed " + RelicCatalog.KeyOf(id) +
                    ", which cannot appear in it");
            }
        }

        /// <summary>
        /// The pools genuinely differ, or the test above proves nothing. If this ever fails
        /// because the last mode-exclusive relic was removed, the grant test has gone blind.
        /// </summary>
        [Test]
        public void TheModePoolsAreNotTheSameList()
        {
            List<RelicId> delve = RelicCatalog.PoolFor(GameModes.Delve);
            List<RelicId> versus = RelicCatalog.PoolFor(GameModes.Versus);

            Assert.That(delve.Count, Is.Not.EqualTo(versus.Count),
                "no relic is exclusive to a mode any more, so nothing distinguishes the pools");
        }

        // ---- the Debt of Flesh ----

        /// <summary>
        /// A Debt of Flesh stacks here and does not in the source, which is what lets the
        /// bazaar awaken it and makes its awakened rate reachable at all.
        /// </summary>
        [Test]
        public void ADebtOfFleshStacksOnlyUnderTheShippedRules()
        {
            Assert.That(RunRules.Shipped().Stacks(RelicId.DebtOfFlesh), Is.True);
            Assert.That(RunRules.AsRecorded().Stacks(RelicId.DebtOfFlesh), Is.False,
                "the source's answer must stay the source's, or the corpus gate is meaningless");

            // Nothing else moves.
            foreach (RelicDef def in RelicCatalog.All)
            {
                if (def.Id == RelicId.DebtOfFlesh) continue;
                Assert.That(RunRules.Shipped().Stacks(def.Id), Is.EqualTo(def.Stackable), def.Key);
                Assert.That(RunRules.AsRecorded().Stacks(def.Id), Is.EqualTo(def.Stackable), def.Key);
            }
        }

        [Test]
        public void ADebtOfFleshPaysDoubleOnceAwakened()
        {
            RunState none = Holding();
            Assert.That(DelveRun.DebtPayment(none), Is.Zero);

            RunState plain = Holding(RelicId.DebtOfFlesh);
            Assert.That(DelveRun.DebtPayment(plain), Is.EqualTo(10));

            RunState awake = Holding(RelicId.DebtOfFlesh);
            awake.Awaken(RelicId.DebtOfFlesh);
            Assert.That(DelveRun.DebtPayment(awake), Is.EqualTo(20));
        }

        /// <summary>
        /// Asking for a deal the shelf could never have offered buys nothing.
        /// </summary>
        /// <remarks>
        /// The recorded runs only ever ask for legal deals, so nothing in the corpus notices if
        /// the bazaar stops checking. A caller — a UI with a stale button, a bot, a bad save —
        /// can ask for anything, and the price must not be taken for a deal that cannot happen.
        /// </remarks>
        [Test]
        public void TheBazaarRefusesADealTheShelfCouldNotOffer()
        {
            var refused = new Shopper(BazaarDeal.Awaken(RelicId.LuckyClover));
            RunState a = AtTheBazaar(refused, RelicId.LuckyClover);

            Assert.That(refused.Saw, Is.True, "the run never reached the bazaar");
            Assert.That(a.IsAwake(RelicId.LuckyClover), Is.False,
                "a Lucky Clover does not stack, so the shelf never carried it");
            Assert.That(refused.GoldAfter, Is.EqualTo(refused.GoldBefore),
                "nothing was bought, so nothing should have been paid");

            // The same shape, legally: a Debt of Flesh stacks here, so this one goes through.
            var taken = new Shopper(BazaarDeal.Awaken(RelicId.DebtOfFlesh));
            RunState b = AtTheBazaar(taken, RelicId.DebtOfFlesh);

            Assert.That(b.IsAwake(RelicId.DebtOfFlesh), Is.True);
            Assert.That(taken.GoldBefore - taken.GoldAfter, Is.EqualTo(DelveRun.AwakenPrice),
                "a legal awakening costs the full price — what a Debt of Flesh pays back is " +
                "health, not coin");
        }

        /// <summary>Walks a run to the bazaar and asks for one particular deal there.</summary>
        private sealed class Shopper : IRunChoices, IRunObserver
        {
            private readonly BazaarDeal _deal;

            public bool Saw;
            public int GoldBefore;
            public int GoldAfter;

            public Shopper(BazaarDeal deal) { _deal = deal; }

            public int Event(RunState run, DungeonEvent ev) { return ev.Choices.Count - 1; }

            public bool Reroll(RunState run, IReadOnlyList<RelicId> offer, int price) { return false; }

            public RelicId Draft(RunState run, IReadOnlyList<RelicId> offer) { return offer[0]; }

            public bool Revive(RunState run, int floor) { return false; }

            public BazaarDeal Bazaar(RunState run, IReadOnlyList<RelicId> offer)
            {
                Saw = true;
                GoldBefore = run.Gold;
                return _deal;
            }

            void IRunObserver.Bazaar(RunState run, int floor, IReadOnlyList<RelicId> offer, BazaarDeal deal)
            {
                GoldAfter = run.Gold;
            }

            public void Event(RunState run, int floor, int index, int choice) { }

            public void Draft(RunState run, int floor, IReadOnlyList<RelicId> offer, int rerolls, RelicId pick) { }

            public void Fight(RunState run, int floor, IReadOnlyList<EnemyState> pack, CombatResult result) { }

            public void Revived(RunState run, int floor) { }

            public void End(RunState run, bool dead, int floor) { }
        }

        /// <summary>A hero tough and rich enough to reach floor 7 with the price in hand.</summary>
        private static RunState AtTheBazaar(Shopper shopper, RelicId kit)
        {
            var setup = new RunSetup
            {
                Hp = 4000, Atk = 60, Gold = 500,
                StartKit = new List<RelicId> { kit },
            };

            return DelveRun.Resolve(99u, setup, shopper, shopper);
        }

        /// <summary>The bazaar cannot awaken what the shelf would never carry.</summary>
        [Test]
        public void OnlyAStackingRelicInHandCanBeAwakened()
        {
            RunState run = Holding(RelicId.DebtOfFlesh, RelicId.LuckyClover);

            Assert.That(run.CanAwaken(RelicId.DebtOfFlesh), Is.True, "it stacks under the shipped rules");
            Assert.That(run.CanAwaken(RelicId.LuckyClover), Is.False, "a Clover does not stack");
            Assert.That(run.CanAwaken(RelicId.OxHeart), Is.False, "not in hand at all");

            // One copy, one awakening.
            run.Awaken(RelicId.DebtOfFlesh);
            Assert.That(run.CanAwaken(RelicId.DebtOfFlesh), Is.False, "the only copy is already awake");

            run.Items.Add(RelicId.DebtOfFlesh);
            Assert.That(run.CanAwaken(RelicId.DebtOfFlesh), Is.True, "a second copy is not");

            run.Rules = RunRules.AsRecorded();
            Assert.That(run.CanAwaken(RelicId.DebtOfFlesh), Is.False,
                "under the source's rules it never stacked, so the bazaar never offered it");
        }
    }
}
