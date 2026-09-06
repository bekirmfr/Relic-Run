using System;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;

namespace RelicRun.Core.Combat
{
    /// <summary>One chain of consequences, however the host engine tracks it.</summary>
    public interface IChain
    {
        /// <summary>
        /// Records another activation of this relic by this actor and returns its multiplier.
        /// Call it whenever the relic activates, even if the effect is then skipped — the visit
        /// count is what makes the next pass weaker.
        /// </summary>
        double Scale(ICombatActor actor, RelicId relic);
    }

    /// <summary>
    /// A side in a fight, as the relic layer needs to see it. Both the hero in a delve and
    /// either duellist satisfy this.
    /// </summary>
    public interface ICombatActor
    {
        int Php { get; set; }
        int Pmax { get; }

        /// <summary>Copies held, plus one for an awakened copy.</summary>
        int Effective(RelicId id);

        /// <summary>Copies held, ignoring awakening.</summary>
        int CountRaw(RelicId id);

        bool IsAwake(RelicId id);

        /// <summary>The relic's name as this side's log shows it.</summary>
        string Label(RelicId id);

        /// <summary>Attack gained this floor or round.</summary>
        int Fury { get; set; }

        /// <summary>Defence gained from Stone and Iron activations.</summary>
        int Stone { get; set; }

        /// <summary>Speed gained from Gale activations.</summary>
        int Gale { get; set; }

        /// <summary>Luck gained from Rabbit and its kin.</summary>
        int LuckGain { get; set; }

        /// <summary>Defence rung up by Sentinel Bell.</summary>
        int Sentinel { get; set; }

        /// <summary>Whether Fortune's Edge has charged the next strike.</summary>
        bool BladeCharged { get; set; }

        int Gold { get; set; }

        /// <summary>Attack tempered by Quenched Blade.</summary>
        int QuenchBonus { get; set; }

        /// <summary>Heals counted toward Quenched Blade's every-third cadence.</summary>
        int QuenchCount { get; set; }

        /// <summary>Luck signals counted toward Rabbit's Foot.</summary>
        int RabbitCount { get; set; }

        /// <summary>Gold gains counted toward the socketed gold trigger.</summary>
        int GoldCount { get; set; }

        /// <summary>Outstanding debt that incoming gold pays down first.</summary>
        int DebtLeft { get; set; }

        /// <summary>Relics of a kind, with the mode's Hollow Idol rule already applied.</summary>
        int SetCount(RelicKind kind);

        /// <summary>A stat, read through the ledger.</summary>
        int StatValue(Stats.Stat stat);

        /// <summary>Strikes thrown in this fight.</summary>
        int Strikes { get; set; }

        /// <summary>Strikes thrown across the whole run.</summary>
        int StrikeTotal { get; set; }

        /// <summary>Strikes counted toward the socketed attack trigger.</summary>
        int StrikeCount { get; set; }

        /// <summary>Notches Anvil Heart has cut.</summary>
        int AnvilBonus { get; set; }

        /// <summary>Permanent defence banked over the run.</summary>
        int DefenceBonus { get; set; }

        /// <summary>Strikes counted toward Momentum Bead.</summary>
        int MomentumCount { get; set; }

        /// <summary>Speed built by Momentum Bead.</summary>
        int MomentumBonus { get; set; }

        /// <summary>Permanent attack banked from the Adrenaline Gland.</summary>
        int Adrenaline { get; set; }

        /// <summary>Hits taken, counted toward the socketed pain trigger.</summary>
        int PainCount { get; set; }

        /// <summary>Stone Emitter activations.</summary>
        int StoneCount { get; set; }

        /// <summary>Attack sharpened permanently this floor, by the Edge set or its kin.</summary>
        int HeadsmanBonus { get; set; }

        int Kills { get; set; }

        /// <summary>Whether the Cat's Whisker has already spent its one free counter.</summary>
        bool WhiskerUsed { get; set; }

        /// <summary>The Hare's Drum handing back the next action.</summary>
        bool InstantRiposte { get; set; }

        // ---- inventory, walked by slot because a socket belongs to one copy ----

        int ItemCount { get; }

        RelicId ItemAt(int slot);

        SocketTrigger TriggerAt(int slot);

        SocketEmitter EmitterAt(int slot);

        bool HasFiredThisBeat(int slot);

        void MarkFiredThisBeat(int slot);
    }

    /// <summary>The primitives a relic effect can reach for.</summary>
    public interface ICombatBus
    {
        void DealDamage(ICombatActor from, int amount, string source, int depth, RelicId relic, IChain chain);
        void Heal(ICombatActor actor, int amount, string source, int depth, RelicId relic, IChain chain);
        void GainGold(ICombatActor actor, int amount, string source, int depth, RelicId relic, IChain chain);
        void EmitLuck(ICombatActor actor, string source, int depth, RelicId relic, IChain chain, bool quiet);

        /// <summary>A plain log line attributed to this side.</summary>
        void Line(ICombatActor actor, RelicId relic, string text, int depth);

        /// <summary>Whether this side still has something to hit.</summary>
        bool HasTarget(ICombatActor actor);

        /// <summary>The other side.</summary>
        ICombatActor Opponent(ICombatActor actor);

        void ReportFizzle(int depth);
        void ReportHeal(ICombatActor actor, int amount, string source, int depth, RelicId relic);
        void ReportHealFull(ICombatActor actor, string source, int depth, RelicId relic);
        void ReportGold(ICombatActor actor, int amount, string source, int depth, RelicId relic);
        void ReportLuck(ICombatActor actor, string source, int depth, RelicId relic);

        /// <summary>An awakened Vampire Tooth eating one point off the opponent's ceiling.</summary>
        void ReduceOpponentCeiling(ICombatActor actor, int depth);

        /// <summary>
        /// Stutterstep dragging the opponent's speed down, and — once awakened — leaving them
        /// staggered. Which field holds either is the one thing a stat-block foe and a full
        /// duellist do not share.
        /// </summary>
        void SlowOpponent(ICombatActor actor, int amount, bool stagger);

        void FireEmitter(ICombatActor actor, RelicId id, int depth, IChain chain, double scale);

        void FireTrigger(ICombatActor actor, SocketTrigger trigger, int depth,
            RelicId exclude, RelicId cause, IChain chain);

        /// <summary>Clears the per-beat guard so a genuine event may wake each copy once.</summary>
        void BeginBeat(ICombatActor actor);

        IChain NewChain();

        double NextRandom();

        /// <summary>Executioner's Coin. Returns whether the sentence was carried out.</summary>
        bool TryExecute(ICombatActor attacker);

        /// <summary>Whether Duelist's Oath recognises this opponent.</summary>
        bool FacingWorthyBlood(ICombatActor attacker);

        void ReportMomentum(ICombatActor actor, int amount);

        /// <summary>Marks a critical strike as in flight, so Mirror Scale can answer it.</summary>
        void BeginCrit();

        void EndCrit();

        /// <summary>How a plain strike is labelled in the log.</summary>
        string PlainStrikeLabel(ICombatActor attacker);

        string CritLabel(ICombatActor attacker);

        RelicId CritRelic(ICombatActor attacker);

        string CritSignalLabel(ICombatActor attacker);

        string LooseCoinsLabel { get; }

        string HookSpillLabel(ICombatActor attacker);

        /// <summary>Extra coins a spill picks up, beyond the Hook and the Greed set.</summary>
        int SpillBonus(ICombatActor attacker);

        // ---- the defender's answer to a landed blow ----

        bool FleshSetSpent(ICombatActor actor);

        void SpendFleshSet(ICombatActor actor);

        bool SoilSpent(ICombatActor actor);

        void SpendSoil(ICombatActor actor);

        bool CurseSetSpent(ICombatActor actor);

        void SpendCurseSet(ICombatActor actor);

        bool AdrenalineSpent(ICombatActor actor);

        void SpendAdrenaline(ICombatActor actor);

        void ReportAdrenalineGain(ICombatActor actor, int amount);

        /// <summary>The attacker's Vampire Tooth drinking from the wound it opened.</summary>
        void AttackerLifesteal(ICombatActor attacker, ICombatActor defender);

        string RefusesToFallLabel(ICombatActor actor);

        string SoilLabel(ICombatActor actor);

        /// <summary>Marks which copy is firing, so events can name it.</summary>
        void BeginFire(ICombatActor actor, int slot);

        void EndFire(ICombatActor actor);

        // ---- a kill, and the blow that forces one ----

        void ReportKill(ICombatActor killer, int depth);

        /// <summary>What the fallen leaves behind, already scaled by this mode's rules.</summary>
        int LootFor(ICombatActor killer);

        string LootLabel { get; }

        /// <summary>The target's remaining health, for a blow meant to be exactly lethal.</summary>
        int TargetHealth(ICombatActor attacker);

        /// <summary>The target's defence, added back so an execution is not reduced below lethal.</summary>
        int TargetDefence(ICombatActor attacker);

        bool ExecutionSpent(ICombatActor attacker);

        void SpendExecution(ICombatActor attacker);

        /// <summary>The target's maximum health, which the execution window is a share of.</summary>
        int TargetMaxHealth(ICombatActor attacker);

        // ---- the mode-specific half of landing a blow ----

        /// <summary>
        /// Whether a blow can be thrown at all. A delve asks only whether the foe is still
        /// standing; a duel also asks whether the striker is, because either side can be dead
        /// by the time a chain gets back around to them.
        /// </summary>
        bool CanDeal(ICombatActor attacker);

        /// <summary>
        /// Rolls the defender's evasion. Returning true means the blow was slipped and every
        /// dodge reaction has already fired.
        /// </summary>
        bool TargetEvades(ICombatActor attacker, string source, int depth, IChain chain);

        /// <summary>
        /// Reduces a blow to what actually lands, and runs whatever can turn it aside first —
        /// a Guard set, a stagger, a Martyr's Knot, an awakened Iron Skin. Returns
        /// <see cref="TurnedAside"/> when nothing lands at all.
        /// </summary>
        int Mitigate(ICombatActor attacker, int amount, string source, int depth);

        /// <summary>Subtracts the blow and reports it from the hero's point of view.</summary>
        void ApplyDamage(ICombatActor attacker, int dealt, string source, int depth, RelicId relic);

        /// <summary>Whatever answers a blow that landed and did not kill.</summary>
        void AfterDamage(ICombatActor attacker, int dealt, int depth, IChain chain);
    }

    /// <summary>
    /// What a relic does when it activates — the single source of truth for both modes.
    /// </summary>
    /// <remarks>
    /// Several relics here are pure passives in normal play: Ox Heart, Whetstone, Iron Skin.
    /// A socketed trigger gives them an activation in their own theme, which is how a socket
    /// makes a relic gain verbs it never had.
    ///
    /// This used to exist twice, once per engine, differing only in a handful of numbers. That
    /// is exactly the duplication the port set out to remove, so the numbers moved into
    /// <see cref="CombatRules"/> and the logic became this.
    /// </remarks>
    public static class RelicEffects
    {
        /// <summary>
        /// Fires a relic's own effect.
        /// </summary>
        /// <param name="viaTrigger">
        /// True when a socketed trigger woke it. A trigger speaks for the one upgraded copy; a
        /// native activation speaks for every copy the side holds.
        /// </param>
        public static void Apply(ICombatActor actor, ICombatBus bus, CombatRules rules,
            RelicId id, int depth, IChain chain, double scale, bool viaTrigger)
        {
            if (depth > rules.ChainCap) return;

            RelicTuning tuning = RelicTuning.For(id, rules.Mode);
            int copies = viaTrigger ? 1 : Math.Max(1, actor.Effective(id));
            string name = actor.Label(id) + (viaTrigger ? " ⚡" : string.Empty);

            int Scaled(int amount)
            {
                return JsMath.RoundToInt(amount * copies * scale);
            }

            switch (id)
            {
                case RelicId.AlchemistsVial: bus.Heal(actor, Scaled(1), name, depth + 1, id, chain); break;
                case RelicId.OxHeart: bus.Heal(actor, Scaled(2), name, depth + 1, id, chain); break;
                case RelicId.VampireTooth: bus.Heal(actor, Scaled(1), name, depth + 1, id, chain); break;

                case RelicId.MidasBlade: bus.GainGold(actor, Scaled(2), name, depth + 1, id, chain); break;
                case RelicId.CutpurseHook: bus.GainGold(actor, Scaled(1), name, depth + 1, id, chain); break;
                case RelicId.EmberCask: bus.GainGold(actor, Scaled(1), name, depth + 1, id, chain); break;

                // The delve gives the greed relics an activation of their own; the duel does not.
                case RelicId.GreedyCurse:
                case RelicId.PiggyBank:
                case RelicId.CoinMagnet:
                    if (tuning.HasActivation)
                    {
                        bus.GainGold(actor, Scaled(id == RelicId.PiggyBank ? 1 : 2), name, depth + 1, id, chain);
                    }

                    break;

                case RelicId.BloodAltar:
                case RelicId.ThornVest:
                case RelicId.WeightedDice:
                    if (bus.HasTarget(actor)) bus.DealDamage(actor, Scaled(2), name, depth + 1, id, chain);
                    break;

                case RelicId.HexThread:
                    if (bus.HasTarget(actor)) bus.DealDamage(actor, Scaled(1), name, depth + 1, id, chain);
                    break;

                case RelicId.LuckyClover:
                case RelicId.CoinSinger:
                    bus.EmitLuck(actor, name, depth + 1, id, chain, false);
                    break;

                case RelicId.Whetstone:
                {
                    int bonus = Scaled(1);
                    if (bonus > 0)
                    {
                        actor.Fury += bonus;
                        bus.Line(actor, id, name + " +" + bonus + " ATK", depth + 1);
                    }

                    break;
                }

                case RelicId.IronSkin:
                {
                    int bonus = Scaled(2);
                    if (bonus > 0)
                    {
                        actor.Stone += bonus;
                        bus.Line(actor, id, name + " +" + bonus + " DEF", depth + 1);
                    }

                    break;
                }

                case RelicId.BerserkerCharm:
                {
                    // It bites twice as hard once its bearer is bloodied.
                    int bonus = Scaled(actor.Php < actor.Pmax / 2.0 ? 2 : 1);
                    if (bonus > 0)
                    {
                        actor.Fury += bonus;
                        bus.Line(actor, id, name + " +" + bonus + " ATK", depth + 1);
                    }

                    break;
                }

                case RelicId.RabbitsFoot:
                {
                    int bonus = Scaled(1);
                    if (bonus > 0)
                    {
                        actor.LuckGain += bonus;
                        bus.Line(actor, id, name + " +" + bonus + " LUCK", depth + 1);
                    }

                    break;
                }

                case RelicId.SwiftBoots:
                case RelicId.BattleDash:
                {
                    int bonus = Scaled(2);
                    if (bonus > 0)
                    {
                        if (rules.LogSpeedGainBeforeApplying)
                        {
                            bus.Line(actor, id, name + " +" + bonus + " SPD", depth + 1);
                            actor.Gale += bonus;
                        }
                        else
                        {
                            actor.Gale += bonus;
                            bus.Line(actor, id, name + " +" + bonus + " SPD", depth + 1);
                        }
                    }

                    break;
                }

                case RelicId.AdrenalineGland:
                {
                    int bonus = Scaled(1);
                    if (bonus > 0)
                    {
                        actor.Fury += bonus;
                        bus.AdrenalineLine(actor, bonus, depth + 1, name);
                    }

                    break;
                }

                case RelicId.SentinelBell:
                {
                    int bonus = Scaled(1);
                    int cap = actor.IsAwake(RelicId.SentinelBell) ? tuning.CapAwakened : tuning.Cap;
                    if (bonus > 0 && actor.Sentinel < cap)
                    {
                        actor.Sentinel = Math.Min(cap, actor.Sentinel + bonus);
                        bus.Line(actor, id, name + " +" + bonus + " DEF", depth + 1);
                    }

                    break;
                }

                case RelicId.FortunesEdge:
                    actor.BladeCharged = true;
                    bus.Line(actor, id, name + " charges the blade", depth + 1);
                    break;

                case RelicId.QuickenedPulse:
                {
                    int bonus = actor.Effective(id);
                    actor.Gale += bonus;
                    bus.Line(actor, id, name + " +" + bonus + " SPD", depth + 1);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Adrenaline is the one activation the two modes report differently: the delve gives it a
    /// typed event, the duel a plain line for the rival. The bus decides.
    /// </summary>
    public static class CombatBusExtensions
    {
        public static void AdrenalineLine(this ICombatBus bus, ICombatActor actor, int amount,
            int depth, string name)
        {
            var typed = bus as IAdrenalineReporter;
            if (typed != null)
            {
                typed.ReportAdrenaline(actor, amount, depth, name);
                return;
            }

            bus.Line(actor, RelicId.AdrenalineGland, name + " +" + amount + " ATK", depth);
        }
    }

    /// <summary>Implemented by a bus that reports Adrenaline as its own event type.</summary>
    public interface IAdrenalineReporter
    {
        void ReportAdrenaline(ICombatActor actor, int amount, int depth, string name);
    }
}
