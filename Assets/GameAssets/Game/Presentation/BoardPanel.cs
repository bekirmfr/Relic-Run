using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Game.Services;

namespace RelicRun.Game.Presentation
{
public sealed class BoardPanel : SheetPanel
    {
        public override Page Shows
        {
            get { return Page.Board; }
        }

        public override void Draw(SaveVault vault, DateTimeOffset now)
        {
            BoardCard card = BoardCards.Of(vault.Earned, vault.Chosen.Name);

            var rows = new List<Row>(card.Rows.Count);

            foreach (BoardRow run in card.Rows)
            {
                string who = run.Name ?? Say("lbAnon");

                string meta = Say("lbMeta", With("f", run.Floor.ToString(),
                    "k", run.Kills.ToString()));

                rows.Add(new Row(run.Rank + "  " + who + "  " + run.Score + "  " + meta,
                    run.Mine ? Lit : Plain));
            }

            // The empty board is its own screen rather than an empty list. A list with nothing in
            // it is a bug that looks like a design; a line saying no runs yet cannot be mistaken
            // for one.
            Sheet(Say("lbTitle"), card.Empty ? Say("lbEmpty") : Say("lbLocal"), rows);
        }
    }
}
