using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Combat
{
    /// <summary>
    /// Resolves a whole floor to completion and returns the event stream. Port of
    /// <c>simulateFloor</c>.
    /// </summary>
    /// <remarks>
    /// Architecture invariant 2: nothing here animates or waits. A floor runs start to finish
    /// synchronously and hands back events, which the presentation layer replays on timers.
    ///
    /// Time is integer ticks, not seconds. Each combatant fills a 100-point gauge and SPD is
    /// the drain rate; when neither gauge is empty the clock jumps straight to whichever empties
    /// next. There is no delta time anywhere, so the result depends only on the seed.
    ///
    /// Control flow is ported literally, including the order in which conditions are evaluated,
    /// because RNG draws are order-coupled: moving a single <c>rng()</c> call changes outcomes
    /// for the same seed. Tidying comes after the corpus is green, not before.
    ///
    /// Scope: this is the Phase 2 engine. Hero relics are not wired in yet — the chain bus
    /// (Phase 3) and relic effects (Phase 4) land next. Enemy relics that this engine reads are
    /// already handled, since bosses carry kits from Dungeon 2 onward.
    /// </remarks>
    public sealed class CombatEngine
    {
        /// <summary>Points a combatant must drain before acting.</summary>
        private const int Gauge = 100;

        /// <summary>Chain depth beyond which effects fizzle rather than continue.</summary>
        private const int ChainCap = 40;

        /// <summary>Stops a pathological loop from hanging the harness.</summary>
        private const int MaxIterations = 600;

        private HeroState _hero;
        private Mulberry32 _rng;
        private List<CombatEvent> _events;
        private CarryState _carry;

        private EnemyState _cur;
        private int _enemyHp;
        private int _tick;

        // Floor-scoped counters.
        private int _strikeCount;
        private int _painCount;
        private int _goldCount;

        // Fight-scoped state.
        private int _fightStrikes;
        private int _hitCount;
        private bool _foeFury;

        /// <summary>Runs every foe in the pack, in order, until they are dead or the hero is.</summary>
        public CombatResult ResolveFloor(HeroState hero, IReadOnlyList<EnemyState> pack, Mulberry32 rng)
        {
            if (hero == null) throw new ArgumentNullException(nameof(hero));
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            _hero = hero;
            _rng = rng;
            _events = new List<CombatEvent>();
            _carry = hero.Carry != null ? hero.Carry.Clone() : new CarryState();
            _tick = 0;

            _strikeCount = _carry.StrikeCount;
            _painCount = _carry.PainCount;
            _goldCount = _carry.GoldCount;

            int guard = 0;

            for (int k = 0; k < pack.Count && _hero.Php > 0; k++)
            {
                _cur = pack[k];
                _enemyHp = _cur.Hp;

                _hitCount = 0;
                _fightStrikes = 0;
                _foeFury = false;

                // Sentinel Bell's stacks are per fight, but the carried value is whatever the
                // last fight left, which is what a revive resumes from.
                _carry.SentinelBonus = 0;

                Snap(CombatEventType.Enter, 0, another: k > 0);

                // Battle Dash resolves before the gauges run. Both sides dashing cancels out.
                bool heroDash = CountItem(RelicId.BattleDash) > 0;
                bool enemyDash = _cur.CountRelic(RelicId.BattleDash) > 0;

                int heroGauge = (heroDash && !enemyDash) ? 0 : Gauge;
                int enemyGauge = (enemyDash && !heroDash) ? 0 : Gauge;

                if (heroDash && enemyDash)
                {
                    Snap(CombatEventType.First, 0, source: "Both dash — the openers cancel!");
                }
                else if (heroDash)
                {
                    Snap(CombatEventType.DashOpen, 0, source: RelicCatalog.KeyOf(RelicId.BattleDash),
                        relic: RelicId.BattleDash);
                }
                else if (enemyDash)
                {
                    Snap(CombatEventType.EnemyDashOpen, 0, relic: RelicId.BattleDash, foe: true);
                }

                while (_hero.Php > 0 && _enemyHp > 0 && guard++ < MaxIterations)
                {
                    if (enemyGauge <= 0 && heroGauge > 0)
                    {
                        EnemyHits();
                        if (_hero.Php <= 0) break;
                        enemyGauge += Gauge;
                    }
                    else if (heroGauge <= 0 && enemyGauge > 0)
                    {
                        PlayerHits();
                        if (_enemyHp <= 0 || _hero.Php <= 0) break;
                        heroGauge += Gauge;
                    }
                    else if (heroGauge <= 0 && enemyGauge <= 0)
                    {
                        // Simultaneous: the enemy resolves first, so a killing blow still costs
                        // the hero the hit they were already taking.
                        EnemyHits();
                        if (_hero.Php <= 0) break;
                        enemyGauge += Gauge;

                        PlayerHits();
                        if (_enemyHp <= 0 || _hero.Php <= 0) break;
                        heroGauge += Gauge;
                    }
                    else
                    {
                        // Jump the clock to whichever gauge empties first.
                        int heroSpd = HeroStat(Stat.Spd);
                        int enemySpd = Math.Max(10, _cur.Spd);
                        int step = Math.Min(CeilDiv(heroGauge, heroSpd), CeilDiv(enemyGauge, enemySpd));
                        heroGauge -= heroSpd * step;
                        enemyGauge -= enemySpd * step;
                        _tick += step;
                    }
                }
            }

            if (_hero.Php <= 0)
            {
                Snap(CombatEventType.Death, 0);
            }

            _carry.StrikeCount = _strikeCount;
            _carry.PainCount = _painCount;
            _carry.GoldCount = _goldCount;

            return new CombatResult(_events, _carry);
        }

        // ---------- turns ----------

        private void EnemyHits()
        {
            int defense = HeroStat(Stat.Def);
            int damage = Defense.Apply(_cur.Atk + (_foeFury ? 2 : 0), defense, _hero.DefenseModel);

            _hitCount++;
            damage = Math.Max(1, damage);

            // Weighted Dice on the foe: a LCK% chance to land half again as hard.
            bool crit = false;
            if (_cur.CountRelic(RelicId.WeightedDice) > 0 && _rng.Next() < _cur.Lck / 100.0)
            {
                crit = true;
                damage = JsMath.RoundToInt(damage * 1.5);
            }

            _hero.Php -= damage;
            Snap(CombatEventType.PlayerDamage, 0, amount: damage, enemyCrit: crit);

            // Vampire Tooth on the foe: it drinks back what it just took.
            int tooth = _cur.CountRelic(RelicId.VampireTooth);
            if (tooth > 0 && _enemyHp > 0 && _enemyHp < EnemyMax)
            {
                int healed = Math.Min(tooth, EnemyMax - _enemyHp);
                _enemyHp += healed;
                Snap(CombatEventType.EnemyHeal, 1, amount: healed, relic: RelicId.VampireTooth, foe: true);
            }

            if (_hero.Php > 0)
            {
                _painCount++;
            }
        }

        private void PlayerHits()
        {
            _fightStrikes++;
            _hero.StrikeTotal++;

            DealDamage(HeroStat(Stat.Atk), "you", 0);

            if (_enemyHp > 0)
            {
                // Loose coins: a LCK% spill on a landed strike. The draw happens whether or not
                // the hero carries Cutpurse's Hook, so it must not be skipped when they do not.
                if (_rng.Next() < HeroStat(Stat.Lck) / 100.0)
                {
                    GainGold(1, "looseCoins", 1);
                }

                _strikeCount++;
            }
        }

        // ---------- primitives ----------

        private void DealDamage(int amount, string source, int depth, RelicId relic = RelicId.None)
        {
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
                Snap(CombatEventType.EnemyMiss, 0, foe: _cur.Relics != null);
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

        private void OnKill(int depth)
        {
            _hero.Kills++;
            Snap(CombatEventType.Kill, depth);
            GainGold(_cur.Drop, "killLoot", 0);
        }

        private void GainGold(int amount, string source, int depth, RelicId relic = RelicId.None)
        {
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

            int real = Math.Max(1, amount);
            _hero.Gold += real;

            Snap(CombatEventType.Gold, depth, amount: real, source: source, relic: relic);

            _goldCount++;
        }

        // ---------- helpers ----------

        private int EnemyMax
        {
            get { return _cur.MaxHp > 0 ? _cur.MaxHp : _cur.Hp; }
        }

        private static int CeilDiv(int numerator, int denominator)
        {
            return (numerator + denominator - 1) / denominator;
        }

        private int CountItem(RelicId id)
        {
            int n = 0;
            for (int i = 0; i < _hero.Items.Count; i++)
            {
                if (_hero.Items[i] == id) n++;
            }

            return n;
        }

        /// <summary>Reads a stat through the ledger. Nothing here computes one inline.</summary>
        private int HeroStat(Stat stat)
        {
            return StatLedger.Of(BuildStatContext(), stat);
        }

        private StatContext BuildStatContext()
        {
            return new StatContext
            {
                Items = _hero.Items,
                Awakened = _hero.Awakened,
                IsVersus = _hero.IsVersus,
                BaseAtk = _hero.BaseAtk,
                BaseDef = _hero.BaseDef,
                BaseSpd = _hero.BaseSpd,
                BaseLck = _hero.BaseLck,
                Adrenaline = _hero.Adrenaline,
                MidasBonus = _hero.MidasBonus,
                AtkBonus = _hero.AtkBonus,
                DefBonus = _hero.DefBonus,
                SpdBonus = _hero.SpdBonus,
                LuckBonus = _hero.LuckBonus,
                Php = _hero.Php,
                Pmax = _hero.Pmax,
                Gold = _hero.Gold,
                Floor = _hero.Floor,
                FoeRank = _cur != null ? _cur.Rank : EnemyRank.None,
                Mods = DynamicMods(),
            };
        }

        /// <summary>
        /// In-fight stat boosts, as labelled ledger entries. Empty until Phase 3 wires the
        /// emitters that produce them.
        /// </summary>
        private IReadOnlyList<StatModifier> DynamicMods()
        {
            return System.Array.Empty<StatModifier>();
        }

        private void Snap(CombatEventType type, int depth, int? amount = null, string source = null,
            RelicId relic = RelicId.None, int? relicSlot = null, bool? enemyCrit = null,
            bool? another = null, bool? foe = null)
        {
            var counters = new CombatCounters(
                strikes: _strikeCount,
                pain: _painCount,
                gold: _goldCount,
                stoneCount: _carry.StoneCount,
                fightStrikes: _fightStrikes,
                quenchCount: _carry.QuenchCount,
                momentumCount: 0,
                rabbitCount: 0,
                strikeTotal: _hero.StrikeTotal,
                sentinelBonus: _carry.SentinelBonus);

            var state = new CombatSnapshot(
                tick: _tick,
                heroHp: _hero.Php,
                enemyHp: Math.Max(0, _enemyHp),
                gold: _hero.Gold,
                enemyMaxHp: _cur != null ? EnemyMax : 1,
                enemyAtk: _cur != null ? _cur.Atk : 0,
                enemyRank: _cur != null ? _cur.Rank : EnemyRank.Guard,
                enemyArmor: _cur != null ? _cur.Armor : 0,
                enemySpd: _cur != null ? _cur.Spd : 25,
                enemyLck: _cur != null ? _cur.Lck : 10,
                enemyVariant: _cur != null ? _cur.Variant : 0,
                enemyIndex: _cur != null ? _cur.SpeciesIndex : 0,
                heroAdrenaline: _hero.Adrenaline,
                enemyRelics: _cur != null ? _cur.Relics : null,
                heroMods: DynamicMods(),
                counters: counters);

            _events.Add(new CombatEvent(type, depth, state, amount, source,
                relic, relicSlot, enemyCrit, another, foe));
        }
    }
}
