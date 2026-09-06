using System;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Combat
{
    /// <summary>What a defender does about a blow that landed on them.</summary>
    public enum DefenderReaction
    {
        /// <summary>Mirror Scale throwing a critical hit straight back.</summary>
        MirrorScale,

        /// <summary>Troll Marrow knitting a bloodied defender together.</summary>
        Marrow,

        /// <summary>The attacker's Vampire Tooth drinking from the wound it just opened.</summary>
        AttackerLifesteal,

        /// <summary>The Adrenaline Gland banking permanent attack from the first hurt.</summary>
        Adrenaline,

        /// <summary>Thorn Vest biting whoever struck it.</summary>
        Thorns,

        /// <summary>The Greedy Curse's socketed emitter, and the pain cadence.</summary>
        PainCadence,
    }

    /// <summary>
    /// The defender's half of a landed blow: refusing to fall, then answering.
    /// </summary>
    /// <remarks>
    /// Both modes run the same reactions; they run them in a different ORDER, which is why the
    /// sequence is data rather than code. A delve answers with Mirror Scale first and Thorn Vest
    /// last; a duel does the reverse. Nothing here reads the mode — it walks the list it is
    /// handed.
    ///
    /// Applying the blow itself is still each engine's own, because a delve foe is a stat block
    /// whose crit multiplies AFTER mitigation while a full side's multiplies before. That is a
    /// difference in the shape of the calculation, not a number, and folding it in would mean
    /// changing what a crit means rather than where it is written.
    /// </remarks>
    public static class CombatDamage
    {
        /// <summary>The order a delve answers in.</summary>
        public static readonly DefenderReaction[] DelveOrder =
        {
            DefenderReaction.MirrorScale,
            DefenderReaction.Marrow,
            DefenderReaction.AttackerLifesteal,
            DefenderReaction.Adrenaline,
            DefenderReaction.Thorns,
            DefenderReaction.PainCadence,
        };

        /// <summary>The order a duel answers in.</summary>
        public static readonly DefenderReaction[] DuelOrder =
        {
            DefenderReaction.Thorns,
            DefenderReaction.Adrenaline,
            DefenderReaction.Marrow,
            DefenderReaction.MirrorScale,
            DefenderReaction.PainCadence,
        };

        /// <summary>
        /// The death-defiance ladder, in the order it is tried: the Flesh set once per floor,
        /// then Gravekeeper's Soil once per run, then the Curse set going out in a blast.
        /// </summary>
        public static void RefuseDeath(ICombatActor defender, ICombatBus bus, CombatRules rules)
        {
            if (defender.Php > 0) return;

            if (defender.SetCount(RelicKind.Flesh) >= 7 && !bus.FleshSetSpent(defender))
            {
                bus.SpendFleshSet(defender);
                defender.Php = 1;
                bus.Line(defender, RelicId.None, bus.RefusesToFallLabel(defender), 0);
            }

            if (defender.Php <= 0 && defender.Effective(RelicId.GravekeepersSoil) > 0 &&
                !bus.SoilSpent(defender))
            {
                bus.SpendSoil(defender);

                RelicTuning soil = RelicTuning.For(RelicId.GravekeepersSoil, rules.Mode);
                double fraction = defender.IsAwake(RelicId.GravekeepersSoil)
                    ? soil.ReviveFractionAwakened
                    : soil.ReviveFraction;

                defender.Php = Math.Max(1, JsMath.RoundToInt(defender.Pmax * fraction));
                bus.Line(defender, RelicId.GravekeepersSoil, bus.SoilLabel(defender), 0);
            }

            // The Curse set goes out in a blast. Unreachable in the recorded corpus, which never
            // rolls seven CURSE relics, so this path is ported but not verified.
            if (defender.Php <= 0 && rules.CurseSetExplodes && defender.SetCount(RelicKind.Curse) >= 7 &&
                !bus.CurseSetSpent(defender) && bus.HasTarget(defender))
            {
                bus.SpendCurseSet(defender);
                bus.DealDamage(defender, defender.StatValue(Stat.Atk) * 3, "Curse set", 1,
                    RelicId.None, bus.NewChain());
            }
        }

        /// <summary>
        /// A kill, and everything that answers one.
        /// </summary>
        public static void OnKill(ICombatActor killer, ICombatBus bus, CombatRules rules, int depth, IChain chain)
        {
            killer.Kills++;
            bus.ReportKill(killer, depth);

            // The kill is one genuine event, so everything it sets off shares a single chain.
            chain = chain ?? bus.NewChain();

            // The Edge set sharpens permanently with every kill this floor.
            if (rules.EdgeSetSharpensOnKill && killer.SetCount(RelicKind.Edge) >= 7)
            {
                killer.HeadsmanBonus += 1;
                bus.Line(killer, RelicId.None, "Edge set — +1 ATK", 1);
            }

            CombatPrimitives.GainGold(killer, bus, rules, bus.LootFor(killer), bus.LootLabel,
                0, RelicId.None, chain);

            // Tollkeeper's Ring collects at the gate on every death.
            int toll = killer.Effective(RelicId.TollkeepersRing);
            if (toll > 0 && RelicTuning.For(RelicId.TollkeepersRing, rules.Mode).AnswersOnKill)
            {
                double scale = chain.Scale(killer, RelicId.TollkeepersRing);
                CombatPrimitives.GainGold(killer, bus, rules,
                    Math.Max(1, JsMath.RoundToInt(5 * toll * scale)),
                    killer.Label(RelicId.TollkeepersRing), 1, RelicId.TollkeepersRing, chain);
                bus.FireEmitter(killer, RelicId.TollkeepersRing, 0, chain, scale);
            }

            // Coin Magnet takes no node of its own — it swelled the loot above — but its
            // socketed emitter still answers.
            int magnet = killer.Effective(RelicId.CoinMagnet);
            if (magnet > 0 && RelicTuning.For(RelicId.CoinMagnet, rules.Mode).AmplifiesLoot)
            {
                bus.FireEmitter(killer, RelicId.CoinMagnet, 0, chain, chain.Scale(killer, RelicId.CoinMagnet));
            }

            if (rules.KillFiresTrigger)
            {
                bus.FireTrigger(killer, SocketTrigger.Kill, 0, RelicId.CoinMagnet, RelicId.None, chain);
            }
        }

        /// <summary>
        /// Executioner's Coin finishes a foe already on the edge. It fires once per floor in a
        /// delve and once per duel in versus, and the window is a share of the target's pool.
        /// </summary>
        public static bool TryExecute(ICombatActor attacker, ICombatBus bus, CombatRules rules)
        {
            int copies = attacker.Effective(RelicId.ExecutionersCoin);
            if (copies == 0 || bus.ExecutionSpent(attacker) || bus.TargetHealth(attacker) <= 0)
            {
                return false;
            }

            RelicTuning coin = RelicTuning.For(RelicId.ExecutionersCoin, rules.Mode);
            double share = attacker.IsAwake(RelicId.ExecutionersCoin)
                ? coin.ExecuteThresholdAwakened
                : coin.ExecuteThreshold;

            double window = bus.TargetMaxHealth(attacker) * share;
            if (coin.ExecuteScalesWithCopies) window *= Math.Min(2, copies);

            if (bus.TargetHealth(attacker) > window) return false;

            bus.SpendExecution(attacker);
            bus.Line(attacker, RelicId.ExecutionersCoin,
                attacker.Label(RelicId.ExecutionersCoin) + " — the sentence is carried out", 0);

            // Defence is added back so the blow is still lethal after reduction.
            bus.DealDamage(attacker, bus.TargetHealth(attacker) + bus.TargetDefence(attacker),
                attacker.Label(RelicId.ExecutionersCoin), 1, RelicId.ExecutionersCoin, bus.NewChain());
            return true;
        }

        /// <summary>Runs the defender's answers in this mode's order.</summary>
        public static void React(ICombatActor defender, ICombatActor attacker, int damageDealt,
            bool wasCrit, ICombatBus bus, CombatRules rules)
        {
            DefenderReaction[] order = rules.ReactionOrder;
            for (int i = 0; i < order.Length; i++)
            {
                switch (order[i])
                {
                    case DefenderReaction.MirrorScale: MirrorScale(defender, damageDealt, wasCrit, bus); break;
                    case DefenderReaction.Marrow: Marrow(defender, bus, rules); break;
                    case DefenderReaction.AttackerLifesteal: bus.AttackerLifesteal(attacker, defender); break;
                    case DefenderReaction.Adrenaline: Adrenaline(defender, bus, rules); break;
                    case DefenderReaction.Thorns: Thorns(defender, bus, rules); break;
                    case DefenderReaction.PainCadence: PainCadence(defender, bus, rules); break;
                }
            }
        }

        /// <summary>Mirror Scale throws a critical hit back in full. Only a crit sets it off.</summary>
        private static void MirrorScale(ICombatActor defender, int damageDealt, bool wasCrit, ICombatBus bus)
        {
            if (!wasCrit || defender.Effective(RelicId.MirrorScale) == 0) return;
            if (defender.Php <= 0 || !bus.HasTarget(defender)) return;

            bus.DealDamage(defender, damageDealt, defender.Label(RelicId.MirrorScale), 1,
                RelicId.MirrorScale, bus.NewChain());
        }

        /// <summary>Troll Marrow knits a bloodied defender back together.</summary>
        private static void Marrow(ICombatActor defender, ICombatBus bus, CombatRules rules)
        {
            int marrow = defender.Effective(RelicId.TrollMarrow);
            if (marrow == 0 || defender.Php <= 0 || defender.Php >= defender.Pmax / 2.0) return;

            IChain chain = bus.NewChain();
            CombatPrimitives.Heal(defender, bus, rules, 2 * marrow,
                defender.Label(RelicId.TrollMarrow), 1, RelicId.TrollMarrow, chain);

            // Awakened, it knits from the attacker's flesh rather than its own.
            if (defender.IsAwake(RelicId.TrollMarrow) && bus.HasTarget(defender))
            {
                bus.DealDamage(defender, 2 * marrow, defender.Label(RelicId.TrollMarrow), 2,
                    RelicId.TrollMarrow, chain);
            }
        }

        /// <summary>The Adrenaline Gland banks permanent attack the first time it is hurt.</summary>
        private static void Adrenaline(ICombatActor defender, ICombatBus bus, CombatRules rules)
        {
            int adrenaline = CombatPrimitives.ReactionCount(
                defender, RelicTuning.For(RelicId.AdrenalineGland, rules.Mode), RelicId.AdrenalineGland);

            if (adrenaline <= 0 || defender.Php <= 0 || bus.AdrenalineSpent(defender)) return;

            bus.SpendAdrenaline(defender);
            IChain chain = bus.NewChain();
            double scale = chain.Scale(defender, RelicId.AdrenalineGland);

            defender.Adrenaline += adrenaline;
            bus.ReportAdrenalineGain(defender, adrenaline);
            bus.FireEmitter(defender, RelicId.AdrenalineGland, 0, chain, scale);
        }

        /// <summary>Thorn Vest answers the blow that landed, not the one it threw.</summary>
        private static void Thorns(ICombatActor defender, ICombatBus bus, CombatRules rules)
        {
            int thorns = defender.Effective(RelicId.ThornVest);
            if (thorns <= 0 || defender.Php <= 0) return;

            IChain chain = bus.NewChain();
            double scale = chain.Scale(defender, RelicId.ThornVest);

            bus.DealDamage(defender,
                JsMath.RoundToInt(2 * thorns * scale) + (defender.SetCount(RelicKind.Guard) >= 5 ? 1 : 0),
                defender.Label(RelicId.ThornVest), 1, RelicId.ThornVest, chain);

            bus.FireEmitter(defender, RelicId.ThornVest, 0, chain, scale);
        }

        /// <summary>Only a hit the defender survived counts toward the pain cadence.</summary>
        private static void PainCadence(ICombatActor defender, ICombatBus bus, CombatRules rules)
        {
            if (defender.Php <= 0) return;

            if (rules.GreedEmitterFiresOnPain && defender.CountRaw(RelicId.GreedyCurse) > 0)
            {
                IChain chain = bus.NewChain();
                bus.FireEmitter(defender, RelicId.GreedyCurse, 0, chain,
                    chain.Scale(defender, RelicId.GreedyCurse));
            }

            defender.PainCount++;

            // Socketed pain triggers fire on every third hit taken.
            if (defender.PainCount % 3 == 0)
            {
                bus.FireTrigger(defender, SocketTrigger.Hit, 0, RelicId.None, RelicId.None, bus.NewChain());
            }
        }
    }
}
