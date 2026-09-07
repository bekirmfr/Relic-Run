using System;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;

namespace RelicRun.Core.Run
{
    /// <summary>How a run ended.</summary>
    public enum RunEnding
    {
        /// <summary>The hero fell. The purse is lost with them.</summary>
        Died = 0,

        /// <summary>The hero walked out with what they had.</summary>
        CashedOut = 1,

        /// <summary>The hero reached the bottom.</summary>
        Cleared = 2,
    }

    /// <summary>What a finished run was worth.</summary>
    public struct RunReward
    {
        /// <summary>Gold actually taken home. Zero on a death.</summary>
        public int Banked;

        /// <summary>The bragging number: depth, kills and the crown, scaled by the dungeon.</summary>
        public int Score;

        /// <summary>Experience paid out, which is score scaled by how relevant the dungeon still is.</summary>
        public int Xp;

        /// <summary>Nought to three, on how far down the run reached.</summary>
        public int Stars;
    }

    /// <summary>
    /// Scoring a finished run.
    /// </summary>
    /// <remarks>
    /// The score is deliberately not the same number as the XP. Score is the bragging figure —
    /// base times the dungeon's multiplier, never touched by the progression table — so two
    /// delvers' identical runs compare. XP is that score scaled by how far past the player's
    /// frontier the dungeon sits, so grinding a dungeon already left behind pays less. Gold is
    /// untouched at both ends: the purse collected is the purse banked.
    /// </remarks>
    public static class RunOutcome
    {
        /// <summary>Score per floor descended.</summary>
        private const int PerFloor = 40;

        /// <summary>Score per kill. A duel does not count them.</summary>
        private const int PerKill = 5;

        /// <summary>Score for reaching the bottom.</summary>
        private const int ClearBonus = 150;

        public static RunReward Score(RunEnding how, bool versus, int gold, int floor, int kills,
            RunState run, int tier, int frontier)
        {
            var reward = new RunReward();
            reward.Banked = Bank(how, gold, run);

            // A duel is scored on depth and the crown alone.
            int baseScore = (floor - 1) * PerFloor + (how == RunEnding.Cleared ? ClearBonus : 0);
            if (!versus) baseScore += kills * PerKill;

            // Half the purse counts toward the score, so gold is the spread beyond a clear.
            //
            // The hall's own multiplier, read from the table rather than computed. The two are
            // not the same number past the fifth hall — the table rounds to four places, and
            // 1.6105 is not 1.1^5 — and the difference is worth a point of score.
            reward.Score = JsMath.RoundToInt(
                (baseScore + reward.Banked / 2.0) * DungeonCatalog.Get(tier).Multiplier);

            double relevance = versus ? 1.0 : Progression.RewardMultiplier(tier, frontier);
            reward.Xp = Math.Max(reward.Score > 0 ? 1 : 0,
                JsMath.RoundToInt(reward.Score * relevance));

            reward.Stars = StarsFor(how, floor);
            return reward;
        }

        /// <summary>
        /// What the purse actually banks. A death takes everything; a Piggy Bank takes its cut
        /// of a cash-out, and of nothing else.
        /// </summary>
        public static int Bank(RunEnding how, int gold, RunState run)
        {
            if (how == RunEnding.Died) return 0;
            if (how != RunEnding.CashedOut || run == null || !run.Has(RelicId.PiggyBank)) return gold;

            double cut = run.IsAwake(RelicId.PiggyBank) ? 1.5 : 1.25;
            return (int)Math.Floor(gold * cut);
        }

        /// <summary>
        /// Nought to three stars, on how far down the run reached. Walking out early is held to
        /// a lower bar than dying there, because leaving was a decision.
        /// </summary>
        /// <remarks>
        /// Depth is a fraction of thirteen floors, so only thirteen values are reachable and a
        /// threshold is pinned no more tightly than the gap it sits in. 0.72 falls between
        /// floor 10 (0.6923) and floor 11 (0.7692): anything in that range behaves identically,
        /// and no corpus can tell them apart. The 0.6 and 0.4 thresholds sit in narrower gaps
        /// and are pinned.
        /// </remarks>
        public static int StarsFor(RunEnding how, int floor)
        {
            if (how == RunEnding.Cleared) return 3;

            double reached = (floor - 1) / (double)DelveRun.MaxFloor;
            if (reached >= 0.72 || (how == RunEnding.CashedOut && reached >= 0.6)) return 2;
            return reached >= 0.4 ? 1 : 0;
        }
    }
}
