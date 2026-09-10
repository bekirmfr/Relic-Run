using System.Collections.Generic;
using RelicRun.Core.Meta;

namespace RelicRun.Core.Presentation
{
    /// <summary>One thing the supporter pack buys.</summary>
    public struct Perk
    {
        /// <summary>The key its name is translated under.</summary>
        public string TitleKey;

        /// <summary>And the key for the line under it.</summary>
        public string WhatKey;
    }

    /// <summary>Everything the supporter modal shows.</summary>
    public struct SupporterCard
    {
        /// <summary>Whether it has been bought, which decides which half of the card shows.</summary>
        public bool Owned;

        /// <summary>What it buys, in the order the source lists them.</summary>
        public IReadOnlyList<Perk> Perks;

        /// <summary>How many sparks buying it hands over.</summary>
        public int Grants;

        /// <summary>What the delver would have afterwards.</summary>
        public int Afterwards;
    }

    /// <summary>
    /// The supporter pack, worked out.
    /// </summary>
    /// <remarks>
    /// The one modal in the game that is entirely translated — title, blurb, all three perks,
    /// price, the thank-you and the small print are every one of them a locale key. So almost
    /// nothing is spelled here: what is here is which perks exist, in what order, and the one
    /// number the pack actually moves.
    ///
    /// Reachable from three places — the title, the profile and the result screen — which is
    /// three more than any other modal, and is the source telling you what it is for.
    /// </remarks>
    public static class SupporterCards
    {
        /// <summary>
        /// How many sparks the pack hands over.
        /// </summary>
        /// <remarks>
        /// A constant in the source beside the chain cap and the revive price, which is where it
        /// belongs: it is a number the economy is balanced against rather than a line of copy.
        /// The copy says "300 starting sparks" separately, in eight languages, and if this ever
        /// moves that is eight strings to change.
        /// </remarks>
        public const int Sparks = 300;

        /// <summary>What the pack buys, in the order the source lists them.</summary>
        public static readonly IReadOnlyList<Perk> Perks = new[]
        {
            new Perk { TitleKey = "gildedTitle", WhatKey = "gildedTitleD" },
            new Perk { TitleKey = "noAdsPerk", WhatKey = "noAdsPerkD" },
            new Perk { TitleKey = "startSparks", WhatKey = "startSparksD" },
        };

        /// <summary>The card, for this delver.</summary>
        public static SupporterCard Of(Preferences prefs, SaveState save)
        {
            bool owned = prefs != null && prefs.Supporter;
            int held = save != null ? save.Sparks : 0;

            return new SupporterCard
            {
                Owned = owned,
                Perks = Perks,
                Grants = Sparks,

                // What they would have, not what they would gain. Already owning it grants
                // nothing further — the pack is bought once, and a card offering another three
                // hundred to somebody who has already paid is an offer that cannot be taken.
                Afterwards = owned ? held : held + Sparks,
            };
        }
    }
}
