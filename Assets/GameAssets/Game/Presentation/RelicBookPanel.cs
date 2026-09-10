using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Game.Services;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// Every relic in the game, found or not, and a way into each one's card.
    /// </summary>
    /// <remarks>
    /// Nothing here is hidden — the source shows all fifty from the first launch, and it is
    /// right to: a delver choosing between three relics in a draft needs to know what the other
    /// forty-seven do, and a book that unlocked as you went would be useless exactly when it was
    /// needed. That is the whole design difference between this and the bestiary.
    ///
    /// Each line opens the relic's own card, which is where the decision is actually made. The
    /// row here is a summary; the card is the dossier.
    /// </remarks>
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

                // Captured per row rather than read at press time. Rows are recycled as the
                // book scrolls, and a listener that looked up "the relic this row is showing"
                // would open whatever the row had drifted onto by then.
                RelicId which = entry.Relic;

                rows.Add(new Row(name + "  (" + entry.Kind + ")" + wheel + "  " + what, Plain,
                    () => Card(which)));
            }

            Sheet(RelicBookCards.Title, RelicBookCards.Note, rows);
        }

        /// <summary>
        /// Opens one relic's card, with nothing held.
        /// </summary>
        /// <remarks>
        /// Nothing held is the truth here rather than a shortcut: the book is a menu screen and
        /// there is no run to hold anything. The same card opened from a draft is handed what is
        /// in hand and shows the same relic differently — awakened, socketed, counted.
        /// </remarks>
        private void Card(RelicId relic)
        {
            if (Modals == null) return;

            Modals.Relic(relic);
        }
    }
}
