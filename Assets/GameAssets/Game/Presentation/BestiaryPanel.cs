using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Game.Services;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// Thirteen species, as much of each as the delver has earned.
    /// </summary>
    /// <remarks>
    /// Fog of war, and the fog is the point: an unmet species shows neither its name nor its
    /// card. What it does show is that it EXISTS, which is why the row stays in the list — a
    /// delver should know how much they have not seen.
    ///
    /// A met species opens its dossier; an unmet one opens nothing, because there is nothing
    /// behind it that is not the thing being withheld.
    /// </remarks>
    public sealed class BestiaryPanel : SheetPanel
    {
        public override Page Shows
        {
            get { return Page.Bestiary; }
        }

        public override void Draw(SaveVault vault, DateTimeOffset now)
        {
            BestiaryCard card = BestiaryCards.Of(vault.Earned);

            var rows = new List<Row>(card.Foes.Count);

            foreach (BestiaryEntry foe in card.Foes)
            {
                // An unmet species shows neither its name nor its card. What it does show is
                // that it exists — the fog is the reward, and hiding the row entirely would
                // hide how much there is left to find.
                string name = foe.Met ? Say(foe.NameKey) : BestiaryCards.Unknown;
                string lore = foe.Met ? foe.Lore : Unmet;

                // Captured per row rather than read at press time: rows are recycled as the
                // list scrolls, and a listener that asked which species this row was showing
                // would open whatever it had drifted onto.
                int which = foe.Species;

                rows.Add(new Row(name + "  " + lore, foe.Met ? Plain : Faint,
                    foe.Met ? (Action)(() => Card(which)) : null));
            }

            Sheet(BestiaryCards.Title, card.Count, rows);
        }

        /// <summary>Opens one species' dossier.</summary>
        private void Card(int species)
        {
            if (Modals == null) return;

            Modals.Foe(species);
        }

        /// <summary>What is written where an unmet species' card would be.</summary>
        /// <remarks>
        /// The source's line, and English like the cards themselves. It is here rather than in
        /// Core because it belongs beside the placeholder it replaces, and because Core has no
        /// business holding a sentence only one screen ever shows.
        /// </remarks>
        public const string Unmet = "Not yet met. The dark keeps its own records.";
    }
}
