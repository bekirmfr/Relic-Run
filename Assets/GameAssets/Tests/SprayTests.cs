using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Core.Stats;

namespace RelicRun.Tests
{
    /// <summary>
    /// What comes off a body, and off whose.
    /// </summary>
    /// <remarks>
    /// The counts and the depth filter are rules; where each mote lands is a look and lives in
    /// the widget. That split is the same one the flying numbers live under, and it is what makes
    /// this testable at all — a scatter is not a thing to assert about, and "four motes on a
    /// genuine blow" is.
    /// </remarks>
    [TestFixture]
    public class SprayTests
    {
        private static CombatEvent Happened(CombatEventType type, int depth = 0)
        {
            var counters = new CombatCounters(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            var state = new CombatSnapshot(0, 100, 10, 0, 10, 3, EnemyRank.Guard, 0, 25, 10, 0, 0,
                0, null, null, counters);

            return new CombatEvent(type, depth, state, 1);
        }

        /// <summary>
        /// A dying body bleeds and comes apart, whichever body it is.
        /// </summary>
        /// <remarks>
        /// Both sides asserted, because this is exactly where a screen quietly stops treating the
        /// delver as a thing in the world: a foe gets an ending and the delver gets a fade. The
        /// source gives them the same one.
        /// </remarks>
        [Test]
        public void ADyingBodyBleedsAndShatters()
        {
            Spray foe = Sprays.Of(Happened(CombatEventType.Kill));
            Spray delver = Sprays.Of(Happened(CombatEventType.Death));

            Assert.That(foe.Kind, Is.EqualTo(SprayKind.Blood));
            Assert.That(foe.Count, Is.EqualTo(Sprays.Drops));
            Assert.That(foe.Shatters, Is.True);
            Assert.That(foe.OnDelver, Is.False);

            Assert.That(delver.Kind, Is.EqualTo(SprayKind.Blood));
            Assert.That(delver.Count, Is.EqualTo(Sprays.Drops));
            Assert.That(delver.Shatters, Is.True);
            Assert.That(delver.OnDelver, Is.True, "the delver's own death happens to the delver");
        }

        /// <summary>A struck body throws grit, and less of it than a dying one throws blood.</summary>
        [Test]
        public void AStruckBodyThrowsDust()
        {
            Spray foe = Sprays.Of(Happened(CombatEventType.EnemyDamage));

            Assert.That(foe.Kind, Is.EqualTo(SprayKind.Dust));
            Assert.That(foe.Count, Is.EqualTo(Sprays.Motes));
            Assert.That(foe.Shatters, Is.False, "being hit is not being killed");

            Assert.That(Sprays.Motes, Is.LessThan(Sprays.Drops),
                "a death should be messier than a graze, or neither reads as what it is");
        }

        /// <summary>
        /// Only a genuine blow raises dust off a foe.
        /// </summary>
        /// <remarks>
        /// Relic damage arrives in chains of six, and a foe chipped six times would be buried in
        /// grit for a hit the delver never threw. Depth is what tells the two apart, and it is
        /// the only thing that does.
        /// </remarks>
        [Test]
        public void ChainedDamageDoesNotDustTheFoe()
        {
            Assert.That(Sprays.Of(Happened(CombatEventType.EnemyDamage, depth: 0)).Any, Is.True);

            Assert.That(Sprays.Of(Happened(CombatEventType.EnemyDamage, depth: 1)).Any, Is.False,
                "a chained tick is not a blow");
            Assert.That(Sprays.Of(Happened(CombatEventType.EnemyDamage, depth: 5)).Any, Is.False);
        }

        /// <summary>
        /// A struck delver dusts however deep the chain, which is the source's asymmetry.
        /// </summary>
        /// <remarks>
        /// Asserted rather than smoothed over. It is defensible — being chipped six times feels
        /// like six hits from the inside and like one from the outside — but nothing in the source
        /// says so, and making it symmetric would be inventing balance rather than porting it. A
        /// test is where a decision like this survives being tidied up by somebody in a hurry.
        /// </remarks>
        [Test]
        public void AChainedTickStillDustsTheDelver()
        {
            Assert.That(Sprays.Of(Happened(CombatEventType.PlayerDamage, depth: 0)).Any, Is.True);
            Assert.That(Sprays.Of(Happened(CombatEventType.PlayerDamage, depth: 4)).Any, Is.True,
                "the source does not filter the delver's dust by depth, and this port does not");

            Assert.That(Sprays.Of(Happened(CombatEventType.PlayerDamage)).OnDelver, Is.True);

            Assert.That(Sprays.Of(Happened(CombatEventType.PlayerDamage)).Shatters, Is.False,
                "a delver being hit is not a delver coming apart, and the screen should not " +
                "announce a death on every graze");
        }

        /// <summary>
        /// A spray of nothing is nothing, however much of it is asked for.
        /// </summary>
        /// <remarks>
        /// The constructor is public, so a count without a kind is a thing somebody can build —
        /// and it would be spawned as four motes of no particular substance. Cheap to say no to,
        /// and the alternative is a widget deciding what an unkinded spray looks like.
        /// </remarks>
        [Test]
        public void ASprayWithNoKindIsNoSpray()
        {
            Assert.That(new Spray(SprayKind.None, false, 5).Any, Is.False);
            Assert.That(new Spray(SprayKind.Blood, false, 0).Any, Is.False, "and nor is an empty one");
            Assert.That(new Spray(SprayKind.Blood, false, 1).Any, Is.True);
        }

        /// <summary>Most events throw nothing at all.</summary>
        /// <remarks>
        /// Gold, heals, entrances, log lines. A screen that sprayed on every event would be a
        /// screen permanently full of blood, and the four that do spray would stop meaning
        /// anything.
        /// </remarks>
        [TestCase(CombatEventType.Enter)]
        [TestCase(CombatEventType.Gold)]
        [TestCase(CombatEventType.Heal)]
        [TestCase(CombatEventType.First)]
        public void MostEventsThrowNothing(CombatEventType type)
        {
            Assert.That(Sprays.Of(Happened(type)).Any, Is.False);
            Assert.That(Sprays.Of(Happened(type)).Kind, Is.EqualTo(SprayKind.None));
        }
    }
}
