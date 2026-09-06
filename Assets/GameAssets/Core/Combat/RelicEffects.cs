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

        void FireEmitter(ICombatActor actor, RelicId id, int depth, IChain chain, double scale);

        void FireTrigger(ICombatActor actor, SocketTrigger trigger, int depth,
            RelicId exclude, RelicId cause, IChain chain);
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
                    if (rules.GreedRelicsHaveActivation)
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
                    int cap = actor.IsAwake(RelicId.SentinelBell) ? rules.SentinelCapAwakened : rules.SentinelCap;
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
