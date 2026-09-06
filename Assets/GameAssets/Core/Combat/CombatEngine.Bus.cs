using System;
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

            int dealt = Defense.Apply(amount, _cur.Armor, _hero.DefenseModel);
            _enemyHp -= dealt;
            Snap(CombatEventType.EnemyDamage, depth, amount: dealt, source: source, relic: relic);

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

        private void Heal(int amount, string source, int depth,
            RelicId relic = RelicId.None, ChainContext chain = null)
        {
            chain = chain ?? NewChain();

            if (depth > ChainCap)
            {
                Snap(CombatEventType.Fizzle, depth);
                return;
            }

            if (amount <= 0)
            {
                if (relic != RelicId.None)
                {
                    Snap(CombatEventType.Fizzle, depth);
                }

                return;
            }

            int real = Math.Min(amount, _hero.Pmax - _hero.Php);

            // Healing a full pool is not nothing — it is a wasted activation, and the log says so.
            if (real <= 0)
            {
                Snap(CombatEventType.HealFull, depth, source: source, relic: relic);
                return;
            }

            _hero.Php += real;
            Snap(CombatEventType.Heal, depth, amount: real, source: source, relic: relic);

            // Blood Altar turns mercy into violence. It never answers its own heal, which is
            // what stops it looping with the Vial forever.
            int altar = CountItem(RelicId.BloodAltar);
            if (altar > 0 && relic != RelicId.BloodAltar)
            {
                // Scaled before the enemy check: the activation counts even when there is
                // nothing left to hit, so a later pass in the same chain is already weaker.
                double scale = chain.Scale(RelicId.BloodAltar);
                if (_enemyHp > 0)
                {
                    DealDamage(JsMath.RoundToInt(2 * altar * scale),
                        RelicCatalog.KeyOf(RelicId.BloodAltar), depth + 1, RelicId.BloodAltar, chain);
                }
            }
        }

        private void GainGold(int amount, string source, int depth,
            RelicId relic = RelicId.None, ChainContext chain = null)
        {
            chain = chain ?? NewChain();

            if (depth > ChainCap)
            {
                Snap(CombatEventType.Fizzle, depth);
                return;
            }

            if (amount <= 0)
            {
                if (relic != RelicId.None)
                {
                    Snap(CombatEventType.Fizzle, depth);
                }

                return;
            }

            // Purse modifiers apply to every coin, whatever its source: the Greedy Curse's
            // doubling, the Greed set's cut, and Fortune's Debt taking its tithe back.
            double real = amount;

            if (CountItem(RelicId.GreedyCurse) > 0)
            {
                real *= 2;
            }

            if (SetCount(RelicKind.Greed) >= 3)
            {
                real = JsMath.Round(real * 1.15);
            }

            if (EffectiveCount(RelicId.FortunesDebt) > 0 && !IsAwake(RelicId.FortunesDebt))
            {
                real = JsMath.Round(real * 0.9);
            }

            int gained = Math.Max(1, (int)real);
            _hero.Gold += gained;
            Snap(CombatEventType.Gold, depth, amount: gained, source: source, relic: relic);

            // Alchemist's Vial answers every coin with a drop of blood restored.
            int vial = EffectiveCount(RelicId.AlchemistsVial);
            if (vial > 0)
            {
                double scale = chain.Scale(RelicId.AlchemistsVial);
                Heal(JsMath.RoundToInt(vial * scale),
                    RelicCatalog.KeyOf(RelicId.AlchemistsVial), depth + 1, RelicId.AlchemistsVial, chain);
            }

            _goldCount++;
        }

        /// <summary>
        /// Puts a luck signal on the bus. It carries no value of its own — relics that listen
        /// for luck decide what it is worth.
        /// </summary>
        /// <param name="quiet">
        /// Emit without a log line, for a signal whose cause has already been reported.
        /// </param>
        private void EmitLuck(string source, int depth, RelicId relic, ChainContext chain, bool quiet = false)
        {
            if (depth > ChainCap)
            {
                Snap(CombatEventType.Fizzle, depth);
                return;
            }

            if (!quiet)
            {
                Snap(CombatEventType.Luck, depth, source: source, relic: relic);
            }
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
            }

            if (magnet > 0)
            {
                // Counts as an activation even though its emitter is not wired until sockets land.
                chain.Scale(RelicId.CoinMagnet);
            }
        }
    }
}
