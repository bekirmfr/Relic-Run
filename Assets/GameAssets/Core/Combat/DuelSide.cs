using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Combat
{
    /// <summary>
    /// One competitor in a duel. Unlike a delve foe, this is a FULL player — items, sockets,
    /// counters, set bonuses, its own chains and its own luck engine.
    /// </summary>
    /// <remarks>
    /// Versus matches real people's builds against each other, so a rival cannot be a stat
    /// block: it has to run the same machinery the hero does or the simulation would lie about
    /// what their loadout actually does.
    /// </remarks>
    public sealed class DuelSide
    {
        /// <summary>Display name. The rival's relics are announced with it.</summary>
        public string Name = "Rival";

        /// <summary>
        /// True for the player. Events are reported from their point of view, so the hero's
        /// damage is "enemy damage" and the rival's is "player damage".
        /// </summary>
        public bool IsHero;

        public IReadOnlyList<RelicId> Items = new List<RelicId>();
        public HashSet<RelicId> Awakened = new HashSet<RelicId>();
        public Dictionary<int, SocketTrigger> SocketTriggers = new Dictionary<int, SocketTrigger>();
        public Dictionary<int, SocketEmitter> SocketEmitters = new Dictionary<int, SocketEmitter>();

        public int BaseAtk = 5;
        public int BaseDef;
        public int BaseSpd = 25;
        public int BaseLck = 10;

        public int Php = 60;
        public int Pmax = 60;
        public int Gold;
        public int Kills;

        /// <summary>The purse this side pays out when it loses the round.</summary>
        public int Drop;

        public int Adrenaline;
        public int MidasBonus;
        public int AtkBonus;
        public int DefBonus;
        public int SpdBonus;
        public int LuckBonus;

        /// <summary>Bestiary index used only to pick the rival's portrait.</summary>
        public int SpeciesIndex;

        public int Variant;
        public EnemyRank Rank = EnemyRank.Boss;

        // ---- transient, reset at the start of each duel ----

        /// <summary>Attack gained this round, from Fury emitters and the relics that grant it.</summary>
        public int Fury;

        public int Stone;
        public int Gale;
        public int LuckGain;
        public int Momentum;
        public int MomentumCount;
        public int QuenchBonus;
        public int QuenchCount;

        /// <summary>Defence rung up by Sentinel Bell. Capped at 3, or 5 when awakened.</summary>
        public int Sentinel;

        public int Strikes;
        public int StrikeCount;
        public int PainCount;
        public int GoldCount;
        public int StoneCount;
        public int RabbitCount;
        public int StrikeTotal;

        public bool BladeCharged;
        public bool Blocked;
        public bool HitTaken;
        public bool AdrenalineUsed;
        public bool WhiskerUsed;
        public bool ExecutionerUsed;
        public bool InstantRiposte;
        public bool FleshSetUsed;
        public bool SoilUsed;
        public bool GlassBroken;
        public bool FleshSetApplied;

        public int HitCount;
        public int HideLearned;
        public int MartyrCount;
        public int BootsUsedOnStrike = -1;

        /// <summary>Set by an awakened Stutterstep on the opponent: the next swing hits itself.</summary>
        public bool Staggered;

        public int AnvilBonus;
        public int DebtLeft;

        /// <summary>Per-slot guard, cleared each beat, so a genuine event wakes a copy once.</summary>
        public bool[] FiredThisBeat;

        public int CountRelic(RelicId id)
        {
            int n = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i] == id) n++;
            }

            return n;
        }

        public bool IsAwake(RelicId id)
        {
            return Awakened.Contains(id);
        }

        /// <summary>Copies held, plus one for an awakened copy.</summary>
        public int Effective(RelicId id)
        {
            return CountRelic(id) + (IsAwake(id) ? 1 : 0);
        }

        /// <summary>
        /// Relics of a kind. Unlike the delve engine, Hollow Idol does NOT count itself toward
        /// sets here — the duel counts raw kinds only.
        /// </summary>
        public int SetCount(RelicKind kind)
        {
            int n = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                if (RelicCatalog.KindOf(Items[i]) == kind) n++;
            }

            return n;
        }

        /// <summary>The relic label as the log shows it. A rival's relics are named as theirs.</summary>
        public string Label(RelicId id)
        {
            string key = RelicCatalog.KeyOf(id);
            return IsHero ? key : Name + "'s " + key;
        }

        /// <summary>In-fight modifiers, as labelled ledger entries.</summary>
        public List<StatModifier> DynamicMods()
        {
            var mods = new List<StatModifier>(4);
            if (Fury != 0) mods.Add(new StatModifier(Stat.Atk, "Fury (this round)", Fury, "dyn"));
            if (QuenchBonus != 0) mods.Add(new StatModifier(Stat.Atk, "Quenched Blade (this round)", QuenchBonus, "dyn"));
            if (Stone != 0) mods.Add(new StatModifier(Stat.Def, "Stone / Iron emitters (this fight)", Stone, "dyn"));
            if (Sentinel != 0) mods.Add(new StatModifier(Stat.Def, "Sentinel Bell (this floor)", Sentinel, "dyn"));
            if (Gale != 0) mods.Add(new StatModifier(Stat.Spd, "Gale emitters (this round)", Gale, "dyn"));
            if (Momentum != 0) mods.Add(new StatModifier(Stat.Spd, "Momentum Bead (this fight)", Momentum, "dyn"));
            if (LuckGain != 0) mods.Add(new StatModifier(Stat.Lck, "Rabbit / Gambler (this fight)", LuckGain, "dyn"));
            return mods;
        }

        /// <summary>
        /// The context the stat ledger reads. Every rival is "worthy blood", so Duelist's Oath
        /// always applies in versus.
        /// </summary>
        public StatContext StatContext()
        {
            return new StatContext
            {
                Items = Items,
                Awakened = Awakened,
                IsVersus = true,
                BaseAtk = BaseAtk,
                BaseDef = BaseDef,
                BaseSpd = BaseSpd,
                BaseLck = BaseLck,
                Adrenaline = Adrenaline,
                MidasBonus = MidasBonus,
                AtkBonus = AtkBonus,
                DefBonus = DefBonus,
                SpdBonus = SpdBonus,
                LuckBonus = LuckBonus,
                Php = Php,
                Pmax = Pmax,
                Gold = Gold,
                Floor = 0,
                FoeRank = EnemyRank.Boss,
                Mods = DynamicMods(),
            };
        }

        public int StatOf(Stat stat)
        {
            return StatLedger.Of(StatContext(), stat);
        }

        /// <summary>Clears everything that lives only for the length of one duel.</summary>
        public void ResetForDuel()
        {
            Stone = 0;
            Gale = 0;
            LuckGain = 0;
            Momentum = 0;
            MomentumCount = 0;
            Sentinel = 0;
            Strikes = 0;
            StrikeCount = 0;
            PainCount = 0;
            GoldCount = 0;
            StoneCount = 0;
            RabbitCount = 0;
            BladeCharged = false;
            Blocked = false;
            HitTaken = false;
            AdrenalineUsed = false;
            WhiskerUsed = false;
            ExecutionerUsed = false;
            InstantRiposte = false;
            HitCount = 0;
            HideLearned = 0;
            MartyrCount = 0;
            BootsUsedOnStrike = -1;
            Staggered = false;
            FiredThisBeat = new bool[Items.Count];
        }
    }
}
