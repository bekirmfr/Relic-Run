using System;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Combat
{
    /// <summary>
    /// Sockets: the awakening layer, and the emergent-behaviour engine of the game.
    /// </summary>
    /// <remarks>
    /// A socket is bought at the bazaar and welded onto <b>one copy</b> of a relic. It is either
    /// a TRIGGER (a new way for that copy to activate) or an EMITTER (something extra it does
    /// when it activates). The relic's own behaviour never changes — the socket just adds
    /// another wire into the same bus, and chains and decay govern the rest.
    ///
    /// This is why inventory is an ordered list and sockets are keyed by index: copy #2 of Iron
    /// Skin can be awakened while copy #1 is not. Collapsing the inventory to counts anywhere
    /// silently destroys the whole system.
    ///
    /// A socketed relic re-firing counts as ONE copy (<c>k = 1</c>) because only the upgraded
    /// copy is speaking, whereas a native activation counts every copy the hero holds.
    /// </remarks>
    public sealed partial class CombatEngine
    {
        private void FireEmitter(RelicId id, int depth, ChainContext chain, double scale)
        {
            SocketFiring.FireEmitter(_actor, this, _rules, id, depth, chain, scale);
        }

        private void FireTrigger(SocketTrigger trigger, int depth, RelicId exclude,
            RelicId cause, ChainContext chain)
        {
            SocketFiring.FireTrigger(_actor, this, _rules, trigger, depth, exclude, cause, chain);
        }

        /// <summary>Fires the emitter welded to one specific inventory slot.</summary>

        /// <summary>Fires the emitter on every copy of a relic that has one.</summary>

        /// <summary>
        /// Puts a trigger event on the bus. Every copy whose socket listens for it activates.
        /// </summary>
        /// <param name="cause">
        /// The relic that caused this, if any. This is the provenance rule: a relic-caused event
        /// JOINS the running chain one level deeper and decays with it, while a genuine event
        /// starts a fresh chain — and then only once per copy per beat, which is what
        /// <see cref="_firedThisBeat"/> enforces.
        /// </param>

        /// <summary>
        /// A relic's own effect, re-fireable by a socketed trigger.
        /// </summary>
        /// <remarks>
        /// Several relics here are pure passives in normal play — Ox Heart, Whetstone, Iron Skin.
        /// A trigger gives them an activation in their own theme, which is how a socket makes a
        /// relic gain verbs it never had.
        /// </remarks>

        /// <summary>
        /// Rabbit's Foot answers every luck signal with luck of its own — but never its own,
        /// which would make a single relic a perpetual motion machine.
        /// </summary>
        private void RabbitReact(int depth, RelicId cause, ChainContext chain)
        {
            int rabbits = CountItem(RelicId.RabbitsFoot);
            if (rabbits <= 0 || cause == RelicId.RabbitsFoot)
            {
                return;
            }

            chain = chain ?? NewChain();
            double scale = chain.Scale(RelicId.RabbitsFoot);
            int bonus = JsMath.RoundToInt(rabbits * scale);
            if (bonus > 0)
            {
                _luckBonus += bonus;
                Snap(CombatEventType.First, depth + 1, relic: RelicId.RabbitsFoot,
                    source: RelicCatalog.KeyOf(RelicId.RabbitsFoot) + " +" + bonus + " LUCK");
            }

            FireEmitter(RelicId.RabbitsFoot, depth, chain, scale);
        }

        // ---------- socket lookup ----------

        private SocketTrigger TriggerAt(int slot)
        {
            SocketTrigger trigger;
            return _hero.SocketTriggers.TryGetValue(slot, out trigger) ? trigger : SocketTrigger.None;
        }

        private SocketEmitter EmitterAt(int slot)
        {
            SocketEmitter emitter;
            return _hero.SocketEmitters.TryGetValue(slot, out emitter) ? emitter : SocketEmitter.None;
        }

        /// <summary>Base strength of each emitter, before chain decay.</summary>
    }
}
