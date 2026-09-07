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

        /// <summary>
        /// Whether a defender stops answering once its Thorn Vest has killed the striker.
        /// </summary>
        /// <remarks>
        /// The source's duel does: the thorns bite, and if that was the striker's last moment,
        /// the rest of the answer — the adrenaline, the marrow's second wind, the mirror — never
        /// happens. The delve answers on regardless.
        ///
        /// The delve is right, and versus takes its answer here as it does for the seven. The
        /// defender WAS hit — that is what provoked the thorns — so an Adrenaline Gland and a
        /// Troll Marrow have already earned their trigger. Whether the counter-blow happened to
        /// be lethal is nothing to do with them, and dropping their answer because it was is a
        /// short-circuit rather than a rule.
        ///
        /// Found by the lobby duel tier, which is the only place a defender was ever carrying
        /// both a Thorn Vest and something later in the ladder while the striker was low enough
        /// to die to it.
        /// </remarks>
        public bool ReactionsStopWhenTheStrikerFalls;

        /// <summary>
        /// Whether an awakened Hollow Idol backs the dominant family instead of counting twice.
        /// </summary>
        /// <remarks>
        /// See <see cref="Stats.SetCounts"/>. The source's answer — a second count toward every
        /// set — is unreachable there, because the bazaar only wakes what stacks and a Hollow
        /// Idol does not. It is kept for the corpus and nothing else.
        /// </remarks>
        public bool IdolBacksTheDominantKind;

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

        /// <summary>Whether an awakened Iron Skin shrugs one blow off outright each fight.</summary>
        public bool IronSkinGlancesOneBlow = true;

        /// <summary>Whether an awakened Hare's Drum's riposte also lands a blow of its own.</summary>
        public bool RiposteStrikesWhenAwakened = true;

        /// <summary>
        /// Whether the defender's answers to a blow all run inside the chain of the blow, or
        /// each open one of their own. It decides how far those answers decay: sharing one
        /// chain means a relic that answers twice is worth less the second time.
        /// </summary>
        public bool ReactionsShareOneChain = true;

        /// <summary>Whether the Curse set detonates as its bearer falls.</summary>
        public bool CurseSetExplodes = true;

        /// <summary>Whether the Greedy Curse's emitter fires as its bearer takes a hit.</summary>
        public bool GreedEmitterFiresOnPain = true;

        public static CombatRules Delve()
        {
            return new CombatRules { IdolBacksTheDominantKind = true };
        }

        /// <summary>
        /// The delve as the corpus recorded it. Nothing but the gate should ask for these.
        /// </summary>
        /// <remarks>
        /// The counterpart of <see cref="DuelAsRecorded"/>, and for now it differs in one place:
        /// an awakened Hollow Idol counted twice toward every set rather than backing the family
        /// the delver leans on.
        /// </remarks>
        public static CombatRules DelveAsRecorded()
        {
            return new CombatRules { IdolBacksTheDominantKind = false };
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
                StoneEmitterFloorsBeforeFizzle = false,
                EdgeSetSharpensOnKill = false,
                KillFiresTrigger = false,
                KillSharesTheBlowsChain = true,
                ReactionsShareOneChain = false,
            };
        }

        /// <summary>
        /// The duel exactly as the JS recorded it, which is what the corpus replays.
        /// </summary>
        /// <remarks>
        /// Seven rules that once separated a duel from a delve have been deliberately settled
        /// on the delve's answer, so <see cref="Duel"/> no longer reproduces the source. The
        /// gate that proves the port is still exact needs the old answers, and this is where
        /// they live — the only difference between this and the shipped rules IS the change,
        /// which makes it readable in one place and impossible to drift.
        ///
        /// Nothing but the corpus test should construct this.
        /// </remarks>
        public static CombatRules DuelAsRecorded()
        {
            CombatRules rules = Duel();

            rules.ArmorMeetsRelicDamage = false;
            rules.StaggerTripsOnBeingHit = true;
            rules.WhetstoneSundersOnlyPlainStrikes = false;
            rules.ReturnedBlowUsesTheStrikersAttack = false;
            rules.ReturnedBlowDepth = 1;
            rules.IronSkinGlancesOneBlow = false;
            rules.RiposteStrikesWhenAwakened = false;
            rules.ReactionsStopWhenTheStrikerFalls = true;

            return rules;
        }
    }
}
