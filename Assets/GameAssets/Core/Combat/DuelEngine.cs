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
    public sealed class DuelEngine : ICombatBus, IAdrenalineReporter
    {
        private void DealDamage(DuelSide side, int amount, string source, int depth,
            RelicId relic, DuelChain chain)
        {
            CombatDamage.Deal(side, this, _rules, amount, source, depth, relic, chain ?? NewChain());
        }

        /// <summary>Either side can already be down by the time a chain gets back around.</summary>
        bool ICombatBus.CanDeal(ICombatActor attacker)
        {
            var side = (DuelSide)attacker;
            return Other(side).Php > 0 && side.Php > 0;
        }

        /// <summary>Evasion lives entirely in the Lucky Clover, on either side.</summary>
        bool ICombatBus.TargetEvades(ICombatActor attacker, string source, int depth, IChain chain)
        {
            var side = (DuelSide)attacker;
            DuelSide target = Other(side);

            if (target.Effective(RelicId.LuckyClover) == 0) return false;
            if (_rng.Next() >= target.StatOf(Stat.Lck) / 100.0) return false;

            if (side.IsHero) Snap(CombatEventType.EnemyMiss, 0, foe: true);
            else Snap(CombatEventType.Miss, 0, relic: RelicId.LuckyClover);

            CombatDamage.OnDodge(target, this, _rules);
            return true;
        }

        /// <summary>
        /// A duellist meets a full defender: a Guard set that turns the opening blow aside, a
        /// stagger or a Martyr's Knot that sends it back, then armor and Padded Hide. Only a
        /// genuine strike meets any of it — relic damage lands raw.
        /// </summary>

        int ICombatBus.ApplyDamage(ICombatActor attacker, int dealt, string source, int depth, RelicId relic)
        {
            var side = (DuelSide)attacker;
            Other(side).Php -= dealt;

            if (side.IsHero)
            {
                Snap(CombatEventType.EnemyDamage, depth, amount: dealt, source: source, relic: relic);
            }
            else
            {
                Snap(CombatEventType.PlayerDamage, depth, amount: dealt, relic: relic,
                    foe: relic != RelicId.None ? (bool?)true : false,
                    enemyCrit: _critChain && depth == 0);
            }

            return dealt;
        }

        void ICombatBus.AfterDamage(ICombatActor attacker, int dealt, int depth, IChain chain)
        {
            if (depth != 0) return;
            var side = (DuelSide)attacker;
            CombatDamage.React(Other(side), side, dealt, _critChain, this, _rules, chain);
        }

        private void FireEmitter(DuelSide side, RelicId id, int depth, DuelChain chain, double scale)
        {
            SocketFiring.FireEmitter(side, this, _rules, id, depth, chain ?? NewChain(), scale);
        }

        private void FireTrigger(DuelSide side, SocketTrigger trigger, int depth,
            RelicId exclude, RelicId cause, DuelChain chain)
        {
            SocketFiring.FireTrigger(side, this, _rules, trigger, depth, exclude, cause, chain);
        }

        /// <summary>Chains die four levels deep here, against forty in a delve.</summary>
        private readonly CombatRules _rules;

        public DuelEngine() : this(CombatRules.Duel())
        {
        }

        /// <summary>Only the corpus gate passes anything but the shipped rules.</summary>
        public DuelEngine(CombatRules rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            _rules = rules;
        }

        private int ChainCap { get { return _rules.ChainCap; } }

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

            var heroSlot = new AtbSlot
            {
                Gauge = AtbScheduler.OpeningGauge(dashA, dashB, _a.SetCount(RelicKind.Pace) >= 5),
                Alive = () => _a.Php > 0,
                Act = () => CombatTurn.Strike(_a, this, _rules, null),
                Speed = () => _a.StatOf(Stat.Spd),
                WantsFreeAction = () => CombatTurn.FreeAction(_a),
                WantsRiposte = () => CombatTurn.Riposte(_a, this, _rules),
            };

            var rivalSlot = new AtbSlot
            {
                Gauge = AtbScheduler.OpeningGauge(dashB, dashA, _b.SetCount(RelicKind.Pace) >= 5),
                Alive = () => _b.Php > 0,
                Act = () => CombatTurn.Strike(_b, this, _rules, null),
                Speed = () => _b.StatOf(Stat.Spd),
                WantsFreeAction = () => CombatTurn.FreeAction(_b),
                WantsRiposte = () => CombatTurn.Riposte(_b, this, _rules),
            };

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

            // The rival holds the first slot, mirroring the delve: on a tie it acts before the
            // hero, so a killing blow still costs the hero the hit they were already taking.
            AtbScheduler.Run(rivalSlot, heroSlot, step => _tick += step);

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

        /// <summary>Hare's Drum: a dodge lets that side answer immediately.</summary>

        /// <summary>Awakened Swift Boots skip the wait on every fourth strike.</summary>

        // ---------- chains ----------

        /// <summary>
        /// A chain in a duel is keyed per side as well as per relic, so both duellists can draw
        /// on the same chain without their decay counters colliding.
        /// </summary>
        private sealed class DuelChain : IChain
        {
            private readonly Dictionary<string, int> _visits = new Dictionary<string, int>();
            private readonly CombatRules _rules;

            public DuelChain(CombatRules rules)
            {
                _rules = rules;
            }

            public double Scale(ICombatActor actor, RelicId relic)
            {
                var side = (DuelSide)actor;
                string key = (side.IsHero ? "A" : "B") + (int)relic;
                _visits.TryGetValue(key, out int seen);
                seen++;
                _visits[key] = seen;

                int chain = side.SetCount(RelicKind.Chain, false);
                double decay = chain >= 7 ? 0.75 : chain >= 3 ? 0.6 : 0.5;
                return Math.Pow(decay, seen - 1);
            }
        }

        private DuelChain NewChain()
        {
            return new DuelChain(_rules);
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





        /// <summary>
        /// Rabbit's Foot answers only every third luck signal here. Duels run long enough that
        /// answering every one would snowball luck out of control.
        /// </summary>

        // ---------- the defender's answer ----------

        bool ICombatBus.FleshSetSpent(ICombatActor actor) { return ((DuelSide)actor).FleshSetUsed; }

        void ICombatBus.SpendFleshSet(ICombatActor actor) { ((DuelSide)actor).FleshSetUsed = true; }

        bool ICombatBus.SoilSpent(ICombatActor actor) { return ((DuelSide)actor).SoilUsed; }

        void ICombatBus.SpendSoil(ICombatActor actor) { ((DuelSide)actor).SoilUsed = true; }

        /// <summary>The Curse set does not detonate in a duel.</summary>
        bool ICombatBus.CurseSetSpent(ICombatActor actor) { return true; }

        void ICombatBus.SpendCurseSet(ICombatActor actor) { }

        bool ICombatBus.AdrenalineSpent(ICombatActor actor) { return ((DuelSide)actor).AdrenalineUsed; }

        void ICombatBus.SpendAdrenaline(ICombatActor actor) { ((DuelSide)actor).AdrenalineUsed = true; }

        void ICombatBus.ReportAdrenalineGain(ICombatActor actor, int amount)
        {
            var side = (DuelSide)actor;
            if (side.IsHero) Snap(CombatEventType.Adrenaline, 1, amount: amount, relic: RelicId.AdrenalineGland);
            else Line(side, RelicId.AdrenalineGland, side.Label(RelicId.AdrenalineGland) + " +" + amount + " ATK", 1);
        }

        /// <summary>
        /// A duellist drinks on their own turn rather than as the defender answers, so nothing
        /// happens here.
        /// </summary>
        void ICombatBus.AttackerLifesteal(ICombatActor attacker, ICombatActor defender) { }

        string ICombatBus.RefusesToFallLabel(ICombatActor actor)
        {
            var side = (DuelSide)actor;
            return "Flesh set — " + (side.IsHero ? "you refuse" : side.Name + " refuses") + " to fall";
        }

        string ICombatBus.SoilLabel(ICombatActor actor)
        {
            return ((DuelSide)actor).Label(RelicId.GravekeepersSoil) + " — the ground gives back";
        }



        // ---------- sockets ----------






        // ---------- the shared primitives ----------

        private void Heal(DuelSide side, int amount, string source, int depth, RelicId relic, DuelChain chain)
        {
            CombatPrimitives.Heal(side, this, _rules, amount, source, depth, relic, chain ?? NewChain());
        }

        private void GainGold(DuelSide side, int amount, string source, int depth, RelicId relic, DuelChain chain)
        {
            CombatPrimitives.GainGold(side, this, _rules, amount, source, depth, relic, chain ?? NewChain());
        }

        private void EmitLuck(DuelSide side, string source, int depth, RelicId relic, DuelChain chain, bool quiet = false)
        {
            CombatPrimitives.EmitLuck(side, this, _rules, source, depth, relic, chain ?? NewChain(), quiet);
        }

        // ---------- ICombatBus ----------

        ICombatActor ICombatBus.Opponent(ICombatActor actor) { return Other((DuelSide)actor); }

        void ICombatBus.ReportFizzle(int depth) { Snap(CombatEventType.Fizzle, depth); }

        void ICombatBus.ReportHeal(ICombatActor actor, int amount, string source, int depth, RelicId relic)
        {
            var side = (DuelSide)actor;
            if (side.IsHero) Snap(CombatEventType.Heal, depth, amount: amount, source: source, relic: relic);
            else Snap(CombatEventType.EnemyHeal, depth, amount: amount,
                relic: relic != RelicId.None ? relic : RelicId.VampireTooth, foe: true);
        }

        /// <summary>Only the hero's wasted heal is worth a line; the rival's is inferred.</summary>
        void ICombatBus.ReportHealFull(ICombatActor actor, string source, int depth, RelicId relic)
        {
            if (((DuelSide)actor).IsHero) Snap(CombatEventType.HealFull, depth, source: source, relic: relic);
        }

        void ICombatBus.ReportGold(ICombatActor actor, int amount, string source, int depth, RelicId relic)
        {
            var side = (DuelSide)actor;
            if (side.IsHero) Snap(CombatEventType.Gold, depth, amount: amount, source: source, relic: relic);
            else Line(side, relic != RelicId.None ? relic : RelicId.MidasBlade, source + " +" + amount + "g", depth);
        }

        void ICombatBus.ReportLuck(ICombatActor actor, string source, int depth, RelicId relic)
        {
            if (((DuelSide)actor).IsHero) Snap(CombatEventType.Luck, depth, source: source, relic: relic);
        }

        void ICombatBus.SlowOpponent(ICombatActor actor, int amount, bool stagger)
        {
            var side = (DuelSide)actor;
            DuelSide foe = Other(side);
            foe.BaseSpd = Math.Max(10, foe.BaseSpd - amount);
            if (stagger) foe.Staggered = true;
            Snap(CombatEventType.EnemySlow, 1, amount: amount, relic: RelicId.Stutterstep,
                foe: side.IsHero ? (bool?)null : true);
        }

        void ICombatBus.ReduceOpponentCeiling(ICombatActor actor, int depth)
        {
            var side = (DuelSide)actor;
            DuelSide foe = Other(side);
            if (foe.Pmax <= 12) return;

            foe.Pmax -= 1;
            if (foe.Php > foe.Pmax) foe.Php = foe.Pmax;
            Line(side, RelicId.VampireTooth, side.Label(RelicId.VampireTooth) + " takes 1 max HP", depth);
        }

        void ICombatBus.FireEmitter(ICombatActor actor, RelicId id, int depth, IChain chain, double scale)
        {
            SocketFiring.FireEmitter(actor, this, _rules, id, depth, chain, scale);
        }

        /// <summary>A duel does not name the firing copy on its events.</summary>
        void ICombatBus.BeginFire(ICombatActor actor, int slot) { }

        void ICombatBus.EndFire(ICombatActor actor) { }

        void ICombatBus.FireTrigger(ICombatActor actor, SocketTrigger trigger, int depth,
            RelicId exclude, RelicId cause, IChain chain)
        {
            SocketFiring.FireTrigger(actor, this, _rules, trigger, depth, exclude, cause, chain);
        }

        void ICombatBus.DealDamage(ICombatActor from, int amount, string source, int depth, RelicId relic, IChain chain)
        {
            DealDamage((DuelSide)from, amount, source, depth, relic, (DuelChain)chain);
        }

        void ICombatBus.Heal(ICombatActor actor, int amount, string source, int depth, RelicId relic, IChain chain)
        {
            Heal((DuelSide)actor, amount, source, depth, relic, (DuelChain)chain);
        }

        void ICombatBus.GainGold(ICombatActor actor, int amount, string source, int depth, RelicId relic, IChain chain)
        {
            GainGold((DuelSide)actor, amount, source, depth, relic, (DuelChain)chain);
        }

        void ICombatBus.EmitLuck(ICombatActor actor, string source, int depth, RelicId relic, IChain chain, bool quiet)
        {
            EmitLuck((DuelSide)actor, source, depth, relic, (DuelChain)chain, quiet);
        }

        string ICombatBus.SidePrefix(ICombatActor actor) { return Prefix((DuelSide)actor); }

        DefenseModel ICombatBus.DefenseModelOf(ICombatActor actor) { return DefenseModel.Percentage; }

        void ICombatBus.Line(ICombatActor actor, RelicId relic, string text, int depth)
        {
            Line((DuelSide)actor, relic, text, depth);
        }

        bool ICombatBus.HasTarget(ICombatActor actor)
        {
            return Other((DuelSide)actor).Php > 0;
        }

        /// <summary>The rival's Adrenaline is a plain line; the hero's is a typed event.</summary>
        // ---------- what a turn asks of this mode ----------

        void ICombatBus.BeginBeat(ICombatActor actor)
        {
            var side = (DuelSide)actor;
            System.Array.Clear(side.FiredThisBeat, 0, side.FiredThisBeat.Length);
        }

        IChain ICombatBus.NewChain() { return NewChain(); }

        double ICombatBus.NextRandom() { return _rng.Next(); }

        bool ICombatBus.TryExecute(ICombatActor attacker)
        {
            return CombatDamage.TryExecute(attacker, this, _rules);
        }

        /// <summary>Either duellist collects for the other.</summary>
        bool ICombatBus.ClaimsTheKill(ICombatActor killer) { return true; }

        void ICombatBus.ReportKill(ICombatActor killer, int depth)
        {
            if (((DuelSide)killer).IsHero) Snap(CombatEventType.Kill, depth);
        }

        /// <summary>The winner takes the loser's round purse.</summary>
        int ICombatBus.LootFor(ICombatActor killer) { return Other((DuelSide)killer).Drop; }

        string ICombatBus.LootLabel { get { return "loot"; } }

        int ICombatBus.TargetHealth(ICombatActor attacker) { return Other((DuelSide)attacker).Php; }

        int ICombatBus.TargetDefence(ICombatActor attacker)
        {
            return Other((DuelSide)attacker).StatOf(Stat.Def);
        }

        int ICombatBus.TargetMaxHealth(ICombatActor attacker) { return Other((DuelSide)attacker).Pmax; }

        bool ICombatBus.ExecutionSpent(ICombatActor attacker) { return ((DuelSide)attacker).ExecutionerUsed; }

        void ICombatBus.SpendExecution(ICombatActor attacker) { ((DuelSide)attacker).ExecutionerUsed = true; }

        /// <summary>Every rival is worthy blood.</summary>
        bool ICombatBus.FacingWorthyBlood(ICombatActor attacker) { return true; }

        /// <summary>Only the hero's momentum is announced; the rival's builds silently.</summary>
        void ICombatBus.ReportMomentum(ICombatActor actor, int amount)
        {
            if (((DuelSide)actor).IsHero)
            {
                Snap(CombatEventType.Momentum, 1, amount: amount, relic: RelicId.MomentumBead);
            }
        }

        void ICombatBus.BeginCrit() { _critChain = true; }

        void ICombatBus.EndCrit() { _critChain = false; }

        string ICombatBus.PlainStrikeLabel(ICombatActor attacker)
        {
            return ((DuelSide)attacker).IsHero ? "you" : null;
        }

        string ICombatBus.CritLabel(ICombatActor attacker)
        {
            var side = (DuelSide)attacker;
            return side.IsHero ? side.Label(RelicId.WeightedDice) : null;
        }

        RelicId ICombatBus.CritRelic(ICombatActor attacker)
        {
            return ((DuelSide)attacker).IsHero ? RelicId.WeightedDice : RelicId.None;
        }

        string ICombatBus.CritSignalLabel(ICombatActor attacker)
        {
            var side = (DuelSide)attacker;
            return side.IsHero ? side.Label(RelicId.WeightedDice) : "dice";
        }

        string ICombatBus.LooseCoinsLabel { get { return "loose coins"; } }

        string ICombatBus.HookSpillLabel(ICombatActor attacker)
        {
            return ((DuelSide)attacker).Label(RelicId.CutpurseHook);
        }

        /// <summary>There is no corpse to loot in a duel, so the Magnet adds nothing.</summary>
        int ICombatBus.SpillBonus(ICombatActor attacker) { return 0; }

        void IAdrenalineReporter.ReportAdrenaline(ICombatActor actor, int amount, int depth, string name)
        {
            var side = (DuelSide)actor;
            if (side.IsHero) Snap(CombatEventType.Adrenaline, depth, amount: amount, relic: RelicId.AdrenalineGland);
            else Line(side, RelicId.AdrenalineGland, name + " +" + amount + " ATK", depth);
        }

        // ---------- a turn ----------


        /// <summary>
        /// Executioner's Coin, versus rules: the threshold is 10% per copy up to two, and it
        /// fires once for the whole duel rather than once per floor.
        /// </summary>
    }
}
