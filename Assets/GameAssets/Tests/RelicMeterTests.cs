using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;

namespace RelicRun.Tests
{
    /// <summary>
    /// What one copy of a relic has to show for itself.
    /// </summary>
    /// <remarks>
    /// One COPY. A socket is bolted to an inventory slot, so the second Whetstone on the shelf
    /// can be counting toward something the first is not — which is invariant 6 showing up in
    /// the interface rather than in the engine, and the reason inventory is an ordered list with
    /// duplicates rather than a tally.
    /// </remarks>
    [TestFixture]
    public class RelicMeterTests
    {
        private static CombatCounters Counted(int strikes = 0, int pain = 0, int gold = 0,
            int quench = 0, int momentum = 0, int rabbit = 0, int strikeTotal = 0, int sentinel = 0,
            int stone = 0)
        {
            return new CombatCounters(strikes, pain, gold, stone, 0, quench, momentum, rabbit,
                strikeTotal, sentinel);
        }

        private static HeroState Delver(int anvil = 0, bool soil = false)
        {
            return new HeroState { AnvilBonus = anvil, SoilUsed = soil };
        }

        private static RelicMeter Meter(RelicId relic, SocketTrigger socket = SocketTrigger.None,
            CombatCounters counters = default(CombatCounters), HeroState hero = null,
            bool awakened = false, bool versus = false,
            SocketEmitter emitter = SocketEmitter.None)
        {
            return RelicMeter.For(relic, socket, counters, hero ?? Delver(), awakened, versus,
                emitter);
        }

        /* ---------- sockets ---------- */

        [Test]
        public void ASocketedTriggerCountsEveryThird()
        {
            RelicMeter meter = Meter(RelicId.Whetstone, SocketTrigger.Attack, Counted(strikes: 7));

            Assert.That(meter.Attached.Any, Is.True);
            Assert.That(meter.Attached.Filled, Is.EqualTo(1), "seven strikes is one past the sixth");
            Assert.That(meter.Attached.Total, Is.EqualTo(3));
            Assert.That(meter.Attached.Counting, Is.EqualTo("strikes"));
        }

        [Test]
        public void EachTriggerCountsItsOwnThing()
        {
            Assert.That(Meter(RelicId.Whetstone, SocketTrigger.Hit, Counted(pain: 2))
                .Attached.Counting, Is.EqualTo("hits taken"));
            Assert.That(Meter(RelicId.Whetstone, SocketTrigger.Gold, Counted(gold: 2))
                .Attached.Counting, Is.EqualTo("gold gains"));
        }

        /// <summary>Most relics have nothing to show, and show nothing.</summary>
        /// <remarks>
        /// A shelf where every card wore a gauge would be a shelf of gauges. The four that count
        /// and the three that spend are the ones worth watching.
        /// </remarks>
        [Test]
        public void MostCopiesHaveNothingToShow()
        {
            Assert.That(Meter(RelicId.Whetstone).Any, Is.False);
            Assert.That(Meter(RelicId.OxHeart).Any, Is.False);
            Assert.That(Meter(RelicId.LuckyClover).Any, Is.False);
        }

        /// <summary>A trigger nothing counts toward shows nothing rather than an empty gauge.</summary>
        [Test]
        public void ATriggerWithNoCadenceShowsNothing()
        {
            Assert.That(Meter(RelicId.Whetstone, SocketTrigger.Kill).Attached.Any, Is.False);
            Assert.That(Meter(RelicId.Whetstone, SocketTrigger.Floor).Attached.Any, Is.False);
        }

        /* ---------- rhythms ---------- */

        [Test]
        public void TheAnvilCountsStrikesAndSpendsSharpenings()
        {
            RelicMeter meter = Meter(RelicId.AnvilHeart, counters: Counted(strikeTotal: 23),
                hero: Delver(anvil: 2));

            Assert.That(meter.Native.Filled, Is.EqualTo(3));
            Assert.That(meter.Native.Total, Is.EqualTo(10));
            Assert.That(meter.Uses.Left, Is.EqualTo(8), "two of ten spent");
            Assert.That(meter.Uses.Cap, Is.EqualTo(10));
        }

        /// <summary>
        /// A spent Anvil stops counting toward a sharpening it cannot do.
        /// </summary>
        /// <remarks>
        /// A gauge filling toward something that will not happen is a promise the relic has
        /// already broken.
        /// </remarks>
        [Test]
        public void ASpentAnvilStopsCounting()
        {
            RelicMeter meter = Meter(RelicId.AnvilHeart, counters: Counted(strikeTotal: 23),
                hero: Delver(anvil: 10));

            Assert.That(meter.Native.Any, Is.False);
            Assert.That(meter.Uses.Spent, Is.True);
        }

        /// <summary>
        /// Waking the Anvil buys two more, and BOTH gauges are told.
        /// </summary>
        /// <remarks>
        /// The rhythm half of this was asserted and the budget half was not, which is exactly
        /// where the bug was: the budget asked for sharpenings against a cap of ten no matter
        /// what, so a woken Anvil at ten used showed a rhythm still counting and a budget already
        /// spent. Two gauges on one relic disagreeing about whether it can still do anything.
        ///
        /// The tray greys a spent relic and puts a cross on it, so the delver would have been
        /// shown a dead relic that was not dead — by the gauge whose whole job is to answer that
        /// question.
        /// </remarks>
        [Test]
        public void AWokenAnvilHasTwoMoreOnBothGauges()
        {
            HeroState spent = Delver(anvil: 10);

            RelicMeter asleep = Meter(RelicId.AnvilHeart, hero: spent);
            RelicMeter awake = Meter(RelicId.AnvilHeart, hero: spent, awakened: true);

            Assert.That(asleep.Native.Any, Is.False);
            Assert.That(asleep.Uses.Left, Is.Zero);
            Assert.That(asleep.Uses.Cap, Is.EqualTo(RelicMeter.AnvilCap));
            Assert.That(asleep.Uses.Spent, Is.True);

            Assert.That(awake.Native.Any, Is.True,
                "the bazaar bought it two more sharpenings");

            Assert.That(awake.Uses.Left, Is.EqualTo(2),
                "the budget still thinks the cap is ten, so it reads a live relic as spent");
            Assert.That(awake.Uses.Cap, Is.EqualTo(RelicMeter.AwokenAnvilCap));
            Assert.That(awake.Uses.Spent, Is.False,
                "a relic with two sharpenings left is not spent, and the tray would grey it out");
        }

        /// <summary>
        /// A defence emitter counts activations, like the three triggers do.
        /// </summary>
        /// <remarks>
        /// The fourth cadence, and it was missing. The source keeps triggers and emitters in one
        /// table per inventory slot — t_attack, t_hit, t_gold, e_def as four peers — and this
        /// port splits them into two dictionaries, so the one that was not a trigger fell down
        /// the gap between them and its gauge never filled.
        /// </remarks>
        [Test]
        public void ADefenceEmitterCountsActivations()
        {
            RelicMeter meter = Meter(RelicId.Whetstone, emitter: SocketEmitter.Def,
                counters: Counted(stone: 8));

            Assert.That(meter.Attached.Any, Is.True, "the fourth cadence never fills");
            Assert.That(meter.Attached.Filled, Is.EqualTo(2));
            Assert.That(meter.Attached.Total, Is.EqualTo(RelicMeter.SocketEvery));
            Assert.That(meter.Attached.Counting, Is.EqualTo("activations"));
        }

        /// <summary>
        /// The emitters that are not Def have nothing to count, and neither do most triggers.
        /// </summary>
        /// <remarks>
        /// Asserted rather than assumed, because "add the missing one" is the kind of fix that
        /// keeps going. Kill, Dodge and Luck fire on the event itself rather than on every third
        /// of it, so a gauge on them would fill toward nothing.
        /// </remarks>
        [Test]
        public void TheOtherSocketsCountNothing()
        {
            foreach (SocketEmitter emitter in
                     new[] { SocketEmitter.Dmg, SocketEmitter.Heal, SocketEmitter.Gold,
                             SocketEmitter.Atk, SocketEmitter.Spd })
            {
                Assert.That(Meter(RelicId.Whetstone, emitter: emitter,
                    counters: Counted(stone: 8)).Attached.Any, Is.False, emitter + " grew a gauge");
            }

            foreach (SocketTrigger trigger in
                     new[] { SocketTrigger.Kill, SocketTrigger.Dodge, SocketTrigger.Luck })
            {
                Assert.That(Meter(RelicId.Whetstone, trigger,
                    Counted(strikes: 8, pain: 8, gold: 8, stone: 8)).Attached.Any, Is.False,
                    trigger + " fires on the event, so it counts toward nothing");
            }
        }

        /// <summary>A trigger wins over an emitter, because a copy showing two would show one.</summary>
        [Test]
        public void ATriggerIsPreferredToAnEmitter()
        {
            RelicMeter meter = Meter(RelicId.Whetstone, SocketTrigger.Attack,
                Counted(strikes: 7, stone: 8), emitter: SocketEmitter.Def);

            Assert.That(meter.Attached.Counting, Is.EqualTo("strikes"));
            Assert.That(meter.Attached.Filled, Is.EqualTo(1));
        }

        [Test]
        public void TheQuenchedBladeAndTheBeadCountToThree()
        {
            Assert.That(Meter(RelicId.QuenchedBlade, counters: Counted(quench: 5)).Native.Filled,
                Is.EqualTo(2));
            Assert.That(Meter(RelicId.MomentumBead, counters: Counted(momentum: 4)).Native.Filled,
                Is.EqualTo(1));
        }

        /// <summary>
        /// The Rabbit's Foot only counts in a duel.
        /// </summary>
        /// <remarks>
        /// In a delve it answers luck without counting toward anything, so a gauge there would
        /// fill and then do nothing — which reads as the relic being broken rather than as the
        /// interface being wrong.
        /// </remarks>
        [Test]
        public void TheRabbitOnlyCountsInADuel()
        {
            Assert.That(Meter(RelicId.RabbitsFoot, counters: Counted(rabbit: 2)).Native.Any,
                Is.False);
            Assert.That(Meter(RelicId.RabbitsFoot, counters: Counted(rabbit: 2), versus: true)
                .Native.Filled, Is.EqualTo(2));
        }

        /* ---------- budgets ---------- */

        [Test]
        public void TheBellRingsThreeTimes()
        {
            RelicMeter meter = Meter(RelicId.SentinelBell, counters: Counted(sentinel: 1));

            Assert.That(meter.Uses.Left, Is.EqualTo(2));
            Assert.That(meter.Uses.Cap, Is.EqualTo(3));
            Assert.That(meter.Uses.IsDebt, Is.False);
        }

        [Test]
        public void TheSoilIsSpentOrItIsNot()
        {
            Assert.That(Meter(RelicId.GravekeepersSoil).Uses.Left, Is.EqualTo(1));
            Assert.That(Meter(RelicId.GravekeepersSoil, hero: Delver(soil: true)).Uses.Spent,
                Is.True);
        }

        /// <summary>A budget cannot go below nothing however far past the cap the count is.</summary>
        [Test]
        public void ABudgetNeverReadsAsOwingUses()
        {
            Assert.That(Meter(RelicId.AnvilHeart, hero: Delver(anvil: 40)).Uses.Left, Is.Zero);
            Assert.That(Meter(RelicId.SentinelBell, counters: Counted(sentinel: 9)).Uses.Left,
                Is.Zero);
        }

        /* ---------- two clocks at once ---------- */

        /// <summary>
        /// A socketed relic with a rhythm of its own runs both, and shows both.
        /// </summary>
        /// <remarks>
        /// The Anvil counts strikes toward a sharpening; a socket on the same copy counts strikes
        /// toward something else entirely. Merging them would show one number for two clocks, and
        /// a delver watching the one they could see would be surprised by the other.
        /// </remarks>
        [Test]
        public void TwoClocksRunAtOnceAndBothAreShown()
        {
            RelicMeter meter = Meter(RelicId.AnvilHeart, SocketTrigger.Attack,
                Counted(strikes: 5, strikeTotal: 23));

            Assert.That(meter.Attached.Filled, Is.EqualTo(2), "five strikes, counting to three");
            Assert.That(meter.Native.Filled, Is.EqualTo(3), "twenty-three, counting to ten");
            Assert.That(meter.Attached.Total, Is.Not.EqualTo(meter.Native.Total));
        }
    }
}
