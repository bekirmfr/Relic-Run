using System.Collections.Generic;
using RelicRun.Core.Content;

namespace RelicRun.Core.Presentation
{
    /// <summary>One relic on offer, as the draft draws it.</summary>
    public struct Offered
    {
        public RelicId Relic;

        public RelicKind Family;

        /// <summary>The family's colour, as <c>#RRGGBB</c>.</summary>
        public string FamilyHex;

        /// <summary>The key its name is translated under, or null.</summary>
        public string NameKey;

        /// <summary>Its English name, shown when there is no key.</summary>
        public string Name;

        /// <summary>Its English description, or null when the description is translated.</summary>
        public string What;

        /// <summary>The key its description is translated under, or null.</summary>
        public string WhatKey;

        /// <summary>The two channels it turns one into the other, or null.</summary>
        public Channel[] Wheel;

        /// <summary>How many are already held.</summary>
        public int Owned;

        /// <summary>
        /// Which held relics this one would chain with.
        /// </summary>
        /// <remarks>
        /// The single most useful thing on the card, and the reason a draft is a decision rather
        /// than a shopping list: a relic that reacts to something the delver already emits is
        /// worth several that do not. Ids rather than names, because names are translated for
        /// nineteen of the fifty and Core does not translate.
        /// </remarks>
        public IReadOnlyList<RelicId> Chains;
    }

    /// <summary>Everything the draft shows.</summary>
    public struct DraftCard
    {
        public IReadOnlyList<Offered> Offer;

        /// <summary>What a fresh offer costs.</summary>
        public int Price;

        /// <summary>Whether the purse can cover it.</summary>
        public bool CanReroll;

        /// <summary>How many have been paid for already this run.</summary>
        public int Rerolls;
    }

    /// <summary>
    /// The draft, worked out.
    /// </summary>
    /// <remarks>
    /// Two relics on a table and one floor's worth of consequences. What makes it a decision is
    /// not the two cards but what is already on the shelf — which is why <see cref="Offered.Chains"/>
    /// is here and why it is the thing the screen should put in front of a delver.
    ///
    /// A reroll is offered only when it can be paid for. The run itself never asks otherwise —
    /// <c>Delve</c> skips straight past the question with an empty purse — so a screen that drew
    /// the button anyway would be drawing one the run will not stop for.
    /// </remarks>
    public static class DraftCards
    {
        /// <summary>The draft, for this offer against what is held.</summary>
        public static DraftCard Of(IReadOnlyList<RelicId> offer, IReadOnlyList<RelicId> held,
            int gold, int price, int rerolls)
        {
            var cards = new List<Offered>();

            if (offer != null)
            {
                foreach (RelicId one in offer) cards.Add(One(one, held));
            }

            return new DraftCard
            {
                Offer = cards,
                Price = price,
                CanReroll = gold >= price,
                Rerolls = rerolls,
            };
        }

        /// <summary>One card of the offer.</summary>
        public static Offered One(RelicId relic, IReadOnlyList<RelicId> held)
        {
            RelicDef def = RelicCatalog.Get(relic);
            RelicTextDef text = RelicText.Get(relic);
            SetDef family = SetCatalog.Get(def.Kind);

            var owned = 0;

            if (held != null)
            {
                foreach (RelicId one in held)
                {
                    if (one == relic) owned++;
                }
            }

            return new Offered
            {
                Relic = relic,
                Family = def.Kind,
                FamilyHex = family != null ? family.Hex : null,
                NameKey = text != null ? text.NameKey : null,
                Name = text != null ? text.Name : string.Empty,
                What = text != null ? text.What : null,
                WhatKey = text != null ? text.WhatKey : null,
                Wheel = Wheel(def),
                Owned = owned,
                Chains = Chains(def, held),
            };
        }

        /// <summary>
        /// Which held relics this one would chain with, each named once.
        /// </summary>
        /// <remarks>
        /// A chain runs both ways: this relic reacting to something held, or something held
        /// reacting to this. Both are worth knowing and the source counts both, which is why a
        /// Blood Altar reads as chaining with a Vampire Tooth from either side of the table.
        ///
        /// A relic never chains with ITSELF, however many copies are on the shelf. That is the
        /// source's filter and it is right: a second Thorn Vest is a bigger Thorn Vest, not a
        /// combination, and listing it would make every duplicate look like a synergy.
        /// </remarks>
        public static IReadOnlyList<RelicId> Chains(RelicDef relic, IReadOnlyList<RelicId> held)
        {
            var with = new List<RelicId>();

            if (held == null || relic == null) return with;

            foreach (RelicId one in held)
            {
                if (one == relic.Id || with.Contains(one)) continue;

                RelicDef other = RelicCatalog.Get(one);

                if (other == null) continue;

                if (Meets(relic.Reacts, other.Emits) || Meets(relic.Emits, other.Reacts))
                {
                    with.Add(one);
                }
            }

            return with;
        }

        private static bool Meets(Channel[] mine, Channel[] theirs)
        {
            if (mine == null || theirs == null) return false;

            foreach (Channel one in mine)
            {
                foreach (Channel other in theirs)
                {
                    if (one == other) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// What a relic turns into what, or null when it turns nothing.
        /// </summary>
        /// <remarks>
        /// A wheel needs both halves, exactly as in the book and on the relic card. Drawing half
        /// an arrow would claim a conversion that never happens.
        /// </remarks>
        private static Channel[] Wheel(RelicDef relic)
        {
            if (relic.Reacts == null || relic.Reacts.Length == 0) return null;
            if (relic.Emits == null || relic.Emits.Length == 0) return null;

            return new[] { relic.Reacts[0], relic.Emits[0] };
        }
    }
}
