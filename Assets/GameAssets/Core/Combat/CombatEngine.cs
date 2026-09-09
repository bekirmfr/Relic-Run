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

        private readonly CombatRules _rules;

        /// <summary>
        /// A delve fought under the rules the game ships.
        /// </summary>
        public CombatEngine() : this(null)
        {
        }

        /// <summary>
        /// A delve fought under stated rules — <see cref="CombatRules.DelveAsRecorded"/> for the
        /// corpus gate, which predates the shipped answer in one place.
        /// </summary>
        public CombatEngine(CombatRules rules)
        {
            _rules = rules ?? CombatRules.Delve();
        }

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

        /// <summary>The family an awakened Hollow Idol backs, or <see cref="SetCounts.NoKind"/>.</summary>
        private int _idolBackedKind = SetCounts.NoKind;
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
        private bool _hitTaken;
        private bool _foeCrit;

        /// <summary>Which foe of the pack is being fought, so a revive can resume there.</summary>
        private int _foeIndex;

        /// <summary>Set by an awakened Stutterstep: the foe's next swing hits itself.</summary>
        private bool _staggered;

        /// <summary>Set by Hare's Drum: the hero's gauge empties again immediately.</summary>
        private bool _instantRiposte;

        /// <summary>Whether the Curse set has already detonated this floor.</summary>
        private bool _curseSetSpent;

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

            public int Adrenaline
            {
                get { return _engine._hero.Adrenaline; }
                set { _engine._hero.Adrenaline = value; }
            }

            public int PainCount
            {
                get { return _engine._painCount; }
                set { _engine._painCount = value; }
            }

            public int StoneCount
            {
                get { return _engine._carry.StoneCount; }
                set { _engine._carry.StoneCount = value; }
            }

            public int HeadsmanBonus
            {
                get { return _engine._headsmanBonus; }
                set { _engine._headsmanBonus = value; }
            }

            public bool WhiskerUsed
            {
                get { return _engine._whiskerUsed; }
                set { _engine._whiskerUsed = value; }
            }

            public bool InstantRiposte
            {
                get { return _engine._instantRiposte; }
                set { _engine._instantRiposte = value; }
            }

            public int BootsUsedOnStrike
            {
                get { return _engine._bootsUsedOnStrike; }
                set { _engine._bootsUsedOnStrike = value; }
            }

            /// <summary>The Guard set turns one blow aside per fight.</summary>
            public bool Blocked
            {
                get { return _engine._blockedFight; }
                set { _engine._blockedFight = value; }
            }

            /// <summary>
            /// Always false. A delve staggers the FOE, never the hero — the Stutterstep that
            /// would do it is the hero's own.
            /// </summary>
            public bool Staggered { get; set; }

            public bool IronGlanced
            {
                get { return _engine._ironGlanced; }
                set { _engine._ironGlanced = value; }
            }

            public int HitCount
            {
                get { return _engine._hitCount; }
                set { _engine._hitCount = value; }
            }

            public bool HitTaken
            {
                get { return _engine._hitTaken; }
                set { _engine._hitTaken = value; }
            }

            public int HideLearned
            {
                get { return _engine._hideLearned; }
                set { _engine._hideLearned = value; }
            }

            public int MartyrCount
            {
                get { return _engine._martyrCount; }
                set { _engine._martyrCount = value; }
            }

            public int Kills
            {
                get { return _engine._hero.Kills; }
                set { _engine._hero.Kills = value; }
            }

            public int ItemCount { get { return _engine._hero.Items.Count; } }

            public RelicId ItemAt(int slot) { return _engine._hero.Items[slot]; }

            public SocketTrigger TriggerAt(int slot) { return _engine.TriggerAt(slot); }

            public SocketEmitter EmitterAt(int slot) { return _engine.EmitterAt(slot); }

            public bool HasFiredThisBeat(int slot) { return _engine._firedThisBeat[slot]; }

            public void MarkFiredThisBeat(int slot) { _engine._firedThisBeat[slot] = true; }
        }

        private HeroActor _actor;
        private FoeActor _foe;

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

        string ICombatBus.SidePrefix(ICombatActor actor) { return string.Empty; }

        /// <summary>Both sides of a delve are reduced on the hero's curve.</summary>
        DefenseModel ICombatBus.DefenseModelOf(ICombatActor actor) { return _hero.DefenseModel; }

        void ICombatBus.Line(ICombatActor actor, RelicId relic, string text, int depth)
        {
            Snap(CombatEventType.First, depth, relic: relic, source: text);
        }

        bool ICombatBus.HasTarget(ICombatActor actor) { return Opponent(actor).Php > 0; }

        /// <summary>
        /// A delve foe is a stat block, not a relic-bearing side. It carries no relics the
        /// shared rules read, so an empty stand-in keeps those rules from having to special-case
        /// the asymmetry.
        /// </summary>
        /// <summary>The delve's two sides: the hero swings at the foe, and the foe back.</summary>
        ICombatActor ICombatBus.Opponent(ICombatActor actor) { return Opponent(actor); }

        private ICombatActor Opponent(ICombatActor actor) { return actor == _actor ? (ICombatActor)_foe : _actor; }

        void ICombatBus.ReportFizzle(int depth) { Snap(CombatEventType.Fizzle, depth); }

        bool ICombatBus.CanDeal(ICombatActor attacker) { return Opponent(attacker).Php > 0; }

        /// <summary>Only the hero collects for a corpse; a foe that fells the hero ends the run.</summary>
        bool ICombatBus.ClaimsTheKill(ICombatActor killer) { return killer == _actor; }

        /// <summary>
        /// Evasion lives entirely in the Lucky Clover, on either side. A delve exempts the
        /// hero's crits: a foe cannot slip a Weighted Dice blow, only a plain one.
        /// </summary>
        bool ICombatBus.TargetEvades(ICombatActor attacker, string source, int depth, IChain chain)
        {
            if (attacker == _actor && source != "you") return false;

            ICombatActor target = Opponent(attacker);
            if (target.CountRaw(RelicId.LuckyClover) == 0) return false;
            if (_rng.Next() >= target.StatValue(Stat.Lck) / 100.0) return false;

            if (attacker == _actor)
            {
                Snap(CombatEventType.EnemyMiss, 0, foe: true);
            }
            else
            {
                Snap(CombatEventType.Miss, 0, relic: RelicId.LuckyClover);
                CombatDamage.OnDodge(_actor, this, _rules);
            }

            return true;
        }

        int ICombatBus.ApplyDamage(ICombatActor attacker, int dealt, string source, int depth, RelicId relic)
        {
            if (attacker == _actor)
            {
                _enemyHp -= dealt;
                Snap(CombatEventType.EnemyDamage, depth, amount: dealt, source: source, relic: relic);
                return dealt;
            }

            // The foe's Weighted Dice multiplies AFTER mitigation — a stat block rolls its crit
            // on the blow that landed, where a full side rolls it on the blow it throws.
            _foeCrit = _cur.CountRelic(RelicId.WeightedDice) > 0 && _rng.Next() < _cur.Lck / 100.0;
            if (_foeCrit) dealt = JsMath.RoundToInt(dealt * 1.5);

            _hero.Php -= dealt;
            Snap(CombatEventType.PlayerDamage, depth, amount: dealt, enemyCrit: _foeCrit);
            return dealt;
        }

        /// <summary>
        /// The hero answers a blow with the full reaction ladder. A foe answers with the two
        /// traits a stat block carries — and its Thorn Vest is a flatter thing than the hero's,
        /// so it is a monster trait rather than the relic.
        /// </summary>
        void ICombatBus.AfterDamage(ICombatActor attacker, int dealt, int depth, IChain chain)
        {
            if (attacker != _actor)
            {
                CombatDamage.React(_actor, _foe, dealt, _foeCrit, this, _rules, chain);
                return;
            }

            int thorns = _cur.CountRelic(RelicId.ThornVest);
            if (depth == 0 && thorns > 0 && _hero.Php > 0)
            {
                _hero.Php -= thorns;
                Snap(CombatEventType.PlayerDamage, 1, amount: thorns, relic: RelicId.ThornVest, foe: true);
                if (_hero.Php <= 0) return;
            }

            if (!_foeFury && _cur.CountRelic(RelicId.BerserkerCharm) > 0 && _enemyHp < EnemyMax / 2.0)
            {
                _foeFury = true;
                Snap(CombatEventType.EnemyFury, 1, amount: 2, relic: RelicId.BerserkerCharm, foe: true);
            }
        }

        /// <summary>The foe's whole turn: it swings, and the shared ladder does the rest.</summary>
        private void EnemyHits()
        {
            // The foe's action is one genuine event: everything it provokes shares this chain,
            // and each socketed copy may wake at most once inside it.
            System.Array.Clear(_firedThisBeat, 0, _firedThisBeat.Length);

            int swing = _foe.StatValue(Stat.Atk) + (_foeFury ? 2 : 0);
            CombatDamage.Deal(_foe, this, _rules, swing, null, 0, RelicId.None, NewChain());
        }

        /// <summary>A delve foe cannot be struck by a corpse it already made, so only it is asked.</summary>

        /// <summary>Foes evade only with a Lucky Clover of their own.</summary>

        /// <summary>
        /// A stat-block foe has nothing that turns a blow aside, so this is armor alone — and
        /// armor meets every blow, unlike a duel, where only a genuine strike meets a defence.
        /// An awakened Whetstone cuts straight through it, but only on the hero's own strikes.
        /// </summary>


        /// <summary>
        /// A foe answers with the two things a stat block can carry: a Thorn Vest that bites
        /// whoever struck it, and a Berserker Charm that enrages once it is bloodied.
        /// </summary>

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
        void ICombatBus.SlowOpponent(ICombatActor actor, int amount, bool stagger)
        {
            _cur.Spd = Math.Max(10, _cur.Spd - amount);
            Snap(CombatEventType.EnemySlow, 1, amount: amount, relic: RelicId.Stutterstep);
            if (stagger) _staggered = true;
        }

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
            SocketFiring.FireEmitter(actor, this, _rules, id, depth, chain, scale);
        }

        void ICombatBus.BeginFire(ICombatActor actor, int slot) { _fireSlot = slot; }

        void ICombatBus.EndFire(ICombatActor actor) { _fireSlot = -1; }

        void ICombatBus.FireTrigger(ICombatActor actor, SocketTrigger trigger, int depth,
            RelicId exclude, RelicId cause, IChain chain)
        {
            SocketFiring.FireTrigger(actor, this, _rules, trigger, depth, exclude, cause, chain);
        }

        /// <summary>A side that holds nothing, for rules that ask about an opponent's relics.</summary>
        /// <summary>
        /// A delve foe, as an actor in its own right.
        /// </summary>
        /// <remarks>
        /// It carries relics — Battle Dash, Berserker Charm, Lucky Clover, Thorn Vest, Vampire
        /// Tooth, Weighted Dice — so the shared rules can read it the same way they read a
        /// duellist. What it does not have is awakenings, sockets, chain counters or a purse:
        /// those read as empty, which is exactly what makes the shared ladder collapse to
        /// "armor only" when the foe is the one being hit.
        /// </remarks>
        private sealed class FoeActor : ICombatActor
        {
            private readonly CombatEngine _engine;

            public FoeActor(CombatEngine engine)
            {
                _engine = engine;
            }

            public int Php
            {
                get { return _engine._enemyHp; }
                set { _engine._enemyHp = value; }
            }

            public int Pmax { get { return _engine.EnemyMax; } }

            public int Effective(RelicId id) { return _engine._cur.CountRelic(id); }

            public int CountRaw(RelicId id) { return _engine._cur.CountRelic(id); }

            /// <summary>A foe's relics never wake.</summary>
            public bool IsAwake(RelicId id) { return false; }

            public string Label(RelicId id) { return RelicCatalog.KeyOf(id); }

            public int SetCount(RelicKind kind)
            {
                IReadOnlyList<RelicId> relics = _engine._cur.Relics;
                if (relics == null) return 0;

                int n = 0;
                for (int i = 0; i < relics.Count; i++)
                {
                    if (RelicCatalog.KindOf(relics[i]) == kind) n++;
                }

                return n;
            }

            public int StatValue(Stat stat)
            {
                switch (stat)
                {
                    // Bare attack. A Berserker Charm's rage swells the blow the foe throws,
                    // not its attack — a Martyr's Knot returns the unenraged number.
                    case Stat.Atk: return _engine._cur.Atk;
                    case Stat.Def: return _engine._cur.Armor;
                    case Stat.Spd: return Math.Max(10, _engine._cur.Spd);
                    case Stat.Lck: return _engine._cur.Lck;
                    default: return 0;
                }
            }

            /// <summary>An awakened Stutterstep leaves the foe, not the hero, swinging wide.</summary>
            public bool Staggered
            {
                get { return _engine._staggered; }
                set { _engine._staggered = value; }
            }

            // A stat block has none of the rest: no bonuses to bank, no sockets to fire, no
            // purse to fill. Every one of these reads as empty by design.

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

            public int Strikes { get; set; }

            public int StrikeTotal { get; set; }

            public int StrikeCount { get; set; }

            public int AnvilBonus { get; set; }

            public int DefenceBonus { get; set; }

            public int MomentumCount { get; set; }

            public int MomentumBonus { get; set; }

            public int Adrenaline { get; set; }

            public int PainCount { get; set; }

            public int StoneCount { get; set; }

            public int HeadsmanBonus { get; set; }

            public bool WhiskerUsed { get; set; }

            public bool InstantRiposte { get; set; }

            public int BootsUsedOnStrike { get; set; }

            public int Kills { get; set; }

            public bool Blocked { get; set; }

            public bool IronGlanced { get; set; }

            public int HitCount { get; set; }

            public bool HitTaken { get; set; }

            public int HideLearned { get; set; }

            public int MartyrCount { get; set; }

            public int ItemCount { get { return 0; } }

            public RelicId ItemAt(int slot) { return RelicId.None; }

            public SocketTrigger TriggerAt(int slot) { return SocketTrigger.None; }

            public SocketEmitter EmitterAt(int slot) { return SocketEmitter.None; }

            public bool HasFiredThisBeat(int slot) { return true; }

            public void MarkFiredThisBeat(int slot) { }
        }


        // ---------- what a turn asks of this mode ----------

        void ICombatBus.BeginBeat(ICombatActor actor)
        {
            System.Array.Clear(_firedThisBeat, 0, _firedThisBeat.Length);
        }

        IChain ICombatBus.NewChain() { return NewChain(); }

        double ICombatBus.NextRandom() { return _rng.Next(); }

        bool ICombatBus.TryExecute(ICombatActor attacker)
        {
            return CombatDamage.TryExecute(attacker, this, _rules);
        }

        void ICombatBus.ReportKill(ICombatActor killer, int depth) { Snap(CombatEventType.Kill, depth); }

        /// <summary>A corpse's purse, swelled by the Greed set and by Coin Magnet.</summary>
        int ICombatBus.LootFor(ICombatActor killer)
        {
            int magnet = RelicTuning.For(RelicId.CoinMagnet, _rules.Mode).AmplifiesLoot
                ? EffectiveCount(RelicId.CoinMagnet)
                : 0;

            return JsMath.RoundToInt(_cur.Drop * (SetCount(RelicKind.Greed) >= 7 ? 1.5 : 1.0)) + magnet;
        }

        string ICombatBus.LootLabel { get { return "killLoot"; } }

        int ICombatBus.TargetHealth(ICombatActor attacker) { return _enemyHp; }

        int ICombatBus.TargetDefence(ICombatActor attacker) { return _cur != null ? _cur.Armor : 0; }

        int ICombatBus.TargetMaxHealth(ICombatActor attacker) { return EnemyMax; }

        bool ICombatBus.ExecutionSpent(ICombatActor attacker) { return _carry.ExecutionerUsed; }

        void ICombatBus.SpendExecution(ICombatActor attacker) { _carry.ExecutionerUsed = true; }

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

        string ICombatBus.PlainStrikeLabel(ICombatActor attacker) { return attacker == _actor ? "you" : null; }

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
            _foe = new FoeActor(this);
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
                _foeIndex = k;
                _cur = pack[k];
                _enemyHp = _cur.Hp;

                _hitCount = 0;
                _fightStrikes = 0;
                _foeFury = false;
                _blockedFight = false;
                _whiskerUsed = false;
                _ironGlanced = false;
                _hitTaken = false;
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
                    Gauge = AtbScheduler.OpeningGauge(heroDash, enemyDash, SetCount(RelicKind.Pace) >= 5),
                    Alive = () => _hero.Php > 0,
                    Act = () => CombatTurn.Strike(_actor, this, _rules, null),
                    Speed = () => HeroStat(Stat.Spd),
                    WantsFreeAction = () => CombatTurn.FreeAction(_actor),
                    WantsRiposte = () => CombatTurn.Riposte(_actor, this, _rules),
                };

                var enemySlot = new AtbSlot
                {
                    Gauge = (enemyDash && !heroDash) ? 0 : AtbScheduler.Gauge,
                    Alive = () => _enemyHp > 0,
                    Act = EnemyHits,
                    Speed = () => Math.Max(10, _cur.Spd),
                };


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

            return new CombatResult(_events, _carry, _foeIndex, _enemyHp);
        }

        // ---------- turns ----------

        /// <summary>Hare's Drum: a dodge lets the hero answer immediately.</summary>

        /// <summary>Awakened Swift Boots skip the wait on every fourth strike.</summary>

        // ---------- the defender's answer ----------

        bool ICombatBus.FleshSetSpent(ICombatActor actor) { return _carry.FleshSetUsed; }

        void ICombatBus.SpendFleshSet(ICombatActor actor) { _carry.FleshSetUsed = true; }

        bool ICombatBus.SoilSpent(ICombatActor actor) { return _hero.SoilUsed; }

        void ICombatBus.SpendSoil(ICombatActor actor) { _hero.SoilUsed = true; }

        bool ICombatBus.CurseSetSpent(ICombatActor actor) { return _curseSetSpent; }

        void ICombatBus.SpendCurseSet(ICombatActor actor) { _curseSetSpent = true; }

        bool ICombatBus.AdrenalineSpent(ICombatActor actor) { return _adrenalineUsed; }

        void ICombatBus.SpendAdrenaline(ICombatActor actor) { _adrenalineUsed = true; }

        void ICombatBus.ReportAdrenalineGain(ICombatActor actor, int amount)
        {
            Snap(CombatEventType.Adrenaline, 1, amount: amount, relic: RelicId.AdrenalineGland);
        }

        /// <summary>A foe's Vampire Tooth drinks back what it just took. Famine Bell silences it.</summary>
        void ICombatBus.AttackerLifesteal(ICombatActor attacker, ICombatActor defender)
        {
            int tooth = _cur != null ? _cur.CountRelic(RelicId.VampireTooth) : 0;
            if (tooth <= 0 || _enemyHp <= 0 || _enemyHp >= EnemyMax) return;
            if (EffectiveCount(RelicId.FamineBell) > 0) return;

            int healed = Math.Min(tooth, EnemyMax - _enemyHp);
            _enemyHp += healed;
            Snap(CombatEventType.EnemyHeal, 1, amount: healed, relic: RelicId.VampireTooth, foe: true);
        }

        string ICombatBus.RefusesToFallLabel(ICombatActor actor) { return "Flesh set — you refuse to fall"; }

        string ICombatBus.SoilLabel(ICombatActor actor)
        {
            return RelicCatalog.KeyOf(RelicId.GravekeepersSoil) + " — the ground gives you back";
        }


        /// <summary>The hero slipped the blow. Everything that keys off a dodge fires here.</summary>

        /// <summary>
        /// <summary>
        /// Executioner's Coin finishes a foe already below a fifth of its health. It fires at
        /// most once per floor, and the flag lives in carry state so a revive cannot hand the
        /// hero a second execution.
        /// </summary>


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

            // Hollow Idol counts itself toward every set, when the mode says it does — and an
            // awakened one throws three more behind the family the delver leans on. See
            // Stats.SetCounts; the ledger reads the same rule, so the sheet cannot disagree
            // with the fight.
            bool idolCounts = RelicTuning.For(RelicId.HollowIdol, _rules.Mode).CountsTowardEverySet;
            _idolBackedKind = SetCounts.NoKind;

            if (!idolCounts)
            {
                _hollowIdols = 0;
            }
            else if (_rules.IdolBacksTheDominantKind)
            {
                _hollowIdols = CountItem(RelicId.HollowIdol);
                if (_hollowIdols > 0 && IsAwake(RelicId.HollowIdol))
                {
                    _idolBackedKind = SetCounts.Dominant(_kindCounts);
                }
            }
            else
            {
                // The source's answer: an awakened copy simply counted twice, everywhere. The
                // stat ledger credited it once, so the two disagreed; both are kept as written.
                _hollowIdols = EffectiveCount(RelicId.HollowIdol);
            }

            _chainDecay = ChainContext.DecayFor(SetCount(RelicKind.Chain));
        }

        /// <summary>Relics of a kind. Hollow Idol counts itself toward every set.</summary>
        private int SetCount(RelicKind kind)
        {
            int backed = (int)kind == _idolBackedKind ? SetCounts.IdolBacksTheDominant : 0;
            return _kindCounts[(int)kind] + _hollowIdols + backed;
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
                IdolBacksTheDominantKind = _rules.IdolBacksTheDominantKind,
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
                counters: counters,
                anvilSpent: _hero.AnvilBonus,
                soilUsed: _hero.SoilUsed);

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
