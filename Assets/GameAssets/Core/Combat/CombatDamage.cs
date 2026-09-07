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
        /// Everything a slipped blow sets off. The caller has already reported the miss.
        /// </summary>
        /// <remarks>
        /// A dodge is one genuine event, so it opens its own chain and every relic that answers
        /// decays inside it.
        /// </remarks>
        public static void OnDodge(ICombatActor dodger, ICombatBus bus, CombatRules rules)
        {
            IChain chain = bus.NewChain();

            // Without a Clover there is no dodge in the first place, so this always fires.
            double cloverScale = chain.Scale(dodger, RelicId.LuckyClover);
            CombatPrimitives.EmitLuck(dodger, bus, rules, dodger.Label(RelicId.LuckyClover), 1,
                RelicId.LuckyClover, chain, false);
            bus.FireEmitter(dodger, RelicId.LuckyClover, 0, chain, cloverScale);

            if (dodger.Effective(RelicId.LoadedHorseshoe) > 0)
            {
                CombatPrimitives.EmitLuck(dodger, bus, rules, dodger.Label(RelicId.LoadedHorseshoe), 1,
                    RelicId.LoadedHorseshoe, chain, false);
            }

            if (dodger.IsAwake(RelicId.LuckyClover) && dodger.Effective(RelicId.LuckyClover) > 0)
            {
                CombatPrimitives.EmitLuck(dodger, bus, rules, dodger.Label(RelicId.LuckyClover), 1,
                    RelicId.LuckyClover, chain, false);
            }

            // The Sentinel Bell rings defence, and once awakened rings a blow with it.
            int sentinel = dodger.Effective(RelicId.SentinelBell);
            if (sentinel > 0)
            {
                dodger.Sentinel += sentinel;
                bus.Line(dodger, RelicId.SentinelBell,
                    dodger.Label(RelicId.SentinelBell) + " +" + sentinel + " DEF", 1);
                if (dodger.IsAwake(RelicId.SentinelBell))
                {
                    bus.DealDamage(dodger, 1, dodger.Label(RelicId.SentinelBell), 1,
                        RelicId.SentinelBell, chain);
                }
            }

            // The Cat's Whisker counters once, or every time once awakened.
            if (dodger.Effective(RelicId.CatsWhisker) > 0 &&
                (dodger.IsAwake(RelicId.CatsWhisker) || !dodger.WhiskerUsed))
            {
                dodger.WhiskerUsed = true;
                bus.DealDamage(dodger, Math.Max(1, JsMath.RoundToInt(dodger.StatValue(Stat.Atk) / 2.0)),
                    dodger.Label(RelicId.CatsWhisker), 1, RelicId.CatsWhisker, chain);
            }

            // The Luck set turns every dodge into a counter.
            if (dodger.SetCount(RelicKind.Luck) >= 7)
            {
                bus.DealDamage(dodger, 2, "Luck set", 1, RelicId.LuckyClover, chain);
            }

            int stutter = dodger.Effective(RelicId.Stutterstep);
            if (stutter > 0)
            {
                bus.SlowOpponent(dodger, stutter, dodger.IsAwake(RelicId.Stutterstep));
            }

            if (dodger.Effective(RelicId.HaresDrum) > 0) dodger.InstantRiposte = true;

            bus.FireTrigger(dodger, SocketTrigger.Dodge, 0, RelicId.LuckyClover, RelicId.None, chain);
        }

        /// <summary>
        /// What a blow is reduced to, and everything the defender can do to stop it first.
        /// </summary>
        /// <remarks>
        /// One ladder for every blow in the game, driven by the DEFENDER's own relics. A delve
        /// foe carries relics but no Guard set, no Martyr's Knot and no Padded Hide, so the
        /// ladder collapses to armor alone when the foe is the one being hit — which is why
        /// the delve looked for a long time as though it needed a second, simpler version of
        /// this. It did not: the JS simply wrote the ladder out twice, once for the hero as
        /// defender and once for the foe.
        ///
        /// Returns <see cref="TurnedAside"/> when nothing lands at all.
        /// </remarks>
        public static int Mitigate(ICombatActor attacker, ICombatActor defender, ICombatBus bus,
            CombatRules rules, int amount, string source, int depth)
        {
            // Only a genuine strike meets a defender's relics. Armor is the exception in a
            // delve, where it stands against relic damage too.
            if (depth != 0)
            {
                return rules.ArmorMeetsRelicDamage
                    ? Armor(attacker, defender, bus, rules, amount, source)
                    : Math.Max(1, amount);
            }

            // The Guard set turns the opening blow of the fight aside entirely.
            if (defender.SetCount(RelicKind.Guard) >= 7 && !defender.Blocked)
            {
                defender.Blocked = true;
                bus.Line(defender, RelicId.None,
                    bus.SidePrefix(defender) + "Guard set — the first blow glances off", 0);
                return TurnedAside;
            }

            // An awakened Stutterstep leaves a side swinging into its own blade, and the two
            // modes disagree about which side that is. A delve trips the STRIKER on its own
            // turn — "their next strike hits themselves", as the relic reads. A duel trips the
            // side that is struck. Either way the blow lands on the striker, which is the
            // staggered one in a delve and the other one in a duel.
            ICombatActor tripping = rules.StaggerTripsOnBeingHit ? defender : attacker;
            if (tripping.Staggered)
            {
                tripping.Staggered = false;
                ThrowBack(attacker, defender, bus, rules, amount, RelicId.Stutterstep,
                    defender.Label(RelicId.Stutterstep) + " — the foe trips into its own blade");
                return TurnedAside;
            }

            // An awakened Martyr's Knot puts every third blow back on the striker.
            if (defender.IsAwake(RelicId.MartyrsKnot) && defender.Effective(RelicId.MartyrsKnot) > 0)
            {
                defender.MartyrCount++;
                if (defender.MartyrCount % 3 == 0)
                {
                    ThrowBack(attacker, defender, bus, rules, amount, RelicId.MartyrsKnot,
                        defender.Label(RelicId.MartyrsKnot) + " bears it — the blow lands on the foe");
                    return TurnedAside;
                }
            }

            // The Greedy Curse makes every blow bite one deeper — unless awakened, when it
            // charges the purse instead. The coin goes through the gold bus, so it feeds the
            // Vial and the Singer like any other coin.
            int greed = defender.Effective(RelicId.GreedyCurse);
            if (greed > 0)
            {
                if (defender.IsAwake(RelicId.GreedyCurse))
                {
                    CombatPrimitives.GainGold(defender, bus, rules, 2,
                        defender.Label(RelicId.GreedyCurse), 1, RelicId.GreedyCurse, bus.NewChain());
                }
                else
                {
                    amount += 1;
                }
            }

            int real = Armor(attacker, defender, bus, rules, amount, source);

            // An awakened Iron Skin shrugs one blow off outright each fight.
            if (defender.IsAwake(RelicId.IronSkin) && defender.Effective(RelicId.IronSkin) > 0 &&
                !defender.IronGlanced &&
                rules.IronSkinGlancesOneBlow)
            {
                defender.IronGlanced = true;
                bus.Line(defender, RelicId.IronSkin,
                    defender.Label(RelicId.IronSkin) + " — the blow glances off", 0);
                return TurnedAside;
            }

            defender.HitCount++;

            int hide = defender.Effective(RelicId.PaddedHide);
            if (defender.IsAwake(RelicId.PaddedHide) && hide > 0 &&
                defender.HitCount > 1 && defender.HideLearned > 0)
            {
                real = Math.Max(1, JsMath.RoundToInt(real * 0.5));
                bus.Line(defender, RelicId.PaddedHide,
                    defender.Label(RelicId.PaddedHide) + " has learned this blow", 1);
            }

            // Padded Hide softens only the first blow of the fight, and learns it for the rest.
            if (!defender.HitTaken)
            {
                defender.HitTaken = true;
                if (defender.IsAwake(RelicId.PaddedHide) && hide > 0) defender.HideLearned = 1;
                if (hide > 0)
                {
                    real -= 2 * hide;
                    bus.Line(defender, RelicId.PaddedHide,
                        defender.Label(RelicId.PaddedHide) + " softens the blow: -" + (2 * hide), 1);
                }
            }

            return Math.Max(1, real);
        }

        /// <summary>Armor, and the awakened Whetstone that cuts straight through it.</summary>
        private static int Armor(ICombatActor attacker, ICombatActor defender, ICombatBus bus,
            CombatRules rules, int amount, string source)
        {
            bool sunders = attacker.IsAwake(RelicId.Whetstone) && attacker.Effective(RelicId.Whetstone) > 0;
            if (sunders && rules.WhetstoneSundersOnlyPlainStrikes && source != bus.PlainStrikeLabel(attacker))
            {
                sunders = false;
            }

            return Defense.Apply(amount, sunders ? 0 : defender.StatValue(Stat.Def),
                bus.DefenseModelOf(defender));
        }

        /// <summary>A blow the defender turned around and sent back at the striker.</summary>
        private static void ThrowBack(ICombatActor attacker, ICombatActor defender, ICombatBus bus,
            CombatRules rules, int amount, RelicId relic, string line)
        {
            bus.Line(defender, relic, line, rules.ReturnedBlowDepth);
            if (attacker.Php <= 0) return;

            int back = rules.ReturnedBlowUsesTheStrikersAttack
                ? attacker.StatValue(Stat.Atk)
                : amount;

            bus.DealDamage(defender, Math.Max(1, back), defender.Label(relic), 1, relic, bus.NewChain());
        }

        /// <summary>Returned by <see cref="Mitigate"/> when nothing lands.</summary>
        public const int TurnedAside = -1;

        /// <summary>
        /// Landing a blow, from the checks that stop it to the answers that follow it.
        /// </summary>
        /// <remarks>
        /// The frame is shared; the middle is not. How a blow is reduced differs between the
        /// modes in shape rather than in degree — a delve foe is a stat block with no defences
        /// of its own to run, while a duellist meets a full defender every time — so
        /// <see cref="ICombatBus.Mitigate"/> stays with each engine and everything around it
        /// lives here.
        /// </remarks>
        public static void Deal(ICombatActor attacker, ICombatBus bus, CombatRules rules,
            int amount, string source, int depth, RelicId relic, IChain chain)
        {
            chain = chain ?? bus.NewChain();

            if (depth > rules.ChainCap)
            {
                bus.ReportFizzle(depth);
                return;
            }

            if (!bus.CanDeal(attacker)) return;

            if (amount <= 0)
            {
                // A relic that decayed to nothing still reports; a plain miss does not.
                if (relic != RelicId.None) bus.ReportFizzle(depth);
                return;
            }

            // Evasion lives entirely in the Lucky Clover, and only a genuine strike can be
            // slipped — relic damage always finds its mark.
            if (depth == 0 && bus.TargetEvades(attacker, source, depth, chain)) return;

            int dealt = Mitigate(attacker, bus.Opponent(attacker), bus, rules, amount, source, depth);
            if (dealt == TurnedAside) return;

            dealt = bus.ApplyDamage(attacker, dealt, source, depth, relic);

            // Ember Cask shakes gold loose from relic damage — the BLOOD to GOLD arc.
            int cask = attacker.Effective(RelicId.EmberCask);
            if (relic != RelicId.None && relic != RelicId.EmberCask && cask > 0)
            {
                double scale = chain.Scale(attacker, RelicId.EmberCask);
                int coins = JsMath.RoundToInt(cask * scale);
                if (coins > 0)
                {
                    CombatPrimitives.GainGold(attacker, bus, rules, coins,
                        attacker.Label(RelicId.EmberCask), depth + 1, RelicId.EmberCask, chain);
                    bus.FireEmitter(attacker, RelicId.EmberCask, depth, chain, scale);
                }
            }

            // A stat-block foe has nothing to refuse death with, so this is a no-op there.
            RefuseDeath(bus.Opponent(attacker), bus, rules);

            if (!bus.HasTarget(attacker))
            {
                if (bus.ClaimsTheKill(attacker))
                {
                    OnKill(attacker, bus, rules, depth, rules.KillSharesTheBlowsChain ? chain : null);
                }

                return;
            }

            bus.AfterDamage(attacker, dealt, depth, chain);
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
            bool wasCrit, ICombatBus bus, CombatRules rules, IChain blow)
        {
            DefenderReaction[] order = rules.ReactionOrder;
            for (int i = 0; i < order.Length; i++)
            {
                // A delve answers inside the chain of the blow itself, so a relic that answers
                // twice is worth less the second time. A duel gives each answer a fresh chain.
                IChain chain = rules.ReactionsShareOneChain ? blow : bus.NewChain();

                switch (order[i])
                {
                    case DefenderReaction.MirrorScale: MirrorScale(defender, damageDealt, wasCrit, bus, chain); break;
                    case DefenderReaction.Marrow: Marrow(defender, bus, rules, chain); break;
                    case DefenderReaction.AttackerLifesteal: bus.AttackerLifesteal(attacker, defender); break;
                    case DefenderReaction.Adrenaline: Adrenaline(defender, bus, rules, chain); break;
                    case DefenderReaction.Thorns:
                        Thorns(defender, bus, rules, chain);

                        // Thorns are the one answer that can kill, and in a duel a dead striker
                        // ends the answer: nothing after this fires.
                        if (rules.ReactionsStopWhenTheStrikerFalls && attacker.Php <= 0) return;
                        break;
                    case DefenderReaction.PainCadence: PainCadence(defender, bus, rules, chain); break;
                }
            }
        }

        /// <summary>Mirror Scale throws a critical hit back in full. Only a crit sets it off.</summary>
        private static void MirrorScale(ICombatActor defender, int damageDealt, bool wasCrit,
            ICombatBus bus, IChain chain)
        {
            if (!wasCrit || defender.Effective(RelicId.MirrorScale) == 0) return;
            if (defender.Php <= 0 || !bus.HasTarget(defender)) return;

            bus.DealDamage(defender, damageDealt, defender.Label(RelicId.MirrorScale), 1,
                RelicId.MirrorScale, chain);
        }

        /// <summary>Troll Marrow knits a bloodied defender back together.</summary>
        private static void Marrow(ICombatActor defender, ICombatBus bus, CombatRules rules, IChain chain)
        {
            int marrow = defender.Effective(RelicId.TrollMarrow);
            if (marrow == 0 || defender.Php <= 0 || defender.Php >= defender.Pmax / 2.0) return;
            CombatPrimitives.Heal(defender, bus, rules, 2 * marrow,
                defender.Label(RelicId.TrollMarrow), 1, RelicId.TrollMarrow, chain);

            // Awakened, it knits from the attacker's flesh rather than its own. Where answers
            // do not share a chain, the bite opens one of its own rather than continuing the
            // heal's — so the two halves of one relic decay independently.
            if (defender.IsAwake(RelicId.TrollMarrow) && bus.HasTarget(defender))
            {
                bus.DealDamage(defender, 2 * marrow, defender.Label(RelicId.TrollMarrow), 2,
                    RelicId.TrollMarrow, rules.ReactionsShareOneChain ? chain : bus.NewChain());
            }
        }

        /// <summary>The Adrenaline Gland banks permanent attack the first time it is hurt.</summary>
        private static void Adrenaline(ICombatActor defender, ICombatBus bus, CombatRules rules, IChain chain)
        {
            int adrenaline = CombatPrimitives.ReactionCount(
                defender, RelicTuning.For(RelicId.AdrenalineGland, rules.Mode), RelicId.AdrenalineGland);

            if (adrenaline <= 0 || defender.Php <= 0 || bus.AdrenalineSpent(defender)) return;

            bus.SpendAdrenaline(defender);
            double scale = chain.Scale(defender, RelicId.AdrenalineGland);

            defender.Adrenaline += adrenaline;
            bus.ReportAdrenalineGain(defender, adrenaline);
            bus.FireEmitter(defender, RelicId.AdrenalineGland, 0, chain, scale);
        }

        /// <summary>Thorn Vest answers the blow that landed, not the one it threw.</summary>
        private static void Thorns(ICombatActor defender, ICombatBus bus, CombatRules rules, IChain chain)
        {
            int thorns = defender.Effective(RelicId.ThornVest);
            if (thorns <= 0 || defender.Php <= 0) return;
            double scale = chain.Scale(defender, RelicId.ThornVest);

            bus.DealDamage(defender,
                JsMath.RoundToInt(2 * thorns * scale) + (defender.SetCount(RelicKind.Guard) >= 5 ? 1 : 0),
                defender.Label(RelicId.ThornVest), 1, RelicId.ThornVest, chain);

            bus.FireEmitter(defender, RelicId.ThornVest, 0, chain, scale);
        }

        /// <summary>Only a hit the defender survived counts toward the pain cadence.</summary>
        private static void PainCadence(ICombatActor defender, ICombatBus bus, CombatRules rules, IChain chain)
        {
            if (defender.Php <= 0) return;

            if (rules.GreedEmitterFiresOnPain && defender.CountRaw(RelicId.GreedyCurse) > 0)
            {
                bus.FireEmitter(defender, RelicId.GreedyCurse, 0, chain,
                    chain.Scale(defender, RelicId.GreedyCurse));
            }

            defender.PainCount++;

            // Socketed pain triggers fire on every third hit taken.
            if (defender.PainCount % 3 == 0)
            {
                bus.FireTrigger(defender, SocketTrigger.Hit, 0, RelicId.None, RelicId.None, chain);
            }
        }
    }
}
