using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Core.Stats;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// What a screen draws for one event: the numbers flying off, and the two gauges.
    /// </summary>
    /// <remarks>
    /// The gauges are the interesting half. Each fills over exactly the time until its owner
    /// strikes again, so the bar arriving full and the blow landing are the same moment — and
    /// the way that goes wrong does not look like a timing bug. It looks like the game lying
    /// about whose turn it is.
    ///
    /// Which means the gauge has to look FORWARD through events that have not been shown yet,
    /// adding up the holds a delver will actually sit through. That is only possible because the
    /// fight is over before any of it is drawn.
    /// </remarks>
    [TestFixture]
    public class FightFrameTests
    {
        private static readonly IReadOnlyList<RelicId> NoRelics = new RelicId[0];
        private static readonly IReadOnlyList<StatModifier> NoMods = new StatModifier[0];

        private static CombatEvent At(int tick, CombatEventType type, string source = null,
            int depth = 0, int amount = 5, RelicId relic = RelicId.None, bool? crit = null)
        {
            var state = new CombatSnapshot(tick, 100, 50, 0, 50, 5, EnemyRank.Guard, 0, 25, 10,
                0, 0, 0, NoRelics, NoMods, default(CombatCounters));

            return new CombatEvent(type, depth, state, amount, source, relic, null, crit);
        }

        private static CombatEvent Swing(int tick)
        {
            return At(tick, CombatEventType.EnemyDamage, "you");
        }

        private static CombatEvent Blow(int tick)
        {
            return At(tick, CombatEventType.PlayerDamage);
        }

        private static Pacing Slow { get { return Pacing.For(10, false, 1, PacingRules.Shipped()); } }

        private static FightFrame Frame(IReadOnlyList<CombatEvent> events, int index)
        {
            return FightFrame.Of(events, index, Slow, null);
        }

        /* ---------- who is attacking ---------- */

        /// <summary>
        /// A relic's damage is not a blow, and a foe's thorns are not the foe attacking.
        /// </summary>
        /// <remarks>
        /// This is the whole reason the gauges work. A relic fires on the delver's turn and hurts
        /// the foe; counting it as a swing would reset the delver's gauge on every chain link, so
        /// the bar would tick backwards whenever a good loadout was doing its job.
        /// </remarks>
        [Test]
        public void OnlyASwingCountsAsAnAttack()
        {
            Assert.That(FightFrame.HeroStrikes(Swing(0)), Is.True);
            Assert.That(FightFrame.HeroStrikes(At(0, CombatEventType.EnemyMiss)), Is.True,
                "a swing that missed is still a swing");
            Assert.That(FightFrame.HeroStrikes(At(0, CombatEventType.EnemyDamage, "Thorn Vest")),
                Is.False, "a relic did that, not the delver");

            Assert.That(FightFrame.EnemyStrikes(Blow(0)), Is.True);
            Assert.That(FightFrame.EnemyStrikes(At(0, CombatEventType.Miss)), Is.True);
            Assert.That(FightFrame.EnemyStrikes(At(0, CombatEventType.PlayerDamage, depth: 1)),
                Is.False, "thorns answering a strike is not the foe taking a turn");
        }

        /* ---------- the gauges ---------- */

        /// <summary>A gauge fills over exactly the time until the next blow lands.</summary>
        [Test]
        public void AGaugeFillsUntilTheNextBlow()
        {
            var fight = new[] { Swing(0), At(1, CombatEventType.Gold, "Coin Magnet"), Swing(4) };

            Gauge hero = Frame(fight, 0).Hero;

            Assert.That(hero.Changed, Is.True);
            Assert.That(hero.Ms, Is.EqualTo(500 + 1500),
                "the hold to the gold line, then the hold to the next swing");
        }

        /// <summary>An event that says nothing about a gauge leaves it alone.</summary>
        /// <remarks>
        /// Most events are like this. A gauge that reset on every log line would be a stopwatch
        /// nobody could read.
        /// </remarks>
        [Test]
        public void AnEventThatIsNotAnAttackLeavesBothGaugesAlone()
        {
            var fight = new[] { Swing(0), At(1, CombatEventType.Heal, "Ox Heart"), Swing(4) };

            FightFrame frame = Frame(fight, 1);

            Assert.That(frame.Hero.Changed, Is.False);
            Assert.That(frame.Enemy.Changed, Is.False);
        }

        /// <summary>
        /// A unit that never attacks again keeps its cadence rather than freezing.
        /// </summary>
        /// <remarks>
        /// It dies first. A frozen bar would tell the delver so a second before the game did,
        /// which is not the screen's news to break.
        /// </remarks>
        [Test]
        public void ADoomedUnitKeepsWindingUp()
        {
            var fight = new[] { Swing(0), At(2, CombatEventType.Kill, "you") };

            Gauge hero = Frame(fight, 0).Hero;

            Assert.That(hero.Changed, Is.True);
            Assert.That(hero.Ms, Is.EqualTo(Gauge.KeepTheCadence));
        }

        /// <summary>
        /// A gauge stops looking at the next foe's entrance.
        /// </summary>
        /// <remarks>
        /// A bar that wound up across a change of opponent would be timing a blow against
        /// somebody who has not walked on yet, and would arrive full at a foe who was not there
        /// when it started filling.
        /// </remarks>
        [Test]
        public void AGaugeDoesNotWindUpPastTheNextFoe()
        {
            var fight = new[]
            {
                Swing(0), At(2, CombatEventType.Kill, "you"),
                At(3, CombatEventType.Enter), Swing(5),
            };

            Assert.That(Frame(fight, 0).Hero.Ms, Is.EqualTo(Gauge.KeepTheCadence),
                "the swing after the entrance does not count");
        }

        /// <summary>Meeting a foe sets both gauges going.</summary>
        [Test]
        public void MeetingAFoeStartsBothGauges()
        {
            var fight = new[] { At(0, CombatEventType.Enter), Swing(2), Blow(4) };

            FightFrame frame = Frame(fight, 0);

            Assert.That(frame.Hero.Changed, Is.True);
            Assert.That(frame.Enemy.Changed, Is.True);
            Assert.That(frame.Hero.Ms, Is.EqualTo(1000), "two ticks to the delver's swing");
            Assert.That(frame.Enemy.Ms, Is.EqualTo(2000), "four to the foe's");
        }

        /// <summary>
        /// A dash means somebody strikes before they have wound up at all.
        /// </summary>
        /// <remarks>
        /// The gauge starts full rather than starting to fill, because the blow is already
        /// landing. A bar that filled first would show a wind-up that did not happen.
        /// </remarks>
        [Test]
        public void ADashOpensWithNoWindUp()
        {
            var fight = new[]
            {
                At(0, CombatEventType.Enter), At(0, CombatEventType.DashOpen, "Battle Dash"),
                Swing(0), Blow(3),
            };

            Assert.That(Frame(fight, 0).Enemy.Ms, Is.Zero, "the foe is hit before it can act");
        }

        /* ---------- the flying numbers ---------- */

        [Test]
        public void ABlowPutsANumberOnWhoeverTookIt()
        {
            IReadOnlyList<Flier> onFoe = Frame(new[] { Swing(0) }, 0).Fliers;
            Assert.That(onFoe.Count, Is.EqualTo(1));
            Assert.That(onFoe[0].Where, Is.EqualTo(FlierAt.Enemy));
            Assert.That(onFoe[0].Kind, Is.EqualTo(FlierKind.Damage));
            Assert.That(onFoe[0].Text, Is.EqualTo("-5"));

            IReadOnlyList<Flier> onHero = Frame(new[] { Blow(0) }, 0).Fliers;
            Assert.That(onHero[0].Where, Is.EqualTo(FlierAt.Hero));
            Assert.That(onHero[0].Kind, Is.EqualTo(FlierKind.Hurt));
        }

        /// <summary>
        /// A critical is the Weighted Dice, and only when it doubled a swing.
        /// </summary>
        /// <remarks>
        /// The same relic doubling a chain link is not the delver landing a critical, and
        /// shouting CRIT over a relic's own damage would take the credit from the relic.
        /// </remarks>
        [Test]
        public void OnlyADoubledSwingIsACritical()
        {
            Flier swung = Frame(new[]
            {
                At(0, CombatEventType.EnemyDamage, "you", 0, 12, RelicId.WeightedDice),
            }, 0).Fliers[0];

            Assert.That(swung.Kind, Is.EqualTo(FlierKind.Crit));
            Assert.That(swung.Text, Is.EqualTo("CRIT! -12"));

            Flier chained = Frame(new[]
            {
                At(0, CombatEventType.EnemyDamage, "Weighted Dice", 2, 12, RelicId.WeightedDice),
            }, 0).Fliers[0];

            Assert.That(chained.Kind, Is.EqualTo(FlierKind.Chain), "a chain link is not a critical");
        }

        [Test]
        public void ARelicsDamageFliesAsAChain()
        {
            Flier flier = Frame(new[]
            {
                At(0, CombatEventType.EnemyDamage, "Ember Cask", 1),
            }, 0).Fliers[0];

            Assert.That(flier.Kind, Is.EqualTo(FlierKind.Chain),
                "eight relics firing should not look like the delver hitting eight times");
        }

        [Test]
        public void GoldFliesOffThePurse()
        {
            Flier flier = Frame(new[] { At(0, CombatEventType.Gold, "Coin Magnet", 0, 3) }, 0)
                .Fliers[0];

            Assert.That(flier.Where, Is.EqualTo(FlierAt.Gold));
            Assert.That(flier.Text, Is.EqualTo("+3"));
        }

        [Test]
        public void MostEventsThrowNoNumbers()
        {
            Assert.That(Frame(new[] { At(0, CombatEventType.Fizzle) }, 0).Fliers, Is.Empty);
            Assert.That(Frame(new[] { At(0, CombatEventType.Enter) }, 0).Fliers, Is.Empty);
            Assert.That(Frame(new[] { At(0, CombatEventType.Death) }, 0).Fliers, Is.Empty);
        }

        /* ---------- what it sounds like ---------- */

        /// <summary>
        /// Six of the twenty event kinds make a noise, and the rest are silent.
        /// </summary>
        /// <remarks>
        /// Silence is the interesting half. A chain of eight relics firing would otherwise be
        /// eight noises on top of one another, and a delver would learn nothing from any of them
        /// — so a relic's damage sounds like a blow because it IS one, and a relic's heal, luck
        /// signal and fizzle say nothing at all.
        /// </remarks>
        [Test]
        public void SixThingsMakeANoiseAndTheRestAreQuiet()
        {
            var heard = new Dictionary<CombatEventType, FightSound>
            {
                { CombatEventType.Gold, FightSound.Gold },
                { CombatEventType.PlayerDamage, FightSound.Hurt },
                { CombatEventType.EnemyDamage, FightSound.Hit },
                { CombatEventType.Heal, FightSound.Heal },
                { CombatEventType.Kill, FightSound.Kill },
                { CombatEventType.Death, FightSound.Death },
            };

            var noisy = 0;

            foreach (CombatEventType type in System.Enum.GetValues(typeof(CombatEventType)))
            {
                FightSound sound = FightFrame.SoundOf(At(0, type));

                if (heard.ContainsKey(type))
                {
                    Assert.That(sound, Is.EqualTo(heard[type]), type.ToString());
                    noisy++;
                }
                else
                {
                    Assert.That(sound, Is.EqualTo(FightSound.None),
                        type + " makes a noise nobody asked for");
                }
            }

            Assert.That(noisy, Is.EqualTo(6));
        }

        /// <summary>A relic's blow sounds like a blow, because it is one.</summary>
        [Test]
        public void ARelicsDamageSoundsLikeTheDelversOwn()
        {
            Assert.That(FightFrame.SoundOf(At(0, CombatEventType.EnemyDamage, "Ember Cask", 1)),
                Is.EqualTo(FightSound.Hit));
            Assert.That(FightFrame.SoundOf(Swing(0)), Is.EqualTo(FightSound.Hit));
        }

        [Test]
        public void TheFrameCarriesTheSound()
        {
            Assert.That(Frame(new[] { At(0, CombatEventType.Kill, "you") }, 0).Sound,
                Is.EqualTo(FightSound.Kill));
            Assert.That(Frame(new[] { At(0, CombatEventType.Fizzle) }, 0).Sound,
                Is.EqualTo(FightSound.None));
        }

        /* ---------- a real fight ---------- */

        /// <summary>
        /// Every event of a recorded fight makes a frame, and the gauges stay sane.
        /// </summary>
        /// <remarks>
        /// A gauge longer than the fight, or a negative one that is not the doomed-unit signal,
        /// would be a bar that never fills or one that fills backwards. Both are the kind of
        /// thing a screenshot cannot show and a delver notices immediately.
        /// </remarks>
        [Test]
        public void ARecordedFightDrawsFrameByFrame()
        {
            IReadOnlyList<CombatEvent> fight = ARecordedFight();
            Pacing pacing = Pacing.For(fight.Count, false, 1, PacingRules.Shipped());

            var touched = 0;
            var doomed = 0;

            for (int i = 0; i < fight.Count; i++)
            {
                FightFrame frame = FightFrame.Of(fight, i, pacing, null);

                foreach (Gauge gauge in new[] { frame.Hero, frame.Enemy })
                {
                    if (!gauge.Changed) continue;

                    touched++;
                    if (gauge.Ms == Gauge.KeepTheCadence) { doomed++; continue; }

                    Assert.That(gauge.Ms, Is.GreaterThanOrEqualTo(0), "a gauge that fills backwards");
                    Assert.That(gauge.Ms, Is.LessThan(120000), "a gauge that never fills");
                }

                foreach (Flier flier in frame.Fliers)
                {
                    Assert.That(flier.Text, Is.Not.Null.And.Not.Empty);
                }
            }

            Assert.That(touched, Is.GreaterThan(4), "the gauges never moved");
            Assert.That(doomed, Is.GreaterThan(0), "somebody dies in every fight");
        }

        /// <summary>A frame is the same however many times it is asked for.</summary>
        /// <remarks>
        /// The source scatters each flying number by a random offset, and that randomness stays
        /// in the view for exactly this reason: a view-model that rolled dice would make the same
        /// fight look different on replay.
        /// </remarks>
        [Test]
        public void TheSameEventAlwaysDrawsTheSame()
        {
            IReadOnlyList<CombatEvent> fight = ARecordedFight();
            Pacing pacing = Pacing.For(fight.Count, false, 1, PacingRules.Shipped());

            for (int i = 0; i < fight.Count; i++)
            {
                FightFrame once = FightFrame.Of(fight, i, pacing, null);
                FightFrame again = FightFrame.Of(fight, i, pacing, null);

                Assert.That(again.Hero.ToString(), Is.EqualTo(once.Hero.ToString()), "event " + i);
                Assert.That(again.Enemy.ToString(), Is.EqualTo(once.Enemy.ToString()), "event " + i);
                Assert.That(again.Fliers.Count, Is.EqualTo(once.Fliers.Count), "event " + i);

                for (int f = 0; f < once.Fliers.Count; f++)
                {
                    Assert.That(again.Fliers[f].ToString(), Is.EqualTo(once.Fliers[f].ToString()));
                }
            }
        }

        private static IReadOnlyList<CombatEvent> ARecordedFight()
        {
            List<CorpusFight.Case> cases = CorpusFight.Load("mixed.json");
            CorpusFight.Case busiest = null;

            for (int i = 0; i < cases.Count && i < 40; i++)
            {
                if (busiest == null || cases[i].Events.Count > busiest.Events.Count) busiest = cases[i];
            }

            var engine = new CombatEngine(CombatRules.DelveAsRecorded());
            return engine.ResolveFloor(busiest.Hero, busiest.Pack,
                new Core.Determinism.Mulberry32(busiest.FightSeed)).Events;
        }
    }
}
