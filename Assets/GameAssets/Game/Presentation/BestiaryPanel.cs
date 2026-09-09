using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Game.Services;

namespace RelicRun.Game.Presentation
{
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

                rows.Add(new Row(name + "  " + lore, foe.Met ? Plain : Faint));
            }

            Sheet(BestiaryCards.Title, card.Count, rows);
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
