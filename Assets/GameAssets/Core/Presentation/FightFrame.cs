using System.Collections.Generic;
using RelicRun.Core.Combat;

namespace RelicRun.Core.Presentation
{
    /// <summary>Where a number flies up from.</summary>
    public enum FlierAt
    {
        /// <summary>Over the foe.</summary>
        Enemy,

        /// <summary>Over the delver.</summary>
        Hero,

        /// <summary>Over the purse.</summary>
        Gold,
    }

    /// <summary>What a flying number means, which is what colours it.</summary>
    public enum FlierKind
    {
        /// <summary>A plain blow.</summary>
        Damage,

        /// <summary>A blow that landed hard.</summary>
        Crit,

        /// <summary>A relic did it rather than a person.</summary>
        Chain,

        /// <summary>It did not land.</summary>
        Miss,

        Heal,

        /// <summary>A stat went up.</summary>
        Buff,

        /// <summary>It happened to the delver.</summary>
        Hurt,

        Gold,
    }

    /// <summary>One number flying off somebody.</summary>
    /// <remarks>
    /// No position. The source scatters each flier by a random offset so two in a row do not
    /// overlap, and random belongs nowhere near Core — a view-model that rolled dice would make
    /// the same fight look different on replay, which is the one thing this whole layer exists
    /// to avoid. The scattering is the view's, and it is welcome to it.
    /// </remarks>
    public readonly struct Flier
    {
        public readonly FlierAt Where;
        public readonly FlierKind Kind;
        public readonly string Text;

        public Flier(FlierAt where, FlierKind kind, string text)
        {
            Where = where;
            Kind = kind;
            Text = text;
        }

        public override string ToString() { return Where + " " + Kind + " " + Text; }
    }

    /// <summary>
    /// What one of the two attack gauges should do.
    /// </summary>
    /// <remarks>
    /// The gauges are the clock of a fight made visible: each fills over exactly the time until
    /// its owner strikes again, so the bar arriving full and the blow landing are the same
    /// moment. Getting that wrong does not look like a timing bug — it looks like the game is
    /// lying about whose turn it is.
    /// </remarks>
    public readonly struct Gauge
    {
        /// <summary>Whether this event says anything about the gauge at all.</summary>
        public readonly bool Changed;

        /// <summary>
        /// How long the fill should take, in milliseconds.
        /// </summary>
        /// <remarks>
        /// Zero means no wind-up: the blow is already landing. <see cref="KeepTheCadence"/> means
        /// this unit never attacks again — it dies first — and the bar should restart at whatever
        /// duration it last used, so a doomed delver still looks like they are winding up rather
        /// than freezing mid-fight.
        /// </remarks>
        public readonly int Ms;

        /// <summary>Restart at the duration this gauge last used.</summary>
        public const int KeepTheCadence = -1;

        public Gauge(bool changed, int ms)
        {
            Changed = changed;
            Ms = ms;
        }

        public static readonly Gauge Untouched = new Gauge(false, 0);

        public override string ToString()
        {
            return !Changed ? "untouched" : Ms == KeepTheCadence ? "same again" : Ms + "ms";
        }
    }

    /// <summary>
    /// What a fight sounds like. Six noises, and mostly silence.
    /// </summary>
    /// <remarks>
    /// Six of the twenty event kinds make a sound and the rest do not, which is the source's
    /// arrangement and a good one: a chain of eight relics firing would otherwise be eight
    /// noises on top of one another, and the delver would learn nothing from any of them.
    /// </remarks>
    public enum FightSound
    {
        None,

        /// <summary>Coin.</summary>
        Gold,

        /// <summary>The delver taking a blow.</summary>
        Hurt,

        /// <summary>The delver landing one.</summary>
        Hit,

        Heal,
        Kill,
        Death,
    }

    /// <summary>
    /// Everything a screen needs to draw one event, and nothing it does not.
    /// </summary>
    /// <remarks>
    /// Derived entirely from the event list and where in it we are. That is invariant 5 taken to
    /// its conclusion: the screen holds no combat state of its own, so it cannot get out of step
    /// with the fight, and the same event drawn twice draws the same.
    ///
    /// The gauges are the part worth reading twice. Each looks FORWARD through the remaining
    /// events for the next blow its owner lands, adds up the holds along the way, and fills over
    /// exactly that long — so the bar reaching full is the blow landing rather than a guess that
    /// happens to look close. It stops looking at the next foe's entrance, because a gauge that
    /// wound up across a change of opponent would be timing a blow against somebody who is not
    /// there yet.
    /// </remarks>
    public sealed class FightFrame
    {
        /// <summary>What the log says.</summary>
        public readonly CombatLine Line;

        /// <summary>The numbers flying off, in the order they should be spawned.</summary>
        public readonly IReadOnlyList<Flier> Fliers;

        /// <summary>What the delver's attack gauge should do.</summary>
        public readonly Gauge Hero;

        /// <summary>What the foe's attack gauge should do.</summary>
        public readonly Gauge Enemy;

        /// <summary>What it sounds like, if anything.</summary>
        public readonly FightSound Sound;

        public FightFrame(CombatLine line, IReadOnlyList<Flier> fliers, Gauge hero, Gauge enemy,
            FightSound sound)
        {
            Line = line;
            Fliers = fliers;
            Hero = hero;
            Enemy = enemy;
            Sound = sound;
        }

        /// <summary>The frame for one event of a fight.</summary>
        public static FightFrame Of(IReadOnlyList<CombatEvent> events, int index, Pacing pacing,
            CombatLog log)
        {
            CombatEvent shown = events[index];

            return new FightFrame(
                log == null ? new CombatLine(LineKind.Silent, "") : log.For(shown),
                FliersFor(shown),
                GaugeFor(events, index, pacing, true),
                GaugeFor(events, index, pacing, false),
                SoundOf(shown));
        }

        /// <summary>
        /// What an event sounds like.
        /// </summary>
        /// <remarks>
        /// A relic's damage sounds the same as the delver's own swing, deliberately: the source
        /// keys this on the event kind and not on who caused it, so a chain landing reads as one
        /// series of blows rather than as a different instrument. Silence for everything else,
        /// which is most things.
        /// </remarks>
        public static FightSound SoundOf(CombatEvent shown)
        {
            switch (shown.Type)
            {
                case CombatEventType.Gold: return FightSound.Gold;
                case CombatEventType.PlayerDamage: return FightSound.Hurt;
                case CombatEventType.EnemyDamage: return FightSound.Hit;
                case CombatEventType.Heal: return FightSound.Heal;
                case CombatEventType.Kill: return FightSound.Kill;
                case CombatEventType.Death: return FightSound.Death;
                default: return FightSound.None;
            }
        }

        /* ---------- who is attacking ---------- */

        /// <summary>
        /// A blow the delver landed, or tried to.
        /// </summary>
        /// <remarks>
        /// A relic's damage is not a blow. It happens on the delver's turn and it hurts the foe,
        /// but the delver did not swing for it, and a gauge that reset on every chain link would
        /// tick backwards every time a relic fired.
        /// </remarks>
        public static bool HeroStrikes(CombatEvent shown)
        {
            return (shown.Type == CombatEventType.EnemyDamage && shown.Source == "you") ||
                   shown.Type == CombatEventType.EnemyMiss;
        }

        /// <summary>
        /// A blow the foe landed, or tried to.
        /// </summary>
        /// <remarks>
        /// Depth zero only, for the same reason: a foe's thorns answering a strike is damage the
        /// delver took on their OWN turn, and counting it would make the foe's gauge reset
        /// whenever the delver attacked.
        /// </remarks>
        public static bool EnemyStrikes(CombatEvent shown)
        {
            return (shown.Type == CombatEventType.PlayerDamage && shown.Depth == 0) ||
                   shown.Type == CombatEventType.Miss;
        }

        /* ---------- the gauges ---------- */

        private static Gauge GaugeFor(IReadOnlyList<CombatEvent> events, int index, Pacing pacing,
            bool hero)
        {
            CombatEvent shown = events[index];
            bool mine = hero ? HeroStrikes(shown) : EnemyStrikes(shown);

            // The other side's opening dash is worth noticing: it means this side is about to be
            // struck before it has done anything, and its own gauge has to start from nothing.
            CombatEventType theirDash = hero
                ? CombatEventType.EnemyDashOpen
                : CombatEventType.DashOpen;

            bool entering = shown.Type == CombatEventType.Enter;
            if (!mine && !entering && shown.Type != theirDash) return Gauge.Untouched;

            if (entering && Opens(events, index, hero)) return new Gauge(true, 0);

            int until = Until(events, index, pacing, hero);

            // Nothing left to wind up for: this unit dies before it swings again. Freezing the
            // bar would say so, and saying so is not the screen's job.
            if (until == 0 && mine) return new Gauge(true, Gauge.KeepTheCadence);

            return new Gauge(true, until);
        }

        /// <summary>
        /// How long until this side strikes again, in milliseconds. Zero when it never does.
        /// </summary>
        /// <remarks>
        /// The holds are added up rather than the ticks, because the holds are what the delver
        /// actually waits through — capped, floored, and divided by whatever speed they chose.
        /// A gauge timed off the raw ticks would drift away from the fight the moment anybody
        /// pressed the speed control.
        /// </remarks>
        private static int Until(IReadOnlyList<CombatEvent> events, int index, Pacing pacing,
            bool hero)
        {
            int waited = 0;

            for (int j = index + 1; j < events.Count; j++)
            {
                waited += pacing.Between(events[j - 1].State.Tick, events[j].State.Tick);

                if (events[j].Type == CombatEventType.Enter) return 0;
                if (hero ? HeroStrikes(events[j]) : EnemyStrikes(events[j])) return waited;
            }

            return 0;
        }

        /// <summary>
        /// Whether the OTHER side dashes in before this one gets to swing.
        /// </summary>
        /// <remarks>
        /// Both dash events are checked for the foe's gauge and only one for the delver's, which
        /// looks like an oversight in the source and is kept: a delver's own Battle Dash and a
        /// rival's both mean the foe is about to be hit first, while only the rival's opening
        /// concerns the delver's bar.
        /// </remarks>
        private static bool Opens(IReadOnlyList<CombatEvent> events, int index, bool hero)
        {
            if (Dashes(events, index, hero, CombatEventType.DashOpen)) return true;

            return !hero && Dashes(events, index, false, CombatEventType.EnemyDashOpen);
        }

        private static bool Dashes(IReadOnlyList<CombatEvent> events, int index, bool hero,
            CombatEventType dash)
        {
            for (int j = index + 1; j < events.Count; j++)
            {
                if (events[j].Type == CombatEventType.Enter) return false;
                if (events[j].Type == dash) return true;
                if (hero ? HeroStrikes(events[j]) : EnemyStrikes(events[j])) return false;
            }

            return false;
        }

        /* ---------- the flying numbers ---------- */

        private static readonly Flier[] Nothing = new Flier[0];

        private static IReadOnlyList<Flier> FliersFor(CombatEvent shown)
        {
            switch (shown.Type)
            {
                case CombatEventType.EnemyDamage:
                    return One(FlierAt.Enemy, Struck(shown), Sign("-", shown, Struck(shown)));

                case CombatEventType.EnemyMiss:
                    return One(FlierAt.Enemy, FlierKind.Chain, "MISS");

                case CombatEventType.EnemyHeal:
                    return One(FlierAt.Enemy, FlierKind.Heal, "+" + Amount(shown));

                case CombatEventType.EnemyFury:
                    return One(FlierAt.Enemy, FlierKind.Heal, "+" + Amount(shown) + " ATK");

                case CombatEventType.EnemySlow:
                    return One(FlierAt.Enemy, FlierKind.Chain, "-" + Amount(shown) + " SPD");

                case CombatEventType.PlayerDamage:
                    return One(FlierAt.Hero, FlierKind.Hurt,
                        (shown.EnemyCrit == true ? "CRIT! -" : "-") + Amount(shown));

                case CombatEventType.Miss:
                    return One(FlierAt.Hero, FlierKind.Miss, "MISS");

                case CombatEventType.Heal:
                    return One(FlierAt.Hero, FlierKind.Heal, "+" + Amount(shown));

                case CombatEventType.Adrenaline:
                    return One(FlierAt.Hero, FlierKind.Buff, "+" + Amount(shown) + " ATK");

                case CombatEventType.Momentum:
                    return One(FlierAt.Hero, FlierKind.Buff, "+" + Amount(shown) + " SPD");

                case CombatEventType.Gold:
                    return One(FlierAt.Gold, FlierKind.Gold, "+" + Amount(shown));

                default:
                    return Nothing;
            }
        }

        /// <summary>
        /// How a blow on the foe reads: the delver's own, a critical, or a relic's.
        /// </summary>
        /// <remarks>
        /// A critical is Weighted Dice and only at depth zero — the relic doubling a chain link
        /// is not the delver landing a critical, and the source is careful about that. Anything
        /// not the delver's own swing reads as a chain, which is what keeps a shelf of relics
        /// from looking like the delver hitting eight times.
        /// </remarks>
        private static FlierKind Struck(CombatEvent shown)
        {
            if (shown.Relic == Content.RelicId.WeightedDice && shown.Depth == 0) return FlierKind.Crit;

            return shown.Source == "you" ? FlierKind.Damage : FlierKind.Chain;
        }

        private static string Sign(string sign, CombatEvent shown, FlierKind kind)
        {
            return (kind == FlierKind.Crit ? "CRIT! " + sign : sign) + Amount(shown);
        }

        private static string Amount(CombatEvent shown)
        {
            return shown.Amount.HasValue ? shown.Amount.Value.ToString() : "0";
        }

        private static IReadOnlyList<Flier> One(FlierAt where, FlierKind kind, string text)
        {
            return new[] { new Flier(where, kind, text) };
        }
    }
}
