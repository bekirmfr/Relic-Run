using System.Collections.Generic;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;

namespace RelicRun.Core.Presentation
{
    /// <summary>
    /// What colour a log line reads in. Not a colour — a meaning the screen colours.
    /// </summary>
    /// <remarks>
    /// Kept as a meaning rather than a hex value on purpose. The log is one of the few places
    /// where colour carries information rather than decoration — a delver skimming it is looking
    /// for the red lines — and a view-model that handed out hex codes would be deciding the
    /// game's palette from inside Core.
    /// </remarks>
    public enum LineKind
    {
        /// <summary>Nothing to say. The line is not shown at all.</summary>
        Silent,

        /// <summary>A foe steps up.</summary>
        Enter,

        /// <summary>The delver did something.</summary>
        You,

        /// <summary>A relic of the delver's did something.</summary>
        Chain,

        /// <summary>A relic of the FOE's did something.</summary>
        EnemyChain,

        /// <summary>It happened, and it did not matter.</summary>
        Dim,

        /// <summary>It happened to the delver, and it hurt.</summary>
        Hurt,

        /// <summary>It went the delver's way.</summary>
        Good,

        Gold,
        Kill,
        Death,
    }

    /// <summary>One line of the combat log.</summary>
    public readonly struct CombatLine
    {
        public readonly LineKind Kind;
        public readonly string Text;

        /// <summary>
        /// How deep in a chain this was, which the log shows as arrows.
        /// </summary>
        /// <remarks>
        /// Carried rather than rendered. The source prefixes up to three <c>↳</c> and then stops
        /// counting; that is a decision about a strip of UI, and it belongs where the strip is.
        /// </remarks>
        public readonly int Depth;

        public CombatLine(LineKind kind, string text, int depth = 0)
        {
            Kind = kind;
            Text = text ?? "";
            Depth = depth;
        }

        /// <summary>Whether this reaches the log at all.</summary>
        public bool Shown { get { return Kind != LineKind.Silent && Text.Length > 0; } }

        public override string ToString() { return Kind + ": " + Text; }
    }

    /// <summary>
    /// Turns a combat event into a line a delver can read.
    /// </summary>
    /// <remarks>
    /// The whole of the view-model for the log, and a fair test of invariant 5: an event carries
    /// its own snapshot, so this needs no memory of the fight at all. Hand it any event out of
    /// any fight in any order and it says the same thing.
    ///
    /// Nine of the twenty event kinds never pass through the locale at all, and a tenth —
    /// being hit — is localised only when the hit was ordinary. That is the source's doing
    /// rather than a shortcut here: it localises the common lines and writes the rest inline.
    /// It is carried across rather than quietly fixed, because inventing eight translations is
    /// not a porting decision. <see cref="English"/> names them so the gap is a checkable list
    /// rather than a surprise, and the keys that DO exist are checked to exist.
    /// </remarks>
    public sealed class CombatLog
    {
        private readonly Locale _locale;
        private readonly string _rival;

        /// <param name="rival">
        /// The other delver's name in a duel, or null in a delve. The source shows a rival's own
        /// name where it would otherwise say "Rival", and shows the bestiary name for a foe.
        /// </param>
        public CombatLog(Locale locale, string rival = null)
        {
            _locale = locale;
            _rival = string.IsNullOrEmpty(rival) ? null : rival;
        }

        /// <summary>What one event reads as.</summary>
        public CombatLine For(CombatEvent shown)
        {
            switch (shown.Type)
            {
                case CombatEventType.Enter:
                    // No trim. The source needs one because its own lookup does not clean,
                    // and "{i} {n} blocks the way" with no number leaves a leading space.
                    // Locale.Clean already trims, so a second one here is dead code that
                    // reads as though it were load-bearing.
                    return Line(LineKind.Enter, Say("logEnter", "i", "", "n", Foe(shown)), shown);

                case CombatEventType.EnemyDamage:
                    return shown.Source == "you"
                        ? Line(LineKind.You, Say("logYouStrike", "n", Amount(shown)), shown)
                        : Line(LineKind.Chain,
                            Say("logSrcDeals", "s", shown.Source, "n", Amount(shown)), shown);

                case CombatEventType.EnemyMiss:
                    return Line(LineKind.Dim, Foe(shown) + " sidesteps your strike!", shown);

                case CombatEventType.EnemyHeal:
                    return Line(LineKind.EnemyChain,
                        "♥ " + Rival() + "'s " + Name(RelicId.VampireTooth) + " heals " +
                        Amount(shown), shown);

                case CombatEventType.EnemySlow:
                    return Line(LineKind.Chain,
                        Name(RelicId.Stutterstep) + " staggers the foe: -" + Amount(shown) + " SPD",
                        shown);

                case CombatEventType.Momentum:
                    return Line(LineKind.Chain,
                        Name(RelicId.MomentumBead) + " rolls: +" + Amount(shown) + " SPD", shown);

                case CombatEventType.EnemyFury:
                    return Line(LineKind.EnemyChain,
                        Rival() + "'s " + Name(RelicId.BerserkerCharm) + " rages: +" +
                        Amount(shown) + " ATK", shown);

                case CombatEventType.PlayerDamage:
                    return Struck(shown);

                case CombatEventType.Miss:
                    return Line(LineKind.Good, Say("logMiss", "n", Foe(shown)), shown);

                case CombatEventType.First:
                    return Line(shown.Foe == true ? LineKind.EnemyChain : LineKind.Chain,
                        shown.Source, shown);

                case CombatEventType.DashOpen:
                    return Line(LineKind.Good, shown.Source + " — first strike!", shown);

                case CombatEventType.EnemyDashOpen:
                    return Line(LineKind.Hurt,
                        (_rival ?? "The rival") + "'s " + Name(RelicId.BattleDash) +
                        " — they strike first!", shown);

                case CombatEventType.Heal:
                    return Line(LineKind.Good, shown.Source == "you"
                        ? Say("logHeal", "n", Amount(shown))
                        : Say("logSrcHeals", "s", shown.Source, "n", Amount(shown)), shown);

                case CombatEventType.HealFull:
                    return Line(LineKind.Dim, Say("logFull", "s", shown.Source), shown);

                case CombatEventType.Gold:
                    return Line(LineKind.Gold,
                        Say("logGold", "n", Amount(shown), "s", shown.Source), shown);

                case CombatEventType.Kill:
                    return Line(LineKind.Kill, Say("logKill", "n", Foe(shown)), shown);

                case CombatEventType.Adrenaline:
                    return Line(LineKind.Chain, Say("logAdren", "n", Amount(shown)), shown);

                case CombatEventType.Fizzle:
                    return Line(LineKind.Dim, Say("logFizzle"), shown);

                case CombatEventType.Luck:
                    return Line(LineKind.Chain, "✦ " + shown.Source + ": a lucky break", shown);

                case CombatEventType.Death:
                    return Line(LineKind.Death, Say("logDeath"), shown);

                default:
                    return new CombatLine(LineKind.Silent, "", shown.Depth);
            }
        }

        /// <summary>
        /// Being hit, which reads three different ways.
        /// </summary>
        /// <remarks>
        /// A foe's thorns is not the foe hitting you, and a critical is not an ordinary hit. The
        /// source separates all three and so does this — the middle one is the only line in the
        /// log that shouts.
        /// </remarks>
        private CombatLine Struck(CombatEvent shown)
        {
            if (shown.Foe == true)
            {
                return Line(LineKind.EnemyChain,
                    Rival() + "'s " + Name(RelicId.ThornVest) + " bites for " + Amount(shown), shown);
            }

            if (shown.EnemyCrit == true)
            {
                return Line(LineKind.Hurt,
                    Foe(shown) + " lands a CRITICAL hit for " + Amount(shown) + "!", shown);
            }

            return Line(LineKind.Hurt,
                Say("logHitYou", "n", Foe(shown), "a", Amount(shown)), shown);
        }

        /// <summary>
        /// The foe's name: the rival's own in a duel, the bestiary's otherwise.
        /// </summary>
        /// <remarks>
        /// A duel is against a person with a name, and calling them "Skeleton" because their
        /// avatar happens to be one would read as a bug rather than as flavour.
        /// </remarks>
        private string Foe(CombatEvent shown)
        {
            return _rival ?? Say("en" + shown.State.EnemyIndex);
        }

        private string Rival() { return _rival ?? "Rival"; }

        /// <summary>
        /// A relic's name, in the delver's language when there is one to be in.
        /// </summary>
        /// <remarks>
        /// Nineteen of the fifty have a locale key and translate. The other thirty-one were added
        /// to the source's expansion table and are English everywhere; showing the shipped name is
        /// the honest answer, and better than showing a key nobody wrote.
        /// </remarks>
        private string Name(RelicId relic)
        {
            RelicTextDef text = RelicText.Get(relic);
            if (text == null) return relic.ToString();

            return text.Translated ? Say(text.NameKey) : text.Name;
        }

        private static string Amount(CombatEvent shown)
        {
            return shown.Amount.HasValue ? shown.Amount.Value.ToString() : "0";
        }

        private CombatLine Line(LineKind kind, string text, CombatEvent shown)
        {
            return new CombatLine(kind, text, shown.Depth);
        }

        /// <summary>A localised line, with its values filled.</summary>
        /// <remarks>
        /// Through <see cref="Locale"/>, so a key nobody wrote shows up as the key rather than as
        /// a blank — visible in testing instead of invisible in a build.
        /// </remarks>
        private string Say(string key, params string[] values)
        {
            if (_locale == null) return key;
            if (values == null || values.Length == 0) return _locale.Get(key);

            var filled = new Dictionary<string, string>(values.Length / 2);
            for (int i = 0; i + 1 < values.Length; i += 2) filled[values[i]] = values[i + 1];

            return _locale.Get(key, filled);
        }

        /// <summary>
        /// The keys this asks the locale for. Every one of them should exist.
        /// </summary>
        /// <remarks>
        /// Listed so a test can check them. <see cref="Locale"/> falls back to showing the key
        /// itself, which is the right behaviour and also the reason a typo here would reach a
        /// delver as <c>logYouStrike</c> rather than as a crash.
        /// </remarks>
        public static readonly IReadOnlyList<string> Keys = new[]
        {
            "logEnter", "logYouStrike", "logSrcDeals", "logHitYou", "logMiss", "logHeal",
            "logSrcHeals", "logFull", "logGold", "logKill", "logAdren", "logFizzle", "logDeath",
        };

        /// <summary>
        /// The event kinds whose line is written in English and stays that way.
        /// </summary>
        /// <remarks>
        /// A tracked gap, in the same sense as the four in <c>validate.mjs</c>. Nine kinds never
        /// reach the locale because the source wrote their lines inline, and a tenth is worse
        /// than that rather than better: being hit is localised for an ordinary blow and English
        /// for a critical one and for a foe's thorns, so the same event reads in two languages
        /// depending on how hard it landed.
        ///
        /// Named here so whoever localises them has the list, and so the number cannot drift
        /// without a test noticing.
        /// </remarks>
        public static readonly IReadOnlyList<CombatEventType> English = new[]
        {
            CombatEventType.EnemyMiss, CombatEventType.EnemyHeal, CombatEventType.EnemySlow,
            CombatEventType.Momentum, CombatEventType.EnemyFury, CombatEventType.DashOpen,
            CombatEventType.EnemyDashOpen, CombatEventType.Luck, CombatEventType.First,
        };
    }
}
