using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Combat
{
    /// <summary>
    /// The chain bus: the primitives every effect in the game ultimately goes through.
    /// </summary>
    /// <remarks>
    /// Kept as part of <see cref="CombatEngine"/> rather than split into its own type, because
    /// these operations read and write the fight's live state on every line — enemy HP, the
    /// hero's pool and purse, the counters. A separate class would only take the engine as a
    /// parameter and pass everything straight back, which is a boundary in name only. The split
    /// here is by file, so the bus reads on its own without inventing a fake seam.
    ///
    /// Each primitive takes <c>(amount, source, depth, relic, chain)</c>. The relic is the
    /// chain's provenance: set, the event was caused by a relic and joins the existing chain;
    /// unset, it is a genuine event that opens a new one.
    /// </remarks>
    public sealed partial class CombatEngine
    {
        private void DealDamage(int amount, string source, int depth,
            RelicId relic = RelicId.None, ChainContext chain = null)
        {
            CombatDamage.Deal(_actor, this, _rules, amount, source, depth, relic, chain ?? NewChain());
        }




        /// <summary>
        /// Puts a luck signal on the bus. It carries no value of its own — relics that listen
        /// for luck decide what it is worth.
        /// </summary>
        /// <param name="quiet">
        /// Emit without a log line, for a signal whose cause has already been reported.
        /// </param>

        // ---------- the shared primitives ----------

        private void Heal(int amount, string source, int depth,
            RelicId relic = RelicId.None, ChainContext chain = null)
        {
            CombatPrimitives.Heal(_actor, this, _rules, amount, source, depth, relic, chain ?? NewChain());
        }

        private void GainGold(int amount, string source, int depth,
            RelicId relic = RelicId.None, ChainContext chain = null)
        {
            CombatPrimitives.GainGold(_actor, this, _rules, amount, source, depth, relic, chain ?? NewChain());
        }

        private void EmitLuck(string source, int depth, RelicId relic, ChainContext chain, bool quiet = false)
        {
            CombatPrimitives.EmitLuck(_actor, this, _rules, source, depth, relic, chain ?? NewChain(), quiet);
        }

    }
}
