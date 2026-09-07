using System.Collections.Generic;

namespace RelicRun.Core.Meta
{
    /// <summary>One run's row on the personal leaderboard.</summary>
    /// <remarks>
    /// The board sorts on score alone, so <see cref="Stamp"/> orders nothing and is carried for
    /// display only. It is deliberately outside the corpus: a clock is not a rule.
    /// </remarks>
    public struct ScoreRow
    {
        public int Score;
        public int Floor;
        public int Kills;

        /// <summary>How many relics the run ended holding.</summary>
        public int Relics;

        public string Name;

        /// <summary>When the run finished, in milliseconds. Display only.</summary>
        public long Stamp;
    }

    /// <summary>
    /// What a delver keeps between runs.
    /// </summary>
    /// <remarks>
    /// The source spreads this across a dozen keys in one key-value store, read and written from
    /// wherever needs it. Gathering it into one object is the whole of the change: every field
    /// here is a key there, with the same meaning, and <see cref="RunBanking"/> is the only
    /// thing that writes the ones a finished run touches.
    ///
    /// Settings, the wardrobe and the sprite studio also live in that store and are NOT here.
    /// They are not progression, nothing in Core reads them, and they belong with the systems
    /// that own them.
    /// </remarks>
    public sealed class SaveState
    {
        /// <summary>Runs finished, however they ended.</summary>
        public int Runs;

        /// <summary>Runs that reached the bottom.</summary>
        public int Clears;

        /// <summary>Gold banked across every run, which one achievement reads.</summary>
        public int GoldLife;

        /// <summary>The wallet: gold banked and not yet spent.</summary>
        public int Gold;

        /// <summary>Best score in a single run.</summary>
        public int Best;

        /// <summary>Experience, which is where the delver's level comes from.</summary>
        public int Xp;

        /// <summary>The deepest hall unlocked. One at the start, and never less.</summary>
        public int Unlocked = 1;

        /// <summary>Versus lobbies won.</summary>
        public int VsCrowns;

        /// <summary>Hard currency, spent on a revive.</summary>
        public int Sparks;

        /// <summary>The ten best runs, best first.</summary>
        public readonly List<ScoreRow> Scores = new List<ScoreRow>();

        /// <summary>Backdrops seen, by the art each hall is drawn against.</summary>
        public readonly List<string> Arts = new List<string>();

        /// <summary>Best score on each Daily Delve, keyed by the day's seed.</summary>
        public readonly Dictionary<uint, int> DailyBest = new Dictionary<uint, int>();

        /// <summary>Days whose Daily Delve has been started, so it cannot be replayed.</summary>
        public readonly HashSet<uint> DailyDone = new HashSet<uint>();

        /// <summary>Bestiary: species indices that have been met.</summary>
        public readonly HashSet<int> Seen = new HashSet<int>();

        /// <summary>The delver's level, which is derived from the experience and not stored.</summary>
        public int Level
        {
            get { return Run.Progression.LevelFor(Xp); }
        }

        public int DailyBestFor(uint seed)
        {
            int best;
            return DailyBest.TryGetValue(seed, out best) ? best : 0;
        }
    }
}
