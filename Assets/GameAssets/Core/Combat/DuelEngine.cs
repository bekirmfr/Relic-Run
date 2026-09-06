using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Combat
{
    /// <summary>
    /// The symmetric duel engine behind Versus. Port of <c>simulateDuel</c>.
    /// </summary>
    /// <remarks>
    /// This is a sibling of <see cref="CombatEngine"/>, not a configuration of it. The two share
    /// their foundations — the stat ledger, chain decay, damage reduction, the relic catalogue —
    /// but the rules genuinely differ, and the differences are balance decisions rather than
    /// accidents:
    ///
    /// <list type="bullet">
    /// <item>The chain cap is 4, not 40. A duel is long, so runaway loops are throttled hard.</item>
    /// <item>Hollow Idol does not count toward set bonuses here.</item>
    /// <item>Decay is keyed per SIDE per relic, so both duellists can spend the same chain.</item>
    /// <item>Rabbit's Foot answers every third luck signal instead of every one, or long
    /// matches would snowball luck.</item>
    /// <item>A Famine Bell starves the OPPONENT; your own only taxes you unless awakened.</item>
    /// <item>Every rival counts as worthy blood, so Duelist's Oath always applies.</item>
    /// </list>
    ///
    /// Events are reported from the hero's point of view: the hero's damage is enemy damage,
    /// the rival's is player damage, and most of the rival's own bookkeeping is logged as plain
    /// lines rather than typed events.
    /// </remarks>
    public sealed class DuelEngine
    {
        private const int Gauge = 100;

        /// <summary>Chains die four levels deep here, against forty in a delve.</summary>
        private const int ChainCap = 4;

        private const int MaxIterations = 600;

        private DuelSide _a;
        private DuelSide _b;
        private Mulberry32 _rng;
        private List<CombatEvent> _events;
        private int _tick;

        /// <summary>Set while a critical strike resolves, so Mirror Scale can answer it.</summary>
        private bool _critChain;

        public CombatResult Resolve(DuelSide hero, DuelSide rival, Mulberry32 rng)
        {
            if (hero == null) throw new ArgumentNullException(nameof(hero));
            if (rival == null) throw new ArgumentNullException(nameof(rival));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            _a = hero;
            _b = rival;
            _a.IsHero = true;
            _b.IsHero = false;
            _rng = rng;
            _events = new List<CombatEvent>();
            _tick = 0;
            _critChain = false;

            _a.ResetForDuel();
            _b.ResetForDuel();

            foreach (DuelSide side in Both())
            {
                if (side.SetCount(RelicKind.Flesh) >= 3 && !side.FleshSetApplied)
                {
                    side.FleshSetApplied = true;
                    side.Pmax += 3;
                    side.Php = Math.Min(side.Pmax, side.Php + 3);
                    Line(side, RelicId.None, Prefix(side) + "Flesh set — +3 max HP", 0);
                }
            }

            Snap(CombatEventType.Enter, 0, another: false);

            bool dashA = _a.Effective(RelicId.BattleDash) > 0;
            bool dashB = _b.Effective(RelicId.BattleDash) > 0;

            FireTrigger(_a, SocketTrigger.Floor, 0, RelicId.None, RelicId.None, null);
            FireTrigger(_b, SocketTrigger.Floor, 0, RelicId.None, RelicId.None, null);
            FireTrigger(_a, SocketTrigger.Fight, 0, RelicId.None, RelicId.None, null);
            FireTrigger(_b, SocketTrigger.Fight, 0, RelicId.None, RelicId.None, null);

            int gaugeA = (dashA && !dashB) ? 0 : Gauge;
            int gaugeB = (dashB && !dashA) ? 0 : Gauge;

            if (gaugeA > 0 && _a.SetCount(RelicKind.Pace) >= 5) gaugeA = Math.Max(0, gaugeA - 25);
            if (gaugeB > 0 && _b.SetCount(RelicKind.Pace) >= 5) gaugeB = Math.Max(0, gaugeB - 25);

            if (dashA && dashB)
            {
                Snap(CombatEventType.First, 0, source: "Both dash — the openers cancel!");
            }
            else if (dashA)
            {
                Snap(CombatEventType.DashOpen, 0, source: RelicCatalog.KeyOf(RelicId.BattleDash),
                    relic: RelicId.BattleDash);
            }
            else if (dashB)
            {
                Snap(CombatEventType.EnemyDashOpen, 0, relic: RelicId.BattleDash, foe: true);
            }

            int guard = 0;
            while (_a.Php > 0 && _b.Php > 0 && guard++ < MaxIterations)
            {
                if (gaugeB <= 0 && gaugeA > 0)
                {
                    Strike(_b);
                    if (_a.Php <= 0) break;
                    gaugeB += Gauge;
                    if (Riposte(_a)) gaugeA = 0;
                }
                else if (gaugeA <= 0 && gaugeB > 0)
                {
                    Strike(_a);
                    if (_b.Php <= 0 || _a.Php <= 0) break;
                    gaugeA += Gauge;
                    if (Riposte(_b)) gaugeB = 0;
                }
                else if (gaugeA <= 0 && gaugeB <= 0)
                {
                    // The rival resolves first on a tie, mirroring the delve engine.
                    Strike(_b);
                    if (_a.Php <= 0) break;
                    gaugeB += Gauge;

                    Strike(_a);
                    if (_b.Php <= 0 || _a.Php <= 0) break;
                    gaugeA += Gauge;
                }
                else
                {
                    if (FreeAction(_a)) { gaugeA = 0; continue; }
                    if (FreeAction(_b)) { gaugeB = 0; continue; }

                    int spdA = _a.StatOf(Stat.Spd);
                    int spdB = _b.StatOf(Stat.Spd);
                    int step = Math.Min(CeilDiv(gaugeA, spdA), CeilDiv(gaugeB, spdB));
                    gaugeA -= spdA * step;
                    gaugeB -= spdB * step;
                    _tick += step;
                }
            }

            if (_a.Php <= 0)
            {
                Snap(CombatEventType.Death, 0);
            }

            var carry = new CarryState
            {
                FuryBonus = _a.Fury,
                StoneBonus = _a.Stone,
                GaleBonus = _a.Gale,
                LuckBonus = _a.LuckGain,
                StrikeCount = _a.StrikeCount,
                PainCount = _a.PainCount,
                GoldCount = _a.GoldCount,
                StoneCount = _a.StoneCount,
                FleshSetUsed = _a.FleshSetUsed,
                QuenchBonus = _a.QuenchBonus,
                QuenchCount = _a.QuenchCount,
                SentinelBonus = _a.Sentinel,
            };

            return new CombatResult(_events, carry);
        }

        private IEnumerable<DuelSide> Both()
        {
            yield return _a;
            yield return _b;
        }

        private DuelSide Other(DuelSide side)
        {
            return side == _a ? _b : _a;
        }

        private static string Prefix(DuelSide side)
        {
            return side.IsHero ? string.Empty : side.Name + "'s ";
        }

        private static int CeilDiv(int numerator, int denominator)
        {
            return (numerator + denominator - 1) / denominator;
        }

        /// <summary>Hare's Drum: a dodge lets that side answer immediately.</summary>
        private bool Riposte(DuelSide side)
        {
            if (!side.InstantRiposte) return false;
            side.InstantRiposte = false;
            Line(side, RelicId.HaresDrum, side.Label(RelicId.HaresDrum) + " — instant riposte!", 0);
            return true;
        }

        /// <summary>Awakened Swift Boots skip the wait on every fourth strike.</summary>
        private static bool FreeAction(DuelSide side)
        {
            if (!side.IsAwake(RelicId.SwiftBoots) || side.Effective(RelicId.SwiftBoots) == 0) return false;
            if (side.Strikes <= 0 || side.Strikes % 4 != 0) return false;
            if (side.BootsUsedOnStrike == side.Strikes) return false;

            side.BootsUsedOnStrike = side.Strikes;
            return true;
        }

        // ---------- chains ----------

        /// <summary>
        /// A chain in a duel is keyed per side as well as per relic, so both duellists can draw
        /// on the same chain without their decay counters colliding.
        /// </summary>
        private sealed class DuelChain
        {
            private readonly Dictionary<string, int> _visits = new Dictionary<string, int>();

            public double Scale(DuelSide side, RelicId relic)
            {
                string key = (side.IsHero ? "A" : "B") + (int)relic;
                _visits.TryGetValue(key, out int seen);
                seen++;
                _visits[key] = seen;

                int chain = side.SetCount(RelicKind.Chain);
                double decay = chain >= 7 ? 0.75 : chain >= 3 ? 0.6 : 0.5;
                return Math.Pow(decay, seen - 1);
            }

            public bool DeepHealDone;
        }

        private static DuelChain NewChain()
        {
            return new DuelChain();
        }

        // ---------- events ----------

        private void Snap(CombatEventType type, int depth, int? amount = null, string source = null,
            RelicId relic = RelicId.None, bool? enemyCrit = null, bool? another = null, bool? foe = null)
        {
            var counters = new CombatCounters(
                strikes: _a.StrikeCount, pain: _a.PainCount, gold: _a.GoldCount,
                stoneCount: _a.StoneCount, fightStrikes: _a.Strikes, quenchCount: _a.QuenchCount,
                momentumCount: _a.MomentumCount, rabbitCount: _a.RabbitCount,
                strikeTotal: _a.StrikeTotal, sentinelBonus: _a.Sentinel);

            var state = new CombatSnapshot(
                tick: _tick,
                heroHp: _a.Php,
                enemyHp: Math.Max(0, _b.Php),
                gold: _a.Gold,
                enemyMaxHp: _b.Pmax,
                enemyAtk: _b.StatOf(Stat.Atk),
                enemyRank: _b.Rank,
                enemyArmor: _b.StatOf(Stat.Def),
                enemySpd: _b.StatOf(Stat.Spd),
                enemyLck: _b.StatOf(Stat.Lck),
                enemyVariant: _b.Variant,
                enemyIndex: _b.SpeciesIndex,
                heroAdrenaline: _a.Adrenaline,
                enemyRelics: null,
                heroMods: _a.DynamicMods(),
                counters: counters,
                heroFury: _a.Fury);

            _events.Add(new CombatEvent(type, depth, state, amount, source,
                relic, null, enemyCrit, another, foe));
        }

        /// <summary>A plain log line, attributed to whichever side produced it.</summary>
        private void Line(DuelSide side, RelicId relic, string text, int depth)
        {
            Snap(CombatEventType.First, depth, source: text, relic: relic, foe: side.IsHero ? (bool?)null : true);
        }

        // ---------- primitives ----------

        private void DealDamage(DuelSide side, int amount, string source, int depth,
            RelicId relic, DuelChain chain)
        {
            chain = chain ?? NewChain();
            DuelSide target = Other(side);

            if (depth > ChainCap)
            {
                Snap(CombatEventType.Fizzle, depth);
                return;
            }

            if (target.Php <= 0 || side.Php <= 0) return;

            if (amount <= 0)
            {
                if (relic != RelicId.None) Snap(CombatEventType.Fizzle, depth);
                return;
            }

            // Evasion lives entirely in the Lucky Clover, on either side.
            if (depth == 0 && target.Effective(RelicId.LuckyClover) > 0 &&
                _rng.Next() < target.StatOf(Stat.Lck) / 100.0)
            {
                if (side.IsHero) Snap(CombatEventType.EnemyMiss, 0, foe: true);
                else Snap(CombatEventType.Miss, 0, relic: RelicId.LuckyClover);
                OnDodge(target);
                return;
            }

            // The Guard set turns the first blow of the duel aside.
            if (depth == 0 && target.SetCount(RelicKind.Guard) >= 7 && !target.Blocked)
            {
                target.Blocked = true;
                Line(target, RelicId.None, Prefix(target) + "Guard set — the first blow glances off", 0);
                return;
            }

            int real = amount;

            // An awakened Stutterstep leaves the staggered side swinging into itself.
            if (depth == 0 && target.Staggered)
            {
                target.Staggered = false;
                Line(target, RelicId.Stutterstep,
                    target.Label(RelicId.Stutterstep) + " — the foe trips into its own blade", 1);
                if (side.Php > 0)
                {
                    DealDamage(target, Math.Max(1, amount), target.Label(RelicId.Stutterstep), 1,
                        RelicId.Stutterstep, NewChain());
                }

                return;
            }

            // An awakened Martyr's Knot puts every third blow back on the striker.
            if (depth == 0 && target.IsAwake(RelicId.MartyrsKnot) && target.Effective(RelicId.MartyrsKnot) > 0)
            {
                target.MartyrCount++;
                if (target.MartyrCount % 3 == 0)
                {
                    Line(target, RelicId.MartyrsKnot,
                        target.Label(RelicId.MartyrsKnot) + " bears it — the blow lands on the foe", 1);
                    if (side.Php > 0)
                    {
                        DealDamage(target, Math.Max(1, amount), target.Label(RelicId.MartyrsKnot), 1,
                            RelicId.MartyrsKnot, NewChain());
                    }

                    return;
                }
            }

            // An awakened Greedy Curse charges the purse instead of deepening the wound.
            if (depth == 0 && target.IsAwake(RelicId.GreedyCurse) && target.Effective(RelicId.GreedyCurse) > 0)
            {
                target.Gold += 2;
                Line(target, RelicId.GreedyCurse, target.Label(RelicId.GreedyCurse) + " charges the purse: +2 gold", 1);
            }

            if (depth == 0)
            {
                // Only genuine strikes meet armor and first-hit padding; relic damage lands raw.
                bool sunders = side.IsAwake(RelicId.Whetstone) && side.Effective(RelicId.Whetstone) > 0;
                real = Defense.Apply(real, sunders ? 0 : target.StatOf(Stat.Def));

                target.HitCount++;

                int hide = target.Effective(RelicId.PaddedHide);
                if (target.IsAwake(RelicId.PaddedHide) && hide > 0 && target.HitCount > 1 && target.HideLearned > 0)
                {
                    real = Math.Max(1, JsMath.RoundToInt(real * 0.5));
                    Line(target, RelicId.PaddedHide, target.Label(RelicId.PaddedHide) + " has learned this blow", 1);
                }

                if (!target.HitTaken)
                {
                    target.HitTaken = true;
                    if (target.IsAwake(RelicId.PaddedHide) && hide > 0) target.HideLearned = 1;
                    if (hide > 0)
                    {
                        real -= 2 * hide;
                        Line(target, RelicId.PaddedHide,
                            target.Label(RelicId.PaddedHide) + " softens the blow: -" + (2 * hide), 1);
                    }
                }

                real = Math.Max(1, real);
            }
            else
            {
                real = Math.Max(1, real);
            }

            target.Php -= real;

            if (side.IsHero)
            {
                Snap(CombatEventType.EnemyDamage, depth, amount: real, source: source, relic: relic);
            }
            else
            {
                Snap(CombatEventType.PlayerDamage, depth, amount: real, relic: relic,
                    foe: relic != RelicId.None ? (bool?)true : false,
                    enemyCrit: _critChain && depth == 0);
            }

            // Ember Cask shakes gold loose from relic damage.
            if (relic != RelicId.None && relic != RelicId.EmberCask && side.Effective(RelicId.EmberCask) > 0)
            {
                double scale = chain.Scale(side, RelicId.EmberCask);
                int coins = JsMath.RoundToInt(side.Effective(RelicId.EmberCask) * scale);
                if (coins > 0)
                {
                    GainGold(side, coins, side.Label(RelicId.EmberCask), depth + 1, RelicId.EmberCask, chain);
                    FireEmitter(side, RelicId.EmberCask, depth, chain, scale);
                }
            }

            // The death-defiance ladder, available to both sides.
            if (target.Php <= 0)
            {
                if (target.SetCount(RelicKind.Flesh) >= 7 && !target.FleshSetUsed)
                {
                    target.FleshSetUsed = true;
                    target.Php = 1;
                    Line(target, RelicId.None,
                        "Flesh set — " + (target.IsHero ? "you refuse" : target.Name + " refuses") + " to fall", 0);
                }
                else if (target.Effective(RelicId.GravekeepersSoil) > 0 && !target.SoilUsed)
                {
                    target.SoilUsed = true;
                    target.Php = Math.Max(1, JsMath.RoundToInt(target.Pmax * 0.25));
                    Line(target, RelicId.GravekeepersSoil,
                        target.Label(RelicId.GravekeepersSoil) + " — the ground gives back", 0);
                }
            }

            if (target.Php <= 0)
            {
                OnKill(side, depth, chain);
                return;
            }

            if (depth == 0)
            {
                int thorns = target.Effective(RelicId.ThornVest);
                if (thorns > 0)
                {
                    DuelChain thornChain = NewChain();
                    double scale = thornChain.Scale(target, RelicId.ThornVest);
                    DealDamage(target,
                        JsMath.RoundToInt(2 * thorns * scale) + (target.SetCount(RelicKind.Guard) >= 5 ? 1 : 0),
                        target.Label(RelicId.ThornVest), 1, RelicId.ThornVest, thornChain);
                    FireEmitter(target, RelicId.ThornVest, 0, thornChain, scale);
                }

                int adrenaline = target.Effective(RelicId.AdrenalineGland);
                if (adrenaline > 0 && !target.AdrenalineUsed)
                {
                    target.AdrenalineUsed = true;
                    DuelChain adrenChain = NewChain();
                    double scale = adrenChain.Scale(target, RelicId.AdrenalineGland);
                    target.Adrenaline += adrenaline;
                    if (target.IsHero)
                    {
                        Snap(CombatEventType.Adrenaline, 1, amount: adrenaline, relic: RelicId.AdrenalineGland);
                    }
                    else
                    {
                        Line(target, RelicId.AdrenalineGland,
                            target.Label(RelicId.AdrenalineGland) + " +" + adrenaline + " ATK", 1);
                    }

                    FireEmitter(target, RelicId.AdrenalineGland, 0, adrenChain, scale);
                }

                int marrow = target.Effective(RelicId.TrollMarrow);
                if (target.Php < target.Pmax / 2.0 && marrow > 0)
                {
                    Heal(target, 2 * marrow, target.Label(RelicId.TrollMarrow), 1, RelicId.TrollMarrow, NewChain());
                    if (target.IsAwake(RelicId.TrollMarrow) && Other(target).Php > 0)
                    {
                        DealDamage(target, 2 * marrow, target.Label(RelicId.TrollMarrow), 2,
                            RelicId.TrollMarrow, NewChain());
                    }
                }

                if (_critChain && target.Effective(RelicId.MirrorScale) > 0 && target.Php > 0 && side.Php > 0)
                {
                    DealDamage(target, real, target.Label(RelicId.MirrorScale), 1, RelicId.MirrorScale, NewChain());
                }

                target.PainCount++;
                if (target.PainCount % 3 == 0)
                {
                    FireTrigger(target, SocketTrigger.Hit, 0, RelicId.None, RelicId.None, NewChain());
                }
            }
        }

        private void Heal(DuelSide side, int amount, string source, int depth, RelicId relic, DuelChain chain)
        {
            chain = chain ?? NewChain();

            if (depth > ChainCap)
            {
                Snap(CombatEventType.Fizzle, depth);
                return;
            }

            DuelSide foe = Other(side);
            var after = new List<Action>();

            if (amount > 0)
            {
                int martyr = side.Effective(RelicId.MartyrsKnot);
                if (relic != RelicId.None && relic != RelicId.MartyrsKnot && martyr > 0)
                {
                    amount += martyr;
                    int d = depth + 1;
                    after.Add(() => Line(side, RelicId.MartyrsKnot,
                        side.Label(RelicId.MartyrsKnot) + " +" + martyr, d));
                }

                if (side.SetCount(RelicKind.Flesh) >= 5) amount += 1;

                // The opponent's bell starves you outright; your own only taxes you unless awakened.
                amount -= foe.Effective(RelicId.FamineBell) +
                          (side.IsAwake(RelicId.FamineBell) ? 0 : side.Effective(RelicId.FamineBell));
            }

            if (amount <= 0)
            {
                if (relic != RelicId.None) Snap(CombatEventType.Fizzle, depth);
                return;
            }

            int quenched = side.Effective(RelicId.QuenchedBlade);
            if (quenched > 0)
            {
                side.QuenchCount++;
                if (side.IsAwake(RelicId.QuenchedBlade) || side.QuenchCount % 3 == 0)
                {
                    side.QuenchBonus += quenched;
                    int d = depth + 1;
                    after.Add(() => Line(side, RelicId.QuenchedBlade,
                        side.Label(RelicId.QuenchedBlade) + " +" + quenched + " ATK", d));
                }
            }

            int real = Math.Min(amount, side.Pmax - side.Php);

            // An awakened Vampire Tooth eats the opponent's ceiling.
            if (relic == RelicId.VampireTooth && side.IsAwake(RelicId.VampireTooth) && foe.Pmax > 12)
            {
                foe.Pmax -= 1;
                if (foe.Php > foe.Pmax) foe.Php = foe.Pmax;
                Line(side, RelicId.VampireTooth, side.Label(RelicId.VampireTooth) + " takes 1 max HP", depth + 1);
            }

            if (real <= 0)
            {
                if (side.IsHero) Snap(CombatEventType.HealFull, depth, source: source, relic: relic);
                foreach (Action line in after) line();
                return;
            }

            side.Php += real;
            if (side.IsHero)
            {
                Snap(CombatEventType.Heal, depth, amount: real, source: source, relic: relic);
            }
            else
            {
                Snap(CombatEventType.EnemyHeal, depth, amount: real,
                    relic: relic != RelicId.None ? relic : RelicId.VampireTooth, foe: true);
            }

            foreach (Action line in after) line();

            int altar = side.Effective(RelicId.BloodAltar);
            if (altar > 0 && relic != RelicId.BloodAltar)
            {
                double scale = chain.Scale(side, RelicId.BloodAltar);
                DealDamage(side, JsMath.RoundToInt(2 * altar * scale),
                    side.Label(RelicId.BloodAltar), depth + 1, RelicId.BloodAltar, chain);
                if (side.IsAwake(RelicId.BloodAltar))
                {
                    Heal(side, 1, side.Label(RelicId.BloodAltar), depth + 2, RelicId.BloodAltar, chain);
                }

                FireEmitter(side, RelicId.BloodAltar, depth, chain, scale);
            }
        }

        private void GainGold(DuelSide side, int amount, string source, int depth, RelicId relic, DuelChain chain)
        {
            chain = chain ?? NewChain();

            if (depth > ChainCap)
            {
                Snap(CombatEventType.Fizzle, depth);
                return;
            }

            if (amount <= 0)
            {
                if (relic != RelicId.None) Snap(CombatEventType.Fizzle, depth);
                return;
            }

            double real = amount;
            if (side.SetCount(RelicKind.Greed) >= 3) real = JsMath.Round(real * 1.15);
            if (side.Effective(RelicId.FortunesDebt) > 0 && !side.IsAwake(RelicId.FortunesDebt))
            {
                real = JsMath.Round(real * 0.9);
            }

            int gained = Math.Max(1, (int)real);
            if (side.DebtLeft > 0) side.DebtLeft = Math.Max(0, side.DebtLeft - gained);
            side.Gold += gained;

            if (side.IsHero)
            {
                Snap(CombatEventType.Gold, depth, amount: gained, source: source, relic: relic);
            }
            else
            {
                Line(side, relic != RelicId.None ? relic : RelicId.MidasBlade,
                    source + " +" + gained + "g", depth);
            }

            int vial = side.Effective(RelicId.AlchemistsVial);
            if (vial > 0)
            {
                double scale = chain.Scale(side, RelicId.AlchemistsVial);
                Heal(side, JsMath.RoundToInt(vial * scale), side.Label(RelicId.AlchemistsVial),
                    depth + 1, RelicId.AlchemistsVial, chain);
                FireEmitter(side, RelicId.AlchemistsVial, depth, chain, scale);
            }

            if (relic != RelicId.CoinSinger && side.Effective(RelicId.CoinSinger) > 0 &&
                (relic != RelicId.None || side.IsAwake(RelicId.CoinSinger)))
            {
                double scale = chain.Scale(side, RelicId.CoinSinger);
                if (JsMath.RoundToInt(scale) > 0)
                {
                    EmitLuck(side, side.Label(RelicId.CoinSinger), depth + 1, RelicId.CoinSinger, chain);
                    FireEmitter(side, RelicId.CoinSinger, depth, chain, scale);
                }
            }

            side.GoldCount++;
            if (side.GoldCount % 3 == 0)
            {
                FireTrigger(side, SocketTrigger.Gold, depth, RelicId.AlchemistsVial, relic, chain);
            }
        }

        private void EmitLuck(DuelSide side, string source, int depth, RelicId relic, DuelChain chain, bool quiet = false)
        {
            chain = chain ?? NewChain();

            if (depth > ChainCap)
            {
                Snap(CombatEventType.Fizzle, depth);
                return;
            }

            // Only the hero's luck is worth a line of its own; the rival's is inferred.
            if (!quiet && side.IsHero)
            {
                Snap(CombatEventType.Luck, depth, source: source, relic: relic);
            }

            RabbitReact(side, depth, relic, chain);

            if (relic != RelicId.HexThread && side.Effective(RelicId.HexThread) > 0 && Other(side).Php > 0)
            {
                double scale = chain.Scale(side, RelicId.HexThread);
                int lash = JsMath.RoundToInt(side.Effective(RelicId.HexThread) * scale);
                if (lash > 0)
                {
                    DealDamage(side, lash, side.Label(RelicId.HexThread), depth + 1, RelicId.HexThread, chain);
                }
            }

            if (relic != RelicId.FortunesEdge && side.Effective(RelicId.FortunesEdge) > 0 && !side.BladeCharged)
            {
                side.BladeCharged = true;
                Line(side, RelicId.FortunesEdge, side.Label(RelicId.FortunesEdge) + " charges the blade", depth + 1);
            }

            int pulse = side.Effective(RelicId.QuickenedPulse);
            if (relic != RelicId.QuickenedPulse && pulse > 0)
            {
                side.Gale += pulse;
                Line(side, RelicId.QuickenedPulse,
                    side.Label(RelicId.QuickenedPulse) + " +" + pulse + " SPD", depth + 1);
            }

            FireTrigger(side, SocketTrigger.Luck, depth, RelicId.RabbitsFoot, relic, chain);
        }

        /// <summary>
        /// Rabbit's Foot answers only every third luck signal here. Duels run long enough that
        /// answering every one would snowball luck out of control.
        /// </summary>
        private void RabbitReact(DuelSide side, int depth, RelicId cause, DuelChain chain)
        {
            int rabbits = side.Effective(RelicId.RabbitsFoot);
            if (rabbits <= 0 || cause == RelicId.RabbitsFoot) return;

            side.RabbitCount++;
            if (!side.IsAwake(RelicId.RabbitsFoot) && side.RabbitCount % 3 != 0) return;

            chain = chain ?? NewChain();
            double scale = chain.Scale(side, RelicId.RabbitsFoot);
            int bonus = JsMath.RoundToInt(rabbits * scale);
            if (bonus > 0)
            {
                side.LuckGain += bonus;
                Line(side, RelicId.RabbitsFoot, side.Label(RelicId.RabbitsFoot) + " +" + bonus + " LUCK", depth + 1);
            }

            FireEmitter(side, RelicId.RabbitsFoot, depth, chain, scale);
        }

        private void OnDodge(DuelSide side)
        {
            DuelChain chain = NewChain();

            if (side.Effective(RelicId.LuckyClover) > 0)
            {
                double scale = chain.Scale(side, RelicId.LuckyClover);
                EmitLuck(side, side.Label(RelicId.LuckyClover), 1, RelicId.LuckyClover, chain);
                FireEmitter(side, RelicId.LuckyClover, 0, chain, scale);
            }

            if (side.Effective(RelicId.LoadedHorseshoe) > 0)
            {
                EmitLuck(side, side.Label(RelicId.LoadedHorseshoe), 1, RelicId.LoadedHorseshoe, chain);
            }

            if (side.Effective(RelicId.LuckyClover) > 0 && side.IsAwake(RelicId.LuckyClover))
            {
                EmitLuck(side, side.Label(RelicId.LuckyClover), 1, RelicId.LuckyClover, chain);
            }

            int sentinel = side.Effective(RelicId.SentinelBell);
            if (sentinel > 0)
            {
                side.Sentinel += sentinel;
                Line(side, RelicId.SentinelBell, side.Label(RelicId.SentinelBell) + " +" + sentinel + " DEF", 1);
                if (side.IsAwake(RelicId.SentinelBell) && Other(side).Php > 0)
                {
                    DealDamage(side, 1, side.Label(RelicId.SentinelBell), 1, RelicId.SentinelBell, chain);
                }
            }

            if (side.Effective(RelicId.CatsWhisker) > 0 && (side.IsAwake(RelicId.CatsWhisker) || !side.WhiskerUsed))
            {
                side.WhiskerUsed = true;
                DealDamage(side, Math.Max(1, JsMath.RoundToInt(side.StatOf(Stat.Atk) / 2.0)),
                    side.Label(RelicId.CatsWhisker), 1, RelicId.CatsWhisker, chain);
            }

            if (side.SetCount(RelicKind.Luck) >= 7)
            {
                DealDamage(side, 2, "Luck set", 1, RelicId.LuckyClover, chain);
            }

            DuelSide foe = Other(side);
            int stutter = side.Effective(RelicId.Stutterstep);
            if (stutter > 0)
            {
                foe.BaseSpd = Math.Max(10, foe.BaseSpd - stutter);
                if (side.IsAwake(RelicId.Stutterstep)) foe.Staggered = true;
                Snap(CombatEventType.EnemySlow, 1, amount: stutter, relic: RelicId.Stutterstep,
                    foe: side.IsHero ? (bool?)null : true);
            }

            if (side.Effective(RelicId.HaresDrum) > 0)
            {
                side.InstantRiposte = true;
            }

            FireTrigger(side, SocketTrigger.Dodge, 0, RelicId.LuckyClover, RelicId.None, chain);
        }

        private void OnKill(DuelSide side, int depth, DuelChain chain)
        {
            side.Kills++;
            if (side.IsHero)
            {
                Snap(CombatEventType.Kill, depth);
            }

            // The winner takes the loser's round purse.
            int purse = Other(side).Drop;
            if (purse > 0)
            {
                GainGold(side, purse, "loot", 0, RelicId.None, chain ?? NewChain());
            }
        }

        // ---------- sockets ----------

        private void FireEmitterAt(DuelSide side, int slot, int depth, DuelChain chain, double scale)
        {
            if (!side.SocketEmitters.TryGetValue(slot, out SocketEmitter emitter) || emitter == SocketEmitter.None)
            {
                return;
            }

            chain = chain ?? NewChain();
            RelicId id = side.Items[slot];
            int amount = JsMath.RoundToInt(EmitterAmount(emitter) * scale);

            if (amount <= 0)
            {
                Snap(CombatEventType.Fizzle, depth + 1);
                return;
            }

            string name = side.Label(id);
            switch (emitter)
            {
                case SocketEmitter.Dmg: DealDamage(side, amount, name, depth + 1, id, chain); break;
                case SocketEmitter.Heal: Heal(side, amount, name, depth + 1, id, chain); break;
                case SocketEmitter.Gold: GainGold(side, amount, name, depth + 1, id, chain); break;
                case SocketEmitter.Atk:
                    side.Fury += amount;
                    Line(side, id, name + " +" + amount + " ATK", depth + 1);
                    break;
                case SocketEmitter.Def:
                {
                    side.StoneCount++;
                    int hardened = Math.Max(amount, 1);
                    side.Stone += hardened;
                    Line(side, id, name + " +" + hardened + " DEF", depth + 1);
                    break;
                }

                case SocketEmitter.Spd:
                    side.Gale += amount;
                    Line(side, id, name + " +" + amount + " SPD", depth + 1);
                    break;
                case SocketEmitter.Luck:
                    side.LuckGain += amount;
                    Line(side, id, name + " +" + amount + " LUCK", depth + 1);
                    break;
            }
        }

        private void FireEmitter(DuelSide side, RelicId id, int depth, DuelChain chain, double scale)
        {
            for (int slot = 0; slot < side.Items.Count; slot++)
            {
                if (side.Items[slot] == id) FireEmitterAt(side, slot, depth, chain, scale);
            }
        }

        private void FireTrigger(DuelSide side, SocketTrigger trigger, int depth,
            RelicId exclude, RelicId cause, DuelChain chain)
        {
            if (depth > ChainCap) return;

            for (int slot = 0; slot < side.Items.Count; slot++)
            {
                RelicId id = side.Items[slot];
                if (id == exclude) continue;
                if (!side.SocketTriggers.TryGetValue(slot, out SocketTrigger fitted) || fitted != trigger) continue;

                if (cause != RelicId.None)
                {
                    if (cause == id) continue;
                    chain = chain ?? NewChain();
                    double scale = chain.Scale(side, id);
                    NativeEffect(side, id, depth + 1, chain, scale, true);
                    FireEmitterAt(side, slot, depth + 1, chain, scale);
                }
                else
                {
                    if (side.FiredThisBeat[slot]) continue;
                    side.FiredThisBeat[slot] = true;

                    DuelChain fresh = NewChain();
                    double scale = fresh.Scale(side, id);
                    NativeEffect(side, id, 0, fresh, scale, true);
                    FireEmitterAt(side, slot, 0, fresh, scale);
                }
            }
        }

        private void NativeEffect(DuelSide side, RelicId id, int depth, DuelChain chain, double scale, bool viaTrigger)
        {
            if (depth > ChainCap) return;
            chain = chain ?? NewChain();

            int copies = viaTrigger ? 1 : Math.Max(1, side.Effective(id));
            string name = side.Label(id) + (viaTrigger ? " ⚡" : string.Empty);

            int Scaled(int amount)
            {
                return JsMath.RoundToInt(amount * copies * scale);
            }

            switch (id)
            {
                case RelicId.AlchemistsVial: Heal(side, Scaled(1), name, depth + 1, id, chain); break;
                case RelicId.OxHeart: Heal(side, Scaled(2), name, depth + 1, id, chain); break;
                case RelicId.VampireTooth: Heal(side, Scaled(1), name, depth + 1, id, chain); break;

                case RelicId.CutpurseHook: GainGold(side, Scaled(1), name, depth + 1, id, chain); break;
                case RelicId.MidasBlade: GainGold(side, Scaled(2), name, depth + 1, id, chain); break;
                case RelicId.EmberCask: GainGold(side, Scaled(1), name, depth + 1, id, chain); break;

                case RelicId.WeightedDice:
                case RelicId.BloodAltar:
                case RelicId.ThornVest:
                    DealDamage(side, Scaled(2), name, depth + 1, id, chain);
                    break;

                case RelicId.HexThread:
                    if (Other(side).Php > 0) DealDamage(side, Scaled(1), name, depth + 1, id, chain);
                    break;

                case RelicId.LuckyClover: EmitLuck(side, name, depth + 1, id, chain); break;
                case RelicId.CoinSinger: EmitLuck(side, name, depth + 1, id, chain); break;

                case RelicId.Whetstone:
                {
                    int bonus = Scaled(1);
                    if (bonus > 0) { side.Fury += bonus; Line(side, id, name + " +" + bonus + " ATK", depth + 1); }
                    break;
                }

                case RelicId.IronSkin:
                {
                    int bonus = Scaled(2);
                    if (bonus > 0) { side.Stone += bonus; Line(side, id, name + " +" + bonus + " DEF", depth + 1); }
                    break;
                }

                case RelicId.BerserkerCharm:
                {
                    int bonus = Scaled(side.Php < side.Pmax / 2.0 ? 2 : 1);
                    if (bonus > 0) { side.Fury += bonus; Line(side, id, name + " +" + bonus + " ATK", depth + 1); }
                    break;
                }

                case RelicId.AdrenalineGland:
                {
                    int bonus = Scaled(1);
                    if (bonus > 0) { side.Fury += bonus; Line(side, id, name + " +" + bonus + " ATK", depth + 1); }
                    break;
                }

                case RelicId.RabbitsFoot:
                {
                    int bonus = Scaled(1);
                    if (bonus > 0) { side.LuckGain += bonus; Line(side, id, name + " +" + bonus + " LUCK", depth + 1); }
                    break;
                }

                case RelicId.SwiftBoots:
                case RelicId.BattleDash:
                {
                    int bonus = Scaled(2);
                    if (bonus > 0) { side.Gale += bonus; Line(side, id, name + " +" + bonus + " SPD", depth + 1); }
                    break;
                }

                case RelicId.SentinelBell:
                {
                    int bonus = Scaled(1);
                    int cap = side.IsAwake(RelicId.SentinelBell) ? 5 : 3;
                    if (bonus > 0 && side.Sentinel < cap)
                    {
                        side.Sentinel = Math.Min(cap, side.Sentinel + bonus);
                        Line(side, id, name + " +" + bonus + " DEF", depth + 1);
                    }

                    break;
                }

                case RelicId.FortunesEdge:
                    side.BladeCharged = true;
                    Line(side, id, name + " charges the blade", depth + 1);
                    break;

                case RelicId.QuickenedPulse:
                {
                    int bonus = side.Effective(id);
                    side.Gale += bonus;
                    Line(side, id, name + " +" + bonus + " SPD", depth + 1);
                    break;
                }
            }
        }

        private static int EmitterAmount(SocketEmitter emitter)
        {
            switch (emitter)
            {
                case SocketEmitter.Dmg: return 2;
                case SocketEmitter.Gold: return 2;
                default: return 1;
            }
        }

        // ---------- a turn ----------

        private void Strike(DuelSide side)
        {
            DuelSide target = Other(side);
            System.Array.Clear(side.FiredThisBeat, 0, side.FiredThisBeat.Length);

            side.Strikes++;
            side.StrikeTotal++;

            if (side.Effective(RelicId.AnvilHeart) > 0 && side.StrikeTotal % 10 == 0 &&
                side.AnvilBonus < (side.IsAwake(RelicId.AnvilHeart) ? 12 : 10))
            {
                side.AnvilBonus++;
                side.DefBonus++;
                Line(side, RelicId.AnvilHeart, side.Label(RelicId.AnvilHeart) + " hardens: +1 DEF", 1);
            }

            // Every rival is worthy blood, so the Oath always announces itself.
            int duelist = side.Effective(RelicId.DuelistsOath);
            if (side.Strikes == 1 && duelist > 0)
            {
                Line(side, RelicId.DuelistsOath,
                    side.Label(RelicId.DuelistsOath) + " — worthy blood: +" + (4 * duelist) + " ATK", 1);
            }

            if (TryExecute(side, target) && target.Php <= 0) return;

            if (side.Effective(RelicId.WeightedDice) > 0 && _rng.Next() < side.StatOf(Stat.Lck) / 100.0)
            {
                DuelChain critChain = NewChain();
                _critChain = true;
                double scale = critChain.Scale(side, RelicId.WeightedDice);

                if (side.IsHero)
                {
                    Snap(CombatEventType.Luck, 0, source: side.Label(RelicId.WeightedDice), relic: RelicId.WeightedDice);
                }

                DealDamage(side,
                    JsMath.RoundToInt(side.StatOf(Stat.Atk) * (side.IsAwake(RelicId.WeightedDice) ? 2 : 1.5)),
                    side.IsHero ? side.Label(RelicId.WeightedDice) : null, 0,
                    side.IsHero ? RelicId.WeightedDice : RelicId.None, critChain);

                if (side.SetCount(RelicKind.Luck) >= 5)
                {
                    EmitLuck(side, "Luck set", 1, RelicId.None, critChain);
                }

                FireEmitter(side, RelicId.WeightedDice, 0, critChain, scale);
                EmitLuck(side, side.IsHero ? side.Label(RelicId.WeightedDice) : "dice", 0,
                    RelicId.WeightedDice, critChain, quiet: true);
                _critChain = false;
            }
            else
            {
                int amount = side.StatOf(Stat.Atk);
                if (side.SetCount(RelicKind.Edge) >= 5 && side.Strikes % 4 == 0)
                {
                    amount = JsMath.RoundToInt(amount * 1.5);
                }

                if (side.BladeCharged)
                {
                    amount = JsMath.RoundToInt(amount * (side.IsAwake(RelicId.FortunesEdge) ? 2 : 1.5));
                    side.BladeCharged = false;
                    Line(side, RelicId.FortunesEdge, side.Label(RelicId.FortunesEdge) + " — charged strike!", 1);
                }

                DealDamage(side, amount, side.IsHero ? "you" : null, 0, RelicId.None, NewChain());
            }

            int momentum = side.Effective(RelicId.MomentumBead);
            if (momentum > 0)
            {
                side.MomentumCount++;
                if (side.MomentumCount % (side.IsAwake(RelicId.MomentumBead) ? 2 : 3) == 0)
                {
                    side.Momentum += momentum;

                    // Only the hero's momentum is announced; the rival's builds silently.
                    if (side.IsHero)
                    {
                        Snap(CombatEventType.Momentum, 1, amount: momentum, relic: RelicId.MomentumBead);
                    }
                }
            }

            if (TryExecute(side, target) && target.Php <= 0) return;

            if (side.SetCount(RelicKind.Pace) >= 7 && side.Strikes % 5 == 0 &&
                target.Php > 0 && side.Php > 0)
            {
                Line(side, RelicId.None, "Pace set — a second strike!", 0);
                DealDamage(side, side.StatOf(Stat.Atk), side.IsHero ? "you" : null, 0, RelicId.None, NewChain());
            }

            if (target.Php > 0 && side.Php > 0)
            {
                DuelChain chain = NewChain();
                int hook = side.Effective(RelicId.CutpurseHook);

                if ((hook > 0 && side.IsAwake(RelicId.CutpurseHook)) ||
                    _rng.Next() < side.StatOf(Stat.Lck) * (hook > 0 ? 2 : 1) / 100.0)
                {
                    if (hook > 0 && side.IsHero)
                    {
                        Snap(CombatEventType.Luck, 0, source: "loose coins", relic: RelicId.CutpurseHook);
                    }

                    double scale = hook > 0 ? chain.Scale(side, RelicId.CutpurseHook) : 1.0;
                    int coins = 1 + (hook > 0 ? JsMath.RoundToInt(hook * scale) : 0) +
                                (side.SetCount(RelicKind.Greed) >= 5 ? 1 : 0);

                    GainGold(side, coins, hook > 0 ? side.Label(RelicId.CutpurseHook) : "loose coins", 1,
                        hook > 0 ? RelicId.CutpurseHook : RelicId.None, chain);

                    if (hook > 0)
                    {
                        FireEmitter(side, RelicId.CutpurseHook, 0, chain, scale);
                        EmitLuck(side, "loose coins", 0, RelicId.CutpurseHook, chain, quiet: true);
                    }
                }

                int tooth = side.Effective(RelicId.VampireTooth);
                if (tooth > 0 && target.Php > 0)
                {
                    double scale = chain.Scale(side, RelicId.VampireTooth);
                    Heal(side, JsMath.RoundToInt(tooth * scale), side.Label(RelicId.VampireTooth), 1,
                        RelicId.VampireTooth, chain);
                    FireEmitter(side, RelicId.VampireTooth, 0, chain, scale);
                }

                side.StrikeCount++;
                if (side.StrikeCount % 3 == 0)
                {
                    FireTrigger(side, SocketTrigger.Attack, 0, RelicId.VampireTooth, RelicId.None, chain);
                }
            }
        }

        /// <summary>
        /// Executioner's Coin, versus rules: the threshold is 10% per copy up to two, and it
        /// fires once for the whole duel rather than once per floor.
        /// </summary>
        private bool TryExecute(DuelSide side, DuelSide target)
        {
            int copies = side.Effective(RelicId.ExecutionersCoin);
            if (copies == 0 || side.ExecutionerUsed || target.Php <= 0) return false;

            double threshold = target.Pmax * (side.IsAwake(RelicId.ExecutionersCoin) ? 0.15 : 0.1) *
                               Math.Min(2, copies);
            if (target.Php > threshold) return false;

            side.ExecutionerUsed = true;
            Line(side, RelicId.ExecutionersCoin,
                side.Label(RelicId.ExecutionersCoin) + " — the sentence is carried out", 0);
            DealDamage(side, target.Php + target.StatOf(Stat.Def),
                side.Label(RelicId.ExecutionersCoin), 1, RelicId.ExecutionersCoin, NewChain());
            return true;
        }
    }
}
