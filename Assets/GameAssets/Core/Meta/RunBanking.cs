using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Run;

namespace RelicRun.Core.Meta
{
    /// <summary>What a finished run is, as far as the save is concerned.</summary>
    public struct FinishedRun
    {
        public RunEnding Ending;
        public bool Versus;

        /// <summary>Whether this was the Daily Delve, which keeps a best of its own.</summary>
        public bool Daily;

        /// <summary>The day's seed, when <see cref="Daily"/>.</summary>
        public uint Seed;

        /// <summary>Which hall was delved.</summary>
        public int Tier;

        public int Floor;
        public int Kills;

        /// <summary>How many relics were in hand at the end.</summary>
        public int Relics;

        /// <summary>Who ran it, for the leaderboard row.</summary>
        public string Name;

        /// <summary>When it finished, in milliseconds. Display only; nothing sorts on it.</summary>
        public long Stamp;
    }

    /// <summary>What banking a run turned out to be worth saying out loud.</summary>
    public struct Banked
    {
        /// <summary>Levels crossed on the way, in order. Empty if none were.</summary>
        public List<int> LevelsCrossed;

        /// <summary>
        /// Whether the run beat the standing personal best — which matching it does not.
        /// </summary>
        /// <remarks>
        /// The save alone cannot answer this afterwards: a run that ties the best leaves the
        /// number exactly where it was, so beating and matching are indistinguishable once the
        /// writing is done. It is reported here because the end screen says so.
        /// </remarks>
        public bool NewBest;
    }

    /// <summary>
    /// Putting a finished run into the save.
    /// </summary>
    /// <remarks>
    /// The arithmetic of what a run was WORTH lives in <see cref="RunOutcome"/>; this is what
    /// happens to it afterwards. The two are apart because a sandbox run is scored and then
    /// deliberately not banked — the score is shown, and nothing persists.
    /// </remarks>
    public static class RunBanking
    {
        /// <summary>How many runs the personal leaderboard holds.</summary>
        public const int BoardSize = 10;

        /// <summary>
        /// Banks a finished run.
        /// </summary>
        /// <remarks>
        /// The order matters and is the source's: the run is counted, then the clear, then the
        /// hall it unlocks; the purse joins the lifetime total and the wallet; the experience is
        /// paid, which is what can move the level; and only then are the best, the daily best
        /// and the board written. A clear also remembers the hall's backdrop, once.
        /// </remarks>
        public static Banked Bank(SaveState save, FinishedRun run, RunReward reward)
        {
            var banked = new Banked { LevelsCrossed = new List<int>() };

            save.Runs++;

            if (run.Ending == RunEnding.Cleared)
            {
                save.Clears++;

                // A cleared hall opens the next one, and never closes one already open.
                if (run.Versus) save.VsCrowns++;
                else if (run.Tier >= save.Unlocked) save.Unlocked = run.Tier + 1;
            }

            if (reward.Banked > 0)
            {
                save.GoldLife += reward.Banked;
                save.Gold += reward.Banked;
            }

            int before = save.Level;
            save.Xp += reward.Xp;
            for (int level = before + 1; level <= save.Level; level++)
            {
                banked.LevelsCrossed.Add(level);
            }

            if (reward.Score > save.Best)
            {
                save.Best = reward.Score;
                banked.NewBest = true;
            }

            if (run.Daily && reward.Score > save.DailyBestFor(run.Seed))
            {
                save.DailyBest[run.Seed] = reward.Score;
            }

            // A cleared hall's backdrop is remembered the first time it is seen.
            if (run.Ending == RunEnding.Cleared && !run.Versus)
            {
                string art = DungeonCatalog.Get(run.Tier).Art;
                if (!save.Arts.Contains(art)) save.Arts.Add(art);
            }

            File(save, new ScoreRow
            {
                Score = reward.Score,
                Floor = run.Floor,
                Kills = run.Kills,
                Relics = run.Relics,
                Name = run.Name,
                Stamp = run.Stamp,
            });

            return banked;
        }

        /// <summary>
        /// Files a row on the board: ten kept, best first, ties in the order they were run.
        /// </summary>
        /// <remarks>
        /// The source appends and then sorts on score, which in a stable sort leaves an earlier
        /// run ahead of a later one that matched it. Inserting after every equal score is the
        /// same rule, said directly, and does not depend on which sort a runtime happens to use.
        /// </remarks>
        private static void File(SaveState save, ScoreRow row)
        {
            int at = save.Scores.Count;
            for (int i = 0; i < save.Scores.Count; i++)
            {
                if (save.Scores[i].Score < row.Score) { at = i; break; }
            }

            save.Scores.Insert(at, row);
            while (save.Scores.Count > BoardSize) save.Scores.RemoveAt(save.Scores.Count - 1);
        }
    }
}
