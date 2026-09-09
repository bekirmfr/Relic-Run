using System.Collections.Generic;
using System.Globalization;
using RelicRun.Core.Meta;

namespace RelicRun.Core.Presentation
{
    /// <summary>One run on the delver's own board.</summary>
    public struct BoardRow
    {
        /// <summary>Its place, from one, written to two digits.</summary>
        public string Rank;

        /// <summary>
        /// Who ran it, or null when nobody said.
        /// </summary>
        /// <remarks>
        /// Null rather than a word, because the word is translated and Core does not pick a
        /// language. The screen shows its own locale's <c>lbAnon</c> in its place.
        /// </remarks>
        public string Name;

        public int Score;

        public int Floor;

        public int Kills;

        /// <summary>Whether this run is the delver's own, so the row can be picked out.</summary>
        public bool Mine;
    }

    /// <summary>Everything the board shows.</summary>
    public struct BoardCard
    {
        public IReadOnlyList<BoardRow> Rows;

        /// <summary>Whether there is nothing to show yet, which is its own screen.</summary>
        public bool Empty;
    }

    /// <summary>
    /// The delver's own runs, ranked.
    /// </summary>
    /// <remarks>
    /// Local, and that is the whole design rather than a stage of it. The source keeps its scores
    /// in the same key-value store as everything else and has no server behind it; an online
    /// board is separate work with its own backend decision, and calling this a placeholder would
    /// be inventing a promise nobody made.
    ///
    /// Unlike the title and the mode picker, every WORD on this screen is translated — the source
    /// runs its title, its tab, the empty line and each row's caption through the translator. So
    /// nothing here is spelled: the card carries numbers and a name, and the screen says them.
    /// </remarks>
    public static class BoardCards
    {
        /// <summary>How many runs the board keeps. The source's, and it is a top ten.</summary>
        public const int Keeps = 10;

        /// <summary>
        /// The board, for this delver.
        /// </summary>
        /// <param name="mine">
        /// The name to pick out, which is the delver's current one. A row matches by NAME rather
        /// than by anything stabler, because that is all a score row carries — and it is what the
        /// source matches on too.
        /// </param>
        public static BoardCard Of(SaveState save, string mine)
        {
            if (save == null) save = new SaveState();

            var rows = new List<BoardRow>(save.Scores.Count);

            for (var i = 0; i < save.Scores.Count && i < Keeps; i++)
            {
                ScoreRow run = save.Scores[i];
                bool named = Named(run.Name);

                rows.Add(new BoardRow
                {
                    // Two digits so the column does not jog when the board passes nine.
                    Rank = (i + 1).ToString("00", CultureInfo.InvariantCulture),
                    Name = named ? run.Name : null,
                    Score = run.Score,
                    Floor = run.Floor,
                    Kills = run.Kills,

                    // An unnamed row is never "mine", even when the delver is also unnamed.
                    // Otherwise every anonymous run on the board would light up as theirs.
                    Mine = named && !string.IsNullOrEmpty(mine) && run.Name == mine,
                });
            }

            return new BoardCard { Rows = rows, Empty = rows.Count == 0 };
        }

        /// <summary>
        /// Whether a row's name is a name.
        /// </summary>
        /// <remarks>
        /// Empty is not, and neither is one the old auto-naming scheme left behind — the source
        /// checks for that prefix here as well as when it loads a delver's own name, because a
        /// board row keeps whatever was current when the run was banked. A delver who was
        /// auto-named a year ago should not have that name read back to them now.
        /// </remarks>
        private static bool Named(string name)
        {
            return !string.IsNullOrEmpty(name) && !name.StartsWith(PreferenceCodec.AutoNamedPrefix);
        }
    }
}
