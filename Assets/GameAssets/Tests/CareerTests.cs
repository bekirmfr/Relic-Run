using System;
using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Meta;
using RelicRun.Core.Run;

namespace RelicRun.Tests
{
    /// <summary>
    /// A delver across many runs, which is the only thing that exercises the SEAMS.
    /// </summary>
    /// <remarks>
    /// Every piece here is gated exactly by a corpus of its own. What no corpus covers is the
    /// joins between them: that a run's setup comes from the level the last run's experience
    /// bought, that scoring reads the frontier before banking moves it, that a hall opens once
    /// and stays open. Those are properties of a career rather than of a run, so this plays one
    /// and asserts what must be true throughout.
    ///
    /// It asserts INVARIANTS rather than numbers. A test that pinned the exact gold after
    /// forty runs would fail on any balance change and teach nobody anything; one that says the
    /// wallet never falls of its own accord keeps meaning what it says.
    /// </remarks>
    [TestFixture]
    public class CareerTests
    {
        /// <summary>Plays whatever it is offered, and never pays for a reroll or a revive.</summary>
        private sealed class Plainly : IRunChoices
        {
            public int Event(RunState run, DungeonEvent ev) { return ev.Choices.Count - 1; }

            public bool Reroll(RunState run, IReadOnlyList<RelicId> offer, int price) { return false; }

            public RelicId Draft(RunState run, IReadOnlyList<RelicId> offer) { return offer[0]; }

            public BazaarDeal Bazaar(RunState run, IReadOnlyList<RelicId> offer,
                IReadOnlyList<int> awakenable)
            {
                if (awakenable.Count > 0 && run.Gold >= DelveRun.PriceOf(run, DelveRun.AwakenPrice))
                {
                    return BazaarDeal.Awaken(awakenable[0]);
                }

                return offer.Count > 0 && run.Gold >= DelveRun.PriceOf(run, DelveRun.BuyPrice)
                    ? BazaarDeal.Buy(offer[0])
                    : BazaarDeal.Walk;
            }

            public bool Revive(RunState run, int floor) { return false; }

            public bool CashOut(RunState run, int floor) { return false; }
        }

        /// <summary>Watches for the ending, which the run reports and the save needs.</summary>
        private sealed class Ending : IRunObserver
        {
            public RunEnding How = RunEnding.Died;

            public void Event(RunState run, int floor, int index, int choice) { }

            public void Deal(RunState run, int floor, IReadOnlyList<RelicId> offer,
                IReadOnlyList<int> awakenable, BazaarDeal deal)
            { }

            public void Draft(RunState run, int floor, IReadOnlyList<RelicId> offer,
                int rerolls, RelicId pick)
            { }

            public void Fight(RunState run, int floor, IReadOnlyList<EnemyState> pack,
                CombatResult result)
            { }

            public void Revived(RunState run, int floor) { }

            public void End(RunState run, RunEnding ending, int floor) { How = ending; }
        }

        [Test]
        public void ACareerHoldsTogetherAcrossManyRuns()
        {
            var save = new SaveState();
            var problems = new List<string>();

            int lastLevel = save.Level;
            int lastUnlocked = save.Unlocked;
            int lastWallet = save.Gold;
            int lastLifetime = save.GoldLife;
            int lastBest = save.Best;
            var lastEarned = new List<string>(Achievements.EarnedBy(save));

            for (uint run = 1; run <= 60; run++)
            {
                // Always the deepest hall open, which is what makes the frontier move.
                int tier = Career.Frontier(save);

                if (!Career.CanDelve(save, tier))
                {
                    problems.Add("run " + run + ": the frontier hall is not delvable");
                    break;
                }

                RunSetup setup = Career.SetupFor(save, tier);
                var watcher = new Ending();
                RunState state = DelveRun.Resolve(run * 7919u, setup, new Plainly(), watcher);

                Settled settled = Career.Settle(save, state, watcher.How, tier,
                    daily: false, day: 0, name: "you", stamp: run);

                string wrong =
                    Never(run, "the wallet fell", save.Gold < lastWallet) ??
                    Never(run, "the lifetime purse fell", save.GoldLife < lastLifetime) ??
                    Never(run, "the best score fell", save.Best < lastBest) ??
                    Never(run, "the level fell", save.Level < lastLevel) ??
                    Never(run, "a hall closed", save.Unlocked < lastUnlocked) ??
                    Never(run, "the frontier jumped more than one hall",
                        save.Unlocked > lastUnlocked + 1) ??
                    Never(run, "the run was not counted", save.Runs != (int)run) ??
                    Never(run, "the board outgrew ten", save.Scores.Count > RunBanking.BoardSize) ??
                    BoardIsSorted(run, save) ??
                    Never(run, "a new best was reported without the best moving",
                        settled.Banked.NewBest && save.Best != settled.Reward.Score) ??
                    Never(run, "the level moved without the run saying so",
                        save.Level != lastLevel + settled.Banked.LevelsCrossed.Count) ??
                    Never(run, "an achievement was un-earned",
                        Career.NewlyEarned(Achievements.EarnedBy(save), save).Count > 0 &&
                        Achievements.EarnedBy(save).Count < lastEarned.Count);

                if (wrong != null) { problems.Add(wrong); break; }

                lastLevel = save.Level;
                lastUnlocked = save.Unlocked;
                lastWallet = save.Gold;
                lastLifetime = save.GoldLife;
                lastBest = save.Best;
                lastEarned = Achievements.EarnedBy(save);
            }

            Assert.That(problems, Is.Empty, string.Join("\n  ", problems));

            // And the career actually went somewhere, or the invariants above held vacuously.
            // These are the weakest statements that still mean the seams were crossed: a level
            // was bought, a hall was cleared and opened the next, and the board had to drop a
            // row. Exact numbers would be brittle and would teach nobody anything.
            Assert.That(save.Runs, Is.EqualTo(60));
            Assert.That(save.Level, Is.GreaterThan(1), "sixty runs bought no levels at all");
            Assert.That(save.Unlocked, Is.GreaterThan(1), "the frontier never moved");
            Assert.That(save.Scores.Count, Is.EqualTo(RunBanking.BoardSize),
                "the board never filled, so it was never asked to drop a row");
            Assert.That(Achievements.EarnedBy(save), Is.Not.Empty, "nothing was ever earned");
        }

        private static string Never(uint run, string what, bool happened)
        {
            return happened ? "run " + run + ": " + what : null;
        }

        private static string BoardIsSorted(uint run, SaveState save)
        {
            for (int i = 1; i < save.Scores.Count; i++)
            {
                if (save.Scores[i - 1].Score < save.Scores[i].Score)
                {
                    return "run " + run + ": the board is out of order at " + i;
                }
            }

            return null;
        }

        /// <summary>
        /// A run is scored against the frontier it STARTED from, not the one it just moved.
        /// </summary>
        /// <remarks>
        /// Experience is scaled by how relevant the hall still is: full rate at the frontier
        /// itself, four fifths one hall back. Clearing the frontier opens the next one — so
        /// banking before scoring would measure the run against the hall it had just unlocked
        /// and quietly pay a fifth less for the best run a delver has ever had.
        /// </remarks>
        [Test]
        public void ClearingTheFrontierIsScoredAtTheFrontiersRate()
        {
            var save = new SaveState { Unlocked = 2 };
            Settled settled = Career.Settle(save, Delved(tier: 2), RunEnding.Cleared, 2,
                daily: false, day: 0, name: "you", stamp: 0);

            Assert.That(save.Unlocked, Is.EqualTo(3), "the clear opened the next hall");
            Assert.That(settled.Reward.Xp, Is.EqualTo(settled.Reward.Score),
                "a run at the frontier pays experience at the full rate, and this one was at it " +
                "when it started");
        }

        /// <summary>
        /// A hall opens only by being cleared, and the frontier is what bounds the next run.
        /// </summary>
        /// <remarks>
        /// The seam this guards is the one a screen would get wrong: the tier a delver may
        /// choose is bounded by what they have unlocked, and clearing the frontier is the only
        /// thing that moves it. Clearing a hall already behind them must not.
        /// </remarks>
        [Test]
        public void OnlyClearingTheFrontierOpensTheNextHall()
        {
            var save = new SaveState { Unlocked = 3 };

            Career.Settle(save, Delved(tier: 1), RunEnding.Cleared, 1, false, 0, "you", 0);
            Assert.That(save.Unlocked, Is.EqualTo(3), "clearing a hall long behind opens nothing");

            Career.Settle(save, Delved(tier: 3), RunEnding.CashedOut, 3, false, 0, "you", 0);
            Assert.That(save.Unlocked, Is.EqualTo(3), "walking out of the frontier opens nothing");

            Career.Settle(save, Delved(tier: 3), RunEnding.Cleared, 3, false, 0, "you", 0);
            Assert.That(save.Unlocked, Is.EqualTo(4), "clearing the frontier opens the next");

            Assert.That(Career.CanDelve(save, 4), Is.True);
            Assert.That(Career.CanDelve(save, 5), Is.False);
        }

        /// <summary>
        /// The setup a delve starts from carries both the delver and the hall.
        /// </summary>
        /// <remarks>
        /// Two separate facts arrive here and a screen would have to know both: the shape of the
        /// hero comes from the LEVEL, and the foes come from the HALL. Dropping either leaves a
        /// run that still plays, which is exactly why it needs saying out loud.
        /// </remarks>
        [Test]
        public void ARunsSetupCarriesTheDelverAndTheHall()
        {
            var novice = new SaveState { Unlocked = 5 };
            var veteran = new SaveState { Unlocked = 5, Xp = XpForLevel(15) };

            RunSetup first = Career.SetupFor(novice, 1);
            RunSetup deep = Career.SetupFor(veteran, 5);

            Assert.That(first.Hp, Is.EqualTo(RunSetup.ForLevel(1).Hp));
            Assert.That(deep.Hp, Is.EqualTo(RunSetup.ForLevel(15).Hp));
            Assert.That(deep.Hp, Is.GreaterThan(first.Hp), "a level is worth something");
            Assert.That(deep.BazaarDeals, Is.GreaterThan(first.BazaarDeals),
                "and level fifteen buys a second bazaar deal");

            Assert.That(first.Dungeon, Is.Not.Null, "the first hall is still a hall");
            Assert.That(first.Dungeon.Multiplier,
                Is.EqualTo(DungeonCatalog.Get(1).Multiplier));
            Assert.That(deep.Dungeon.Multiplier,
                Is.EqualTo(DungeonCatalog.Get(5).Multiplier));
            Assert.That(deep.Dungeon.BossRelics, Is.Not.Empty,
                "the fifth hall's bosses carry its kit");
        }

        /// <summary>The frontier never runs past the halls that exist.</summary>
        /// <remarks>
        /// A save can hold an Unlocked deeper than the game has halls — clearing the last one
        /// writes it, and so would a save from a build with more of them. Reading it back
        /// straight would index off the end of the table.
        /// </remarks>
        [Test]
        public void TheFrontierStopsAtTheLastHall()
        {
            int halls = DungeonCatalog.All.Count;

            Assert.That(Career.Frontier(new SaveState { Unlocked = 0 }), Is.EqualTo(1));
            Assert.That(Career.Frontier(new SaveState { Unlocked = halls }), Is.EqualTo(halls));
            Assert.That(Career.Frontier(new SaveState { Unlocked = halls + 4 }), Is.EqualTo(halls),
                "clearing the last hall writes a frontier past the end of the table");

            Assert.That(Career.CanDelve(new SaveState { Unlocked = halls + 4 }, halls + 1),
                Is.False, "and no hall past the end can be entered");
        }

        /// <summary>A delve is scored on its kills; a duel is not.</summary>
        /// <remarks>
        /// The two modes go through the same scoring, and which one is being settled is an
        /// argument rather than something the state carries — so it is worth one test that the
        /// argument is the right way round.
        /// </remarks>
        [Test]
        public void ADelveIsScoredOnItsKills()
        {
            var save = new SaveState();
            RunState quiet = Delved(1);
            RunState bloody = Delved(1);
            bloody.Hero.Kills = 30;

            int quietScore = Career.Settle(save, quiet, RunEnding.Cleared, 1, false, 0, "you", 0)
                .Reward.Score;
            int bloodyScore = Career.Settle(new SaveState(), bloody, RunEnding.Cleared, 1, false, 0, "you", 0)
                .Reward.Score;

            Assert.That(bloodyScore, Is.GreaterThan(quietScore),
                "thirty kills are worth something in a delve, and would be worth nothing in a duel");
        }

        /// <summary>A match is scored against the hall it was hosted in.</summary>
        [Test]
        public void AMatchIsScoredAgainstItsOwnHall()
        {
            int shallow = Career.Settle(new SaveState { Unlocked = 9 }, Match(hall: 1),
                RunEnding.Cleared, "you", 0).Reward.Score;

            int deep = Career.Settle(new SaveState { Unlocked = 9 }, Match(hall: 9),
                RunEnding.Cleared, "you", 0).Reward.Score;

            Assert.That(deep, Is.GreaterThan(shallow),
                "the ninth hall multiplies a score by more than twice what the first does");
        }

        /// <summary>
        /// A match's own hand is what scoring asks about, awakenings included.
        /// </summary>
        /// <remarks>
        /// Only a cash-out reads it, to give a Piggy Bank its cut, and versus has no cash-out
        /// today. It is written down anyway because the two modes share one scoring routine and
        /// the source's does not check the mode either: whatever gains a cash-out later inherits
        /// a Piggy Bank that already works.
        /// </remarks>
        [Test]
        public void AMatchIsScoredHoldingWhatItWasCarrying()
        {
            VersusMatch bare = Match(hall: 1);
            bare.Gold = 400;

            VersusMatch piggy = Match(hall: 1);
            piggy.Gold = 400;
            piggy.Items.Add(RelicId.PiggyBank);

            VersusMatch woken = Match(hall: 1);
            woken.Gold = 400;
            woken.Items.Add(RelicId.PiggyBank);
            woken.AwakenedSlots.Add(0);

            Assert.That(Banked(bare), Is.EqualTo(400));
            Assert.That(Banked(piggy), Is.EqualTo(500), "a quarter more");
            Assert.That(Banked(woken), Is.EqualTo(600), "and half again once woken");
        }

        private static int Banked(VersusMatch match)
        {
            return Career.Settle(new SaveState(), match, RunEnding.CashedOut, "you", 0)
                .Reward.Banked;
        }

        private static VersusMatch Match(int hall)
        {
            var match = new VersusMatch { Hall = hall, Round = 8 };
            match.Hero.Kills = 4;
            return match;
        }

        /// <summary>Only what was not earned before is newly earned.</summary>
        [Test]
        public void OnlyWhatWasNotEarnedBeforeIsNew()
        {
            var save = new SaveState();
            List<string> before = Achievements.EarnedBy(save);
            Assert.That(before, Is.Empty, "an empty save has earned nothing");

            save.Runs = 1;
            List<string> fresh = Career.NewlyEarned(before, save);
            Assert.That(fresh, Is.EqualTo(new[] { "firstblood" }));

            Assert.That(Career.NewlyEarned(Achievements.EarnedBy(save), save), Is.Empty,
                "asking twice in a row announces nothing, which is the whole point");

            save.Clears = 1;
            Assert.That(Career.NewlyEarned(Achievements.EarnedBy(save), save), Is.Empty);
        }

        private static RunState Delved(int tier)
        {
            var run = new RunState { Floor = DelveRun.MaxFloor };
            run.Gold = 100;
            return run;
        }

        /// <summary>
        /// A Daily is one attempt a day, and it is spent on the way in.
        /// </summary>
        [Test]
        public void ADailyIsSpentOnTheWayIn()
        {
            uint day = DailySeed.For(new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc));

            // Stated as the levels themselves rather than in terms of the constants, which is
            // the only way a test can notice one of them moving. Both mirror the progression
            // table, where level three unlocks the Daily and level five unlocks Versus.
            Assert.That(Career.DailyOpensAt, Is.EqualTo(3));

            Assert.That(Career.DailyIsOpen(new SaveState(), day), Is.False, "not at level one");
            Assert.That(Career.DailyIsOpen(new SaveState { Xp = XpForLevel(2) }, day), Is.False,
                "and not at two either");

            var save = new SaveState { Xp = XpForLevel(3) };
            Assert.That(Career.DailyIsOpen(save, day), Is.True);

            Career.StartDaily(save, day);
            Assert.That(Career.DailyIsOpen(save, day), Is.False, "one attempt, spent");
            Assert.That(Career.DailyIsOpen(save, day + 1), Is.True, "tomorrow is a different run");
        }

        [Test]
        public void VersusOpensAtLevelFive()
        {
            Assert.That(Career.VersusOpensAt, Is.EqualTo(5));

            Assert.That(Career.VersusIsOpen(new SaveState()), Is.False);
            Assert.That(Career.VersusIsOpen(new SaveState { Xp = XpForLevel(4) }), Is.False,
                "four is still shut");

            var ready = new SaveState { Xp = XpForLevel(5) };
            Assert.That(Career.VersusIsOpen(ready), Is.True);

            // And a lobby is sized to the delver, which is the seam worth checking.
            VersusLobby lobby = Career.LobbyFor(ready, 0xC0FFEEu);
            Assert.That(lobby.HeroPool, Is.EqualTo(VersusRun.Make(0xC0FFEEu, ready.Level).HeroPool));
            Assert.That(lobby.Rivals, Is.Not.Empty);
        }

        private static int XpForLevel(int level)
        {
            int xp = 0;
            for (int l = 2; l <= level; l++) xp += Progression.Need(l);
            return xp;
        }
    }
}
