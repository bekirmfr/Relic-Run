using System;
using System.Collections.Generic;

namespace RelicRun.Core.Meta
{
    /// <summary>One thing a delver can be shown to have done.</summary>
    public sealed class Achievement
    {
        /// <summary>The source game's string id, which the corpus compares against.</summary>
        public readonly string Key;

        public readonly string Name;

        public readonly string Description;

        private readonly Func<SaveState, bool> _earned;

        public Achievement(string key, string name, string description, Func<SaveState, bool> earned)
        {
            Key = key;
            Name = name;
            Description = description;
            _earned = earned;
        }

        public bool Earned(SaveState save) { return _earned(save); }
    }

    /// <summary>
    /// The fourteen, in the order they are shown.
    /// </summary>
    /// <remarks>
    /// Every one is a question about <see cref="SaveState"/> and nothing else, which is why
    /// they live in Core rather than with the screen that draws them: what has been earned is a
    /// fact about the save, and a UI that computed it would be a second copy of the rule.
    /// </remarks>
    public static class Achievements
    {
        public static readonly IReadOnlyList<Achievement> All = new List<Achievement>
        {
            new Achievement("firstblood", "First Blood", "Finish a delve.",
                s => s.Runs >= 1),
            new Achievement("clear1", "Hoard-Slayer", "Clear the dungeon.",
                s => s.Clears >= 1),
            new Achievement("clear5", "Regular Customer", "Clear the dungeon 5 times.",
                s => s.Clears >= 5),
            new Achievement("score1k", "Deep Delver", "Score 1,000 in one run.",
                s => s.Best >= 1000),
            new Achievement("score2k", "Cartographer of the Dark", "Score 2,000 in one run.",
                s => s.Best >= 2000),
            new Achievement("crown1", "Crowned", "Win a versus lobby.",
                s => s.VsCrowns >= 1),
            new Achievement("crown5", "Dynasty", "Win 5 versus lobbies.",
                s => s.VsCrowns >= 5),
            new Achievement("rich", "Purse of Renown", "Bank 1,000 gold lifetime.",
                s => s.GoldLife >= 1000),
            new Achievement("lvl5", "Seasoned", "Reach level 5.",
                s => s.Level >= 5),
            new Achievement("lvl10", "Veteran", "Reach level 10.",
                s => s.Level >= 10),
            new Achievement("t2", "Deeper Doors", "Unlock a second dungeon.",
                s => s.Unlocked >= 2),
            new Achievement("t3", "The Third Gate", "Unlock a third dungeon.",
                s => s.Unlocked >= 3),
            new Achievement("mdaily", "The Daily Ritual", "Unlock Daily Delve mode (level 3).",
                s => s.Level >= 3),
            new Achievement("mversus", "Blood in the Arena", "Unlock Versus mode (level 5).",
                s => s.Level >= 5),
        };

        /// <summary>Which of them this save has earned, by key.</summary>
        public static List<string> EarnedBy(SaveState save)
        {
            var earned = new List<string>();
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Earned(save)) earned.Add(All[i].Key);
            }

            return earned;
        }
    }
}
