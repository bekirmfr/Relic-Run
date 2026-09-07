using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Run;

namespace RelicRun.Core.Meta
{
    /// <summary>What settling a finished run produced.</summary>
    public struct Settled
    {
        /// <summary>What the run was worth: banked gold, score, experience, stars.</summary>
        public RunReward Reward;

        /// <summary>What went into the save, and what is worth telling the delver about.</summary>
        public Banked Banked;
    }

    /// <summary>
    /// One delver, across runs.
    /// </summary>
    /// <remarks>
    /// Every piece of a career was already ported and gated — the progression table, the run
    /// loop, the lobby, the scoring, the save — and nothing joined them up. A screen that wanted
    /// to start a delve had to know that the setup comes from the level, that the hall comes
    /// from the tier, that the tier is bounded by what is unlocked, and that finishing means
    /// scoring THEN banking, in that order. Four screens would have known it four times.
    ///
    /// This is the joining up, and nothing more: it starts nothing and plays nothing. The run
    /// loop is <see cref="DelveRun.Resolve"/> and it asks a player questions through an
    /// interface, so how a UI drives it — a worker, a coroutine, a state machine — is a question
    /// for whoever builds the UI, and deliberately not answered here.
    /// </remarks>
    public static class Career
    {
        /// <summary>The level that opens the Daily Delve.</summary>
        public const int DailyOpensAt = 3;

        /// <summary>The level that opens Versus.</summary>
        public const int VersusOpensAt = 5;

        /// <summary>Whether this hall can be delved: it has to have been unlocked.</summary>
        public static bool CanDelve(SaveState save, int tier)
        {
            return tier >= 1 && tier <= save.Unlocked && tier <= DungeonCatalog.All.Count;
        }

        /// <summary>The deepest hall this delver may enter.</summary>
        public static int Frontier(SaveState save)
        {
            return Math.Max(1, Math.Min(save.Unlocked, DungeonCatalog.All.Count));
        }

        /// <summary>
        /// Whether today's Daily is still available: open at level three, and once a day.
        /// </summary>
        public static bool DailyIsOpen(SaveState save, uint day)
        {
            return save.Level >= DailyOpensAt && !save.DailyDone.Contains(day);
        }

        public static bool VersusIsOpen(SaveState save)
        {
            return save.Level >= VersusOpensAt;
        }

        /// <summary>
        /// Spends the day's Daily. Called when the run STARTS, not when it ends.
        /// </summary>
        /// <remarks>
        /// A Daily is one attempt, and abandoning it does not buy another — so the day is
        /// spent on the way in. The best score for the day is kept separately, and only a run
        /// that finishes writes one.
        /// </remarks>
        public static void StartDaily(SaveState save, uint day)
        {
            save.DailyDone.Add(day);
        }

        /// <summary>
        /// The run this delver starts in this hall: their level's shape, and the hall's foes.
        /// </summary>
        public static RunSetup SetupFor(SaveState save, int tier)
        {
            RunSetup setup = RunSetup.ForLevel(save.Level);
            setup.Dungeon = DungeonConfig.ForTier(tier);
            return setup;
        }

        /// <summary>The lobby this delver walks into, sized to their level.</summary>
        public static VersusLobby LobbyFor(SaveState save, uint seed)
        {
            return VersusRun.Make(seed, save.Level);
        }

        /// <summary>
        /// Scores a finished delve and banks it, in that order.
        /// </summary>
        /// <remarks>
        /// The order is not incidental. Scoring reads the save to decide how relevant the hall
        /// still is, and banking then moves the frontier — so banking first would score the run
        /// against the hall it just unlocked.
        /// </remarks>
        public static Settled Settle(SaveState save, RunState run, RunEnding ending, int tier,
            bool daily, uint day, string name, long stamp)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (run == null) throw new ArgumentNullException(nameof(run));

            RunReward reward = RunOutcome.Score(ending, false, run.Gold, run.Floor,
                run.Hero.Kills, run, tier, Frontier(save));

            Banked banked = RunBanking.Bank(save, new FinishedRun
            {
                Ending = ending,
                Versus = false,
                Daily = daily,
                Seed = day,
                Tier = tier,
                Floor = run.Floor,
                Kills = run.Hero.Kills,
                Relics = run.Items.Count,
                Name = name,
                Stamp = stamp,
            }, reward);

            return new Settled { Reward = reward, Banked = banked };
        }

        /// <summary>
        /// Scores a finished match and banks it. A crown is a clear; anything else is a death.
        /// </summary>
        /// <remarks>
        /// A duel is scored on depth and the crown alone — no kills, and no cash-out to take a
        /// Piggy Bank's cut of — but it still needs a hand to ask about one, so the match's is
        /// handed over. The hall a lobby is hosted in scales the score exactly as a delve's does.
        /// </remarks>
        public static Settled Settle(SaveState save, VersusMatch match, RunEnding ending,
            string name, long stamp)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (match == null) throw new ArgumentNullException(nameof(match));

            RunReward reward = RunOutcome.Score(ending, true, match.Gold, match.Round,
                match.Hero.Kills, HandOf(match), match.Hall, Frontier(save));

            Banked banked = RunBanking.Bank(save, new FinishedRun
            {
                Ending = ending,
                Versus = true,
                Tier = match.Hall,
                Floor = match.Round,
                Kills = match.Hero.Kills,
                Relics = match.Items.Count,
                Name = name,
                Stamp = stamp,
            }, reward);

            return new Settled { Reward = reward, Banked = banked };
        }

        /// <summary>
        /// A match's hand, as a run would hold it, for the one question scoring asks of it.
        /// </summary>
        private static RunState HandOf(VersusMatch match)
        {
            var hand = new RunState { Rules = match.Rules };
            hand.Items.AddRange(match.Items);

            foreach (int slot in match.AwakenedSlots) hand.Awaken(slot);
            return hand;
        }

        /// <summary>Which achievements this save has earned that it had not before.</summary>
        /// <remarks>
        /// Achievements are questions about the save rather than events, so nothing announces
        /// one. Asking either side of a run is how a screen finds out what to celebrate.
        /// </remarks>
        public static List<string> NewlyEarned(IReadOnlyList<string> before, SaveState save)
        {
            List<string> now = Achievements.EarnedBy(save);
            var fresh = new List<string>();

            for (int i = 0; i < now.Count; i++)
            {
                bool had = false;
                for (int k = 0; k < before.Count; k++) had |= before[k] == now[i];
                if (!had) fresh.Add(now[i]);
            }

            return fresh;
        }
    }
}
