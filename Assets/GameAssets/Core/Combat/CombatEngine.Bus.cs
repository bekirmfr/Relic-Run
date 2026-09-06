using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Combat
{
    /// <summary>
    /// The chain bus: the primitives every effect in the game ultimately goes through.
    /// </summary>
    /// <remarks>
    /// Kept as part of <see cref="CombatEngine"/> rather than split into its own type, because
    /// these operations read and write the fight's live state on every line — enemy HP, the
    /// hero's pool and purse, the counters. A separate class would only take the engine as a
    /// parameter and pass everything straight back, which is a boundary in name only. The split
    /// here is by file, so the bus reads on its own without inventing a fake seam.
    ///
    /// Each primitive takes <c>(amount, source, depth, relic, chain)</c>. The relic is the
    /// chain's provenance: set, the event was caused by a relic and joins the existing chain;
    /// unset, it is a genuine event that opens a new one.
    /// </remarks>
    public sealed partial class CombatEngine
    {
        private void DealDamage(int amount, string source, int depth,
            RelicId relic = RelicId.None, ChainContext chain = null)
        {
            chain = chain ?? NewChain();

            if (depth > ChainCap)
            {
                Snap(CombatEventType.Fizzle, depth);
                return;
            }

            if (_enemyHp <= 0)
            {
                return;
            }

            if (amount <= 0)
            {
                // A relic that decayed to nothing still reports; a plain miss does not.
                if (relic != RelicId.None)
                {
                    Snap(CombatEventType.Fizzle, depth);
                }

                return;
            }

            // Foes evade only with a Lucky Clover of their own.
            if (depth == 0 && source == "you" && _cur.Relics != null &&
                _cur.CountRelic(RelicId.LuckyClover) > 0 && _rng.Next() < _cur.Lck / 100.0)
            {
                Snap(CombatEventType.EnemyMiss, 0, foe: true);
                return;
            }

            // An awakened Whetstone cuts straight through armor — but only on the hero's own
            // strikes, not on anything a relic throws.
            bool sunders = IsAwake(RelicId.Whetstone) && CountItem(RelicId.Whetstone) > 0 && source == "you";
            int dealt = Defense.Apply(amount, sunders ? 0 : _cur.Armor, _hero.DefenseModel);
            _enemyHp -= dealt;
            Snap(CombatEventType.EnemyDamage, depth, amount: dealt, source: source, relic: relic);

            // Ember Cask shakes gold loose from relic damage - the BLOOD to GOLD arc.
            if (relic != RelicId.None && relic != RelicId.EmberCask &&
                EffectiveCount(RelicId.EmberCask) > 0)
            {
                double caskScale = chain.Scale(RelicId.EmberCask);
                int coins = JsMath.RoundToInt(EffectiveCount(RelicId.EmberCask) * caskScale);
                if (coins > 0)
                {
                    GainGold(coins, RelicCatalog.KeyOf(RelicId.EmberCask), depth + 1, RelicId.EmberCask, chain);
                    FireEmitter(RelicId.EmberCask, depth, chain, caskScale);
                }
            }

            if (_enemyHp <= 0)
            {
                OnKill(depth);
                return;
            }

            // Thorn Vest on the foe bites back at whoever struck it.
            int thorns = _cur.CountRelic(RelicId.ThornVest);
            if (depth == 0 && thorns > 0 && _hero.Php > 0)
            {
                _hero.Php -= thorns;
                Snap(CombatEventType.PlayerDamage, 1, amount: thorns, relic: RelicId.ThornVest, foe: true);
                if (_hero.Php <= 0)
                {
                    return;
                }
            }

            // Berserker Charm on the foe: it enrages once, at half health.
            if (!_foeFury && _cur.CountRelic(RelicId.BerserkerCharm) > 0 && _enemyHp < EnemyMax / 2.0)
            {
                _foeFury = true;
                Snap(CombatEventType.EnemyFury, 1, amount: 2, relic: RelicId.BerserkerCharm, foe: true);
            }
        }



        /// <summary>
        /// Puts a luck signal on the bus. It carries no value of its own — relics that listen
        /// for luck decide what it is worth.
        /// </summary>
        /// <param name="quiet">
        /// Emit without a log line, for a signal whose cause has already been reported.
        /// </param>

        // ---------- the shared primitives ----------

        private void Heal(int amount, string source, int depth,
            RelicId relic = RelicId.None, ChainContext chain = null)
        {
            CombatPrimitives.Heal(_actor, this, _rules, amount, source, depth, relic, chain ?? NewChain());
        }

        private void GainGold(int amount, string source, int depth,
            RelicId relic = RelicId.None, ChainContext chain = null)
        {
            CombatPrimitives.GainGold(_actor, this, _rules, amount, source, depth, relic, chain ?? NewChain());
        }

        private void EmitLuck(string source, int depth, RelicId relic, ChainContext chain, bool quiet = false)
        {
            CombatPrimitives.EmitLuck(_actor, this, _rules, source, depth, relic, chain ?? NewChain(), quiet);
        }

        private void OnKill(int depth)
        {
            _hero.Kills++;
            Snap(CombatEventType.Kill, depth);

            // The kill is one genuine event, so everything it sets off shares a single chain.
            ChainContext chain = NewChain();

            // The Edge set sharpens permanently with every kill this floor.
            if (SetCount(RelicKind.Edge) >= 7)
            {
                _headsmanBonus += 1;
                Snap(CombatEventType.First, 1, source: "Edge set — +1 ATK");
            }

            // Coin Magnet does not pay out on its own — it swells the loot the corpse already
            // drops, so it adds no node of its own to the chain.
            int magnet = EffectiveCount(RelicId.CoinMagnet);
            int loot = JsMath.RoundToInt(_cur.Drop * (SetCount(RelicKind.Greed) >= 7 ? 1.5 : 1.0)) + magnet;

            GainGold(loot, "killLoot", 0, RelicId.None, chain);

            // Tollkeeper's Ring collects at the gate on every death.
            int toll = EffectiveCount(RelicId.TollkeepersRing);
            if (toll > 0)
            {
                double scale = chain.Scale(RelicId.TollkeepersRing);
                GainGold(Math.Max(1, JsMath.RoundToInt(5 * toll * scale)),
                    RelicCatalog.KeyOf(RelicId.TollkeepersRing), 1, RelicId.TollkeepersRing, chain);
                FireEmitter(RelicId.TollkeepersRing, 0, chain, scale);
            }

            if (magnet > 0)
            {
                FireEmitter(RelicId.CoinMagnet, 0, chain, chain.Scale(RelicId.CoinMagnet));
            }

            FireTrigger(SocketTrigger.Kill, 0, RelicId.CoinMagnet, RelicId.None, chain);
        }
    }
}
