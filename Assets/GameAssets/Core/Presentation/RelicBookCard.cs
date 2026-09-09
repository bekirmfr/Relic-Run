using System.Collections.Generic;
using RelicRun.Core.Content;

namespace RelicRun.Core.Presentation
{
    /// <summary>One relic's entry in the book.</summary>
    public struct BookEntry
    {
        public RelicId Relic;

        /// <summary>Which family it belongs to, which is also what colours its frame.</summary>
        public RelicKind Kind;

        /// <summary>
        /// The key its name is translated under, or null for one the source never translated.
        /// </summary>
        /// <remarks>
        /// Not every relic has one. <see cref="RelicTextDef.Translated"/> is the source's own
        /// distinction and it is carried rather than smoothed over: a screen that invented a key
        /// for the untranslated ones would show the key instead of the name.
        /// </remarks>
        public string NameKey;

        /// <summary>Its English name, which is what an untranslated relic shows.</summary>
        public string Name;

        /// <summary>Its English description, or null when the description is translated.</summary>
        public string What;

        /// <summary>The key its description is translated under, or null when it is not.</summary>
        public string WhatKey;

        /// <summary>
        /// The two channels it turns one into the other, or null when it turns nothing.
        /// </summary>
        /// <remarks>
        /// A wheel is what the source calls a relic that eats one kind of signal and emits
        /// another, and the book draws the pair as a little badge. Most relics have none.
        /// </remarks>
        public Channel[] Wheel;
    }

    /// <summary>Everything the relic book shows.</summary>
    public struct RelicBookCard
    {
        public IReadOnlyList<BookEntry> Relics;

        public int Total;
    }

    /// <summary>
    /// The relic book: every relic in the game, whether or not it has been found.
    /// </summary>
    /// <remarks>
    /// Unlike the bestiary, nothing here is hidden. The source shows all fifty from the first
    /// launch, and it is right to: a delver choosing between three relics in a draft needs to
    /// know what the other forty-seven do, and a book that unlocked as you went would be useless
    /// exactly when it was needed.
    ///
    /// That is the whole design difference between the two screens, and it is worth stating
    /// because they otherwise look like the same screen twice.
    ///
    /// Every entry is described in one of two ways and never both: nineteen relics have their
    /// words in the locale tables and the rest carry English of their own. That split is the
    /// source's — it reads its English object FIRST, so a relic in both is never shown its
    /// translation — and carrying it rather than smoothing it over is what stops the book
    /// inventing a key for a relic nobody translated.
    /// </remarks>
    public static class RelicBookCards
    {
        /// <summary>The heading. English in every locale, from the source's own markup.</summary>
        public const string Title = "Relics";

        /// <summary>And the line over it.</summary>
        public const string Note = "THE COMPENDIUM";

        public static RelicBookCard Of()
        {
            var entries = new List<BookEntry>(RelicCatalog.All.Count);

            foreach (RelicDef relic in RelicCatalog.All)
            {
                RelicTextDef text = RelicText.Get(relic.Id);

                entries.Add(new BookEntry
                {
                    Relic = relic.Id,
                    Kind = relic.Kind,
                    NameKey = text.NameKey,
                    Name = text.Name,
                    What = text.What,
                    WhatKey = text.WhatKey,
                    Wheel = Wheel(relic),
                });
            }

            return new RelicBookCard { Relics = entries, Total = entries.Count };
        }

        /// <summary>
        /// What a relic turns into what, or null when it turns nothing.
        /// </summary>
        /// <remarks>
        /// A wheel needs both halves. A relic that reacts to something and emits nothing is not
        /// a wheel — it is a relic that reacts to something — and drawing a half-arrow for it
        /// would say the opposite of what is true.
        /// </remarks>
        private static Channel[] Wheel(RelicDef relic)
        {
            if (relic.Reacts == null || relic.Reacts.Length == 0) return null;
            if (relic.Emits == null || relic.Emits.Length == 0) return null;

            return new[] { relic.Reacts[0], relic.Emits[0] };
        }
    }
}
