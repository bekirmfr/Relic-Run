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

        // Rabbit's Foot used to have a second, hero-only reaction here. It was never called:
        // the live one is CombatPrimitives.RabbitReact, which takes an ICombatActor and has
        // always answered for whoever emitted the luck. So the relic already worked on a foe,
        // and the only thing this copy contributed was the appearance that it did not — a
        // hero-only count sitting in the engine, next to two that were real.

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
