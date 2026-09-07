using System;
using System.Collections.Generic;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Run
{
    /// <summary>What the hero asked the bazaar for, in a versus match.</summary>
    public enum VersusDealKind
    {
        Leave = 0,
        Buy = 1,

        /// <summary>Awaken one COPY — the slot, not the relic. A second copy stays asleep.</summary>
        Awaken = 2,
    }

    /// <summary>One visit to the versus bazaar.</summary>
    public struct VersusDeal
    {
        public VersusDealKind Kind;
        public RelicId Relic;

        /// <summary>Inventory slot, for an awakening.</summary>
        public int Slot;

        public static readonly VersusDeal Leave = new VersusDeal { Kind = VersusDealKind.Leave };

        public static VersusDeal Buy(RelicId id)
        {
            return new VersusDeal { Kind = VersusDealKind.Buy, Relic = id };
        }

        public static VersusDeal Awaken(int slot)
        {
            return new VersusDeal { Kind = VersusDealKind.Awaken, Slot = slot };
        }
    }

    /// <summary>What a versus match asks of whoever is playing it.</summary>
    public interface IVersusChoices
    {
        RelicId Draft(VersusMatch match, IReadOnlyList<RelicId> offer);

        VersusDeal Bazaar(VersusMatch match, IReadOnlyList<RelicId> offer, IReadOnlyList<int> awakenable);
    }

    /// <summary>Told what a match did, as it happens.</summary>
    public interface IVersusObserver
    {
        void Draft(VersusMatch match, IReadOnlyList<RelicId> offer, RelicId pick);

        void Round(VersusMatch match, int foe, bool won);

        void Bazaar(VersusMatch match, IReadOnlyList<RelicId> offer,
            IReadOnlyList<int> awakenable, VersusDeal deal);

        void End(VersusMatch match, RunEnding how);
    }

    /// <summary>
    /// A versus match in progress: the hero, their lobby, and the seven delvers in it.
    /// </summary>
    /// <remarks>
    /// Awakenings are keyed by inventory SLOT rather than relic, because that is what the
    /// bazaar sells — one copy, awake, while a second copy of the same relic stays asleep. The
    /// rules downstream want a per-relic count, and <see cref="AwakenedIds"/> is that
    /// conversion, which is exactly what the source does before a duel starts.
    /// </remarks>
    public sealed class VersusMatch
    {
        public readonly HeroState Hero = new HeroState();

        public readonly List<RelicId> Items = new List<RelicId>();

        /// <summary>Inventory slots whose copy has been awakened.</summary>
        public readonly HashSet<int> AwakenedSlots = new HashSet<int>();

        public readonly List<Rival> Roster = new List<Rival>();

        /// <summary>Lobby lives. At zero the hero is out of the match.</summary>
        public int Lives = 3;

        /// <summary>The hall the hero hosts in.</summary>
        public int Hall = 1;

        /// <summary>Which rival is being fought this round.</summary>
        public int Foe;

        /// <summary>How many rivals the hero has met, per rival, so a rematch changes venue.</summary>
        public readonly Dictionary<int, int> Met = new Dictionary<int, int>();

        public int Round
        {
            get { return Hero.Floor; }
            set { Hero.Floor = value; }
        }

        public int Php
        {
            get { return Hero.Php; }
            set { Hero.Php = value; }
        }

        public int Pmax
        {
            get { return Hero.Pmax; }
            set { Hero.Pmax = value; }
        }

        public int Gold
        {
            get { return Hero.Gold; }
            set { Hero.Gold = value; }
        }

        public VersusMatch()
        {
            Hero.IsVersus = true;
            Hero.Items = Items;
        }

        /// <summary>Awakenings as the rules want them: how many copies of each relic are awake.</summary>
        public Dictionary<RelicId, int> AwakenedIds()
        {
            var counts = new Dictionary<RelicId, int>();
            foreach (int slot in AwakenedSlots)
            {
                if (slot < 0 || slot >= Items.Count) continue;

                int n;
                counts[Items[slot]] = (counts.TryGetValue(Items[slot], out n) ? n : 0) + 1;
            }

            return counts;
        }

        public int Count(RelicId id)
        {
            int n = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i] == id) n++;
            }

            return n;
        }

        /// <summary>Which copies the bazaar would offer to awaken: stacking, and not already awake.</summary>
        public List<int> Awakenable()
        {
            var slots = new List<int>();
            var seen = new HashSet<RelicId>();

            for (int slot = 0; slot < Items.Count; slot++)
            {
                RelicId id = Items[slot];
                if (!RelicCatalog.Get(id).Stackable) continue;
                if (AwakenedSlots.Contains(slot)) continue;
                if (!seen.Add(id)) continue;

                slots.Add(slot);
                if (slots.Count == 6) break;
            }

            return slots;
        }
    }

    /// <summary>
    /// The arena: seven rival delvers, three lives each, and one crown.
    /// </summary>
    /// <remarks>
    /// A round is one duel against one of them. Losing costs the hero a life and pays a
    /// consolation purse; winning costs the rival one. Meanwhile the rivals who were not fought
    /// pair off out of sight and knock each other down, so the lobby thins whether or not the
    /// hero is winning — and every survivor drafts a relic before the next round. The last
    /// delver standing takes the crown.
    ///
    /// The roster is supplied rather than generated. Building one in the source spends most of
    /// its randomness on the rivals' appearance, which belongs with the wardrobe in a later
    /// phase; what a match needs from it is the delvers, and where the generator had got to.
    /// </remarks>
    public static class VersusRun
    {
        /// <summary>Relics offered at each draft.</summary>
        public const int DraftChoices = 3;

        /// <summary>Picks taken before the first round, rather than one.</summary>
        public const int OpeningPicks = 3;

        /// <summary>The round the bazaar sits on.</summary>
        public const int BazaarRound = 5;

        /// <summary>Lives, the hero's and each rival's.</summary>
        public const int Lives = 3;

        /// <summary>What the pool grows by each round.</summary>
        public const double PoolGrowth = 1.15;

        /// <summary>Paid to whoever wins a bout out of sight.</summary>
        public const int BracketPurse = 15;

        /// <summary>Paid per round, to the hero on a loss and to every surviving rival.</summary>
        public const int RoundStipend = 10;

        /// <summary>Paid for taking the crown.</summary>
        public const int CrownPurse = 50;

        /// <summary>Loot a rival drops, before the round stipend.</summary>
        public const int WinBonus = 15;

        /// <summary>Where a hero fights this round: their own hall first, the rival's on a rematch.</summary>
        public static int HallFor(VersusMatch match, int foe)
        {
            int seen;
            match.Met.TryGetValue(foe, out seen);
            match.Met[foe] = seen + 1;

            // First meeting is at home, and they alternate from there.
            return seen % 2 == 0 ? match.Hall : match.Roster[foe].Hall;
        }

        /// <summary>
        /// The rival the hero meets this round, as a stat block.
        /// </summary>
        /// <remarks>
        /// Their base stats are fixed for the whole match; what grows is the pool, by 15% a
        /// round, so stat relics compound on top of a rising body. The last delver left is
        /// fought as a king rather than a boss.
        /// </remarks>
        public static EnemyState NextFoe(VersusMatch match, int round, Mulberry32 rng)
        {
            var alive = new List<int>();
            for (int i = 0; i < match.Roster.Count; i++)
            {
                if (match.Roster[i].Lives > 0) alive.Add(i);
            }

            int chosen = alive[(int)Math.Floor(rng.Next() * alive.Count)];
            match.Foe = chosen;

            Rival rival = match.Roster[chosen];
            int Held(RelicId id)
            {
                int n = 0;
                for (int i = 0; i < rival.Relics.Count; i++)
                {
                    if (rival.Relics[i] == id) n++;
                }

                return n;
            }

            var foe = new EnemyState
            {
                SpeciesIndex = (int)Math.Floor(rng.Next() * 9),
                Hp = JsMath.RoundToInt(
                    (rival.Base.Hp + 13 * Held(RelicId.OxHeart)) * Math.Pow(PoolGrowth, round - 1)),
                Atk = rival.Base.Atk + Held(RelicId.Whetstone),
                Armor = rival.Base.Def + Held(RelicId.IronSkin),
                Spd = rival.Base.Spd,
                Lck = rival.Base.Lck + 15 * Math.Min(1, Held(RelicId.LuckyClover)),
                Rank = alive.Count == 1 ? EnemyRank.King : EnemyRank.Boss,
                Variant = (int)Math.Floor(rng.Next() * 3),
                Relics = new List<RelicId>(rival.Relics),
                Drop = WinBonus + RoundStipend * round,
            };

            if (Held(RelicId.SwiftBoots) > 0) foe.Spd = JsMath.RoundToInt(foe.Spd * 1.25);
            foe.MaxHp = foe.Hp;
            return foe;
        }

        /// <summary>
        /// What every surviving rival does between rounds: take the stipend, look at two relics,
        /// pay to see two more if both are poor, and keep the better one.
        /// </summary>
        public static void RivalsDraft(VersusMatch match, int round, Mulberry32 rng)
        {
            List<RelicId> pool = RelicCatalog.PoolFor(GameModes.Versus);

            foreach (Rival rival in match.Roster)
            {
                if (rival.Lives <= 0) continue;
                rival.Gold += RoundStipend * round;

                var live = new List<RelicId>(pool.Count);
                for (int i = 0; i < pool.Count; i++)
                {
                    if (RelicDraft.IsLive(pool[i], rival.Relics)) live.Add(pool[i]);
                }

                RelicId first = live[(int)Math.Floor(rng.Next() * live.Count)];
                RelicId second = live[(int)Math.Floor(rng.Next() * live.Count)];

                // Both poor and the purse allows: pay to see two more.
                if (Math.Max(RivalDraft.Score(first, rival, round), RivalDraft.Score(second, rival, round))
                        < RivalDraft.WeakOffer && rival.Gold >= RivalDraft.RerollCost)
                {
                    rival.Gold -= RivalDraft.RerollCost;
                    first = live[(int)Math.Floor(rng.Next() * live.Count)];
                    second = live[(int)Math.Floor(rng.Next() * live.Count)];
                }

                RelicId pick =
                    RivalDraft.Score(first, rival, round) >= RivalDraft.Score(second, rival, round)
                        ? first
                        : second;

                if (RelicCatalog.Get(pick).Stackable || !rival.Relics.Contains(pick))
                {
                    rival.Relics.Add(pick);
                }
            }
        }

        /// <summary>The bouts the hero did not fight, resolved out of sight.</summary>
        public static void Bracket(VersusMatch match, Mulberry32 rng)
        {
            var pool = new List<Rival>();
            for (int i = 0; i < match.Roster.Count; i++)
            {
                if (match.Roster[i].Lives > 0 && i != match.Foe) pool.Add(match.Roster[i]);
            }

            for (int i = 0; i + 1 < pool.Count; i += 2)
            {
                bool firstWins = rng.Next() < 0.5;
                Rival winner = firstWins ? pool[i] : pool[i + 1];
                Rival loser = firstWins ? pool[i + 1] : pool[i];

                loser.Lives--;
                winner.Gold += BracketPurse;
            }
        }

        /// <summary>The relics on offer this round.</summary>
        public static List<RelicId> Offer(VersusMatch match, Mulberry32 rng)
        {
            var avail = new List<RelicId>();
            List<RelicId> pool = RelicCatalog.PoolFor(GameModes.Versus);

            for (int i = 0; i < pool.Count; i++)
            {
                RelicId id = pool[i];
                if (!RelicCatalog.Get(id).Stackable && match.Count(id) > 0) continue;
                if (!RelicDraft.IsLive(id, match.Items)) continue;
                avail.Add(id);
            }

            var offer = new List<RelicId>(DraftChoices);
            int want = Math.Min(DraftChoices, avail.Count);
            int guard = 0;

            while (offer.Count < want && guard++ < 200)
            {
                RelicId id = RelicDraft.Weighted(avail, match.Items, rng);
                if (!offer.Contains(id)) offer.Add(id);
            }

            return offer;
        }
    }
}
