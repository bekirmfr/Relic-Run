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
            int quench = 0, int momentum = 0, int rabbit = 0, int strikeTotal = 0, int sentinel = 0)
        {
            return new CombatCounters(strikes, pain, gold, 0, 0, quench, momentum, rabbit,
                strikeTotal, sentinel);
        }

        private static HeroState Delver(int anvil = 0, bool soil = false)
        {
            return new HeroState { AnvilBonus = anvil, SoilUsed = soil };
        }

        private static RelicMeter Meter(RelicId relic, SocketTrigger socket = SocketTrigger.None,
            CombatCounters counters = default(CombatCounters), HeroState hero = null,
            bool awakened = false, bool versus = false)
        {
            return RelicMeter.For(relic, socket, counters, hero ?? Delver(), awakened, versus);
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

        /// <summary>Waking the Anvil buys two more, and the count does not move.</summary>
        [Test]
        public void AWokenAnvilHasTwoMore()
        {
            HeroState spent = Delver(anvil: 10);

            Assert.That(Meter(RelicId.AnvilHeart, hero: spent).Native.Any, Is.False);
            Assert.That(Meter(RelicId.AnvilHeart, hero: spent, awakened: true).Native.Any, Is.True,
                "the bazaar bought it two more sharpenings");
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
