namespace RelicRun.Core.Combat
{
    /// <summary>
    /// The parameters that differ between a delve and a duel.
    /// </summary>
    /// <remarks>
    /// Every field here is a balance dial, not a change of behaviour: a cap, a cadence, a
    /// threshold, or which side a relic reads. They are collected in one place precisely so
    /// that the relic layer stays single-sourced — architecture invariant 3 exists because a
    /// relic rule fixed in one engine and missed in the other is this game's most common bug.
    /// </remarks>
    public sealed class CombatRules
    {
        /// <summary>Chain depth past which effects fizzle.</summary>
        public int ChainCap = 40;

        /// <summary>Whether Hollow Idol counts itself toward every set.</summary>
        public bool HollowIdolCountsTowardSets = true;

        /// <summary>Rabbit's Foot answers every Nth luck signal. Duels throttle it.</summary>
        public int RabbitSignalCadence = 1;

        /// <summary>Ceiling on Sentinel Bell's defence, before awakening.</summary>
        public int SentinelCap = 3;

        /// <summary>Ceiling on Sentinel Bell's defence once awakened.</summary>
        public int SentinelCapAwakened = 3;

        /// <summary>Whether an opponent's Famine Bell starves you as well as your own.</summary>
        public bool FamineBellStarvesOpponent;

        /// <summary>
        /// Whether a speed gain is logged before it lands. Every event carries a full state
        /// snapshot, so this decides whether that line reports the old speed or the new one.
        /// </summary>
        public bool LogSpeedGainBeforeApplying = true;

        /// <summary>
        /// Relics whose activation effect exists only in this mode. The delve grants Greedy
        /// Curse, Piggy Bank and Coin Magnet an activation; the duel does not.
        /// </summary>
        public bool GreedRelicsHaveActivation = true;

        /// <summary>The delve's rules, as the source runs them.</summary>
        public static CombatRules Delve()
        {
            return new CombatRules();
        }

        /// <summary>
        /// The duel's rules. Chains are capped hard because a duel runs long enough that a
        /// forty-deep loop would decide the match on its own.
        /// </summary>
        public static CombatRules Duel()
        {
            return new CombatRules
            {
                ChainCap = 4,
                HollowIdolCountsTowardSets = false,
                RabbitSignalCadence = 3,
                SentinelCap = 3,
                SentinelCapAwakened = 5,
                FamineBellStarvesOpponent = true,
                GreedRelicsHaveActivation = false,
                LogSpeedGainBeforeApplying = false,
            };
        }
    }
}
