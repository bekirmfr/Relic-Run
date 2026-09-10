using System.Collections.Generic;
using RelicRun.Core.Content;

namespace RelicRun.Core.Presentation
{
    /// <summary>One step of a set bonus, and whether it is switched on.</summary>
    public struct SetStep
    {
        /// <summary>How many of the kind it needs.</summary>
        public int Needs;

        /// <summary>How the card writes that: the number in brackets.</summary>
        public string Tag;

        /// <summary>What it does. English.</summary>
        public string What;

        /// <summary>Whether the delver has enough for it.</summary>
        public bool On;
    }

    /// <summary>Everything a set card shows.</summary>
    public struct SetCard
    {
        public RelicKind Family;

        /// <summary>The heading: the family's name and the word SET. English.</summary>
        public string Name;

        /// <summary>The family's colour, as <c>#RRGGBB</c>.</summary>
        public string Hex;

        /// <summary>How many count toward it, the idol included.</summary>
        public int Held;

        /// <summary>Whether a Hollow Idol is among them.</summary>
        public bool Idol;

        /// <summary>The line under the heading, pluralised. English.</summary>
        public string CountText;

        /// <summary>Its steps, shallowest first.</summary>
        public IReadOnlyList<SetStep> Steps;

        /// <summary>
        /// Which relics of this kind are held, each listed once.
        /// </summary>
        /// <remarks>
        /// Ids rather than names, because names are translated for nineteen of the fifty and
        /// Core does not translate. The screen joins them with <see cref="SetCards.Between"/>.
        /// </remarks>
        public IReadOnlyList<RelicId> Members;
    }

    /// <summary>
    /// A set card, worked out.
    /// </summary>
    /// <remarks>
    /// Opened two ways and answering the same question both times: from the kind chip on a
    /// relic's own card, and from the run's set chips. From the relic book nothing is held, so
    /// every step reads as off — which is exactly right, and is how a delver reads what a family
    /// would be worth before committing to it.
    ///
    /// The tier text is English in all eight languages, like the relic cards.
    /// </remarks>
    public static class SetCards
    {
        /// <summary>What separates two relic names in the members line.</summary>
        public const string Between = " · ";

        /// <summary>The word after the family's name in the heading.</summary>
        public const string Suffix = " SET";

        /// <summary>What the count line says when an idol is padding it.</summary>
        public const string IdolNote = " (Hollow Idol +1)";

        /// <summary>The card for a family nobody is collecting.</summary>
        public static SetCard Of(RelicKind family)
        {
            return Of(family, null, CombatMode.Delve);
        }

        /// <summary>The card for a family, against what a delver is carrying.</summary>
        /// <param name="held">
        /// Every relic in hand, duplicates included — a second Whetstone counts a second time
        /// toward EDGE, which is most of how a set is ever completed.
        /// </param>
        /// <param name="mode">
        /// Which game this hand is in, asked about only for the Hollow Idol.
        /// </param>
        /// <remarks>
        /// The mode is here because the port and the source disagree, and the port is right. The
        /// source's card adds the idol to every family unconditionally; the port's rules add it
        /// in a delve and NOT in a duel, where the idol buys nothing and its max-HP cost is pure
        /// downside. A card that copied the source would tell a duellist they had a set tier the
        /// engine was never going to give them — which is the one kind of wrong this screen must
        /// not be, since it exists to be believed.
        ///
        /// So it asks <see cref="RelicTuning"/> rather than deciding, and the card is wrong only
        /// if the rules are.
        /// </remarks>
        public static SetCard Of(RelicKind family, IReadOnlyList<RelicId> held, CombatMode mode)
        {
            SetDef def = SetCatalog.Get(family);
            bool counts = RelicTuning.For(RelicId.HollowIdol, mode).CountsTowardEverySet;

            var members = new List<RelicId>();
            var kind = 0;
            var idols = 0;

            if (held != null)
            {
                foreach (RelicId one in held)
                {
                    if (counts && one == RelicId.HollowIdol) idols++;

                    if (RelicCatalog.KindOf(one) != family) continue;

                    kind++;
                    if (!members.Contains(one)) members.Add(one);
                }
            }

            // Where it counts, it counts toward its OWN family twice — it is a Curse relic, so
            // the Curse count already has it before the padding is added. That IS the source's
            // arithmetic and it is kept: the Curse set has one step, +1 ATK, so the double count
            // buys a bonus a delver holding an idol already has.
            int total = kind + idols;

            var steps = new List<SetStep>();

            if (def != null)
            {
                foreach (SetTier tier in def.Tiers)
                {
                    steps.Add(new SetStep
                    {
                        Needs = tier.Needs,
                        Tag = "(" + tier.Needs + ")",
                        What = tier.What,
                        On = total >= tier.Needs,
                    });
                }
            }

            return new SetCard
            {
                Family = family,
                Name = def != null ? def.Name + Suffix : string.Empty,
                Hex = def != null ? def.Hex : null,
                Held = total,
                Idol = idols > 0,
                CountText = Counted(total, idols > 0),
                Steps = steps,
                Members = members,
            };
        }

        /// <summary>
        /// How many relics, said in English.
        /// </summary>
        /// <remarks>
        /// The pluralisation is the source's and is English-only, like everything else on this
        /// card. It is worked out here rather than in the screen so that there is one place to
        /// change when somebody translates it, instead of one per screen that opens the card.
        /// </remarks>
        private static string Counted(int total, bool idol)
        {
            string many = total == 1 ? " relic" : " relics";

            return total + many + (idol ? IdolNote : string.Empty);
        }
    }
}
