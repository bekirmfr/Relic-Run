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
    /// Scope: the chain bus and the six chain primitives are wired (Phase 3). Individual relic
    /// effects, sockets and emitters land in Phase 4, so <see cref="DynamicMods"/> is still
    /// empty and the socket maps on <see cref="HeroState"/> are carried but unread. Enemy
    /// relics that this engine reads are already handled, since bosses carry kits from
    /// Dungeon 2 onward.
    /// </remarks>
    public sealed partial class CombatEngine : ICombatBus, IAdrenalineReporter
    {

        private readonly CombatRules _rules = CombatRules.Delve();

        /// <summary>Chain depth beyond which effects fizzle rather than continue.</summary>
        private int ChainCap { get { return _rules.ChainCap; } }


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

        // Loadout-derived values. The inventory cannot change mid-floor, so these are computed
        // once per floor rather than per event.
        private int[] _kindCounts;
        private int _hollowIdols;
        private double _chainDecay;

        // In-fight stat boosts. These are what DynamicMods surfaces to the ledger; nothing
        // reads them directly, so every stat still resolves through one formula.
        private int _furyBonus;      // Adrenaline / Fury emitters, this floor
        private int _quenchBonus;    // Quenched Blade, this floor
        private int _headsmanBonus;  // Edge set (7), this floor
        private int _stoneBonus;     // Stone / Iron emitters - label says fight, lives a floor
        private int _galeBonus;      // Gale emitters, this floor
        private int _momentumBonus;  // Momentum Bead - persists across the whole floor
        private int _luckBonus;      // Rabbit / Gambler, this floor

        private int _momentumCount;

        // Once-per-floor flags. Adrenaline resets per fight only when awakened.
        private bool _adrenalineUsed;
        private int _hideLearned;

        // Once-per-fight flags.
        private bool _blockedFight;
        private bool _whiskerUsed;
        private bool _ironGlanced;

        /// <summary>Set by an awakened Stutterstep: the foe's next swing hits itself.</summary>
        private bool _staggered;

        /// <summary>Set by Hare's Drum: the hero's gauge empties again immediately.</summary>
        private bool _instantRiposte;

        /// <summary>Set by Fortune's Edge: the next strike lands charged.</summary>
        private bool _bladeCharged;

        /// <summary>
        /// The inventory slot currently firing, or -1. Relics are per-copy, so events carry the
        /// exact copy responsible and the UI can light up the right icon.
        /// </summary>
        private int _fireSlot = -1;

        /// <summary>Blows the awakened Martyr's Knot has absorbed this floor.</summary>
        private int _martyrCount;

        /// <summary>
        /// The strike number on which awakened Swift Boots last granted a free action.
        /// </summary>
        /// <remarks>
        /// Deliberately scoped to the FLOOR, not the fight, because the source declares it
        /// outside the pack loop while the strike counter resets per fight. The consequence is
        /// that a later fight reaching the same strike number gets no free action. That is
        /// almost certainly unintended in the original, but it is observable in play, so it is
        /// reproduced rather than corrected.
        /// </remarks>
        private int _bootsUsedOnStrike;

        /// <summary>
        /// A log line that must be recorded after the event it modifies, so it captures the
        /// state that event produced rather than the state before it.
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

        private void FlushPending(List<PendingLine> lines)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                Snap(CombatEventType.First, lines[i].Depth, relic: lines[i].Relic, source: lines[i].Source);
            }
        }

        /// <summary>
        /// One entry per inventory slot, cleared at the start of every beat. A genuine event may
        /// wake each socketed copy only once per beat, or two relics answering each other would
        /// resolve forever inside a single turn.
        /// </summary>
        private bool[] _firedThisBeat;

        /// <summary>
        /// The hero as the shared relic layer sees them. In a delve the in-fight bonuses live on
        /// the engine rather than on the hero, so this adapter forwards them.
        /// </summary>
        private sealed class HeroActor : ICombatActor
        {
            private readonly CombatEngine _engine;

            public HeroActor(CombatEngine engine)
            {
                _engine = engine;
            }

            public int Php
            {
                get { return _engine._hero.Php; }
                set { _engine._hero.Php = value; }
            }

            public int Pmax { get { return _engine._hero.Pmax; } }

            public int Effective(RelicId id) { return _engine.EffectiveCount(id); }

            public int CountRaw(RelicId id) { return _engine.CountItem(id); }

            public bool IsAwake(RelicId id) { return _engine.IsAwake(id); }

            public string Label(RelicId id) { return RelicCatalog.KeyOf(id); }

            public int Fury
            {
                get { return _engine._furyBonus; }
                set { _engine._furyBonus = value; }
            }

            public int Stone
            {
                get { return _engine._stoneBonus; }
                set { _engine._stoneBonus = value; }
            }

            public int Gale
            {
                get { return _engine._galeBonus; }
                set { _engine._galeBonus = value; }
            }

            public int LuckGain
            {
                get { return _engine._luckBonus; }
                set { _engine._luckBonus = value; }
            }

            public int Sentinel
            {
                get { return _engine._carry.SentinelBonus; }
                set { _engine._carry.SentinelBonus = value; }
            }

            public bool BladeCharged
            {
                get { return _engine._bladeCharged; }
                set { _engine._bladeCharged = value; }
            }

            public int Gold
            {
                get { return _engine._hero.Gold; }
                set { _engine._hero.Gold = value; }
            }

            public int QuenchBonus
            {
                get { return _engine._quenchBonus; }
                set { _engine._quenchBonus = value; }
            }

            public int QuenchCount
            {
                get { return _engine._carry.QuenchCount; }
                set { _engine._carry.QuenchCount = value; }
            }

            public int RabbitCount
            {
                // The delve declares a Rabbit counter but never reads it back, so it is not
                // stored anywhere; the cadence rule that would use it is duel-only.
                get { return 0; }
                set { }
            }

            public int GoldCount
            {
                get { return _engine._goldCount; }
                set { _engine._goldCount = value; }
            }

            public int DebtLeft
            {
                get { return _engine._hero.DebtLeft; }
                set { _engine._hero.DebtLeft = value; }
            }

            public int SetCount(RelicKind kind) { return _engine.SetCount(kind); }

            public int StatValue(Stat stat) { return _engine.HeroStat(stat); }

            public int Strikes
            {
                get { return _engine._fightStrikes; }
                set { _engine._fightStrikes = value; }
            }

            public int StrikeTotal
            {
                get { return _engine._hero.StrikeTotal; }
                set { _engine._hero.StrikeTotal = value; }
            }

            public int StrikeCount
            {
                get { return _engine._strikeCount; }
                set { _engine._strikeCount = value; }
            }

            public int AnvilBonus
            {
                get { return _engine._hero.AnvilBonus; }
                set { _engine._hero.AnvilBonus = value; }
            }

            public int DefenceBonus
            {
                get { return _engine._hero.DefBonus; }
                set { _engine._hero.DefBonus = value; }
            }

            public int MomentumCount
            {
                get { return _engine._momentumCount; }
                set { _engine._momentumCount = value; }
            }

            public int MomentumBonus
            {
                get { return _engine._momentumBonus; }
                set { _engine._momentumBonus = value; }
            }
        }

        private HeroActor _actor;

        // ---------- ICombatBus ----------

        void ICombatBus.DealDamage(ICombatActor from, int amount, string source, int depth, RelicId relic, IChain chain)
        {
            DealDamage(amount, source, depth, relic, (ChainContext)chain);
        }

        void ICombatBus.Heal(ICombatActor actor, int amount, string source, int depth, RelicId relic, IChain chain)
        {
            Heal(amount, source, depth, relic, (ChainContext)chain);
        }

        void ICombatBus.GainGold(ICombatActor actor, int amount, string source, int depth, RelicId relic, IChain chain)
        {
            GainGold(amount, source, depth, relic, (ChainContext)chain);
        }

        void ICombatBus.EmitLuck(ICombatActor actor, string source, int depth, RelicId relic, IChain chain, bool quiet)
        {
            EmitLuck(source, depth, relic, (ChainContext)chain, quiet);
        }

        void ICombatBus.Line(ICombatActor actor, RelicId relic, string text, int depth)
        {
            Snap(CombatEventType.First, depth, relic: relic, source: text);
        }

        bool ICombatBus.HasTarget(ICombatActor actor) { return _enemyHp > 0; }

        /// <summary>
        /// A delve foe is a stat block, not a relic-bearing side. It carries no relics the
        /// shared rules read, so an empty stand-in keeps those rules from having to special-case
        /// the asymmetry.
        /// </summary>
        ICombatActor ICombatBus.Opponent(ICombatActor actor) { return EmptyActor.Instance; }

        void ICombatBus.ReportFizzle(int depth) { Snap(CombatEventType.Fizzle, depth); }

        void ICombatBus.ReportHeal(ICombatActor actor, int amount, string source, int depth, RelicId relic)
        {
            Snap(CombatEventType.Heal, depth, amount: amount, source: source, relic: relic);
        }

        void ICombatBus.ReportHealFull(ICombatActor actor, string source, int depth, RelicId relic)
        {
            Snap(CombatEventType.HealFull, depth, source: source, relic: relic);
        }

        void ICombatBus.ReportGold(ICombatActor actor, int amount, string source, int depth, RelicId relic)
        {
            Snap(CombatEventType.Gold, depth, amount: amount, source: source, relic: relic);
        }

        void ICombatBus.ReportLuck(ICombatActor actor, string source, int depth, RelicId relic)
        {
            Snap(CombatEventType.Luck, depth, source: source, relic: relic);
        }

        /// <summary>An awakened Vampire Tooth drains the foe's HP ceiling.</summary>
        void ICombatBus.ReduceOpponentCeiling(ICombatActor actor, int depth)
        {
            if (_cur == null || EnemyMax <= 2) return;

            _cur.MaxHp = Math.Max(2, EnemyMax - 1);
            if (_enemyHp > _cur.MaxHp) _enemyHp = _cur.MaxHp;
            Snap(CombatEventType.First, depth, relic: RelicId.VampireTooth,
                source: RelicCatalog.KeyOf(RelicId.VampireTooth) + " takes 1 max HP");
        }

        void ICombatBus.FireEmitter(ICombatActor actor, RelicId id, int depth, IChain chain, double scale)
        {
            FireEmitter(id, depth, (ChainContext)chain, scale);
        }

        void ICombatBus.FireTrigger(ICombatActor actor, SocketTrigger trigger, int depth,
            RelicId exclude, RelicId cause, IChain chain)
        {
            FireTrigger(trigger, depth, exclude, cause, (ChainContext)chain);
        }

        /// <summary>A side that holds nothing, for rules that ask about an opponent's relics.</summary>
        private sealed class EmptyActor : ICombatActor
        {
            public static readonly EmptyActor Instance = new EmptyActor();

            public int Php { get; set; }

            public int Pmax { get { return 0; } }

            public int Effective(RelicId id) { return 0; }

            public int CountRaw(RelicId id) { return 0; }

            public bool IsAwake(RelicId id) { return false; }

            public string Label(RelicId id) { return RelicCatalog.KeyOf(id); }

            public int Fury { get; set; }

            public int Stone { get; set; }

            public int Gale { get; set; }

            public int LuckGain { get; set; }

            public int Sentinel { get; set; }

            public bool BladeCharged { get; set; }

            public int Gold { get; set; }

            public int QuenchBonus { get; set; }

            public int QuenchCount { get; set; }

            public int RabbitCount { get; set; }

            public int GoldCount { get; set; }

            public int DebtLeft { get; set; }

            public int SetCount(RelicKind kind) { return 0; }

            public int StatValue(Stat stat) { return 0; }

            public int Strikes { get; set; }

            public int StrikeTotal { get; set; }

            public int StrikeCount { get; set; }

            public int AnvilBonus { get; set; }

            public int DefenceBonus { get; set; }

            public int MomentumCount { get; set; }

            public int MomentumBonus { get; set; }
        }

        // ---------- what a turn asks of this mode ----------

        void ICombatBus.BeginBeat(ICombatActor actor)
        {
            System.Array.Clear(_firedThisBeat, 0, _firedThisBeat.Length);
        }

        IChain ICombatBus.NewChain() { return NewChain(); }

        double ICombatBus.NextRandom() { return _rng.Next(); }

        bool ICombatBus.TryExecute(ICombatActor attacker) { return TryExecuteInternal(); }

        /// <summary>In a delve only elites and bosses are worth the Oath.</summary>
        bool ICombatBus.FacingWorthyBlood(ICombatActor attacker)
        {
            return _cur != null &&
                   (_cur.Rank == EnemyRank.Boss || _cur.Rank == EnemyRank.King || _cur.Rank == EnemyRank.Elite);
        }

        void ICombatBus.ReportMomentum(ICombatActor actor, int amount)
        {
            Snap(CombatEventType.Momentum, 1, amount: amount, relic: RelicId.MomentumBead);
        }

        void ICombatBus.BeginCrit() { }

        void ICombatBus.EndCrit() { }

        string ICombatBus.PlainStrikeLabel(ICombatActor attacker) { return "you"; }

        string ICombatBus.CritLabel(ICombatActor attacker) { return RelicCatalog.KeyOf(RelicId.WeightedDice); }

        RelicId ICombatBus.CritRelic(ICombatActor attacker) { return RelicId.WeightedDice; }

        string ICombatBus.CritSignalLabel(ICombatActor attacker)
        {
            return RelicCatalog.KeyOf(RelicId.WeightedDice);
        }

        string ICombatBus.LooseCoinsLabel { get { return "looseCoins"; } }

        string ICombatBus.HookSpillLabel(ICombatActor attacker) { return "looseCoins"; }

        /// <summary>Coin Magnet swells a spill as well as a corpse's loot.</summary>
        int ICombatBus.SpillBonus(ICombatActor attacker) { return EffectiveCount(RelicId.CoinMagnet); }

        void IAdrenalineReporter.ReportAdrenaline(ICombatActor actor, int amount, int depth, string name)
        {
            Snap(CombatEventType.Adrenaline, depth, amount: amount, relic: RelicId.AdrenalineGland);
        }

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

            _actor = new HeroActor(this);
            CacheLoadout();
            _firedThisBeat = new bool[_hero.Items.Count];
            _bladeCharged = false;

            _strikeCount = _carry.StrikeCount;
            _painCount = _carry.PainCount;
            _goldCount = _carry.GoldCount;
            _furyBonus = _carry.FuryBonus;
            _quenchBonus = _carry.QuenchBonus;
            _headsmanBonus = _carry.HeadsmanBonus;
            _stoneBonus = _carry.StoneBonus;
            _galeBonus = _carry.GaleBonus;
            _luckBonus = _carry.LuckBonus;
            _momentumBonus = 0;
            _momentumCount = 0;
            _adrenalineUsed = false;
            _hideLearned = 0;
            _martyrCount = 0;
            _bootsUsedOnStrike = -1;

            // The Flesh set thickens the hero once per run, not once per floor.
            if (SetCount(RelicKind.Flesh) >= 3 && !_hero.FleshSetApplied)
            {
                _hero.FleshSetApplied = true;
                _hero.Pmax += 3;
                _hero.Php = Math.Min(_hero.Pmax, _hero.Php + 3);
                Snap(CombatEventType.First, 0, source: "Flesh set — +3 max HP");
            }

            for (int k = 0; k < pack.Count && _hero.Php > 0; k++)
            {
                _cur = pack[k];
                _enemyHp = _cur.Hp;

                _hitCount = 0;
                _fightStrikes = 0;
                _foeFury = false;
                _blockedFight = false;
                _whiskerUsed = false;
                _ironGlanced = false;
                _instantRiposte = false;

                // Momentum deliberately survives between fights on the same floor.
                if (IsAwake(RelicId.AdrenalineGland)) _adrenalineUsed = false;

                // Sentinel Bell's stacks are per fight, but the carried value is whatever the
                // last fight left, which is what a revive resumes from.
                _carry.SentinelBonus = 0;

                Snap(CombatEventType.Enter, 0, another: k > 0);

                System.Array.Clear(_firedThisBeat, 0, _firedThisBeat.Length);
                if (k == 0)
                {
                    FireTrigger(SocketTrigger.Floor, 0, RelicId.None, RelicId.None, NewChain());
                }

                FireTrigger(SocketTrigger.Fight, 0, RelicId.None, RelicId.None, NewChain());

                // Battle Dash resolves before the gauges run. Both sides dashing cancels out.
                bool heroDash = CountItem(RelicId.BattleDash) > 0;
                bool enemyDash = _cur.CountRelic(RelicId.BattleDash) > 0;

                var heroSlot = new AtbSlot
                {
                    Gauge = (heroDash && !enemyDash) ? 0 : AtbScheduler.Gauge,
                    Alive = () => _hero.Php > 0,
                    Act = () => CombatTurn.Strike(_actor, this, _rules, null),
                    Speed = () => HeroStat(Stat.Spd),
                    WantsFreeAction = AwakenedBootsReady,
                    WantsRiposte = TakeRiposte,
                };

                var enemySlot = new AtbSlot
                {
                    Gauge = (enemyDash && !heroDash) ? 0 : AtbScheduler.Gauge,
                    Alive = () => _enemyHp > 0,
                    Act = EnemyHits,
                    Speed = () => Math.Max(10, _cur.Spd),
                };

                // The Pace set starts the hero's gauge a quarter filled.
                if (heroSlot.Gauge > 0 && SetCount(RelicKind.Pace) >= 5)
                {
                    heroSlot.Gauge = Math.Max(0, heroSlot.Gauge - 25);
                }


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

                // The enemy holds the first slot: on a tie it acts before the hero, so a
                // killing blow still costs the hero the hit they were already taking.
                AtbScheduler.Run(enemySlot, heroSlot, step => _tick += step);
            }

            if (_hero.Php <= 0)
            {
                Snap(CombatEventType.Death, 0);
            }

            _carry.StrikeCount = _strikeCount;
            _carry.PainCount = _painCount;
            _carry.GoldCount = _goldCount;
            _carry.FuryBonus = _furyBonus;
            _carry.QuenchBonus = _quenchBonus;
            _carry.HeadsmanBonus = _headsmanBonus;
            _carry.StoneBonus = _stoneBonus;
            _carry.GaleBonus = _galeBonus;
            _carry.LuckBonus = _luckBonus;

            return new CombatResult(_events, _carry);
        }

        // ---------- turns ----------

        /// <summary>Hare's Drum: a dodge lets the hero answer immediately.</summary>
        private bool TakeRiposte()
        {
            if (!_instantRiposte) return false;
            _instantRiposte = false;

            if (IsAwake(RelicId.HaresDrum) && _enemyHp > 0)
            {
                DealDamage(2, RelicCatalog.KeyOf(RelicId.HaresDrum), 1, RelicId.HaresDrum, NewChain());
            }

            Snap(CombatEventType.First, 0, relic: RelicId.HaresDrum,
                source: RelicCatalog.KeyOf(RelicId.HaresDrum) + " — instant riposte!");
            return true;
        }

        /// <summary>Awakened Swift Boots skip the wait on every fourth strike.</summary>
        private bool AwakenedBootsReady()
        {
            if (!IsAwake(RelicId.SwiftBoots) || EffectiveCount(RelicId.SwiftBoots) == 0) return false;
            if (_fightStrikes <= 0 || _fightStrikes % 4 != 0) return false;
            if (_bootsUsedOnStrike == _fightStrikes) return false;

            _bootsUsedOnStrike = _fightStrikes;
            return true;
        }

        private void EnemyHits()
        {
            // The enemy's action is one genuine event: everything it provokes shares this chain,
            // and each socketed copy may wake at most once inside it.
            System.Array.Clear(_firedThisBeat, 0, _firedThisBeat.Length);
            ChainContext chain = NewChain();

            // Evasion lives entirely in the Lucky Clover. Without one the hero cannot dodge at
            // all, and no draw is taken — which is what keeps the RNG stream aligned.
            if (CountItem(RelicId.LuckyClover) > 0 && _rng.Next() < HeroStat(Stat.Lck) / 100.0)
            {
                Dodge(chain);
                return;
            }

            // The Guard set turns the opening blow of each fight aside entirely.
            if (SetCount(RelicKind.Guard) >= 7 && !_blockedFight)
            {
                _blockedFight = true;
                Snap(CombatEventType.First, 0, source: "Guard set — the first blow glances off");
                return;
            }

            int defense = HeroStat(Stat.Def);

            // An awakened Stutterstep leaves the foe swinging into its own blade.
            if (_staggered)
            {
                _staggered = false;
                Snap(CombatEventType.First, 0, relic: RelicId.Stutterstep,
                    source: RelicCatalog.KeyOf(RelicId.Stutterstep) + " — the foe trips into its own blade");
                if (_enemyHp > 0)
                {
                    DealDamage(Math.Max(1, _cur.Atk), RelicCatalog.KeyOf(RelicId.Stutterstep), 1,
                        RelicId.Stutterstep, NewChain());
                }

                return;
            }

            // An awakened Martyr's Knot takes every third blow onto the foe instead.
            if (IsAwake(RelicId.MartyrsKnot) && EffectiveCount(RelicId.MartyrsKnot) > 0)
            {
                _martyrCount++;
                if (_martyrCount % 3 == 0)
                {
                    Snap(CombatEventType.First, 0, relic: RelicId.MartyrsKnot,
                        source: RelicCatalog.KeyOf(RelicId.MartyrsKnot) +
                                " bears it \u2014 the blow lands on the foe");
                    if (_enemyHp > 0)
                    {
                        DealDamage(Math.Max(1, _cur.Atk), RelicCatalog.KeyOf(RelicId.MartyrsKnot), 1,
                            RelicId.MartyrsKnot, NewChain());
                    }

                    return;
                }
            }

            // The Greedy Curse makes every blow bite one deeper — unless awakened, when it pays
            // out instead. The coin goes through GainGold so it feeds the Vial like any other.
            bool greedy = CountItem(RelicId.GreedyCurse) > 0;
            bool greedAwake = IsAwake(RelicId.GreedyCurse);
            if (greedy && greedAwake)
            {
                GainGold(2, RelicCatalog.KeyOf(RelicId.GreedyCurse), 1, RelicId.GreedyCurse, NewChain());
            }

            int damage = Defense.Apply(
                _cur.Atk + (_foeFury ? 2 : 0) + (greedy && !greedAwake ? 1 : 0),
                defense, _hero.DefenseModel);

            // Awakened Iron Skin shrugs off one blow per fight outright.
            if (IsAwake(RelicId.IronSkin) && EffectiveCount(RelicId.IronSkin) > 0 && !_ironGlanced)
            {
                _ironGlanced = true;
                Snap(CombatEventType.First, 0, relic: RelicId.IronSkin,
                    source: RelicCatalog.KeyOf(RelicId.IronSkin) + " — the blow glances off");
                return;
            }

            _hitCount++;

            int hide = EffectiveCount(RelicId.PaddedHide);
            if (IsAwake(RelicId.PaddedHide) && hide > 0 && _hitCount > 1 && _hideLearned > 0)
            {
                damage = Math.Max(1, JsMath.RoundToInt(damage * 0.5));
                Snap(CombatEventType.First, 1, relic: RelicId.PaddedHide,
                    source: RelicCatalog.KeyOf(RelicId.PaddedHide) + " has learned this blow");
            }

            // Padded Hide softens only the first blow of each fight.
            if (_hitCount <= 1)
            {
                if (IsAwake(RelicId.PaddedHide) && hide > 0) _hideLearned = 1;
                if (hide > 0)
                {
                    damage -= 2 * hide;
                    Snap(CombatEventType.First, 1, relic: RelicId.PaddedHide,
                        source: RelicCatalog.KeyOf(RelicId.PaddedHide) + " softens the blow: -" + (2 * hide));
                }
            }

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

            RefuseDeath();

            // Mirror Scale throws a critical hit straight back, in full. It needs a foe that
            // can crit at all, so only a Weighted Dice carrier ever sets it off.
            if (crit && EffectiveCount(RelicId.MirrorScale) > 0 && _hero.Php > 0 && _enemyHp > 0)
            {
                DealDamage(damage, RelicCatalog.KeyOf(RelicId.MirrorScale), 1, RelicId.MirrorScale, chain);
            }

            // Troll Marrow knits the hero back together while they are bloodied.
            int marrow = EffectiveCount(RelicId.TrollMarrow);
            if (_hero.Php > 0 && _hero.Php < _hero.Pmax / 2.0 && marrow > 0)
            {
                Heal(2 * marrow, RelicCatalog.KeyOf(RelicId.TrollMarrow), 1, RelicId.TrollMarrow, chain);
                if (IsAwake(RelicId.TrollMarrow) && _enemyHp > 0)
                {
                    DealDamage(2 * marrow, RelicCatalog.KeyOf(RelicId.TrollMarrow), 2,
                        RelicId.TrollMarrow, chain);
                }
            }

            // Vampire Tooth on the foe: it drinks back what it just took. The Famine Bell
            // silences it.
            int tooth = _cur.CountRelic(RelicId.VampireTooth);
            if (tooth > 0 && _enemyHp > 0 && _enemyHp < EnemyMax &&
                EffectiveCount(RelicId.FamineBell) == 0)
            {
                int healed = Math.Min(tooth, EnemyMax - _enemyHp);
                _enemyHp += healed;
                Snap(CombatEventType.EnemyHeal, 1, amount: healed, relic: RelicId.VampireTooth, foe: true);
            }

            // The Adrenaline Gland banks permanent attack the first time the hero is hurt.
            int adrenaline = CountItem(RelicId.AdrenalineGland);
            if (adrenaline > 0 && _hero.Php > 0 && !_adrenalineUsed)
            {
                _adrenalineUsed = true;
                double adrenScale = chain.Scale(RelicId.AdrenalineGland);
                _hero.Adrenaline += adrenaline;
                Snap(CombatEventType.Adrenaline, 1, amount: adrenaline, relic: RelicId.AdrenalineGland);
                FireEmitter(RelicId.AdrenalineGland, 0, chain, adrenScale);
            }

            // Thorn Vest answers the blow that landed, not the one the hero threw.
            int thorns = EffectiveCount(RelicId.ThornVest);
            if (thorns > 0 && _hero.Php > 0)
            {
                double scale = chain.Scale(RelicId.ThornVest);
                DealDamage(
                    JsMath.RoundToInt(2 * thorns * scale) + (SetCount(RelicKind.Guard) >= 5 ? 1 : 0),
                    RelicCatalog.KeyOf(RelicId.ThornVest), 1, RelicId.ThornVest, chain);
                FireEmitter(RelicId.ThornVest, 0, chain, scale);
            }

            // Only a hit the hero survived counts toward the pain cadence.
            if (_hero.Php > 0)
            {
                if (greedy)
                {
                    FireEmitter(RelicId.GreedyCurse, 0, chain, chain.Scale(RelicId.GreedyCurse));
                }

                _painCount++;

                // Socketed pain triggers fire on every third hit taken.
                if (_painCount % 3 == 0)
                {
                    FireTrigger(SocketTrigger.Hit, 0, RelicId.None, RelicId.None, chain);
                }
            }
        }

        /// <summary>The hero slipped the blow. Everything that keys off a dodge fires here.</summary>
        private void Dodge(ChainContext chain)
        {
            Snap(CombatEventType.Miss, 0, relic: RelicId.LuckyClover);

            double cloverScale = chain.Scale(RelicId.LuckyClover);
            EmitLuck(RelicCatalog.KeyOf(RelicId.LuckyClover), 1, RelicId.LuckyClover, chain);
            FireEmitter(RelicId.LuckyClover, 0, chain, cloverScale);

            if (EffectiveCount(RelicId.LoadedHorseshoe) > 0)
            {
                EmitLuck(RelicCatalog.KeyOf(RelicId.LoadedHorseshoe), 1, RelicId.LoadedHorseshoe, chain);
            }

            if (EffectiveCount(RelicId.LuckyClover) > 0 && IsAwake(RelicId.LuckyClover))
            {
                EmitLuck(RelicCatalog.KeyOf(RelicId.LuckyClover), 1, RelicId.LuckyClover, chain);
            }

            int sentinel = EffectiveCount(RelicId.SentinelBell);
            if (sentinel > 0)
            {
                _carry.SentinelBonus += sentinel;
                Snap(CombatEventType.First, 1, relic: RelicId.SentinelBell,
                    source: RelicCatalog.KeyOf(RelicId.SentinelBell) + " +" + sentinel + " DEF");
                if (IsAwake(RelicId.SentinelBell) && _enemyHp > 0)
                {
                    DealDamage(1, RelicCatalog.KeyOf(RelicId.SentinelBell), 1, RelicId.SentinelBell, chain);
                }
            }

            if (EffectiveCount(RelicId.CatsWhisker) > 0 &&
                (IsAwake(RelicId.CatsWhisker) || !_whiskerUsed) && _enemyHp > 0)
            {
                _whiskerUsed = true;
                DealDamage(Math.Max(1, JsMath.RoundToInt(HeroStat(Stat.Atk) / 2.0)),
                    RelicCatalog.KeyOf(RelicId.CatsWhisker), 1, RelicId.CatsWhisker, chain);
            }

            // The Luck set turns every dodge into a counter.
            if (SetCount(RelicKind.Luck) >= 7 && _enemyHp > 0)
            {
                DealDamage(2, "Luck set", 1, RelicId.LuckyClover, chain);
            }

            int stutter = EffectiveCount(RelicId.Stutterstep);
            if (stutter > 0)
            {
                _cur.Spd = Math.Max(10, _cur.Spd - stutter);
                Snap(CombatEventType.EnemySlow, 1, amount: stutter, relic: RelicId.Stutterstep);
                if (IsAwake(RelicId.Stutterstep)) _staggered = true;
            }

            if (EffectiveCount(RelicId.HaresDrum) > 0)
            {
                _instantRiposte = true;
            }

            FireTrigger(SocketTrigger.Dodge, 0, RelicId.LuckyClover, RelicId.None, chain);
        }

        /// <summary>
        /// The death-defiance ladder, in the order the source tries it: the Flesh set once per
        /// floor, then Gravekeeper's Soil once per run.
        /// </summary>
        private void RefuseDeath()
        {
            if (_hero.Php > 0) return;

            if (SetCount(RelicKind.Flesh) >= 7 && !_carry.FleshSetUsed)
            {
                _carry.FleshSetUsed = true;
                _hero.Php = 1;
                Snap(CombatEventType.First, 0, source: "Flesh set — you refuse to fall");
            }

            if (_hero.Php <= 0 && EffectiveCount(RelicId.GravekeepersSoil) > 0 && !_hero.SoilUsed)
            {
                _hero.SoilUsed = true;
                _hero.Php = Math.Max(1, JsMath.RoundToInt(
                    _hero.Pmax * (IsAwake(RelicId.GravekeepersSoil) ? 0.5 : 0.25)));
                Snap(CombatEventType.First, 0, relic: RelicId.GravekeepersSoil,
                    source: RelicCatalog.KeyOf(RelicId.GravekeepersSoil) + " — the ground gives you back");
            }
        }

        /// <summary>
        /// Executioner's Coin finishes a foe already below a fifth of its health. It fires at
        /// most once per floor, and the flag lives in carry state so a revive cannot hand the
        /// hero a second execution.
        /// </summary>
        private bool TryExecuteInternal()
        {
            if (EffectiveCount(RelicId.ExecutionersCoin) == 0 || _carry.ExecutionerUsed ||
                _enemyHp <= 0 ||
                _enemyHp > EnemyMax * (IsAwake(RelicId.ExecutionersCoin) ? 0.3 : 0.2))
            {
                return false;
            }

            _carry.ExecutionerUsed = true;
            Snap(CombatEventType.First, 0, relic: RelicId.ExecutionersCoin,
                source: RelicCatalog.KeyOf(RelicId.ExecutionersCoin) + " — the sentence is carried out");

            // Armor is added back so the blow is lethal after reduction.
            DealDamage(_enemyHp + _cur.Armor, RelicCatalog.KeyOf(RelicId.ExecutionersCoin), 1,
                RelicId.ExecutionersCoin, NewChain());
            return true;
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

        /// <summary>Counts relics by kind once per floor; set bonuses read these.</summary>
        private void CacheLoadout()
        {
            _kindCounts = new int[8];

            for (int i = 0; i < _hero.Items.Count; i++)
            {
                _kindCounts[(int)RelicCatalog.KindOf(_hero.Items[i])]++;
            }

            // Hollow Idol counts itself toward every set, when the mode says it does. The
            // engine credits an awakened copy twice while the stat ledger credits it once — the
            // two disagree in the source, and both are reproduced as written rather than
            // reconciled, because "fixing" it here would silently change which set bonuses a
            // real loadout reaches.
            _hollowIdols = RelicTuning.For(RelicId.HollowIdol, _rules.Mode).CountsTowardEverySet
                ? EffectiveCount(RelicId.HollowIdol)
                : 0;

            _chainDecay = ChainContext.DecayFor(SetCount(RelicKind.Chain));
        }

        /// <summary>Relics of a kind. Hollow Idol counts itself toward every set.</summary>
        private int SetCount(RelicKind kind)
        {
            return _kindCounts[(int)kind] + _hollowIdols;
        }

        private bool IsAwake(RelicId id)
        {
            foreach (RelicId awake in _hero.Awakened)
            {
                if (awake == id) return true;
            }

            return false;
        }

        /// <summary>Copies held, plus one for an awakened copy.</summary>
        private int EffectiveCount(RelicId id)
        {
            return CountItem(id) + (IsAwake(id) ? 1 : 0);
        }

        /// <summary>Opens a fresh chain. Every genuine event gets its own.</summary>
        private ChainContext NewChain()
        {
            return new ChainContext(_chainDecay);
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
            if (_furyBonus == 0 && _quenchBonus == 0 && _headsmanBonus == 0 && _stoneBonus == 0 &&
                _carry.SentinelBonus == 0 && _galeBonus == 0 && _momentumBonus == 0 && _luckBonus == 0)
            {
                return System.Array.Empty<StatModifier>();
            }

            var mods = new List<StatModifier>(4);
            if (_furyBonus != 0) mods.Add(new StatModifier(Stat.Atk, "Fury (this floor)", _furyBonus, "dyn"));
            if (_quenchBonus != 0) mods.Add(new StatModifier(Stat.Atk, "Quenched Blade (this floor)", _quenchBonus, "dyn"));
            if (_headsmanBonus != 0) mods.Add(new StatModifier(Stat.Atk, "Headsman's Notch (this floor)", _headsmanBonus, "dyn"));
            if (_stoneBonus != 0) mods.Add(new StatModifier(Stat.Def, "Stone / Iron emitters (this fight)", _stoneBonus, "dyn"));
            if (_carry.SentinelBonus != 0) mods.Add(new StatModifier(Stat.Def, "Sentinel Bell (this floor)", _carry.SentinelBonus, "dyn"));
            if (_galeBonus != 0) mods.Add(new StatModifier(Stat.Spd, "Gale emitters (this floor)", _galeBonus, "dyn"));
            if (_momentumBonus != 0) mods.Add(new StatModifier(Stat.Spd, "Momentum Bead (this fight)", _momentumBonus, "dyn"));
            if (_luckBonus != 0) mods.Add(new StatModifier(Stat.Lck, "Rabbit / Gambler (this floor)", _luckBonus, "dyn"));
            return mods;
        }

        private void Snap(CombatEventType type, int depth, int? amount = null, string source = null,
            RelicId relic = RelicId.None, int? relicSlot = null, bool? enemyCrit = null,
            bool? another = null, bool? foe = null)
        {
            _events.Add(Build(type, depth, amount, source, relic, relicSlot, enemyCrit, another, foe));
        }

        /// <summary>
        /// Builds an event without recording it, for the handful of log lines that must appear
        /// after the event they modify rather than before it.
        /// </summary>
        private CombatEvent Build(CombatEventType type, int depth, int? amount = null, string source = null,
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
                momentumCount: _momentumCount,

                // The source declares a Rabbit's Foot counter but never increments it, so this
                // reports zero to match. Its actual effect goes straight to the luck bonus.
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

            // Events carry the exact copy that fired, when one did.
            if (!relicSlot.HasValue && _fireSlot >= 0)
            {
                relicSlot = _fireSlot;
            }

            return new CombatEvent(type, depth, state, amount, source,
                relic, relicSlot, enemyCrit, another, foe);
        }
    }
}
