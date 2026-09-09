using System.Collections.Generic;
using System.Globalization;
using RelicRun.Core.Content;
using RelicRun.Core.Meta;

namespace RelicRun.Core.Presentation
{
    /// <summary>
    /// One species, as much of it as the delver has earned.
    /// </summary>
    /// <remarks>
    /// Not called FoeEntry, because the fight's own setup already has one of those and a
    /// widget importing both namespaces could not tell them apart. Core is the newcomer
    /// here, so Core is what moved.
    /// </remarks>
    public struct BestiaryEntry
    {
        public int Species;

        /// <summary>Whether this one has ever been met.</summary>
        public bool Met;

        /// <summary>
        /// The key its NAME is translated under, or null for one still unmet.
        /// </summary>
        /// <remarks>
        /// A key rather than a name, because the name goes through the translator and Core does
        /// not pick a language. Null when unmet, so the screen shows its own placeholder rather
        /// than being handed one Core made up.
        /// </remarks>
        public string NameKey;

        /// <summary>
        /// Its card, already quoted, or null for one still unmet.
        /// </summary>
        /// <remarks>
        /// English whatever the delver reads in — the source's own decision, stated in its
        /// source. So unlike the name, this one IS spelled here.
        /// </remarks>
        public string Lore;
    }

    /// <summary>Everything the bestiary shows.</summary>
    public struct BestiaryCard
    {
        public IReadOnlyList<BestiaryEntry> Foes;

        public int Met;

        public int Total;

        /// <summary>The line over the list: how much of the dark has been mapped.</summary>
        public string Count;
    }

    /// <summary>
    /// The bestiary, worked out.
    /// </summary>
    /// <remarks>
    /// Fog of war, and the fog is the point. An unmet species shows neither its name nor its
    /// card — the source gives it three question marks and a line about the dark keeping its own
    /// records — because a bestiary that listed everything would be a menu rather than a reward.
    ///
    /// What is NOT hidden is that the species exists at all: the count says thirteen from the
    /// first launch, and every unmet row still takes up space in the list. A delver should know
    /// how much they have not seen.
    /// </remarks>
    public static class BestiaryCards
    {
        /// <summary>What is written where an unmet species' name would be.</summary>
        /// <remarks>
        /// Spaced, which is the source's spelling and not a typo. Three marks run together read
        /// as one confused glyph at this size; spaced, they read as a deliberate blank.
        /// </remarks>
        public const string Unknown = "? ? ?";

        /// <summary>The word after the tally. English, like the rest of the source's chrome.</summary>
        public const string Found = " FOUND";

        /// <summary>
        /// The heading. English in every locale, because the source writes it into its markup.
        /// </summary>
        /// <remarks>
        /// Worth checking rather than assuming, and it was: the board's heading DOES come from
        /// the translator, under <c>lbTitle</c>. Two screens next to each other, one translated
        /// and one not, and the only way to know which is which is to read the markup.
        /// </remarks>
        public const string Title = "Bestiary";

        /// <summary>The key its NAME is translated under, by species index.</summary>
        /// <remarks>
        /// The source's scheme: <c>en0</c> through <c>en12</c>, positional. Built here rather
        /// than spelled thirteen times, and asserted against the shipped strings in the tests —
        /// a key that names nothing shows the key, which is visible but is visible to a delver.
        /// </remarks>
        public static string NameKey(int species)
        {
            return "en" + species.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A species' card, in the quotes the source puts round it.
        /// </summary>
        /// <remarks>
        /// Curly quotes, which both shipped faces cover — checked rather than assumed, the way
        /// the star and the tick were, because this line never passes through the cleaner that
        /// would otherwise strip what cannot be drawn.
        /// </remarks>
        public static string Quoted(string lore)
        {
            return "“" + lore + "”";
        }

        public static BestiaryCard Of(SaveState save)
        {
            if (save == null) save = new SaveState();

            var foes = new List<BestiaryEntry>(EnemyCatalog.All.Count);
            var met = 0;

            for (var i = 0; i < EnemyCatalog.All.Count; i++)
            {
                bool seen = save.Seen.Contains(i);
                if (seen) met++;

                foes.Add(new BestiaryEntry
                {
                    Species = i,
                    Met = seen,
                    NameKey = seen ? NameKey(i) : null,
                    Lore = seen ? Quoted(EnemyCatalog.Get(i).Lore) : null,
                });
            }

            return new BestiaryCard
            {
                Foes = foes,
                Met = met,
                Total = EnemyCatalog.All.Count,
                Count = met.ToString(CultureInfo.InvariantCulture) + " / " +
                        EnemyCatalog.All.Count.ToString(CultureInfo.InvariantCulture) + Found,
            };
        }
    }
}
