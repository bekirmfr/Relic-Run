using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Game.Services;

namespace RelicRun.Game.Presentation
{
public sealed class RelicBookPanel : SheetPanel
    {
        public override Page Shows
        {
            get { return Page.RelicBook; }
        }

        public override void Draw(SaveVault vault, DateTimeOffset now)
        {
            RelicBookCard card = RelicBookCards.Of();

            var rows = new List<Row>(card.Relics.Count);

            foreach (BookEntry entry in card.Relics)
            {
                // Exactly one of the two is set, which Core guarantees and a test holds it to.
                string name = entry.NameKey != null ? Say(entry.NameKey) : entry.Name;
                string what = entry.WhatKey != null ? Say(entry.WhatKey) : entry.What;

                string wheel = entry.Wheel == null
                    ? string.Empty
                    : "  [" + entry.Wheel[0] + " › " + entry.Wheel[1] + "]";

                rows.Add(new Row(name + "  (" + entry.Kind + ")" + wheel + "  " + what, Plain));
            }

            Sheet(RelicBookCards.Title, RelicBookCards.Note, rows);
        }
    }
}
