using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Run;

namespace RelicRun.Tests
{
    /// <summary>
    /// One floor's fighting, resolved the same way whoever asks for it.
    /// </summary>
    /// <remarks>
    /// This exists because there were two spellings of the same procedure: the editor's harness
    /// built a pack and ran the engine inline, and a run does the same thing surrounded by draft
    /// and breather. Two spellings is one chance for a screen to show a fight the run layer would
    /// never have fought — and the symptom is a fight that looks perfectly reasonable.
    /// </remarks>
    [TestFixture]
    public class BoutTests
    {
        /// <summary>
        /// The same plan twice is the same fight twice.
        /// </summary>
        /// <remarks>
        /// The floor of everything else here. A fight nobody can reproduce is a fight nobody can
        /// investigate, and the first question about a strange one is what seed it was.
        /// </remarks>
        [Test]
        public void TheSamePlanFightsTheSameFight()
        {
            IReadOnlyList<CombatEvent> first = Fought(Plan(1234, 3, 2));
            IReadOnlyList<CombatEvent> again = Fought(Plan(1234, 3, 2));

            Assert.That(first.Count, Is.EqualTo(again.Count));
            Assert.That(first.Count, Is.GreaterThan(0), "the fight produced nothing to watch");

            for (var i = 0; i < first.Count; i++)
            {
                Assert.That(first[i].Type, Is.EqualTo(again[i].Type), "event " + i + " differs");
                Assert.That(first[i].Amount, Is.EqualTo(again[i].Amount), "event " + i + " differs");
            }
        }

        /// <summary>
        /// A deeper hall is a harder fight, from the same seed.
        /// </summary>
        /// <remarks>
        /// The tier is not decoration. It picks the backdrop AND multiplies what waits at the
        /// bottom of it, and a plan that dropped it would send a delver into the tenth hall
        /// against the first hall's rats — which looks like a generous hall rather than like a
        /// field nobody passed on.
        /// </remarks>
        [Test]
        public void TheHallDecidesWhatWaits()
        {
            var shallow = new Mulberry32(99);
            var deep = new Mulberry32(99);

            IReadOnlyList<EnemyState> first = EnemyPackGenerator.Build(3, shallow,
                DungeonConfig.ForTier(1));

            IReadOnlyList<EnemyState> tenth = EnemyPackGenerator.Build(3, deep,
                DungeonConfig.ForTier(10));

            Assert.That(first.Count, Is.EqualTo(tenth.Count), "the packs are different shapes");

            var harder = false;

            for (var i = 0; i < first.Count; i++)
            {
                if (tenth[i].Hp > first[i].Hp) harder = true;
            }

            Assert.That(harder, Is.True, "the tenth hall is no harder than the first");

            // And the plan actually passes it on, which is the part that can rot.
            FightPlan low = Plan(99, 3, 1);
            FightPlan high = Plan(99, 3, 10);

            Assert.That(Fought(low).Count, Is.Not.EqualTo(Fought(high).Count),
                "the same fight was resolved for two different halls");
        }

        /// <summary>
        /// A supplied pack is the pack that is fought.
        /// </summary>
        /// <remarks>
        /// Null and a list are different answers, not a shortcut and a long way round: null asks
        /// the floor what waits there, and a list says. A plan that quietly rolled its own over
        /// the top of a supplied one would be a revive resuming against a foe nobody had met.
        ///
        /// The stream is the other half and is NOT observable here. Generating a pack consumes
        /// draws, so supplying one leaves every later roll reading a different part of the
        /// sequence — but a delve fight only draws for crits and dodges, and a delver carrying
        /// neither Weighted Dice nor a Lucky Clover fights a fight that is settled entirely by
        /// the statlines. Which is worth knowing on its own: most early fights are arithmetic.
        /// </remarks>
        [Test]
        public void ASuppliedPackIsTheOneThatIsFought()
        {
            FightPlan rolled = Plan(7, 2, 1);

            FightPlan handed = Plan(7, 2, 1);

            handed.Pack = new List<EnemyState>
            {
                new EnemyState { SpeciesIndex = 0, Hp = 1, MaxHp = 1, Atk = 0, Spd = 1 },
            };

            IReadOnlyList<CombatEvent> waiting = Fought(rolled);
            IReadOnlyList<CombatEvent> named = Fought(handed);

            Assert.That(waiting.Count, Is.GreaterThan(0));
            Assert.That(named.Count, Is.GreaterThan(0));

            Assert.That(named.Count, Is.LessThan(waiting.Count),
                "a pack of one foe on one hit point took as long as the floor's own");
        }

        /// <summary>The plan tells the hero which floor they are on, so the log can say.</summary>
        [Test]
        public void TheDelverIsToldWhichFloorTheyAreOn()
        {
            FightPlan plan = Plan(5, 9, 1);

            Assert.That(plan.Delver.Floor, Is.EqualTo(1), "the fixture started somewhere odd");

            Bout.Fight(plan);

            Assert.That(plan.Delver.Floor, Is.EqualTo(9));
        }

        /// <summary>
        /// What a delver leaves with is clamped to the ceiling the floor opened with.
        /// </summary>
        /// <remarks>
        /// A run does this at the same point and for the same reason: a Bottomless Chalice that
        /// grew the pool mid-fight raises the ceiling only once the fight is over, so the health
        /// it bought does not arrive with it. Reading the raw pool would show a delver walking
        /// off a floor with more health than they can hold — and the next screen would then
        /// quietly take it away again.
        /// </remarks>
        [Test]
        public void HealthIsClampedToTheCeilingTheFloorOpenedWith()
        {
            var plan = new FightPlan
            {
                Delver = new HeroState { Php = 130, Pmax = 130, Gold = 12, Kills = 3 },
            };

            FightSummary grown = Bout.Read(plan, null, 100);

            Assert.That(grown.Left, Is.EqualTo(100), "the pool outran the ceiling it opened with");
            Assert.That(grown.Most, Is.EqualTo(130), "the ceiling itself is not clamped");
            Assert.That(grown.Won, Is.True);
            Assert.That(grown.Gold, Is.EqualTo(12));
            Assert.That(grown.Kills, Is.EqualTo(3));
        }

        /// <summary>
        /// A hero at nothing is dead, and the engine leaves them below it.
        /// </summary>
        /// <remarks>
        /// A killing blow does not stop at zero — the pool goes negative and the run reads
        /// <c>Php &gt; 0</c>. So the summary reports zero and says the fight was lost, and both
        /// halves matter: "-4 left" on the result screen is the sort of thing that gets reported
        /// as a bug in the combat maths.
        /// </remarks>
        [Test]
        public void AFallenDelverIsAtNothingAndLostIt()
        {
            var plan = new FightPlan { Delver = new HeroState { Php = -4, Pmax = 100 } };

            FightSummary fell = Bout.Read(plan, null, 100);

            Assert.That(fell.Won, Is.False);
            Assert.That(fell.Left, Is.Zero);

            var edge = new FightPlan { Delver = new HeroState { Php = 0, Pmax = 100 } };

            Assert.That(Bout.Read(edge, null, 100).Won, Is.False, "a delver at nothing survived");

            var alive = new FightPlan { Delver = new HeroState { Php = 1, Pmax = 100 } };

            Assert.That(Bout.Read(alive, null, 100).Won, Is.True, "a delver at one died");
        }

        /// <summary>
        /// A delver built for a fight is the delver a run would have started.
        /// </summary>
        /// <remarks>
        /// The drift this guards is silent: a fight scene that seeded its own hero would produce
        /// perfectly plausible numbers, and the only symptom would be that the first floor plays
        /// differently depending on which screen started it.
        ///
        /// Checked field by field against the setup rather than against a run, because a run
        /// drafts before its first fight and the two are not comparable event for event.
        /// </remarks>
        [Test]
        public void ADelverIsBuiltTheWayARunWouldBuildThem()
        {
            RunSetup setup = RunSetup.ForLevel(7);

            setup.StartKit = new List<RelicId> { RelicId.Whetstone, RelicId.IronSkin };

            HeroState hero = Bout.Delver(setup);

            Assert.That(hero.Php, Is.EqualTo(setup.Hp));
            Assert.That(hero.Pmax, Is.EqualTo(setup.Hp));
            Assert.That(hero.Gold, Is.EqualTo(setup.Gold));
            Assert.That(hero.BaseAtk, Is.EqualTo(setup.Atk));
            Assert.That(hero.BaseDef, Is.EqualTo(setup.Def));
            Assert.That(hero.BaseSpd, Is.EqualTo(setup.Spd));
            Assert.That(hero.BaseLck, Is.EqualTo(setup.Lck));
            Assert.That(hero.Floor, Is.EqualTo(1));

            Assert.That(hero.Items.Count, Is.EqualTo(2));
            Assert.That(hero.Items[0], Is.EqualTo(RelicId.Whetstone));

            // The list is COPIED. A hero holding the setup's own list would have the bazaar
            // writing into a table every later run reads from.
            Assert.That(ReferenceEquals(hero.Items, setup.StartKit), Is.False);
        }

        /// <summary>
        /// Every statline a run setup carries reaches the delver built from it.
        /// </summary>
        /// <remarks>
        /// Reflected rather than listed, so a field added to <see cref="RunSetup"/> and forgotten
        /// here fails without anybody remembering to come back. The three that are deliberately
        /// NOT carried are named: they shape the run around the fight rather than the delver in
        /// it, and a one-off fight has no draft, no bazaar and no gate to breathe at.
        /// </remarks>
        [Test]
        public void NoStatlineIsLeftBehind()
        {
            var elsewhere = new List<string>
            {
                "Breath", "DraftChoices", "BazaarDeals", "Dungeon", "StartKit",
            };

            var carried = new List<string> { "Hp", "Gold", "Atk", "Def", "Spd", "Lck" };

            var seen = new List<string>();

            foreach (FieldInfo field in typeof(RunSetup).GetFields(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                seen.Add(field.Name);

                Assert.That(carried.Contains(field.Name) || elsewhere.Contains(field.Name), Is.True,
                    "RunSetup." + field.Name + " is neither carried onto the delver nor named as " +
                    "something that shapes the run instead — decide which in BoutTests");
            }

            foreach (string one in carried)
            {
                Assert.That(seen.Contains(one), Is.True, "RunSetup lost " + one);
            }
        }

        /// <summary>A plan with nobody in it is a mistake worth hearing about.</summary>
        /// <remarks>
        /// Thrown rather than warned. Every other missing piece in this port has a sensible
        /// nothing to fall back on; a fight with no delver does not — there is no fight, and
        /// resolving one anyway would mean inventing the hero it was about.
        /// </remarks>
        [Test]
        public void AFightNeedsSomebodyToFightIt()
        {
            Assert.Throws<System.ArgumentNullException>(() => Bout.Fight(new FightPlan()));
            Assert.Throws<System.ArgumentNullException>(() => Bout.Fight(null));
            Assert.Throws<System.ArgumentNullException>(() => Bout.Delver(null));
        }

        private static FightPlan Plan(uint seed, int floor, int tier)
        {
            return new FightPlan
            {
                Seed = seed,
                Floor = floor,
                Tier = tier,
                Delver = Bout.Delver(RunSetup.ForLevel(5)),
            };
        }

        private static IReadOnlyList<CombatEvent> Fought(FightPlan plan)
        {
            return Bout.Fight(plan).Events;
        }
    }
}
