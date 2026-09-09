using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Combat
{
    /// <summary>One foe in a pack.</summary>
    public sealed class EnemyState
    {
        /// <summary>Bestiary index. Selects the sprite sheet row.</summary>
        public int SpeciesIndex;

        /// <summary>Starting HP. The working pool lives in the engine, not here.</summary>
        public int Hp;

        /// <summary>
        /// HP ceiling, normally equal to <see cref="Hp"/>. An awakened Vampire Tooth drains the
        /// ceiling rather than the pool, which is the only thing that moves it.
        /// </summary>
        public int MaxHp;

        public int Atk;
        public int Armor;
        public int Spd = 25;
        public int Lck = 10;

        /// <summary>Gold this foe drops when killed.</summary>
        public int Drop;

        public EnemyRank Rank = EnemyRank.Guard;

        /// <summary>Sprite sheet column: 0 guard, 1 elite, 2 boss. Decided by rank, never randomly.</summary>
        public int Variant;

        /// <summary>
        /// Relics this foe carries, or null when it has none at all.
        /// </summary>
        /// <remarks>
        /// Null and empty are different: the source game only reports a foe's relics when the
        /// list exists, so an empty list still appears in events while a missing one does not.
        ///
        /// What a foe's relics DO is not a short list, and an earlier version of this comment
        /// said it was. The combat code is written against <c>ICombatActor</c> throughout, and
        /// <c>FoeActor</c> answers <c>Effective</c> and <c>CountRaw</c> with the real counts —
        /// so every relic whose effect is read as a count already works for whoever wears it.
        /// Thorn Vest returns damage for a foe exactly as it does for a delver.
        ///
        /// Three things a foe cannot do, and each for its own reason. It cannot WAKE a relic:
        /// <c>FoeActor.IsAwake</c> is false by construction, so anything gated on an awakening —
        /// Iron Skin's glance, the Whetstone's sunder — never fires. It has no inventory SLOTS,
        /// so nothing that walks them reaches it, which in practice means sockets: a foe wears
        /// relics but nothing is bolted to them. And a handful of reads still go through the
        /// hero's own count rather than an actor's.
        ///
        /// Separately, some relics are not combat relics at all. Ox Heart raises the pool when it
        /// is PICKED UP, so a delve foe whose hit points were typed in has nothing for it to
        /// change; a versus rival, built through the run layer, does get its thirteen a copy.
        /// </remarks>
        public IReadOnlyList<RelicId> Relics;

        /// <summary>
        /// Which of this foe's relics have been woken.
        /// </summary>
        /// <remarks>
        /// A foe could not wake anything at all until now — <c>FoeActor.IsAwake</c> answered
        /// false by construction — so every awakened-only effect was dead on a foe however it was
        /// equipped. Iron Skin's glance and the Whetstone's sunder are both gated on an
        /// awakening, and both were therefore inert on the very bosses whose kits contain them.
        ///
        /// Empty by default, and that is not a hedge: an awakened-only relic does nothing for a
        /// DELVER who has not woken it either. The rule is the same for whoever wears it, which
        /// is the whole point — what differs between a delver and a foe is what they can get
        /// hold of, never what it does once they have it.
        /// </remarks>
        public IReadOnlyCollection<RelicId> Awakened = new List<RelicId>();

        /// <summary>Sockets bolted to this foe's relics, keyed by the slot they sit in.</summary>
        /// <remarks>
        /// Empty in every fight the game currently produces, because nothing grants a socket any
        /// more. Kept because the mechanism is still live for whoever has one, and a foe that
        /// could not use a socket would be an actor-specific rule about a relic rather than about
        /// reach.
        /// </remarks>
        public IReadOnlyDictionary<int, SocketTrigger> SocketTriggers =
            new Dictionary<int, SocketTrigger>();

        public IReadOnlyDictionary<int, SocketEmitter> SocketEmitters =
            new Dictionary<int, SocketEmitter>();

        public int CountRelic(RelicId id)
        {
            if (Relics == null)
            {
                return 0;
            }

            int n = 0;
            for (int i = 0; i < Relics.Count; i++)
            {
                if (Relics[i] == id) n++;
            }

            return n;
        }
    }

    /// <summary>
    /// Floor-scoped state that survives the end of a simulation.
    /// </summary>
    /// <remarks>
    /// This is what makes revival exact: the hero resumes mid-floor with every counter and
    /// timed bonus exactly as it stood when they fell, rather than restarting the floor clean.
    /// </remarks>
    public sealed class CarryState
    {
        public int FuryBonus;
        public int StoneBonus;
        public int SentinelBonus;
        public int GaleBonus;
        public int LuckBonus;

        /// <summary>Strikes landed this floor. Socketed attack triggers fire on every third.</summary>
        public int StrikeCount;

        /// <summary>Hits taken this floor. Socketed pain triggers fire on every third.</summary>
        public int PainCount;

        /// <summary>Gold gains this floor. Socketed gold triggers fire on every third.</summary>
        public int GoldCount;

        public int StoneCount;

        /// <summary>Whether the Flesh set's once-per-floor death refusal has been spent.</summary>
        public bool FleshSetUsed;

        public int TitheDefense;
        public bool ExecutionerUsed;
        public bool GamblerUsed;
        public int HeadsmanBonus;
        public int QuenchBonus;
        public int QuenchCount;
        public bool SparkStored;

        public CarryState Clone()
        {
            return (CarryState)MemberwiseClone();
        }
    }

    /// <summary>The hero as the engine sees them. Mutated in place across a floor.</summary>
    public sealed class HeroState
    {
        /// <summary>Inventory in draft order, duplicates included.</summary>
        public IReadOnlyList<RelicId> Items = new List<RelicId>();

        /// <summary>Relics with at least one awakened copy.</summary>
        public IReadOnlyCollection<RelicId> Awakened = new List<RelicId>();

        /// <summary>Socketed components, keyed by INVENTORY INDEX — sockets belong to a copy.</summary>
        public IReadOnlyDictionary<int, SocketTrigger> SocketTriggers = new Dictionary<int, SocketTrigger>();

        public IReadOnlyDictionary<int, SocketEmitter> SocketEmitters = new Dictionary<int, SocketEmitter>();

        public bool IsVersus;
        public int Floor;

        public int Php = 100;
        public int Pmax = 100;
        public int Gold;
        public int Kills;

        public int Adrenaline;
        public int MidasBonus;
        public int AtkBonus;
        public int DefBonus;
        public int SpdBonus;
        public int LuckBonus;

        public int BaseAtk = 5;
        public int BaseDef;
        public int BaseSpd = 25;
        public int BaseLck = 10;

        /// <summary>Strikes landed across the whole run.</summary>
        public int StrikeTotal;

        public int AnvilBonus;
        public int ChaliceGain;
        public int DebtLeft;
        public bool GlassBroken;
        public bool SoilUsed;

        /// <summary>Set once the Flesh set's +3 max HP has been granted this run.</summary>
        public bool FleshSetApplied;

        public DefenseModel DefenseModel = DefenseModel.Percentage;

        /// <summary>Floor state to resume from, or null to start the floor fresh.</summary>
        public CarryState Carry;
    }

    /// <summary>What a resolved floor produced.</summary>
    public sealed class CombatResult
    {
        public readonly IReadOnlyList<CombatEvent> Events;

        /// <summary>Floor-scoped state as it stood at the end — at the moment of death, if the hero fell.</summary>
        public readonly CarryState Carry;

        /// <summary>
        /// How far into the pack the floor got: the index of the foe that was being fought when
        /// it ended, and what was left of it.
        /// </summary>
        /// <remarks>
        /// Only a revive reads these. A hero brought back does not start the floor again — they
        /// resume against the foe that felled them, with its wounds intact.
        /// </remarks>
        public readonly int FoeIndex;

        public readonly int FoeHp;

        public CombatResult(IReadOnlyList<CombatEvent> events, CarryState carry,
            int foeIndex = 0, int foeHp = 0)
        {
            Events = events;
            Carry = carry;
            FoeIndex = foeIndex;
            FoeHp = foeHp;
        }
    }
}
