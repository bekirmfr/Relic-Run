namespace RelicRun.Core.Combat
{
    /// <summary>
    /// What differs between a delve and a duel at the level of the ENGINE.
    /// </summary>
    /// <remarks>
    /// This is deliberately small. Most of what separates the two modes turned out to be
    /// relic behaviour rather than engine behaviour, and lives with the relic in
    /// <see cref="Content.RelicTuning"/> — that is where a designer would look for it, and it
    /// keeps the engine from branching on the mode at all.
    ///
    /// What is left here genuinely belongs to the fight itself rather than to any one relic.
    /// </remarks>
    public sealed class CombatRules
    {
        public Content.CombatMode Mode = Content.CombatMode.Delve;

        /// <summary>
        /// Chain depth past which effects fizzle. A duel caps hard at 4: it runs long enough
        /// that a forty-deep loop would decide the match on its own.
        /// </summary>
        public int ChainCap = 40;

        /// <summary>
        /// Whether a speed gain is logged before it lands. Every event carries a full state
        /// snapshot, so this decides whether that line reports the old speed or the new one.
        /// An inconsistency in the source rather than a design choice, reproduced as written.
        /// </summary>
        public bool LogSpeedGainBeforeApplying = true;

        /// <summary>
        /// The order the defender answers a landed blow in. Both modes run the same reactions;
        /// only the sequence differs, so it is data rather than a branch.
        /// </summary>
        public DefenderReaction[] ReactionOrder = CombatDamage.DelveOrder;

        /// <summary>
        /// Whether the Stone Emitter's minimum is applied before deciding an effect fizzled.
        /// A delve floors it first, so a decayed Stone activation still hardens by one; a duel
        /// checks for nothing first, so the same activation fizzles.
        /// </summary>
        public bool StoneEmitterFloorsBeforeFizzle = true;

        /// <summary>
        /// Whether a kill continues the chain of the blow that earned it. A delve treats loot
        /// as a genuine event and opens a fresh chain; a duel lets its purse decay with what
        /// preceded it.
        /// </summary>
        public bool KillSharesTheBlowsChain;

        /// <summary>Whether the Edge set sharpens permanently with every kill.</summary>
        public bool EdgeSetSharpensOnKill = true;

        /// <summary>Whether a kill puts a socketed trigger on the bus.</summary>
        public bool KillFiresTrigger = true;

        /// <summary>
        /// Whether armor meets relic damage as well as a genuine strike. A delve reduces every
        /// blow; a duel reduces only what a duellist actually swings.
        /// </summary>
        public bool ArmorMeetsRelicDamage = true;

        /// <summary>
        /// Where an awakened Stutterstep's stagger is spent. A delve spends it on the staggered
        /// side's own turn — "their next strike hits themselves", as the relic reads. A duel
        /// spends it when the staggered side is struck instead.
        /// </summary>
        public bool StaggerTripsOnBeingHit;

        /// <summary>
        /// Whether an awakened Whetstone cuts through armor on every blow or only on a plain
        /// strike. A delve exempts crits and relic damage; a duel sunders with everything.
        /// </summary>
        public bool WhetstoneSundersOnlyPlainStrikes = true;

        /// <summary>
        /// Which depth a returned blow's line sits at. A delve reports it as the whole of the
        /// foe's turn; a duel reports it as a consequence of the blow that was turned aside.
        /// </summary>
        public int ReturnedBlowDepth;

        /// <summary>
        /// What a Martyr's Knot or a stagger throws back: the striker's own attack, or the
        /// blow that was turned aside. They differ once the striker is enraged or charged.
        /// </summary>
        public bool ReturnedBlowUsesTheStrikersAttack = true;

        /// <summary>Whether the Curse set detonates as its bearer falls.</summary>
        public bool CurseSetExplodes = true;

        /// <summary>Whether the Greedy Curse's emitter fires as its bearer takes a hit.</summary>
        public bool GreedEmitterFiresOnPain = true;

        public static CombatRules Delve()
        {
            return new CombatRules();
        }

        public static CombatRules Duel()
        {
            return new CombatRules
            {
                Mode = Content.CombatMode.Versus,
                ChainCap = 4,
                LogSpeedGainBeforeApplying = false,
                ReactionOrder = CombatDamage.DuelOrder,
                CurseSetExplodes = false,
                GreedEmitterFiresOnPain = false,
                ArmorMeetsRelicDamage = false,
                StaggerTripsOnBeingHit = true,
                WhetstoneSundersOnlyPlainStrikes = false,
                ReturnedBlowUsesTheStrikersAttack = false,
                ReturnedBlowDepth = 1,
                StoneEmitterFloorsBeforeFizzle = false,
                EdgeSetSharpensOnKill = false,
                KillFiresTrigger = false,
                KillSharesTheBlowsChain = true,
            };
        }
    }
}
