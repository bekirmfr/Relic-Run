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
        /// <summary>Awakened slots in a stated order — a set has none of its own.</summary>
        private static List<int> Slots(RunState run)
        {
            var slots = new List<int>(run.AwakenedSlots);
            slots.Sort();
            return slots;
        }

        private static RunState Holding(params RelicId[] relics)
        {
            var run = new RunState();
            run.Items.AddRange(relics);
            return run;
        }

        // ---- the breather ----

        /// <summary>
        /// A breather is taken at a GATE, and the first floor is not walked out to. The corpus
        /// cannot see this: a run starts at full health, so healing there would change nothing.
        /// </summary>
        [Test]
        public void ThereIsNoBreatherBeforeTheFirstFloor()
        {
            var setup = new RunSetup { Breath = 5 };
            RunState run = Holding();
            run.Pmax = 100;
            run.Php = 40;

            Assert.That(run.BreathHealed, Is.Zero, "a fresh run has not been anywhere to rest");
            Assert.That(DelveRun.Breather(setup, run), Is.EqualTo(5), "and at a gate it would");
        }

        [Test]
        public void ASecondStomachDoublesTheBreather()
        {
            var setup = new RunSetup { Breath = 5 };

            Assert.That(DelveRun.Breather(setup, Holding()), Is.EqualTo(5));
            Assert.That(DelveRun.Breather(setup, Holding(RelicId.SecondStomach)), Is.EqualTo(10));
        }

        /// <summary>An awakened Ox Heart adds two on top, and the Stomach still doubles first.</summary>
        [Test]
        public void AnAwakenedOxHeartAddsToTheBreather()
        {
            var setup = new RunSetup { Breath = 5 };

            RunState heart = Holding(RelicId.OxHeart);
            Assert.That(DelveRun.Breather(setup, heart), Is.EqualTo(5), "asleep it does nothing");

            heart.Awaken(0);
            Assert.That(DelveRun.Breather(setup, heart), Is.EqualTo(7));

            RunState both = Holding(RelicId.SecondStomach, RelicId.OxHeart);
            both.Awaken(1);
            Assert.That(DelveRun.Breather(setup, both), Is.EqualTo(12),
                "the stomach doubles the base, and the heart is added after");
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
        /// A draft offer draws from the MODE's pool too, and for the same reason.
        /// </summary>
        /// <remarks>
        /// A delve cannot yet tell the difference — every delve-only relic is in the delve's
        /// pool — so this is written from the versus side, where the exclusions are real, and
        /// will start biting on the delve side the day a versus-only relic exists.
        /// </remarks>
        [Test]
        public void ADraftOfferDrawsFromTheModesPoolOnly()
        {
            AssertOffersStayInPool(GameModes.Delve);
            AssertOffersStayInPool(GameModes.Versus);
        }

        private static void AssertOffersStayInPool(GameModes mode)
        {
            var seen = new HashSet<RelicId>();
            RunRules rules = RunRules.Shipped();

            for (uint seed = 1; seed <= 200; seed++)
            {
                foreach (RelicId id in
                    RelicDraft.RollOffer(new List<RelicId>(), 3, new Mulberry32(seed), rules, mode))
                {
                    seen.Add(id);
                }
            }

            Assert.That(seen.Count, Is.GreaterThan(1), "the offer never varied, so nothing was sampled");

            foreach (RelicId id in seen)
            {
                Assert.That((RelicCatalog.Get(id).Modes & mode) != 0, Is.True,
                    mode + " was offered " + RelicCatalog.KeyOf(id) + ", which cannot appear in it");
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

        // ---- the floor that was removed ----

        /// <summary>
        /// The source floors an event's defence loss at -2 and this port does not, because
        /// nothing can reach it. This is what keeps that true.
        /// </summary>
        /// <remarks>
        /// Events are drawn without replacement, so each appears at most once in a run: the
        /// worst a run can lose is the sum of each event's own worst case. Only the unclaimed
        /// chest costs defence at all, and only one point, so that sum is -1 and a floor at -2
        /// could never fire.
        ///
        /// Add an event that costs defence and this fails, which is the point — the guard was
        /// removed because it was unreachable, not because runs are allowed to sink past it.
        /// </remarks>
        [Test]
        public void NoRunCanLoseEnoughDefenceToNeedAFloor()
        {
            int worstRun = 0;
            var costly = new List<string>();

            foreach (DungeonEvent ev in DungeonEvents.All)
            {
                int worstEvent = 0;

                foreach (EventChoice choice in ev.Choices)
                {
                    // Enough seeds to see both sides of any roll the outcome makes.
                    for (uint seed = 1; seed <= 200; seed++)
                    {
                        var run = new RunState();
                        run.Pmax = 100;
                        run.Php = 100;
                        run.Gold = 1000;

                        choice.Resolve(run, new Mulberry32(seed));
                        if (run.Hero.DefBonus < worstEvent) worstEvent = run.Hero.DefBonus;
                    }
                }

                if (worstEvent < 0) costly.Add(ev.Key + " " + worstEvent);
                worstRun += worstEvent;
            }

            Assert.That(costly.Count, Is.GreaterThan(0),
                "no event costs defence at all any more, so this guard is watching nothing");

            Assert.That(worstRun, Is.GreaterThanOrEqualTo(-1),
                "a run can now lose " + worstRun + " defence through events (" +
                string.Join(", ", costly) + "), which is past the -2 floor the source keeps and " +
                "this port dropped as unreachable. Restore the floor, or raise this bound " +
                "deliberately.");
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
            awake.Awaken(0);
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
            // Slot 0 is the kit relic, which the shelf will only offer if it stacks.
            var refused = new Shopper(BazaarDeal.Awaken(0));
            RunState a = AtTheBazaar(refused, RelicId.LuckyClover);

            Assert.That(refused.Saw, Is.True, "the run never reached the bazaar");
            Assert.That(refused.Awakenable, Does.Not.Contain(0),
                "a Lucky Clover does not stack, so the shelf never carried it");
            Assert.That(a.IsAwake(RelicId.LuckyClover), Is.False);
            Assert.That(refused.GoldAfter, Is.EqualTo(refused.GoldBefore),
                "nothing was bought, so nothing should have been paid");

            // A purchase the shelf never carried is refused the same way. A Lucky Clover does
            // not stack and one is already in hand, so the shelf cannot have been holding one.
            var offShelf = new Shopper(BazaarDeal.Buy(RelicId.LuckyClover));
            RunState c = AtTheBazaar(offShelf, RelicId.LuckyClover);

            Assert.That(offShelf.Saw, Is.True, "the run never reached the bazaar");
            Assert.That(c.Count(RelicId.LuckyClover), Is.EqualTo(1),
                "the shelf never offered it, so it was never for sale");
            Assert.That(offShelf.GoldAfter, Is.EqualTo(offShelf.GoldBefore));

            // The same shape, legally: a Debt of Flesh stacks here, so this one goes through.
            var taken = new Shopper(BazaarDeal.Awaken(0));
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
            public readonly List<int> Awakenable = new List<int>();

            public Shopper(BazaarDeal deal) { _deal = deal; }

            public int Event(RunState run, DungeonEvent ev) { return ev.Choices.Count - 1; }

            public bool Reroll(RunState run, IReadOnlyList<RelicId> offer, int price) { return false; }

            public RelicId Draft(RunState run, IReadOnlyList<RelicId> offer) { return offer[0]; }

            public bool Revive(RunState run, int floor) { return false; }

            public bool CashOut(RunState run, int floor) { return false; }

            public BazaarDeal Bazaar(RunState run, IReadOnlyList<RelicId> offer,
                IReadOnlyList<int> awakenable)
            {
                if (Saw) return BazaarDeal.Walk;

                Saw = true;
                GoldBefore = run.Gold;
                GoldAfter = run.Gold;
                Awakenable.AddRange(awakenable);
                return _deal;
            }

            void IRunObserver.Deal(RunState run, int floor, IReadOnlyList<RelicId> offer,
                IReadOnlyList<int> awakenable, BazaarDeal deal)
            {
                GoldAfter = run.Gold;
            }

            public void Event(RunState run, int floor, int index, int choice) { }

            public void Draft(RunState run, int floor, IReadOnlyList<RelicId> offer, int rerolls, RelicId pick) { }

            public void Fight(RunState run, int floor, IReadOnlyList<EnemyState> pack, CombatResult result) { }

            public void Revived(RunState run, int floor) { }

            public void End(RunState run, RunEnding ending, int floor) { }
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

        /// <summary>
        /// The bazaar's shelf holds COPIES, one per relic, and only ones that stack.
        /// </summary>
        [Test]
        public void OnlyAStackingCopyNotYetAwakeReachesTheShelf()
        {
            RunState run = Holding(RelicId.DebtOfFlesh, RelicId.LuckyClover, RelicId.DebtOfFlesh);

            Assert.That(run.Awakenable(), Is.EqualTo(new[] { 0 }),
                "a Debt of Flesh stacks under the shipped rules and a Clover does not, and a " +
                "second copy of the same relic is not a second line on the shelf");

            // One copy, one awakening — and then the OTHER copy takes its place.
            run.Awaken(0);
            Assert.That(run.Awakenable(), Is.EqualTo(new[] { 2 }));
            Assert.That(run.IsAwake(RelicId.DebtOfFlesh), Is.True);
            Assert.That(run.AwakenedCount(RelicId.DebtOfFlesh), Is.EqualTo(1));

            run.Awaken(2);
            Assert.That(run.Awakenable(), Is.Empty, "both copies are awake");
            Assert.That(run.AwakenedCount(RelicId.DebtOfFlesh), Is.EqualTo(2));

            RunState recorded = Holding(RelicId.DebtOfFlesh);
            recorded.Rules = RunRules.AsRecorded();
            Assert.That(recorded.Awakenable(), Is.Empty,
                "under the source's rules it never stacked, so the bazaar never offered it");
        }

        /// <summary>
        /// No event outcome can spend more gold than the choice asked for.
        /// </summary>
        /// <remarks>
        /// The source floors the purse at zero after every outcome. That clamp is not ported,
        /// because a choice that costs gold is refused unless the purse covers it, so nothing
        /// can go into the red. This is what keeps that true: an outcome that quietly spends
        /// more than its stated cost fails here rather than turning up as a negative purse.
        /// </remarks>
        [Test]
        public void NoEventCanSpendMoreGoldThanItAsksFor()
        {
            var overspent = new List<string>();

            foreach (DungeonEvent ev in DungeonEvents.All)
            {
                for (int i = 0; i < ev.Choices.Count; i++)
                {
                    for (uint seed = 1; seed <= 200; seed++)
                    {
                        RunState run = Holding();
                        run.Gold = ev.Choices[i].Cost;
                        ev.Choices[i].Resolve(run, new Mulberry32(seed));

                        if (run.Gold < 0)
                        {
                            overspent.Add(ev.Key + " choice " + i + " left " + run.Gold +
                                          " in the purse against a stated cost of " +
                                          ev.Choices[i].Cost);
                            break;
                        }
                    }
                }
            }

            Assert.That(overspent, Is.Empty, string.Join("\n  ", overspent));
        }

        /// <summary>
        /// An oath shattering out of the tray does not hand its neighbour's awakening away.
        /// </summary>
        /// <remarks>
        /// Slots are positional and the source never re-keys them, so this is the port's own
        /// answer — see <see cref="RunRules.AwakeningsFollowTheirCopy"/>. The recorded runs
        /// replay the source's, which is the other half of the same test.
        /// </remarks>
        [Test]
        public void AnAwakeningFollowsTheCopyItWasBoughtFor()
        {
            RunState shipped = Holding(RelicId.DuelistsOath, RelicId.OxHeart, RelicId.VampireTooth);
            shipped.Awaken(2);
            shipped.Drop(RelicId.DuelistsOath);

            Assert.That(shipped.Items, Is.EqualTo(new[] { RelicId.OxHeart, RelicId.VampireTooth }));
            Assert.That(Slots(shipped), Is.EqualTo(new[] { 1 }));
            Assert.That(shipped.IsAwake(RelicId.VampireTooth), Is.True,
                "the tooth is what was paid for");
            Assert.That(shipped.IsAwake(RelicId.OxHeart), Is.False);

            // And a copy that WAS awake, dropped: the awakening goes with it rather than sliding
            // onto whatever takes its place.
            RunState woken = Holding(RelicId.OxHeart, RelicId.VampireTooth);
            woken.Awaken(0);
            woken.Drop(RelicId.OxHeart);

            Assert.That(woken.AwakenedSlots, Is.Empty);
            Assert.That(woken.IsAwake(RelicId.VampireTooth), Is.False,
                "the tooth slid into slot 0; it did not inherit the heart's awakening");

            RunState recorded = Holding(RelicId.DuelistsOath, RelicId.OxHeart, RelicId.VampireTooth);
            recorded.Rules = RunRules.AsRecorded();
            recorded.Awaken(2);
            recorded.Drop(RelicId.DuelistsOath);

            Assert.That(Slots(recorded), Is.EqualTo(new[] { 2 }),
                "the source leaves the map alone, and slot 2 is now past the end of the tray");
            Assert.That(recorded.IsAwake(RelicId.VampireTooth), Is.False,
                "sixty gold, spent on nothing at all");
        }

        // ---- the rules the port keeps and the recording does not ----

        /// <summary>Answers every question the same way, so one rule can be isolated.</summary>
        private sealed class Greedy : IRunChoices, IRunObserver
        {
            public readonly List<string> Trace = new List<string>();

            public int Event(RunState run, DungeonEvent ev) { return 0; }

            public bool Reroll(RunState run, IReadOnlyList<RelicId> offer, int price) { return false; }

            public RelicId Draft(RunState run, IReadOnlyList<RelicId> offer) { return offer[0]; }

            public BazaarDeal Bazaar(RunState run, IReadOnlyList<RelicId> offer,
                IReadOnlyList<int> awakenable)
            {
                if (awakenable.Count > 0 && run.Gold >= DelveRun.PriceOf(run, DelveRun.AwakenPrice))
                {
                    return BazaarDeal.Awaken(awakenable[0]);
                }

                return run.Gold >= DelveRun.PriceOf(run, DelveRun.BuyPrice) && offer.Count > 0
                    ? BazaarDeal.Buy(offer[0])
                    : BazaarDeal.Walk;
            }

            public bool Revive(RunState run, int floor) { return false; }

            public bool CashOut(RunState run, int floor) { return false; }

            public void Event(RunState run, int floor, int index, int choice) { }

            public void Deal(RunState run, int floor, IReadOnlyList<RelicId> offer,
                IReadOnlyList<int> awakenable, BazaarDeal deal)
            { }

            public void Draft(RunState run, int floor, IReadOnlyList<RelicId> offer,
                int rerolls, RelicId pick)
            { }

            public void Fight(RunState run, int floor, IReadOnlyList<EnemyState> pack,
                CombatResult result)
            {
                Trace.Add(floor + ":" + run.Php + "/" + run.Pmax + ":" + run.Gold + ":" +
                          pack.Count + ":" + result.Events.Count);
            }

            public void Revived(RunState run, int floor) { }

            public void End(RunState run, RunEnding ending, int floor)
            {
                Trace.Add("end:" + ending + ":" + floor + ":" + run.Php + "/" + run.Pmax +
                          ":" + run.Gold + ":" + run.Items.Count);
            }
        }

        /// <summary>Every place the port keeps a different answer, and how to put the source's back.</summary>
        private static readonly (string Name, System.Action<RunRules> Revert)[] Kept =
        {
            ("BazaarRollsItsOwnPack", r => r.BazaarRollsItsOwnPack = false),
            ("FleshSetSurvivesTheFloor", r => r.FleshSetSurvivesTheFloor = false),
        };

        /// <summary>
        /// Each rule the port keeps for itself has to actually change a delve.
        /// </summary>
        /// <remarks>
        /// The recorded runs replay under <see cref="RunRules.AsRecorded"/>, so the gate says
        /// nothing about the shipped answers — put one of them back and every gate would still
        /// be green. This is what notices. A rule that can be reverted with no visible effect on
        /// any run is dead, and keeping it was not a real decision.
        ///
        /// Two rules are pinned directly instead of through a whole delve, because both need a
        /// run to have done something specific before they can show at all: stacking needs a
        /// Debt of Flesh to have been drawn (<see cref="OnlyAStackingCopyNotYetAwakeReachesTheShelf"/>),
        /// and re-keying needs an oath drafted late enough to outlive the bazaar with an
        /// awakening bought behind it (<see cref="AnAwakeningFollowsTheCopyItWasBoughtFor"/>).
        /// Both are reachable in play; neither is reachable by walking arbitrary seeds.
        /// </remarks>
        [Test]
        public void EveryRuleThePortKeepsChangesADelve()
        {
            var inert = new List<string>();

            foreach ((string name, System.Action<RunRules> revert) in Kept)
            {
                bool differs = false;

                for (uint seed = 1; seed <= 40 && !differs; seed++)
                {
                    RunRules reverted = RunRules.Shipped();
                    revert(reverted);
                    differs = Walk(seed, RunRules.Shipped()) != Walk(seed, reverted);
                }

                if (!differs) inert.Add(name);
            }

            Assert.That(inert, Is.Empty,
                "these rules can be put back with no effect on any delve, so keeping the port's " +
                "own answer did nothing: " + string.Join(", ", inert));
        }

        /// <summary>One whole delve, flattened to a string that changes when anything does.</summary>
        private static string Walk(uint seed, RunRules rules)
        {
            var greedy = new Greedy();

            // Rich and sturdy, so the run reaches the bazaar with the price in hand and gets
            // deep enough for a Flesh set to have somewhere to grow.
            var setup = new RunSetup { Hp = 400, Atk = 20, Gold = 400, Breath = 5 };
            DelveRun.Resolve(seed, setup, greedy, greedy, rules);
            return string.Join("|", greedy.Trace);
        }
    }
}
