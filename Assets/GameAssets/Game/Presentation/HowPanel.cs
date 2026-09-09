using System;
using System.Collections.Generic;
using RelicRun.Core.Presentation;
using RelicRun.Game.Services;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// How to play: one translated paragraph, taken apart and numbered.
    /// </summary>
    /// <remarks>
    /// A sheet like the four reading screens, because that is exactly what it is — a heading and
    /// a numbered list. The number of steps belongs to the translation rather than to the game,
    /// so nothing here counts them.
    /// </remarks>
    public sealed class HowPanel : SheetPanel
    {
        public override Page Shows
        {
            get { return Page.How; }
        }

        public override void Draw(SaveVault vault, DateTimeOffset now)
        {
            HowCard card = HowCards.Of(Say("howtoBody"));

            var rows = new List<Row>(card.Steps.Count);

            foreach (HowStep step in card.Steps)
            {
                rows.Add(new Row(step.Number + "  " + step.Text, Plain));
            }

            Sheet(Say("howToPlay"), Say("takeRelic"), rows);
        }
    }
}
