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

        private void Heal(int amount, string source, int depth,
            RelicId relic = RelicId.None, ChainContext chain = null)
        {
            chain = chain ?? NewChain();

            if (depth > ChainCap)
            {
                Snap(CombatEventType.Fizzle, depth);
                return;
            }

            // Modifiers adjust the amount now but log AFTER the heal, so the log reads
            // parent-then-children rather than announcing a bonus before what it modified.
            // They are queued as descriptions, not built events: an event captures the whole
            // fight state at the moment it is recorded, so building one early would freeze the
            // hero's HP at its pre-heal value.
            var after = new List<PendingLine>();

            int martyr = EffectiveCount(RelicId.MartyrsKnot);
            if (relic != RelicId.None && relic != RelicId.MartyrsKnot && martyr > 0)
            {
                amount += martyr;
                after.Add(new PendingLine(depth + 1, RelicId.MartyrsKnot,
                    RelicCatalog.KeyOf(RelicId.MartyrsKnot) + " +" + martyr));
            }

            if (SetCount(RelicKind.Flesh) >= 5) amount += 1;

            // The Famine Bell starves every table, the hero's included.
            if (!IsAwake(RelicId.FamineBell)) amount -= EffectiveCount(RelicId.FamineBell);

            if (amount <= 0)
            {
                if (relic != RelicId.None)
                {
                    Snap(CombatEventType.Fizzle, depth);
                }

                return;
            }

            // Quenched Blade tempers on every third heal, whatever caused it.
            int quenched = EffectiveCount(RelicId.QuenchedBlade);
            if (quenched > 0)
            {
                _carry.QuenchCount++;
                if (IsAwake(RelicId.QuenchedBlade) || _carry.QuenchCount % 3 == 0)
                {
                    _quenchBonus += quenched;
                    after.Add(new PendingLine(depth + 1, RelicId.QuenchedBlade,
                        RelicCatalog.KeyOf(RelicId.QuenchedBlade) + " +" + quenched + " ATK"));
                }
            }

            int real = Math.Min(amount, _hero.Pmax - _hero.Php);

            // An awakened Vampire Tooth drains the foe's ceiling, not its pool.
            if (relic == RelicId.VampireTooth && IsAwake(RelicId.VampireTooth) && EnemyMax > 2)
            {
                _cur.MaxHp = Math.Max(2, EnemyMax - 1);
                if (_enemyHp > _cur.MaxHp) _enemyHp = _cur.MaxHp;
                Snap(CombatEventType.First, depth + 1, relic: RelicId.VampireTooth,
                    source: RelicCatalog.KeyOf(RelicId.VampireTooth) + " takes 1 max HP");
            }

            // Healing a full pool is not nothing — it is a wasted activation, and the log says so.
            if (real <= 0)
            {
                Snap(CombatEventType.HealFull, depth, source: source, relic: relic);
                FlushPending(after);
                return;
            }

            _hero.Php += real;
            Snap(CombatEventType.Heal, depth, amount: real, source: source, relic: relic);
            FlushPending(after);

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

                    // Awakened, the Altar takes its own tithe back out of the wound.
                    if (IsAwake(RelicId.BloodAltar))
                    {
                        Heal(1, RelicCatalog.KeyOf(RelicId.BloodAltar), depth + 2, RelicId.BloodAltar, chain);
                    }
                }

                FireEmitter(RelicId.BloodAltar, depth, chain, scale);
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
                FireEmitter(RelicId.AlchemistsVial, depth, chain, scale);
            }

            // Coin Singer turns gold into luck - the GOLD to GRACE arc of the wheel.
            if (relic != RelicId.CoinSinger && EffectiveCount(RelicId.CoinSinger) > 0 &&
                (relic != RelicId.None || IsAwake(RelicId.CoinSinger)))
            {
                double singerScale = chain.Scale(RelicId.CoinSinger);
                if (JsMath.RoundToInt(singerScale) > 0)
                {
                    EmitLuck(RelicCatalog.KeyOf(RelicId.CoinSinger), depth + 1, RelicId.CoinSinger, chain);
                    FireEmitter(RelicId.CoinSinger, depth, chain, singerScale);
                }
            }

            _goldCount++;

            // Socketed gold triggers fire on every third gain, counted across the floor.
            if (_goldCount % 3 == 0)
            {
                FireTrigger(SocketTrigger.Gold, depth, RelicId.AlchemistsVial, relic, chain);
            }
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

            RabbitReact(depth, relic, chain);

            // Hex Thread sharpens fortune to a point - the GRACE to BLOOD arc.
            if (relic != RelicId.HexThread && EffectiveCount(RelicId.HexThread) > 0 && _enemyHp > 0)
            {
                double threadScale = chain.Scale(RelicId.HexThread);
                int lash = JsMath.RoundToInt(EffectiveCount(RelicId.HexThread) * threadScale);
                if (lash > 0)
                {
                    DealDamage(lash, RelicCatalog.KeyOf(RelicId.HexThread), depth + 1, RelicId.HexThread, chain);
                }
            }

            if (relic != RelicId.FortunesEdge && EffectiveCount(RelicId.FortunesEdge) > 0 && !_bladeCharged)
            {
                _bladeCharged = true;
                Snap(CombatEventType.First, depth + 1, relic: RelicId.FortunesEdge,
                    source: RelicCatalog.KeyOf(RelicId.FortunesEdge) + " charges the blade");
            }

            int pulse = EffectiveCount(RelicId.QuickenedPulse);
            if (relic != RelicId.QuickenedPulse && pulse > 0)
            {
                _galeBonus += pulse;
                Snap(CombatEventType.First, depth + 1, relic: RelicId.QuickenedPulse,
                    source: RelicCatalog.KeyOf(RelicId.QuickenedPulse) + " +" + pulse + " SPD");
            }

            FireTrigger(SocketTrigger.Luck, depth, RelicId.RabbitsFoot, relic, chain);
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
