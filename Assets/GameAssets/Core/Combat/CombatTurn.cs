using System;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Combat
{
    /// <summary>
    /// One side's attack, from the wind-up to everything the landed blow sets off.
    /// </summary>
    /// <remarks>
    /// A delve hero and either duellist take the same turn: harden the Anvil, swear the Oath,
    /// try to execute, strike or crit, build momentum, try again, and then spend what the blow
    /// shook loose. It existed twice before, which meant every relic in that sequence — Anvil
    /// Heart, Duelist's Oath, Executioner's Coin, Weighted Dice, Fortune's Edge, Momentum Bead,
    /// Cutpurse's Hook, Vampire Tooth — had two implementations to keep in step.
    /// </remarks>
    public static class CombatTurn
    {
        /// <summary>
        /// The Hare's Drum: a dodge hands the next action straight back. Returns whether one
        /// was owed, so the scheduler can zero the gauge.
        /// </summary>
        public static bool Riposte(ICombatActor actor, ICombatBus bus, CombatRules rules)
        {
            if (!actor.InstantRiposte) return false;
            actor.InstantRiposte = false;

            if (actor.IsAwake(RelicId.HaresDrum) && rules.RiposteStrikesWhenAwakened)
            {
                bus.DealDamage(actor, 2, actor.Label(RelicId.HaresDrum), 1,
                    RelicId.HaresDrum, bus.NewChain());
            }

            bus.Line(actor, RelicId.HaresDrum, actor.Label(RelicId.HaresDrum) + " — instant riposte!", 0);
            return true;
        }

        /// <summary>Awakened Swift Boots skip the wait on every fourth strike.</summary>
        public static bool FreeAction(ICombatActor actor)
        {
            if (!actor.IsAwake(RelicId.SwiftBoots) || actor.Effective(RelicId.SwiftBoots) == 0) return false;
            if (actor.Strikes <= 0 || actor.Strikes % 4 != 0) return false;
            if (actor.BootsUsedOnStrike == actor.Strikes) return false;

            actor.BootsUsedOnStrike = actor.Strikes;
            return true;
        }

        public static void Strike(ICombatActor attacker, ICombatBus bus, CombatRules rules, IChain unusedChain)
        {
            bus.BeginBeat(attacker);

            attacker.Strikes++;
            attacker.StrikeTotal++;

            // Anvil Heart hardens a notch every ten strikes across the whole run.
            int anvil = attacker.Effective(RelicId.AnvilHeart);
            if (anvil > 0 && attacker.StrikeTotal % 10 == 0 &&
                attacker.AnvilBonus < (attacker.IsAwake(RelicId.AnvilHeart) ? 12 : 10))
            {
                attacker.AnvilBonus++;
                attacker.DefenceBonus++;
                bus.Line(attacker, RelicId.AnvilHeart,
                    attacker.Label(RelicId.AnvilHeart) + " hardens: +1 DEF", 1);
            }

            // Duelist's Oath announces itself on the opening blow against worthy blood. Every
            // rival counts as worthy; in a delve only elites and bosses do.
            int duelist = attacker.Effective(RelicId.DuelistsOath);
            if (attacker.Strikes == 1 && duelist > 0 && bus.FacingWorthyBlood(attacker))
            {
                bus.Line(attacker, RelicId.DuelistsOath,
                    attacker.Label(RelicId.DuelistsOath) + " — worthy blood: +" + (4 * duelist) + " ATK", 1);
            }

            if (bus.TryExecute(attacker) && !bus.HasTarget(attacker)) return;

            if (attacker.Effective(RelicId.WeightedDice) > 0 &&
                bus.NextRandom() < attacker.StatValue(Stat.Lck) / 100.0)
            {
                Crit(attacker, bus, rules);
            }
            else
            {
                int amount = attacker.StatValue(Stat.Atk);

                // The Edge set sharpens every fourth strike.
                if (attacker.SetCount(RelicKind.Edge) >= 5 && attacker.Strikes % 4 == 0)
                {
                    amount = JsMath.RoundToInt(amount * 1.5);
                }

                // Fortune's Edge spends its charge on this one blow.
                if (attacker.BladeCharged)
                {
                    amount = JsMath.RoundToInt(amount * (attacker.IsAwake(RelicId.FortunesEdge) ? 2 : 1.5));
                    attacker.BladeCharged = false;
                    bus.Line(attacker, RelicId.FortunesEdge,
                        attacker.Label(RelicId.FortunesEdge) + " — charged strike!", 1);
                }

                bus.DealDamage(attacker, amount, bus.PlainStrikeLabel(attacker), 0, RelicId.None, bus.NewChain());
            }

            // Momentum Bead builds speed across the whole floor, not just this fight.
            int momentum = attacker.Effective(RelicId.MomentumBead);
            if (momentum > 0)
            {
                attacker.MomentumCount++;
                if (attacker.MomentumCount % (attacker.IsAwake(RelicId.MomentumBead) ? 2 : 3) == 0)
                {
                    attacker.MomentumBonus += momentum;
                    bus.ReportMomentum(attacker, momentum);
                }
            }

            if (bus.TryExecute(attacker) && !bus.HasTarget(attacker)) return;

            // The Pace set lands a second strike every fifth blow.
            if (attacker.SetCount(RelicKind.Pace) >= 7 && attacker.Strikes % 5 == 0 &&
                bus.HasTarget(attacker) && attacker.Php > 0)
            {
                bus.Line(attacker, RelicId.None, "Pace set — a second strike!", 0);
                bus.DealDamage(attacker, attacker.StatValue(Stat.Atk),
                    bus.PlainStrikeLabel(attacker), 0, RelicId.None, bus.NewChain());
            }

            if (bus.HasTarget(attacker) && attacker.Php > 0)
            {
                AfterTheBlow(attacker, bus, rules);
            }
        }

        /// <summary>A critical hit, and the luck signal it puts on the bus.</summary>
        private static void Crit(ICombatActor attacker, ICombatBus bus, CombatRules rules)
        {
            IChain chain = bus.NewChain();
            bus.BeginCrit();
            double scale = chain.Scale(attacker, RelicId.WeightedDice);

            bus.ReportLuck(attacker, attacker.Label(RelicId.WeightedDice), 0, RelicId.WeightedDice);

            bus.DealDamage(attacker,
                JsMath.RoundToInt(attacker.StatValue(Stat.Atk) *
                    (attacker.IsAwake(RelicId.WeightedDice) ? 2 : 1.5)),
                bus.CritLabel(attacker), 0, bus.CritRelic(attacker), chain);

            // The Luck set makes crits lucky breaks in their own right.
            if (attacker.SetCount(RelicKind.Luck) >= 5)
            {
                CombatPrimitives.EmitLuck(attacker, bus, rules, "Luck set", 1, RelicId.None, chain, false);
            }

            bus.FireEmitter(attacker, RelicId.WeightedDice, 0, chain, scale);

            // Quiet: the luck line above already reported it, but the signal still travels.
            CombatPrimitives.EmitLuck(attacker, bus, rules, bus.CritSignalLabel(attacker), 0,
                RelicId.WeightedDice, chain, true);
            bus.EndCrit();
        }

        /// <summary>What a landed strike shakes loose: coins, blood, and the attack cadence.</summary>
        private static void AfterTheBlow(ICombatActor attacker, ICombatBus bus, CombatRules rules)
        {
            // The strike is one genuine event; the spill and the lifesteal share its chain.
            IChain chain = bus.NewChain();

            int hook = CombatPrimitives.ReactionCount(
                attacker, RelicTuning.For(RelicId.CutpurseHook, rules.Mode), RelicId.CutpurseHook);

            // Loose coins: a luck-rated spill on a landed strike, which the Hook doubles. The
            // draw happens whether or not the Hook is held, so it must not be skipped when it
            // is absent — every later draw in the fight depends on this one being taken.
            bool spill = (hook > 0 && attacker.IsAwake(RelicId.CutpurseHook)) ||
                         bus.NextRandom() < attacker.StatValue(Stat.Lck) * (hook > 0 ? 2 : 1) / 100.0;

            if (spill)
            {
                if (hook > 0)
                {
                    bus.ReportLuck(attacker, bus.LooseCoinsLabel, 0, RelicId.CutpurseHook);
                }

                double scale = hook > 0 ? chain.Scale(attacker, RelicId.CutpurseHook) : 1.0;
                int coins = 1
                    + (hook > 0 ? JsMath.RoundToInt(hook * scale) : 0)
                    + bus.SpillBonus(attacker)
                    + (attacker.SetCount(RelicKind.Greed) >= 5 ? 1 : 0);

                CombatPrimitives.GainGold(attacker, bus, rules, coins,
                    hook > 0 ? bus.HookSpillLabel(attacker) : bus.LooseCoinsLabel, 1,
                    hook > 0 ? RelicId.CutpurseHook : RelicId.None, chain);

                if (hook > 0)
                {
                    bus.FireEmitter(attacker, RelicId.CutpurseHook, 0, chain, scale);
                    CombatPrimitives.EmitLuck(attacker, bus, rules, bus.LooseCoinsLabel, 0,
                        RelicId.CutpurseHook, chain, true);
                }
            }

            // Vampire Tooth drinks on every landed strike.
            int tooth = CombatPrimitives.ReactionCount(
                attacker, RelicTuning.For(RelicId.VampireTooth, rules.Mode), RelicId.VampireTooth);
            if (tooth > 0 && bus.HasTarget(attacker))
            {
                double scale = chain.Scale(attacker, RelicId.VampireTooth);
                CombatPrimitives.Heal(attacker, bus, rules, JsMath.RoundToInt(tooth * scale),
                    attacker.Label(RelicId.VampireTooth), 1, RelicId.VampireTooth, chain);
                bus.FireEmitter(attacker, RelicId.VampireTooth, 0, chain, scale);
            }

            attacker.StrikeCount++;

            // Socketed attack triggers fire on every third strike, counted across the floor.
            if (attacker.StrikeCount % 3 == 0)
            {
                bus.FireTrigger(attacker, SocketTrigger.Attack, 0, RelicId.VampireTooth, RelicId.None, chain);
            }
        }
    }
}
