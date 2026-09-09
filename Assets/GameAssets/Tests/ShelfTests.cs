using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Core.Stats;
using RelicRun.Core.Determinism;
using RelicRun.Core.Run;
using RelicRun.Tests.Support;
using Newtonsoft.Json.Linq;

namespace RelicRun.Tests
{
    /// <summary>
    /// The shelf a tray draws, and the meters it reads off one event.
    /// </summary>
    /// <remarks>
    /// Two things are being asserted together here and they are worth naming apart. One is that
    /// the shelf folds the hero's three separate lists — items, sockets by index, awakenings by
    /// id — into one thing per copy without losing which is which. The other is that a meter can
    /// be built from the shelf and an event's own snapshot and NOTHING else, which is Invariant 5
    /// for the tray: a fight is over before its first frame is drawn, so a view that reached for
    /// a live hero would show the finished numbers all the way through.
    /// </remarks>
    [TestFixture]
    public class ShelfTests
    {
        private static HeroState Carrying(params RelicId[] items)
        {
            return new HeroState { Items = new List<RelicId>(items) };
        }

        private static CombatSnapshot State(CombatCounters counters, int anvilSpent = 0,
            bool soilUsed = false)
        {
            return new CombatSnapshot(0, 100, 10, 0, 10, 3, EnemyRank.Guard, 0, 25, 10, 0, 0, 0,
                null, null, counters, 0, anvilSpent, soilUsed);
        }

        private static CombatCounters Counted(int strikes = 0, int stone = 0, int fightStrikes = 0,
            int strikeTotal = 0, int quench = 0)
        {
            return new CombatCounters(strikes, 0, 0, stone, fightStrikes, quench, 0, 0,
                strikeTotal, 0);
        }

        /* ---------- the shelf ---------- */

        /// <summary>
        /// Sockets follow the SLOT and awakenings follow the relic.
        /// </summary>
        /// <remarks>
        /// The one asymmetry in the whole model, and the reason folding these three lists belongs
        /// in one place. Two Whetstones: the socket on the second is the second's alone, and
        /// waking one wakes both, because the source wakes by id. Anywhere that had to remember
        /// which of those was which would eventually remember wrong.
        /// </remarks>
        [Test]
        public void ASocketBelongsToACopyAndAWakingBelongsToARelic()
        {
            var hero = new HeroState
            {
                Items = new List<RelicId> { RelicId.Whetstone, RelicId.Whetstone },
                SocketTriggers = new Dictionary<int, SocketTrigger> { { 1, SocketTrigger.Attack } },
                Awakened = new List<RelicId> { RelicId.Whetstone },
            };

            Shelf shelf = Shelf.Of(hero);

            Assert.That(shelf.Count, Is.EqualTo(2), "a duplicate is two copies, not one relic");

            Assert.That(shelf[0].Trigger, Is.EqualTo(SocketTrigger.None),
                "the socket went on the wrong copy");
            Assert.That(shelf[1].Trigger, Is.EqualTo(SocketTrigger.Attack));

            Assert.That(shelf[0].Awakened, Is.True, "waking is by relic, so both copies wake");
            Assert.That(shelf[1].Awakened, Is.True);
        }

        [Test]
        public void AnEmitterIsCarriedToo()
        {
            var hero = new HeroState
            {
                Items = new List<RelicId> { RelicId.Whetstone },
                SocketEmitters = new Dictionary<int, SocketEmitter> { { 0, SocketEmitter.Def } },
            };

            Assert.That(Shelf.Of(hero)[0].Emitter, Is.EqualTo(SocketEmitter.Def));
        }

        /// <summary>A hero carrying nothing has a shelf, not a null.</summary>
        [Test]
        public void AnEmptyShelfIsStillAShelf()
        {
            Assert.That(Shelf.Of(null).Count, Is.Zero);
            Assert.That(new HeroState().Items, Is.Not.Null);
            Assert.That(Shelf.Of(new HeroState()).Count, Is.Zero);
            Assert.That(Shelf.Of(null).OfKind(RelicKind.Edge), Is.Zero);
        }

        [Test]
        public void TheShelfCountsItsKinds()
        {
            Shelf shelf = Shelf.Of(Carrying(RelicId.Whetstone, RelicId.Whetstone,
                RelicId.AnvilHeart));

            Assert.That(shelf.OfKind(RelicCatalog.KindOf(RelicId.Whetstone)),
                Is.GreaterThanOrEqualTo(2), "duplicates count twice toward a set");
        }

        /* ---------- meters from an event ---------- */

        /// <summary>
        /// A meter needs the shelf and one snapshot, and asks for nothing else.
        /// </summary>
        /// <remarks>
        /// The Anvil is the case that proves it: its budget used to be readable only from a live
        /// <c>HeroState</c>, so a tray drawing a resolved fight would have shown the sharpenings
        /// it ended with from the very first tick. The snapshot carries them now, and this walks
        /// two different moments of the same fight to show the gauge actually moves.
        /// </remarks>
        [Test]
        public void AMeterComesFromTheShelfAndTheEventAlone()
        {
            Shelf shelf = Shelf.Of(Carrying(RelicId.AnvilHeart));

            RelicMeter early = RelicMeter.For(shelf, 0,
                State(Counted(strikeTotal: 3), anvilSpent: 0), false);

            RelicMeter late = RelicMeter.For(shelf, 0,
                State(Counted(strikeTotal: 47), anvilSpent: 4), false);

            Assert.That(early.Native.Filled, Is.EqualTo(3));
            Assert.That(early.Uses.Left, Is.EqualTo(RelicMeter.AnvilCap));

            Assert.That(late.Native.Filled, Is.EqualTo(7));
            Assert.That(late.Uses.Left, Is.EqualTo(RelicMeter.AnvilCap - 4),
                "the budget is frozen at whatever the hero ended the fight with");
        }

        [Test]
        public void TheSoilIsReadFromTheEventToo()
        {
            Shelf shelf = Shelf.Of(Carrying(RelicId.GravekeepersSoil));

            Assert.That(RelicMeter.For(shelf, 0, State(Counted()), false).Uses.Left,
                Is.EqualTo(1));
            Assert.That(RelicMeter.For(shelf, 0, State(Counted(), soilUsed: true), false).Uses.Spent,
                Is.True);
        }

        /* ---------- set bonuses ---------- */

        private static Shelf OfKind(RelicKind kind, int many)
        {
            var items = new List<RelicId>();

            foreach (RelicDef def in RelicCatalog.All)
            {
                if (items.Count >= many) break;
                if (def.Kind != kind) continue;

                // Duplicates are legal and a set counts copies, so one relic repeated is a
                // perfectly good five of a kind and keeps this independent of the pool's shape.
                for (int i = 0; i < many; i++) items.Add(def.Id);
            }

            Assert.That(items.Count, Is.GreaterThanOrEqualTo(many), "no " + kind + " relic exists");

            return Shelf.Of(Carrying(items.GetRange(0, many).ToArray()));
        }

        /// <summary>
        /// A fifth Edge relic gives the other four a cadence they did not have.
        /// </summary>
        /// <remarks>
        /// The only part of a copy's gauge that depends on its neighbours, which is why it cannot
        /// live on the copy. The count is strikes THIS FIGHT, not the run's total, so the bar
        /// starts empty on every floor.
        /// </remarks>
        [Test]
        public void AnEdgeSetOfFiveStartsCounting()
        {
            Assert.That(RelicMeter.For(OfKind(RelicKind.Edge, RelicMeter.EdgeSetNeeds - 1), 0,
                State(Counted(fightStrikes: 6)), false).Set.Any, Is.False,
                "four is not a set");

            RelicMeter met = RelicMeter.For(OfKind(RelicKind.Edge, RelicMeter.EdgeSetNeeds), 0,
                State(Counted(fightStrikes: 6)), false);

            Assert.That(met.Set.Any, Is.True);
            Assert.That(met.Set.Total, Is.EqualTo(RelicMeter.EdgeSetEvery));
            Assert.That(met.Set.Filled, Is.EqualTo(2));
            Assert.That(met.Charge.Total, Is.EqualTo(RelicMeter.EdgeSetEvery),
                "a relic with nothing else to count should show the set on its charge bar");
        }

        [Test]
        public void APaceSetNeedsSeven()
        {
            Assert.That(RelicMeter.For(OfKind(RelicKind.Pace, RelicMeter.PaceSetNeeds - 1), 0,
                State(Counted(fightStrikes: 9)), false).Set.Any, Is.False);

            RelicMeter met = RelicMeter.For(OfKind(RelicKind.Pace, RelicMeter.PaceSetNeeds), 0,
                State(Counted(fightStrikes: 9)), false);

            Assert.That(met.Set.Total, Is.EqualTo(RelicMeter.PaceSetEvery));
            Assert.That(met.Set.Filled, Is.EqualTo(4));
        }

        /// <summary>
        /// A relic counting something of its own does not also count for its set.
        /// </summary>
        /// <remarks>
        /// One charge bar, and a rhythm of its own is the better thing to put in it. Asserted
        /// because the alternative is silent: the set cadence would simply replace a gauge that
        /// was already telling the delver something more useful.
        /// </remarks>
        [Test]
        public void ARelicWithItsOwnRhythmIgnoresTheSet()
        {
            // The Quenched Blade, because it is an EDGE relic that also has a rhythm. The first
            // draft of this used the Anvil, which is GUARD — a kind with no set at all — so the
            // test could not have failed however the rule was broken. Mutation testing said so.
            Assert.That(RelicCatalog.KindOf(RelicId.QuenchedBlade), Is.EqualTo(RelicKind.Edge));

            var items = new List<RelicId>();
            for (int i = 0; i < RelicMeter.EdgeSetNeeds; i++) items.Add(RelicId.QuenchedBlade);

            Shelf shelf = Shelf.Of(Carrying(items.ToArray()));
            RelicMeter met = RelicMeter.For(shelf, 0,
                State(Counted(quench: 5, fightStrikes: 6)), false);

            Assert.That(shelf.OfKind(RelicKind.Edge),
                Is.GreaterThanOrEqualTo(RelicMeter.EdgeSetNeeds));
            Assert.That(met.Native.Any, Is.True);
            Assert.That(met.Set.Any, Is.False, "its own rhythm already fills the charge bar");
            Assert.That(met.Charge.Counting, Is.EqualTo(met.Native.Counting));
        }

        /// <summary>
        /// A socketed copy does not count for its set either.
        /// </summary>
        /// <remarks>
        /// The same rule as the rhythm, and worth its own case because the code says "attached OR
        /// native" and a mutant that drops half of that reads perfectly well. The Whetstone is
        /// EDGE and has no rhythm, so its socket is the only thing standing between it and a set
        /// cadence — and its neighbour on the same shelf, with no socket, still gets one.
        /// </remarks>
        [Test]
        public void ASocketedCopyIgnoresTheSetToo()
        {
            Assert.That(RelicCatalog.KindOf(RelicId.Whetstone), Is.EqualTo(RelicKind.Edge));

            var items = new List<RelicId>();
            for (int i = 0; i < RelicMeter.EdgeSetNeeds; i++) items.Add(RelicId.Whetstone);

            var hero = new HeroState
            {
                Items = items,
                SocketTriggers = new Dictionary<int, SocketTrigger> { { 0, SocketTrigger.Attack } },
            };

            Shelf shelf = Shelf.Of(hero);
            CombatSnapshot state = State(Counted(strikes: 7, fightStrikes: 6));

            RelicMeter socketed = RelicMeter.For(shelf, 0, state, false);
            RelicMeter bare = RelicMeter.For(shelf, 1, state, false);

            Assert.That(socketed.Attached.Any, Is.True);
            Assert.That(socketed.Set.Any, Is.False, "the socket already fills the charge bar");
            Assert.That(socketed.Charge.Counting, Is.EqualTo("strikes"));

            Assert.That(bare.Set.Any, Is.True, "its neighbour on the same shelf still has a set");
        }

        /// <summary>
        /// The thresholds are the source's, and are read from it rather than typed.
        /// </summary>
        /// <remarks>
        /// Five and four, seven and five. They look arbitrary because they are: nothing generates
        /// them and there is no rule to re-derive them from. Mutation testing swapped the two
        /// thresholds over and every test in this file still passed, because every one of them
        /// worked out what to expect from the same constants it was checking.
        ///
        /// <c>Tools/capture/sets.mjs</c> reads them off the markup, and reads them TWICE — the
        /// tray decides the fallback fuse and the relic card draws a labelled gauge for the same
        /// rule — so a recorder taking either on its own could not notice them drifting apart.
        /// </remarks>
        [Test]
        public void TheSetThresholdsAreTheSourcesOwn()
        {
            JObject sets = (JObject)Corpus.Object("sets.json")["sets"];

            Assert.That(RelicMeter.EdgeSetNeeds, Is.EqualTo(sets["edge"]["needs"].Value<int>()));
            Assert.That(RelicMeter.EdgeSetEvery, Is.EqualTo(sets["edge"]["every"].Value<int>()));
            Assert.That(RelicMeter.PaceSetNeeds, Is.EqualTo(sets["pace"]["needs"].Value<int>()));
            Assert.That(RelicMeter.PaceSetEvery, Is.EqualTo(sets["pace"]["every"].Value<int>()));

            Assert.That(RelicMeter.EdgeSetNeeds, Is.Not.EqualTo(RelicMeter.PaceSetNeeds),
                "the two sets ask for different numbers, which is why both are recorded");
        }

        /// <summary>A shelf built from nothing is empty rather than broken.</summary>
        /// <remarks>
        /// The constructor is public and takes a list, so somebody will eventually hand it a
        /// null. An empty shelf draws no tray; a null one throws on the first slot.
        /// </remarks>
        [Test]
        public void AShelfBuiltFromNothingIsEmpty()
        {
            var bare = new Shelf(null);

            Assert.That(bare.Count, Is.Zero);
            Assert.That(bare.OfKind(RelicKind.Edge), Is.Zero);
        }

        /// <summary>
        /// The engine actually records what the Anvil has spent.
        /// </summary>
        /// <remarks>
        /// The join between the two halves of this, and the half a unit test cannot see: every
        /// assertion above builds its own snapshot, so all of them would pass against an engine
        /// that wrote a zero into every event. This resolves a real floor and reads the number
        /// back off the events it produced.
        /// </remarks>
        [Test]
        public void TheEngineWritesTheAnvilCountIntoEveryEvent()
        {
            RunSetup setup = RunSetup.ForLevel(1);

            var hero = new HeroState
            {
                Floor = 1,
                Php = setup.Hp,
                Pmax = setup.Hp,
                BaseAtk = setup.Atk,
                BaseDef = setup.Def,
                BaseSpd = setup.Spd,
                BaseLck = setup.Lck,
                Items = new List<RelicId> { RelicId.AnvilHeart },

                // Part-spent before the fight starts, so the number is visible from the first
                // event rather than only after ten strikes.
                AnvilBonus = 4,
            };

            var rng = new Mulberry32(0x5E1F00D);
            List<EnemyState> pack = EnemyPackGenerator.Build(1, rng, setup.Dungeon);
            IReadOnlyList<CombatEvent> events =
                new CombatEngine(CombatRules.Delve()).ResolveFloor(hero, pack, rng).Events;

            Assert.That(events, Is.Not.Empty);

            Assert.That(events[0].State.AnvilSpent, Is.GreaterThanOrEqualTo(4),
                "the engine is not writing the Anvil's count into the events it emits");

            Shelf shelf = Shelf.Of(hero);

            Assert.That(RelicMeter.For(shelf, 0, events[0].State, false).Uses.Left,
                Is.EqualTo(RelicMeter.AnvilCap - events[0].State.AnvilSpent));
        }

        /// <summary>
        /// The charge bar prefers a rhythm, then a socket, then a set; the hairline is the second.
        /// </summary>
        /// <remarks>
        /// Decided in Core rather than in the widget so the tray and the relic card cannot pick
        /// differently — which is the reason the source routes both through one meter and says so
        /// in a comment beside it.
        /// </remarks>
        [Test]
        public void TheChargeBarPrefersARhythmAndTheHairlineIsTheOtherOne()
        {
            var hero = new HeroState
            {
                Items = new List<RelicId> { RelicId.AnvilHeart },
                SocketTriggers = new Dictionary<int, SocketTrigger> { { 0, SocketTrigger.Attack } },
            };

            RelicMeter both = RelicMeter.For(Shelf.Of(hero), 0,
                State(Counted(strikes: 7, strikeTotal: 23)), false);

            Assert.That(both.Charge.Counting, Is.EqualTo(both.Native.Counting));
            Assert.That(both.Charge.Filled, Is.EqualTo(3), "23 strikes into a rhythm of ten");
            Assert.That(both.Hairline.Any, Is.True, "two clocks, and only one bar to show them on");
            Assert.That(both.Hairline.Filled, Is.EqualTo(1), "7 strikes into a cadence of three");

            var socketOnly = new HeroState
            {
                Items = new List<RelicId> { RelicId.Whetstone },
                SocketTriggers = new Dictionary<int, SocketTrigger> { { 0, SocketTrigger.Attack } },
            };

            RelicMeter one = RelicMeter.For(Shelf.Of(socketOnly), 0,
                State(Counted(strikes: 7)), false);

            Assert.That(one.Charge.Filled, Is.EqualTo(1), "the socket fills the bar when nothing else does");
            Assert.That(one.Hairline.Any, Is.False, "one clock is not two");
        }

        [Test]
        public void ARelicCountingNothingShowsNothing()
        {
            Shelf shelf = Shelf.Of(Carrying(RelicId.Whetstone));
            RelicMeter met = RelicMeter.For(shelf, 0, State(Counted(strikes: 7, fightStrikes: 7)),
                false);

            Assert.That(met.Charge.Any, Is.False);
            Assert.That(met.Hairline.Any, Is.False);
            Assert.That(met.Any, Is.False, "most relics have nothing to show, and that is fine");
        }
    }
}
