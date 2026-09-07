using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Run;

namespace RelicRun.Tests
{
    /// <summary>
    /// The revive, which is the one part of the run loop with no corpus behind it.
    /// </summary>
    /// <remarks>
    /// The source's revive lives in a React method: it spends sparks or plays an advertisement,
    /// animates, and calls back into the fight pipeline. None of that lifts, and the Balance
    /// Lab's headless loop has no notion of being brought back — so there is nothing to record.
    ///
    /// What CAN be checked is what the rule says, and these do: half a pool rounded down and
    /// never less than one, once per run, and a resumed fight that starts against the foe that
    /// felled the hero with its wounds intact rather than starting the floor again.
    /// </remarks>
    [TestFixture]
    public class RunReviveTests
    {
        /// <summary>Answers every question the same way, so one behaviour can be isolated.</summary>
        private sealed class Fixed : IRunChoices
        {
            public bool AllowRevive = true;
            public int Revives;

            public int Event(RunState run, DungeonEvent ev) { return ev.Choices.Count - 1; }

            public bool Reroll(RunState run, IReadOnlyList<RelicId> offer, int price) { return false; }

            public RelicId Draft(RunState run, IReadOnlyList<RelicId> offer) { return offer[0]; }

            public BazaarDeal Bazaar(RunState run, IReadOnlyList<RelicId> offer) { return BazaarDeal.Walk; }

            public bool Revive(RunState run, int floor)
            {
                Revives++;
                return AllowRevive;
            }
        }

        private sealed class Watcher : IRunObserver
        {
            public int Revived;
            public readonly List<int> HealthOnRevive = new List<int>();
            public readonly List<int> PoolOnRevive = new List<int>();

            public void Event(RunState run, int floor, int index, int choice) { }

            public void Bazaar(RunState run, int floor, IReadOnlyList<RelicId> offer, BazaarDeal deal) { }

            public void Draft(RunState run, int floor, IReadOnlyList<RelicId> offer, int rerolls, RelicId pick) { }

            public void Fight(RunState run, int floor, IReadOnlyList<EnemyState> pack, CombatResult result) { }

            void IRunObserver.Revived(RunState run, int floor)
            {
                Revived++;
                HealthOnRevive.Add(run.Php);
                PoolOnRevive.Add(run.Pmax);
            }

            public bool Dead;
            public int EndFloor;

            public void End(RunState run, bool dead, int floor)
            {
                Dead = dead;
                EndFloor = floor;
            }
        }

        /// <summary>A frail hero over many seeds, so plenty of runs actually reach a death.</summary>
        private static readonly RunSetup Frail = new RunSetup { Hp = 30, Breath = 0, Atk = 1 };

        [Test]
        public void ReviveRestoresHalfThePoolAndNeverLessThanOne()
        {
            var watcher = new Watcher();
            var choices = new Fixed();

            for (uint seed = 1; seed <= 60; seed++)
            {
                DelveRun.Resolve(seed, Frail, choices, watcher);
            }

            Assert.That(watcher.Revived, Is.GreaterThan(0), "no run died, so nothing was revived");

            for (int i = 0; i < watcher.Revived; i++)
            {
                int pool = watcher.PoolOnRevive[i];
                int want = System.Math.Max(1, (int)System.Math.Floor(pool / 2.0));
                Assert.That(watcher.HealthOnRevive[i], Is.EqualTo(want),
                    "revived on a pool of " + pool);
            }
        }

        [Test]
        public void ReviveIsOfferedOncePerRun()
        {
            for (uint seed = 1; seed <= 60; seed++)
            {
                var watcher = new Watcher();
                var choices = new Fixed();
                RunState run = DelveRun.Resolve(seed, Frail, choices, watcher);

                Assert.That(watcher.Revived, Is.LessThanOrEqualTo(1), "seed " + seed);
                Assert.That(choices.Revives, Is.LessThanOrEqualTo(1),
                    "seed " + seed + ": asked to revive more than once");
                Assert.That(run.Revived, Is.EqualTo(watcher.Revived == 1), "seed " + seed);
            }
        }

        /// <summary>Taking the revive can only ever carry a run further than refusing it.</summary>
        [Test]
        public void TakingAReviveNeverEndsTheRunSooner()
        {
            int compared = 0;

            for (uint seed = 1; seed <= 60; seed++)
            {
                var refused = new Watcher();
                var taken = new Watcher();

                DelveRun.Resolve(seed, Frail, new Fixed { AllowRevive = false }, refused);
                DelveRun.Resolve(seed, Frail, new Fixed { AllowRevive = true }, taken);

                Assert.That(refused.Revived, Is.Zero, "seed " + seed + ": revived after refusing");
                if (taken.Revived == 0) continue;

                compared++;
                Assert.That(refused.Dead, Is.True,
                    "seed " + seed + ": one run was offered a revive and the other did not die");

                // Cleared runs report floor 0, so a clear beats any death outright.
                bool further = !taken.Dead || taken.EndFloor >= refused.EndFloor;
                Assert.That(further, Is.True,
                    "seed " + seed + ": revived and still ended on floor " + taken.EndFloor +
                    ", before floor " + refused.EndFloor);
            }

            Assert.That(compared, Is.GreaterThan(0), "no run was ever revived, so nothing was compared");
        }

        /// <summary>
        /// The floor is not restarted. A revived hero meets the foe that felled them, still
        /// carrying its wounds, so the fight resumed is shorter than the fight begun again.
        /// </summary>
        [Test]
        public void ReviveResumesTheFightRatherThanRestartingTheFloor()
        {
            // Floor 5's pack, fought to the point where the second foe is half dead.
            var rng = new Mulberry32(12345);
            List<EnemyState> pack = EnemyPackGenerator.Build(5, rng, null);
            Assert.That(pack.Count, Is.GreaterThan(1), "need a pack with something behind the first foe");

            int fullHp = pack[1].Hp;
            var resumed = new List<EnemyState>();
            for (int i = 1; i < pack.Count; i++) resumed.Add(pack[i]);
            resumed[0].Hp = System.Math.Max(1, fullHp / 4);

            // A weak hero, so a wounded foe and a whole one take a visibly different number
            // of strikes rather than both falling to the first blow.
            var hero = new HeroState { Php = 400, Pmax = 400, BaseAtk = 2 };
            CombatResult result = new CombatEngine().ResolveFloor(hero, resumed, new Mulberry32(7));

            int firstKill = 0;
            for (int i = 0; i < result.Events.Count; i++)
            {
                if (result.Events[i].Type == CombatEventType.Kill) { firstKill = i; break; }
            }

            Assert.That(firstKill, Is.GreaterThan(0), "the resumed foe was never killed");

            var fresh = new HeroState { Php = 400, Pmax = 400, BaseAtk = 2 };
            List<EnemyState> whole = EnemyPackGenerator.Build(5, new Mulberry32(12345), null);
            var fromScratch = new List<EnemyState>();
            for (int i = 1; i < whole.Count; i++) fromScratch.Add(whole[i]);
            CombatResult full = new CombatEngine().ResolveFloor(fresh, fromScratch, new Mulberry32(7));

            int freshKill = 0;
            for (int i = 0; i < full.Events.Count; i++)
            {
                if (full.Events[i].Type == CombatEventType.Kill) { freshKill = i; break; }
            }

            Assert.That(firstKill, Is.LessThan(freshKill),
                "a wounded foe should fall sooner than a whole one");
        }
    }
}
