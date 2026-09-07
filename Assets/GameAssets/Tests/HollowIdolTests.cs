using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Run;

namespace RelicRun.Tests
{
    /// <summary>
    /// What an awakened Hollow Idol does to a FIGHT.
    /// </summary>
    /// <remarks>
    /// The corpus can only ever pin the source's answer, which was that an awakened copy counted
    /// twice toward every set. The shipped rule is different — it backs the one family the
    /// delver leans on, by three — so it has to be asked directly.
    ///
    /// The Flesh set is what these read, because it announces itself: reaching three of a kind
    /// thickens the hero by three max HP and says so in the log, so whether a set tier was
    /// reached is visible in the event stream rather than inferred from a total.
    /// </remarks>
    [TestFixture]
    public class HollowIdolTests
    {
        private const string FleshSet = "Flesh set";

        /// <summary>Whether the fight granted the Flesh set's three max HP.</summary>
        private static bool ReachedTheFleshSet(IReadOnlyList<RelicId> items, bool awake,
            CombatRules rules)
        {
            var hero = new HeroState
            {
                Items = items,
                Awakened = awake ? new List<RelicId> { RelicId.HollowIdol } : new List<RelicId>(),
                Php = 400,
                Pmax = 400,
                BaseAtk = 40,
            };

            List<EnemyState> pack = EnemyPackGenerator.Build(1, new Mulberry32(11), null);
            CombatResult result = new CombatEngine(rules).ResolveFloor(hero, pack, new Mulberry32(23));

            foreach (CombatEvent e in result.Events)
            {
                if (e.Source != null && e.Source.StartsWith(FleshSet)) return true;
            }

            return false;
        }

        /// <summary>
        /// One Flesh relic and an Idol is two of a kind — until the Idol is woken.
        /// </summary>
        /// <remarks>
        /// With nothing else in hand the Idol backs Flesh, because a tie goes to the family that
        /// comes first and Flesh comes before Curse. One relic, plus the Idol's own count, plus
        /// the three it throws behind them, is five — and the tier opens.
        /// </remarks>
        [Test]
        public void AWokenIdolCanOpenASetTheHandCouldNotReach()
        {
            var hand = new List<RelicId> { RelicId.OxHeart, RelicId.HollowIdol };

            Assert.That(ReachedTheFleshSet(hand, awake: false, CombatRules.Delve()), Is.False,
                "one Flesh relic and an Idol is two of a kind, which reaches nothing");

            Assert.That(ReachedTheFleshSet(hand, awake: true, CombatRules.Delve()), Is.True,
                "woken, the Idol backs Flesh with three more and the tier opens");
        }

        /// <summary>
        /// It backs ONE family — the one the delver leans on — and not every set at once.
        /// </summary>
        /// <remarks>
        /// Two Iron Skins make Guard the family in hand, so that is what a woken Idol backs. The
        /// lone Ox Heart is left where it was: one Flesh relic and the Idol's own count, which
        /// is two, and no tier. Under the source's answer the Idol would have counted twice
        /// toward everything and quietly opened the Flesh set as well.
        /// </remarks>
        [Test]
        public void AWokenIdolBacksOneFamilyRatherThanAllOfThem()
        {
            var hand = new List<RelicId>
            {
                RelicId.OxHeart, RelicId.IronSkin, RelicId.IronSkin, RelicId.HollowIdol,
            };

            Assert.That(ReachedTheFleshSet(hand, awake: true, CombatRules.Delve()), Is.False,
                "the Idol went behind Guard, so Flesh is still two of a kind");

            Assert.That(ReachedTheFleshSet(hand, awake: true, CombatRules.DelveAsRecorded()), Is.True,
                "the source's answer counted it twice toward every set, Flesh included");
        }

        /// <summary>A sleeping Idol backs nothing; it only counts itself, as it always has.</summary>
        [Test]
        public void ASleepingIdolBacksNothing()
        {
            var hand = new List<RelicId>
            {
                RelicId.OxHeart, RelicId.IronSkin, RelicId.IronSkin, RelicId.HollowIdol,
            };

            Assert.That(ReachedTheFleshSet(hand, awake: false, CombatRules.Delve()), Is.False);
            Assert.That(ReachedTheFleshSet(hand, awake: false, CombatRules.DelveAsRecorded()), Is.False);

            // And the one-relic hand stays out of reach while the Idol sleeps, which is what
            // says the tier above was opened by the waking rather than by the Idol being there.
            var alone = new List<RelicId> { RelicId.OxHeart, RelicId.HollowIdol };
            Assert.That(ReachedTheFleshSet(alone, awake: false, CombatRules.Delve()), Is.False);
        }
    }
}
