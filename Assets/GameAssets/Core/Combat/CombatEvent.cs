using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Combat
{
    /// <summary>
    /// Every kind of thing the engine can report. The presentation layer replays these on
    /// timers; it never computes combat itself.
    /// </summary>
    public enum CombatEventType
    {
        /// <summary>A foe steps up.</summary>
        Enter,

        /// <summary>The hero takes damage.</summary>
        PlayerDamage,

        /// <summary>The enemy takes damage.</summary>
        EnemyDamage,

        /// <summary>The hero heals.</summary>
        Heal,

        /// <summary>A heal landed on a full pool and was wasted.</summary>
        HealFull,

        /// <summary>The enemy heals.</summary>
        EnemyHeal,

        Gold,
        Luck,

        /// <summary>An enemy attack missed the hero.</summary>
        Miss,

        /// <summary>The hero's strike was evaded.</summary>
        EnemyMiss,

        /// <summary>A chain ran past the cap, or an effect resolved to nothing.</summary>
        Fizzle,

        Adrenaline,
        Momentum,

        /// <summary>A one-off log line with no numeric payload of its own.</summary>
        First,

        /// <summary>The enemy enraged.</summary>
        EnemyFury,

        /// <summary>The enemy was slowed.</summary>
        EnemySlow,

        DashOpen,
        EnemyDashOpen,
        Kill,
        Death,
    }

    /// <summary>Live counters carried on every event, so the UI never lags a fight behind.</summary>
    public readonly struct CombatCounters
    {
        /// <summary>Strikes landed this floor (<c>a</c>).</summary>
        public readonly int Strikes;

        /// <summary>Hits taken this floor (<c>h</c>).</summary>
        public readonly int Pain;

        /// <summary>Gold gains this floor (<c>g</c>).</summary>
        public readonly int Gold;

        /// <summary>Stone Emitter activations (<c>st</c>).</summary>
        public readonly int StoneCount;

        /// <summary>Strikes in the current fight (<c>f</c>).</summary>
        public readonly int FightStrikes;

        /// <summary>Heals counted toward Quenched Blade (<c>q</c>).</summary>
        public readonly int QuenchCount;

        /// <summary>Strikes counted toward Momentum Bead (<c>m</c>).</summary>
        public readonly int MomentumCount;

        /// <summary>Rabbit's Foot activations (<c>r</c>).</summary>
        public readonly int RabbitCount;

        /// <summary>Strikes landed across the whole run (<c>at</c>).</summary>
        public readonly int StrikeTotal;

        /// <summary>Sentinel Bell defence gained this floor (<c>sb</c>).</summary>
        public readonly int SentinelBonus;

        public CombatCounters(int strikes, int pain, int gold, int stoneCount, int fightStrikes,
            int quenchCount, int momentumCount, int rabbitCount, int strikeTotal, int sentinelBonus)
        {
            Strikes = strikes;
            Pain = pain;
            Gold = gold;
            StoneCount = stoneCount;
            FightStrikes = fightStrikes;
            QuenchCount = quenchCount;
            MomentumCount = momentumCount;
            RabbitCount = rabbitCount;
            StrikeTotal = strikeTotal;
            SentinelBonus = sentinelBonus;
        }
    }

    /// <summary>
    /// A full photograph of the fight at the moment an event fired.
    /// </summary>
    /// <remarks>
    /// Architecture invariant 5. Because every event carries this, the view is stateless about
    /// combat: it renders from the event rather than reading live game state, which is what
    /// keeps playback in step with the numbers.
    /// </remarks>
    public readonly struct CombatSnapshot
    {
        public readonly int Tick;
        public readonly int HeroHp;
        public readonly int HeroMaxHp;
        public readonly int EnemyHp;
        public readonly int Gold;
        public readonly int EnemyMaxHp;
        public readonly int EnemyAtk;
        public readonly EnemyRank EnemyRank;
        public readonly int EnemyArmor;
        public readonly int EnemySpd;
        public readonly int EnemyLck;
        public readonly int EnemyVariant;
        public readonly int EnemyIndex;

        /// <summary>Permanent ATK banked from Adrenaline.</summary>
        public readonly int HeroAdrenaline;

        /// <summary>
        /// Attack the hero has gained this round. Reported only by the duel engine, where a
        /// round-scoped bonus is worth surfacing on its own; a delve leaves it at zero.
        /// </summary>
        public readonly int HeroFury;

        /// <summary>The enemy's relics, or null when this foe has no relic list at all.</summary>
        public readonly IReadOnlyList<RelicId> EnemyRelics;

        /// <summary>The hero's live in-fight stat modifiers.</summary>
        public readonly IReadOnlyList<StatModifier> HeroMods;

        public readonly CombatCounters Counters;

        /// <summary>
        /// How many sharpenings the Sundering Anvil has spent, and whether the Soil has been dug.
        /// </summary>
        /// <remarks>
        /// Neither is a counter and both are budgets, which is why they live on the hero rather
        /// than in <see cref="CombatCounters"/> — and why they were not here. They are here now
        /// because the relic tray has to draw them, and Invariant 5 says an event carries the
        /// state it happened in: a view reaching for the live hero would show the same number
        /// for every step of a fight that was resolved before the first frame was drawn.
        ///
        /// Additive, and the corpus is unbothered: the differ walks the fields the RECORDING
        /// has and asks the replay for each by name, so a field the source never wrote is a
        /// field it never looks for.
        /// </remarks>
        public readonly int AnvilSpent;

        public readonly bool SoilUsed;

        /// <summary>
        /// The delver's stats as they stood, already folded from their rows.
        /// </summary>
        /// <remarks>
        /// The foe's four have always been here and the delver's were not, which meant a screen
        /// could say what it was fighting and not what it was fighting WITH. Folded rather than
        /// listed: <see cref="HeroMods"/> carries the in-fight rows for anything that wants to
        /// explain a number, and these are the totals for anything that only wants to show it.
        ///
        /// Read at the moment the event happened, like everything else here, so a stat that
        /// climbed mid-fight climbs on screen instead of arriving finished.
        /// </remarks>
        public readonly int HeroAtk;

        public readonly int HeroDef;

        public readonly int HeroSpd;

        public readonly int HeroLck;

        public CombatSnapshot(int tick, int heroHp, int heroMaxHp, int enemyHp, int gold, int enemyMaxHp,
            int enemyAtk, EnemyRank enemyRank, int enemyArmor, int enemySpd, int enemyLck,
            int enemyVariant, int enemyIndex, int heroAdrenaline,
            IReadOnlyList<RelicId> enemyRelics, IReadOnlyList<StatModifier> heroMods,
            CombatCounters counters, int heroFury = 0, int anvilSpent = 0, bool soilUsed = false,
            int heroAtk = 0, int heroDef = 0, int heroSpd = 0, int heroLck = 0)
        {
            AnvilSpent = anvilSpent;
            SoilUsed = soilUsed;
            HeroAtk = heroAtk;
            HeroDef = heroDef;
            HeroSpd = heroSpd;
            HeroLck = heroLck;
            HeroFury = heroFury;
            Tick = tick;
            HeroHp = heroHp;
            HeroMaxHp = heroMaxHp;
            EnemyHp = enemyHp;
            Gold = gold;
            EnemyMaxHp = enemyMaxHp;
            EnemyAtk = enemyAtk;
            EnemyRank = enemyRank;
            EnemyArmor = enemyArmor;
            EnemySpd = enemySpd;
            EnemyLck = enemyLck;
            EnemyVariant = enemyVariant;
            EnemyIndex = enemyIndex;
            HeroAdrenaline = heroAdrenaline;
            EnemyRelics = enemyRelics;
            HeroMods = heroMods;
            Counters = counters;
        }
    }

    /// <summary>One thing that happened, with the state it happened in.</summary>
    public readonly struct CombatEvent
    {
        public readonly CombatEventType Type;

        /// <summary>Chain depth. Drives the log's indent, and the chain cap.</summary>
        public readonly int Depth;

        /// <summary>Numeric payload, where the event has one.</summary>
        public readonly int? Amount;

        /// <summary>Log label. In corpus mode this is a relic id or string key, not a display name.</summary>
        public readonly string Source;

        /// <summary>The relic responsible, if any. This is the chain's provenance.</summary>
        public readonly RelicId Relic;

        /// <summary>
        /// Which inventory slot fired, when a specific copy is responsible. Relics are per-copy,
        /// so this is what lets the UI light up the right icon.
        /// </summary>
        public readonly int? RelicSlot;

        /// <summary>Set on hero damage: whether the blow was a critical hit.</summary>
        public readonly bool? EnemyCrit;

        /// <summary>Set on <see cref="CombatEventType.Enter"/>: whether a foe preceded this one.</summary>
        public readonly bool? Another;

        /// <summary>Set when the event belongs to the opposing side.</summary>
        public readonly bool? Foe;

        public readonly CombatSnapshot State;

        public CombatEvent(CombatEventType type, int depth, CombatSnapshot state,
            int? amount = null, string source = null, RelicId relic = RelicId.None,
            int? relicSlot = null, bool? enemyCrit = null, bool? another = null, bool? foe = null)
        {
            Type = type;
            Depth = depth;
            State = state;
            Amount = amount;
            Source = source;
            Relic = relic;
            RelicSlot = relicSlot;
            EnemyCrit = enemyCrit;
            Another = another;
            Foe = foe;
        }

        public override string ToString()
        {
            return Type + (Amount.HasValue ? " " + Amount.Value : "") +
                   (Source != null ? " (" + Source + ")" : "") +
                   " d" + Depth + " tk" + State.Tick;
        }
    }
}
