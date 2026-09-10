using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Game.Services;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// Who the delver is: how far they have come, and what they have earned.
    /// </summary>
    /// <remarks>
    /// A list of things that have already HAPPENED, so almost nothing on it opens anything. The
    /// one exception is the supporter row, which is the source's PACKS button in this screen's
    /// clothing — the profile is where a delver looks for what they own.
    /// </remarks>
    public sealed class ProfilePanel : SheetPanel
    {
        public override Page Shows
        {
            get { return Page.Profile; }
        }

        public override void Draw(SaveVault vault, DateTimeOffset now)
        {
            ProfileCard card = ProfileCards.Of(vault.Earned, vault.Chosen);

            var rows = new List<Row>
            {
                new Row("LEVEL  " + card.Level, Plain),
                new Row("XP  " + card.Xp, Plain),

                // No next level at the ceiling, so nothing about one is said. A screen reading
                // "100% to level 21" would be promising something that does not exist.
                new Row(card.Capped ? "MAX LEVEL" : card.ToNext + "% TO LEVEL " + (card.Level + 1),
                    card.Capped ? Lit : Plain),

                new Row("RUNS  " + card.Runs, Plain),
                new Row("CLEARS  " + card.Clears, Plain),
                new Row("BEST  " + card.Best, Plain),
                new Row("CROWNS  " + card.Crowns, Plain),
                new Row("GOLD BANKED  " + card.GoldLife, Plain),
                // Pressable whether it is owned or not. Owned, the card thanks; unowned, it
                // offers — and a row that vanished once it was bought would hide the one place
                // a delver can check what they paid for.
                new Row(card.Supporter ? "SUPPORTER PACK" : "BASIC", card.Supporter ? Lit : Faint,
                    Pack),
                new Row(string.Empty, Plain),
            };

            foreach (Badge badge in card.Badges)
            {
                rows.Add(new Row(badge.Name + " — " + badge.What,
                    badge.Earned ? Plain : Faint));
            }

            string who = string.IsNullOrEmpty(card.Name) ? Say("lbAnon") : card.Name;

            Sheet(who, card.Won + " / " + card.Badges.Count, rows);
        }

        /// <summary>Opens the supporter pack.</summary>
        private void Pack()
        {
            if (Modals == null) return;

            Modals.Supporter();
        }
    }
}
