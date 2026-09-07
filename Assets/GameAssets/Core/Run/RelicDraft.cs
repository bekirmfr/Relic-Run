using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;

namespace RelicRun.Core.Run
{
    /// <summary>
    /// What the run offers, and what taking it does.
    /// </summary>
    /// <remarks>
    /// Which relics an offer holds is a rule; the order they are ranked in for a player is not,
    /// and is not ported. A draft weights against what is already in hand, so a second copy of
    /// something is half as likely as the first and a third half as likely again.
    /// </remarks>
    public static class RelicDraft
    {
        /// <summary>
        /// Relics that may be offered: everything not already held, unless it stacks, and
        /// nothing whose trigger this loadout could never pull.
        /// </summary>
        public static List<RelicId> Available(IReadOnlyList<RelicId> owned)
        {
            var avail = new List<RelicId>(RelicCatalog.All.Count);
            for (int i = 0; i < RelicCatalog.All.Count; i++)
            {
                RelicDef def = RelicCatalog.All[i];
                if (!def.Stackable && Contains(owned, def.Id)) continue;
                if (!IsLive(def.Id, owned)) continue;
                avail.Add(def.Id);
            }

            return avail;
        }

        /// <summary>
        /// Draws one relic, weighted by 0.5^copies so duplicates thin out.
        /// </summary>
        public static RelicId Weighted(IReadOnlyList<RelicId> avail, IReadOnlyList<RelicId> owned,
            Mulberry32 rng)
        {
            if (avail.Count == 0) return RelicId.None;

            double total = 0;
            var w = new double[avail.Count];
            for (int i = 0; i < avail.Count; i++)
            {
                w[i] = System.Math.Pow(0.5, CountOf(owned, avail[i]));
                total += w[i];
            }

            double x = rng.Next() * total;
            for (int i = 0; i < avail.Count; i++)
            {
                x -= w[i];
                if (x <= 0) return avail[i];
            }

            return avail[avail.Count - 1];
        }

        /// <summary>Rolls one offer of up to <paramref name="choices"/> distinct relics.</summary>
        public static List<RelicId> RollOffer(IReadOnlyList<RelicId> owned, int choices, Mulberry32 rng)
        {
            List<RelicId> avail = Available(owned);
            var offer = new List<RelicId>(choices);
            int want = System.Math.Min(choices, avail.Count);

            while (offer.Count < want)
            {
                RelicId id = Weighted(avail, owned, rng);
                if (!offer.Contains(id)) offer.Add(id);
            }

            return offer;
        }

        /// <summary>
        /// Whether a relic can do anything at all with this loadout: one whose trigger nothing
        /// in hand could ever pull is never offered.
        /// </summary>
        public static bool IsLive(RelicId id, IReadOnlyList<RelicId> owned)
        {
            Channel[] reacts = RelicCatalog.Get(id).Reacts;
            if (reacts.Length == 0) return true;

            for (int i = 0; i < reacts.Length; i++)
            {
                if (Produced(reacts[i], owned)) return true;
            }

            return false;
        }

        /// <summary>Whether anything can put this channel on the bus.</summary>
        private static bool Produced(Channel channel, IReadOnlyList<RelicId> owned)
        {
            switch (channel)
            {
                // A fight produces these whatever the hero is carrying.
                case Channel.Gold:
                case Channel.Hit:
                case Channel.Kill:
                case Channel.Dmg:
                case Channel.Heal:
                    return true;

                // Luck arrives with a Lucky Clover, the only door to a dodge.
                case Channel.Luck:
                    if (Contains(owned, RelicId.LuckyClover)) return true;
                    break;
            }

            // Otherwise something in hand has to emit it.
            for (int i = 0; i < owned.Count; i++)
            {
                Channel[] emits = RelicCatalog.Get(owned[i]).Emits;
                for (int k = 0; k < emits.Length; k++)
                {
                    if (emits[k] == channel) return true;
                }
            }

            return false;
        }

        private static int CountOf(IReadOnlyList<RelicId> list, RelicId id)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == id) n++;
            }

            return n;
        }

        private static bool Contains(IReadOnlyList<RelicId> list, RelicId id)
        {
            return CountOf(list, id) > 0;
        }
    }
}
