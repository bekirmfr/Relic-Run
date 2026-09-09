using RelicRun.Core.Combat;

namespace RelicRun.Core.Presentation
{
    /// <summary>What comes off a body when something happens to it.</summary>
    public enum SprayKind
    {
        None = 0,

        /// <summary>A struck body throws up grit. Small, brief, and constant.</summary>
        Dust = 1,

        /// <summary>A falling body throws blood. Once, and much more of it.</summary>
        Blood = 2,
    }

    /// <summary>
    /// One event's worth of mess.
    /// </summary>
    /// <remarks>
    /// How MUCH and from WHOM is here; where each mote goes is the widget's business, the same
    /// split the flying numbers live under. The scatter is a look and belongs where somebody can
    /// change it by looking at it; which events spray at all is a rule, and rules are gated.
    /// </remarks>
    public readonly struct Spray
    {
        public readonly SprayKind Kind;

        /// <summary>Whose body this comes off.</summary>
        public readonly bool OnDelver;

        public readonly int Count;

        /// <summary>
        /// Whether the body is coming apart as well as bleeding.
        /// </summary>
        /// <remarks>
        /// The source shatters a dying sprite by reading its pixels and letting them fall as a
        /// pile. That is replaced here by a burst, which is the port's one deliberate departure
        /// in this corner: the pile is a beautiful trick and it needs the sprite's own pixels at
        /// run time, which is a texture read per death for an effect that lasts a fifth of a
        /// second. What survives is the fact of it — that a killed body comes apart rather than
        /// simply stopping — and that fact is what this carries.
        /// </remarks>
        public readonly bool Shatters;

        public Spray(SprayKind kind, bool onDelver, int count, bool shatters = false)
        {
            Kind = kind;
            OnDelver = onDelver;
            Count = count;
            Shatters = shatters;
        }

        public bool Any { get { return Kind != SprayKind.None && Count > 0; } }

        public override string ToString()
        {
            return !Any ? "nothing" : Count + " " + Kind + (OnDelver ? " on the delver" : " on the foe") +
                   (Shatters ? ", shattering" : "");
        }
    }

    /// <summary>
    /// Which events throw something off a body, and how much.
    /// </summary>
    /// <remarks>
    /// Four rules and one asymmetry, and the asymmetry is the source's rather than a slip: dust
    /// comes off a struck FOE only on a genuine blow, while a struck DELVER raises dust however
    /// deep the chain that hurt them. Ported as written. It is defensible — a delver being
    /// chipped by six relic ticks feels like six hits and a foe being chipped by six does not —
    /// but nothing in the source says so, and inventing the symmetry would be inventing balance.
    /// </remarks>
    public static class Sprays
    {
        /// <summary>Motes off a struck body.</summary>
        public const int Motes = 4;

        /// <summary>Drops off a falling one, which is twice as much and then some.</summary>
        public const int Drops = 8;

        /// <summary>What this event throws, if anything.</summary>
        public static Spray Of(CombatEvent shown)
        {
            switch (shown.Type)
            {
                // A killed foe bleeds and comes apart.
                case CombatEventType.Kill:
                    return new Spray(SprayKind.Blood, false, Drops, true);

                // So does a delver, and the screen owes them the same ending it gives a bat.
                case CombatEventType.Death:
                    return new Spray(SprayKind.Blood, true, Drops, true);

                // A genuine blow only. Relic damage arrives in chains of six and would bury the
                // foe in grit for a hit the delver did not throw.
                case CombatEventType.EnemyDamage:
                    return shown.Depth > 0
                        ? default(Spray)
                        : new Spray(SprayKind.Dust, false, Motes);

                // No depth test here, and that is the source's asymmetry rather than an omission.
                case CombatEventType.PlayerDamage:
                    return new Spray(SprayKind.Dust, true, Motes);

                default:
                    return default(Spray);
            }
        }
    }
}
