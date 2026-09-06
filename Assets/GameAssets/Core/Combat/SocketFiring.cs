using RelicRun.Core.Content;
using RelicRun.Core.Determinism;

namespace RelicRun.Core.Combat
{
    /// <summary>
    /// Sockets: how a bought component wakes one copy of a relic.
    /// </summary>
    /// <remarks>
    /// A socket is welded onto a single copy, which is why every loop here walks inventory
    /// slots rather than relic ids. Copy #2 of Iron Skin can be awakened while copy #1 is not,
    /// and collapsing inventory to counts anywhere destroys that.
    ///
    /// The provenance rule lives here too: an event caused by a relic JOINS the running chain
    /// one level deeper and decays with it, while a genuine event opens a fresh chain — and
    /// then only once per copy per beat, or two relics answering each other would resolve
    /// forever inside a single turn.
    /// </remarks>
    public static class SocketFiring
    {
        /// <summary>Base strength of each emitter, before chain decay.</summary>
        public static int EmitterAmount(SocketEmitter emitter)
        {
            switch (emitter)
            {
                case SocketEmitter.Dmg: return 2;
                case SocketEmitter.Gold: return 2;
                case SocketEmitter.Heal: return 1;
                case SocketEmitter.Atk: return 1;
                case SocketEmitter.Def: return 1;
                case SocketEmitter.Spd: return 1;
                case SocketEmitter.Luck: return 1;
                default: return 0;
            }
        }

        /// <summary>Fires the emitter welded to one specific inventory slot.</summary>
        public static void FireEmitterAt(ICombatActor actor, ICombatBus bus, CombatRules rules,
            int slot, int depth, IChain chain, double scale)
        {
            SocketEmitter emitter = actor.EmitterAt(slot);
            if (emitter == SocketEmitter.None) return;

            RelicId id = actor.ItemAt(slot);
            bus.BeginFire(actor, slot);
            try
            {
                chain = chain ?? bus.NewChain();
                int baseAmount = EmitterAmount(emitter);
                int amount = JsMath.RoundToInt(baseAmount * scale);

                // The Stone Emitter always hardens by at least its base, because the percentage
                // defence model absorbs a stack that would otherwise round away to nothing. A
                // delve applies that floor before deciding the effect fizzled; a duel after.
                if (emitter == SocketEmitter.Def && rules.StoneEmitterFloorsBeforeFizzle)
                {
                    actor.StoneCount++;
                    amount = System.Math.Max(amount, baseAmount);
                }

                if (amount <= 0)
                {
                    bus.ReportFizzle(depth + 1);
                    return;
                }

                string name = actor.Label(id);
                switch (emitter)
                {
                    case SocketEmitter.Dmg:
                        bus.DealDamage(actor, amount, name, depth + 1, id, chain);
                        break;

                    case SocketEmitter.Heal:
                        CombatPrimitives.Heal(actor, bus, rules, amount, name, depth + 1, id, chain);
                        break;

                    case SocketEmitter.Gold:
                        CombatPrimitives.GainGold(actor, bus, rules, amount, name, depth + 1, id, chain);
                        break;

                    case SocketEmitter.Atk:
                        actor.Fury += amount;
                        bus.Line(actor, id, name + " +" + amount + " ATK", depth + 1);
                        break;

                    case SocketEmitter.Def:
                    {
                        if (!rules.StoneEmitterFloorsBeforeFizzle)
                        {
                            actor.StoneCount++;
                            amount = System.Math.Max(amount, 1);
                        }

                        actor.Stone += amount;
                        bus.Line(actor, id, name + " +" + amount + " DEF", depth + 1);
                        break;
                    }

                    case SocketEmitter.Spd:
                        actor.Gale += amount;
                        bus.Line(actor, id, name + " +" + amount + " SPD", depth + 1);
                        break;

                    case SocketEmitter.Luck:
                        actor.LuckGain += amount;
                        bus.Line(actor, id, name + " +" + amount + " LUCK", depth + 1);
                        break;
                }
            }
            finally
            {
                bus.EndFire(actor);
            }
        }

        /// <summary>Fires the emitter on every copy of a relic that has one.</summary>
        public static void FireEmitter(ICombatActor actor, ICombatBus bus, CombatRules rules,
            RelicId id, int depth, IChain chain, double scale)
        {
            for (int slot = 0; slot < actor.ItemCount; slot++)
            {
                if (actor.ItemAt(slot) == id)
                {
                    FireEmitterAt(actor, bus, rules, slot, depth, chain, scale);
                }
            }
        }

        /// <summary>Puts a trigger on the bus. Every copy whose socket listens for it wakes.</summary>
        /// <param name="cause">
        /// The relic that caused this, if any. Set, the event joins the running chain and decays
        /// with it; unset, it is genuine and opens a fresh one.
        /// </param>
        public static void FireTrigger(ICombatActor actor, ICombatBus bus, CombatRules rules,
            SocketTrigger trigger, int depth, RelicId exclude, RelicId cause, IChain chain)
        {
            if (depth > rules.ChainCap) return;

            for (int slot = 0; slot < actor.ItemCount; slot++)
            {
                RelicId id = actor.ItemAt(slot);
                if (id == exclude || actor.TriggerAt(slot) != trigger) continue;

                if (cause != RelicId.None)
                {
                    // A relic cannot answer itself, or a pair would run forever.
                    if (cause == id) continue;

                    chain = chain ?? bus.NewChain();
                    double scale = chain.Scale(actor, id);
                    bus.BeginFire(actor, slot);
                    RelicEffects.Apply(actor, bus, rules, id, depth + 1, chain, scale, true);
                    FireEmitterAt(actor, bus, rules, slot, depth + 1, chain, scale);
                    bus.EndFire(actor);
                }
                else
                {
                    if (actor.HasFiredThisBeat(slot)) continue;
                    actor.MarkFiredThisBeat(slot);

                    IChain fresh = bus.NewChain();
                    double scale = fresh.Scale(actor, id);
                    bus.BeginFire(actor, slot);
                    RelicEffects.Apply(actor, bus, rules, id, 0, fresh, scale, true);
                    FireEmitterAt(actor, bus, rules, slot, 0, fresh, scale);
                    bus.EndFire(actor);
                }
            }
        }
    }
}
