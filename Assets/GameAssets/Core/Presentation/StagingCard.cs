using System.Collections.Generic;
using System.Globalization;
using RelicRun.Core.Content;
using RelicRun.Core.Meta;

namespace RelicRun.Core.Presentation
{
    /// <summary>One hall the arena can be fought in.</summary>
    public struct ArenaHall
    {
        public int Tier;

        public string Name;

        /// <summary>Whether the delver has opened it. A locked hall cannot host a match.</summary>
        public bool Open;

        /// <summary>Whether this is the one the match would be fought in.</summary>
        public bool Chosen;
    }

    /// <summary>Everything the staging hall shows.</summary>
    public struct StagingCard
    {
        public IReadOnlyList<ArenaHall> Halls;

        /// <summary>Which hall the match would be fought in.</summary>
        public int Chosen;

        public string HallName;

        /// <summary>The line over the roster: what is being assembled, or what it will be.</summary>
        public string Roster;

        /// <summary>Whether the arena can be entered at all.</summary>
        public bool Open;
    }

    /// <summary>
    /// The staging hall, worked out.
    /// </summary>
    /// <remarks>
    /// Where a delver picks the ground before a versus match. The halls offered are the ones they
    /// have UNLOCKED in the dungeons — the arena borrows the same ten, and reaching deeper in
    /// solo play is what earns the right to fight there.
    ///
    /// The roster line counts up while the lobby assembles. It is the one moving thing on the
    /// screen and the source uses it as a loading state: eight delvers do not appear at once, and
    /// a screen that sat still would look stuck rather than busy.
    /// </remarks>
    public static class StagingCards
    {
        /// <summary>What the roster says once everybody is in.</summary>
        public const string Ready = "8 DELVERS · 3 LIVES EACH";

        /// <summary>And while they are still arriving.</summary>
        public const string Assembling = "ASSEMBLING DELVERS · ";

        /// <summary>
        /// The staging hall, for this delver.
        /// </summary>
        /// <param name="chosen">
        /// The hall to fight in. Clamped to what they have opened rather than to what exists —
        /// offering a hall the arena would then refuse is worse than not offering it.
        /// </param>
        /// <param name="seconds">
        /// How long the lobby has been assembling, or zero once it is full. The screen owns the
        /// clock; this only says what to write.
        /// </param>
        public static StagingCard Of(SaveState save, int chosen, int seconds)
        {
            if (save == null) save = new SaveState();

            int frontier = Career.Frontier(save);
            int count = DungeonCatalog.All.Count;

            int at = chosen <= 0 ? frontier : chosen;
            if (at > frontier) at = frontier;
            if (at > count) at = count;

            var halls = new List<ArenaHall>(count);

            for (var tier = 1; tier <= count; tier++)
            {
                halls.Add(new ArenaHall
                {
                    Tier = tier,
                    Name = DungeonCatalog.Get(tier).Name,
                    Open = tier <= frontier,
                    Chosen = tier == at,
                });
            }

            return new StagingCard
            {
                Halls = halls,
                Chosen = at,
                HallName = DungeonCatalog.Get(at).Name,

                Roster = seconds > 0
                    ? Assembling + seconds.ToString(CultureInfo.InvariantCulture) + "s"
                    : Ready,

                Open = Career.VersusIsOpen(save),
            };
        }
    }
}
