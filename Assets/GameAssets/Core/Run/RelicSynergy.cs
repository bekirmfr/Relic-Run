using System.Collections.Generic;
using RelicRun.Core.Content;

namespace RelicRun.Core.Run
{
    /// <summary>
    /// How well a relic fits a loadout, as a number.
    /// </summary>
    /// <remarks>
    /// This is the game's own read of a build, and it is shipped rather than harness: the rival
    /// delvers in versus draft with it between rounds. It scores two things — whether the pick
    /// advances a set tier, and whether it joins the chain graph the loadout already has,
    /// meaning something in hand can fire it or it can feed something in hand. A relic whose
    /// trigger nothing in the run produces is marked down hard, because it would sit dead.
    /// </remarks>
    public static class RelicSynergy
    {
        /// <summary>Set sizes that unlock a tier. A pick that reaches one is worth taking.</summary>
        private static readonly int[] Tiers = { 3, 5, 7 };

        public static double Of(RelicId id, IReadOnlyList<RelicId> owned)
        {
            RelicDef def = RelicCatalog.Get(id);
            double score = 0;

            // How the loadout is already distributed across the kinds.
            var counts = new Dictionary<RelicKind, int>();
            for (int i = 0; i < owned.Count; i++)
            {
                RelicKind kind = RelicCatalog.KindOf(owned[i]);
                int n;
                counts[kind] = (counts.TryGetValue(kind, out n) ? n : 0) + 1;
            }

            int ofKind;
            if (!counts.TryGetValue(def.Kind, out ofKind)) ofKind = 0;

            int dominant = 0;
            foreach (KeyValuePair<RelicKind, int> pair in counts)
            {
                if (pair.Value > dominant) dominant = pair.Value;
            }

            if (System.Array.IndexOf(Tiers, ofKind + 1) >= 0)
            {
                // This pick is the one that opens a tier.
                score += 3;
            }
            else if (ofKind > 0)
            {
                // It rides a kind already being collected, with diminishing interest.
                score += System.Math.Min(2, ofKind * 0.5);
            }

            if (ofKind == dominant && dominant >= 2) score += 0.5;

            // The chain graph: what this loadout puts on the bus, and what listens for it.
            var produced = new HashSet<Channel>();
            var consumed = new HashSet<Channel>();
            for (int i = 0; i < owned.Count; i++)
            {
                RelicDef held = RelicCatalog.Get(owned[i]);
                for (int k = 0; k < held.Emits.Length; k++) produced.Add(held.Emits[k]);
                for (int k = 0; k < held.Reacts.Length; k++) consumed.Add(held.Reacts[k]);
            }

            // A fight puts these on the bus whatever anyone is carrying, and a Clover is the
            // only door to luck. Same set the draft's liveness check uses.
            produced.Add(Channel.Gold);
            produced.Add(Channel.Hit);
            produced.Add(Channel.Kill);
            produced.Add(Channel.Dmg);
            produced.Add(Channel.Heal);
            if (Contains(owned, RelicId.LuckyClover)) produced.Add(Channel.Luck);

            bool anyTriggerFires = false;
            for (int i = 0; i < def.Reacts.Length; i++)
            {
                if (!produced.Contains(def.Reacts[i])) continue;
                score += 2;
                anyTriggerFires = true;
            }

            for (int i = 0; i < def.Emits.Length; i++)
            {
                if (consumed.Contains(def.Emits[i])) score += 2;
            }

            // Nothing in this loadout can ever fire it.
            if (def.Reacts.Length > 0 && !anyTriggerFires) score -= 3;

            return score;
        }

        private static bool Contains(IReadOnlyList<RelicId> owned, RelicId id)
        {
            for (int i = 0; i < owned.Count; i++)
            {
                if (owned[i] == id) return true;
            }

            return false;
        }
    }
}
