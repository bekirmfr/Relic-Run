using System;
using System.Collections.Generic;
using RelicRun.Core.Content;

namespace RelicRun.Core.Combat
{
    /// <summary>
    /// One chain of consequences, opened by a genuine event and decaying as it deepens.
    /// </summary>
    /// <remarks>
    /// This is the heart of the game. A real strike, a real heal, a real coin — anything the
    /// world itself caused — opens a chain. Relics that consume it join one level deeper, and
    /// what they produce can feed further relics still.
    ///
    /// Decay is tracked <b>per relic</b>, not per depth. A relic's first activation inside a
    /// chain is full strength; its second is halved, its third quartered. That distinction is
    /// the whole design: a line running through five different relics never weakens, so long
    /// combos stay rewarding, while a relic feeding itself dies out within a few passes. Two
    /// copies of the same relic are also unaffected — they are one entry here, but each copy is
    /// scaled from the same count, so a stacked relic hits proportionally harder rather than
    /// decaying against itself.
    ///
    /// The CHAIN set softens the falloff: three of a kind moves it from 0.5 to 0.6, seven to
    /// 0.75. Depth itself only drives log indentation and the cap that stops runaways.
    /// </remarks>
    public sealed class ChainContext
    {
        /// <summary>Falloff with no CHAIN set bonus: each revisit halves the effect.</summary>
        public const double BaseDecay = 0.5;

        /// <summary>Falloff at three CHAIN relics.</summary>
        public const double SoftenedDecay = 0.6;

        /// <summary>Falloff at seven CHAIN relics — chains barely weaken at all.</summary>
        public const double BarelyDecay = 0.75;

        private readonly Dictionary<RelicId, int> _visits = new Dictionary<RelicId, int>();
        private readonly double _decay;

        public ChainContext(double decay)
        {
            _decay = decay;
        }

        /// <summary>The falloff for a loadout holding <paramref name="chainRelics"/> CHAIN relics.</summary>
        public static double DecayFor(int chainRelics)
        {
            if (chainRelics >= 7) return BarelyDecay;
            if (chainRelics >= 3) return SoftenedDecay;
            return BaseDecay;
        }

        /// <summary>
        /// Records another activation of this relic in this chain and returns its multiplier.
        /// Always call it when the relic activates, even if the effect is then skipped — the
        /// visit count is what makes the next pass weaker.
        /// </summary>
        public double Scale(RelicId relic)
        {
            _visits.TryGetValue(relic, out int seen);
            seen++;
            _visits[relic] = seen;
            return Math.Pow(_decay, seen - 1);
        }

        /// <summary>How many times a relic has already fired in this chain.</summary>
        public int Visits(RelicId relic)
        {
            _visits.TryGetValue(relic, out int seen);
            return seen;
        }
    }
}
