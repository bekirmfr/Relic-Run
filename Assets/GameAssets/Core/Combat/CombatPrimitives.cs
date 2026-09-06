using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;

namespace RelicRun.Core.Combat
{
    /// <summary>
    /// The bus operations that behave identically in both modes.
    /// </summary>
    /// <remarks>
    /// Healing, gold and luck carry a lot of relic behaviour between them — Martyr's Knot, the
    /// Famine Bell, Quenched Blade, the Alchemist's Vial, Coin Singer, Rabbit's Foot — and all
    /// of it used to exist twice. The engines now supply their differences through
    /// <see cref="ICombatBus"/> and <see cref="CombatRules"/>, and the rules themselves live
    /// here once.
    ///
    /// Damage and the turn loop stay with each engine for now: a delve resolves an enemy's blow
    /// in its own turn function while a duel resolves it inside the damage call, and reconciling
    /// those two shapes is a larger change than moving a shared rule.
    /// </remarks>
    public static class CombatPrimitives
    {
        /// <summary>
        /// Restores health, applying every modifier that touches a heal, then answering it.
        /// </summary>
        public static void Heal(ICombatActor actor, ICombatBus bus, CombatRules rules,
            int amount, string source, int depth, RelicId relic, IChain chain)
        {
            if (depth > rules.ChainCap)
            {
                bus.ReportFizzle(depth);
                return;
            }

            // Modifiers adjust the amount now but log AFTER the heal, so the log reads
            // parent-then-children rather than announcing a bonus before what it modified.
            // They are queued as descriptions rather than built events, because an event
            // captures whole fight state and building one early would freeze the pre-heal HP.
            var after = new List<PendingLine>();

            // Modifiers only apply to a heal that was going to land. An amount already at or
            // below zero is left alone, so a bonus cannot lift a cancelled heal back into range.
            if (amount > 0)
            {
                int martyr = actor.Effective(RelicId.MartyrsKnot);
                if (relic != RelicId.None && relic != RelicId.MartyrsKnot && martyr > 0)
                {
                    amount += martyr;
                    after.Add(new PendingLine(depth + 1, RelicId.MartyrsKnot,
                        actor.Label(RelicId.MartyrsKnot) + " +" + martyr));
                }

                if (actor.SetCount(RelicKind.Flesh) >= 5) amount += 1;

                // Your own bell taxes you unless awakened. In a duel the opponent's starves you too.
                int bell = actor.IsAwake(RelicId.FamineBell) ? 0 : actor.Effective(RelicId.FamineBell);
                if (RelicTuning.For(RelicId.FamineBell, rules.Mode).AffectsOpponent)
                {
                    bell += bus.Opponent(actor).Effective(RelicId.FamineBell);
                }

                amount -= bell;
            }

            if (amount <= 0)
            {
                if (relic != RelicId.None) bus.ReportFizzle(depth);
                return;
            }

            // Quenched Blade tempers on every third heal, whatever caused it.
            int quenched = actor.Effective(RelicId.QuenchedBlade);
            if (quenched > 0)
            {
                actor.QuenchCount++;
                if (actor.IsAwake(RelicId.QuenchedBlade) || actor.QuenchCount % 3 == 0)
                {
                    actor.QuenchBonus += quenched;
                    after.Add(new PendingLine(depth + 1, RelicId.QuenchedBlade,
                        actor.Label(RelicId.QuenchedBlade) + " +" + quenched + " ATK"));
                }
            }

            int real = Math.Min(amount, actor.Pmax - actor.Php);

            // An awakened Vampire Tooth drains the opponent's ceiling rather than its pool.
            if (relic == RelicId.VampireTooth && actor.IsAwake(RelicId.VampireTooth))
            {
                bus.ReduceOpponentCeiling(actor, depth + 1);
            }

            if (real <= 0)
            {
                bus.ReportHealFull(actor, source, depth, relic);
                Flush(bus, actor, after);
                return;
            }

            actor.Php += real;
            bus.ReportHeal(actor, real, source, depth, relic);
            Flush(bus, actor, after);

            // Blood Altar turns mercy into violence. It never answers its own heal, which is
            // what stops it looping with the Vial forever.
            RelicTuning altarTuning = RelicTuning.For(RelicId.BloodAltar, rules.Mode);
            int altar = ReactionCount(actor, altarTuning, RelicId.BloodAltar);
            if (altar > 0 && relic != RelicId.BloodAltar)
            {
                // Scaled before the target check: the activation counts even when there is
                // nothing left to hit, so a later pass in the same chain is already weaker.
                double scale = chain.Scale(actor, RelicId.BloodAltar);
                if (altarTuning.AnswersWithoutTarget || bus.HasTarget(actor))
                {
                    // The damage call guards itself against a dead target; the tithe does not,
                    // which is why the delve gates the whole block and the duel does not.
                    bus.DealDamage(actor, JsMath.RoundToInt(2 * altar * scale),
                        actor.Label(RelicId.BloodAltar), depth + 1, RelicId.BloodAltar, chain);

                    // Awakened, the Altar takes its own tithe back out of the wound.
                    if (actor.IsAwake(RelicId.BloodAltar))
                    {
                        Heal(actor, bus, rules, 1, actor.Label(RelicId.BloodAltar),
                            depth + 2, RelicId.BloodAltar, chain);
                    }
                }

                bus.FireEmitter(actor, RelicId.BloodAltar, depth, chain, scale);
            }
        }

        /// <summary>Adds gold, after the purse modifiers, and answers it.</summary>
        public static void GainGold(ICombatActor actor, ICombatBus bus, CombatRules rules,
            int amount, string source, int depth, RelicId relic, IChain chain)
        {
            if (depth > rules.ChainCap)
            {
                bus.ReportFizzle(depth);
                return;
            }

            if (amount <= 0)
            {
                if (relic != RelicId.None) bus.ReportFizzle(depth);
                return;
            }

            double real = amount;

            // The Greedy Curse pays double, but only where it has an activation at all.
            if (RelicTuning.For(RelicId.GreedyCurse, rules.Mode).HasActivation &&
                actor.Effective(RelicId.GreedyCurse) > 0)
            {
                real *= 2;
            }

            if (actor.SetCount(RelicKind.Greed) >= 3) real = JsMath.Round(real * 1.15);

            if (actor.Effective(RelicId.FortunesDebt) > 0 && !actor.IsAwake(RelicId.FortunesDebt))
            {
                real = JsMath.Round(real * 0.9);
            }

            int gained = Math.Max(1, (int)real);
            if (actor.DebtLeft > 0) actor.DebtLeft = Math.Max(0, actor.DebtLeft - gained);

            actor.Gold += gained;
            bus.ReportGold(actor, gained, source, depth, relic);

            // The Alchemist's Vial answers every coin with a drop of blood restored.
            int vial = actor.Effective(RelicId.AlchemistsVial);
            if (vial > 0)
            {
                double scale = chain.Scale(actor, RelicId.AlchemistsVial);
                Heal(actor, bus, rules, JsMath.RoundToInt(vial * scale),
                    actor.Label(RelicId.AlchemistsVial), depth + 1, RelicId.AlchemistsVial, chain);
                bus.FireEmitter(actor, RelicId.AlchemistsVial, depth, chain, scale);
            }

            // Coin Singer turns gold into luck — the GOLD to GRACE arc of the wheel.
            if (relic != RelicId.CoinSinger && actor.Effective(RelicId.CoinSinger) > 0 &&
                (relic != RelicId.None || actor.IsAwake(RelicId.CoinSinger)))
            {
                double scale = chain.Scale(actor, RelicId.CoinSinger);
                if (JsMath.RoundToInt(scale) > 0)
                {
                    EmitLuck(actor, bus, rules, actor.Label(RelicId.CoinSinger),
                        depth + 1, RelicId.CoinSinger, chain, false);
                    bus.FireEmitter(actor, RelicId.CoinSinger, depth, chain, scale);
                }
            }

            actor.GoldCount++;

            // Socketed gold triggers fire on every third gain.
            if (actor.GoldCount % 3 == 0)
            {
                bus.FireTrigger(actor, SocketTrigger.Gold, depth, RelicId.AlchemistsVial, relic, chain);
            }
        }

        /// <summary>
        /// Puts a luck signal on the bus. It carries no value of its own — the relics listening
        /// for luck decide what it is worth.
        /// </summary>
        public static void EmitLuck(ICombatActor actor, ICombatBus bus, CombatRules rules,
            string source, int depth, RelicId relic, IChain chain, bool quiet)
        {
            if (depth > rules.ChainCap)
            {
                bus.ReportFizzle(depth);
                return;
            }

            if (!quiet)
            {
                bus.ReportLuck(actor, source, depth, relic);
            }

            RabbitReact(actor, bus, rules, depth, relic, chain);

            // Hex Thread sharpens fortune to a point — the GRACE to BLOOD arc.
            int thread = actor.Effective(RelicId.HexThread);
            if (relic != RelicId.HexThread && thread > 0 && bus.HasTarget(actor))
            {
                double scale = chain.Scale(actor, RelicId.HexThread);
                int lash = JsMath.RoundToInt(thread * scale);
                if (lash > 0)
                {
                    bus.DealDamage(actor, lash, actor.Label(RelicId.HexThread),
                        depth + 1, RelicId.HexThread, chain);
                }
            }

            if (relic != RelicId.FortunesEdge && actor.Effective(RelicId.FortunesEdge) > 0 &&
                !actor.BladeCharged)
            {
                actor.BladeCharged = true;
                bus.Line(actor, RelicId.FortunesEdge,
                    actor.Label(RelicId.FortunesEdge) + " charges the blade", depth + 1);
            }

            int pulse = actor.Effective(RelicId.QuickenedPulse);
            if (relic != RelicId.QuickenedPulse && pulse > 0)
            {
                actor.Gale += pulse;
                bus.Line(actor, RelicId.QuickenedPulse,
                    actor.Label(RelicId.QuickenedPulse) + " +" + pulse + " SPD", depth + 1);
            }

            bus.FireTrigger(actor, SocketTrigger.Luck, depth, RelicId.RabbitsFoot, relic, chain);
        }

        /// <summary>
        /// Rabbit's Foot answers luck with luck — but never its own, and in a duel only every
        /// third signal, because a long match would otherwise snowball.
        /// </summary>
        public static void RabbitReact(ICombatActor actor, ICombatBus bus, CombatRules rules,
            int depth, RelicId cause, IChain chain)
        {
            RelicTuning rabbitTuning = RelicTuning.For(RelicId.RabbitsFoot, rules.Mode);
            int rabbits = ReactionCount(actor, rabbitTuning, RelicId.RabbitsFoot);
            if (rabbits <= 0 || cause == RelicId.RabbitsFoot) return;

            actor.RabbitCount++;
            if (rabbitTuning.SignalCadence > 1 && !actor.IsAwake(RelicId.RabbitsFoot) &&
                actor.RabbitCount % rabbitTuning.SignalCadence != 0)
            {
                return;
            }

            double scale = chain.Scale(actor, RelicId.RabbitsFoot);
            int bonus = JsMath.RoundToInt(rabbits * scale);
            if (bonus > 0)
            {
                actor.LuckGain += bonus;
                bus.Line(actor, RelicId.RabbitsFoot,
                    actor.Label(RelicId.RabbitsFoot) + " +" + bonus + " LUCK", depth + 1);
            }

            bus.FireEmitter(actor, RelicId.RabbitsFoot, depth, chain, scale);
        }

        /// <summary>
        /// How many copies answer an event, for the two relics whose reaction strength counts
        /// awakened copies differently between the modes.
        /// </summary>
        internal static int ReactionCount(ICombatActor actor, RelicTuning tuning, RelicId id)
        {
            return tuning.ReactionCountsAwakened ? actor.Effective(id) : actor.CountRaw(id);
        }

        private static void Flush(ICombatBus bus, ICombatActor actor, List<PendingLine> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                bus.Line(actor, lines[i].Relic, lines[i].Source, lines[i].Depth);
            }
        }

        /// <summary>
        /// A log line recorded after the event it modifies, so it captures the state that event
        /// produced rather than the state before it.
        /// </summary>
        private readonly struct PendingLine
        {
            public readonly int Depth;
            public readonly RelicId Relic;
            public readonly string Source;

            public PendingLine(int depth, RelicId relic, string source)
            {
                Depth = depth;
                Relic = relic;
                Source = source;
            }
        }
    }
}
