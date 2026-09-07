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

        /// <summary>Rounds a Duelist's Oath has been carried. It shatters on the fourth.</summary>
        public int OathCarried;

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
    /// A lobby that has already been made: the hall, the hero's pool, the seven delvers, the
    /// offer they were dealt, and how far the generator got while doing all that.
    /// </summary>
    /// <remarks>
    /// Making one spends most of its randomness on the rivals' APPEARANCE, which belongs with
    /// the wardrobe rather than here, so a match is handed a finished lobby the way a fight is
    /// handed a finished pack. <see cref="SetupDraws"/> is what lets the match pick the stream
    /// up where making the lobby left it.
    /// </remarks>
    public sealed class VersusLobby
    {
        public int Hall = 1;

        /// <summary>What both sides open on: half the hero's levelled delve pool.</summary>
        public int HeroPool = 50;

        public List<Rival> Rivals = new List<Rival>();

        /// <summary>The offer dealt before the first pick, which setting up already paid for.</summary>
        public List<RelicId> OpeningOffer = new List<RelicId>();

        /// <summary>Numbers the setup drew, which the match skips past.</summary>
        public int SetupDraws;
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

        /// <summary>Relics the bazaar puts on the shelf.</summary>
        public const int ShopChoices = 5;

        /// <summary>List prices, before a Merchant's Thumb shaves a fifth off them.</summary>
        public const int BuyPrice = 40;

        public const int AwakenPrice = 60;

        /// <summary>Rounds a Duelist's Oath lasts before it shatters.</summary>
        public const int OathLasts = 4;

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


        /// <summary>What the bazaar puts on the shelf: five relics from the versus pool.</summary>
        public static List<RelicId> ShopOffer(VersusMatch match, Mulberry32 rng)
        {
            return Draw(match, ShopChoices, rng);
        }

        /// <summary>The relics on offer, drawn without repeats and weighted against duplicates.</summary>
        private static List<RelicId> Draw(VersusMatch match, int want, Mulberry32 rng)
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

            var offer = new List<RelicId>(want);
            int take = Math.Min(want, avail.Count);
            int guard = 0;

            while (offer.Count < take && guard++ < 200)
            {
                RelicId id = RelicDraft.Weighted(avail, match.Items, rng);
                if (!offer.Contains(id)) offer.Add(id);
            }

            return offer;
        }

        /// <summary>What a counter charges. A Merchant's Thumb shaves a fifth off it.</summary>
        public static int PriceOf(VersusMatch match, int list)
        {
            double price = list * (match.Count(RelicId.MerchantsThumb) > 0 ? 0.8 : 1.0);
            return JsMath.RoundToInt(price);
        }

        /// <summary>A Debt of Flesh pays out in health whenever gold leaves the purse.</summary>
        private static void SpendHeal(VersusMatch match)
        {
            if (match.Count(RelicId.DebtOfFlesh) == 0) return;

            Dictionary<RelicId, int> awake = match.AwakenedIds();
            int copies;
            int amount = awake.TryGetValue(RelicId.DebtOfFlesh, out copies) && copies > 0 ? 20 : 10;
            match.Php = Math.Min(match.Pmax, match.Php + amount);
        }

        /// <summary>The hero, as a duellist.</summary>
        private static DuelSide HeroSide(VersusMatch match)
        {
            var side = new DuelSide
            {
                Name = "you",
                IsHero = true,
                Drop = WinBonus,
                Items = new List<RelicId>(match.Items),

                // Arena parity: the pool is the hero's own, and everything else is the five
                // attack every delver opens with, whatever level they walked in at.
                BaseAtk = 5,
                BaseDef = match.Hero.BaseDef,
                BaseSpd = match.Hero.BaseSpd,
                BaseLck = match.Hero.BaseLck,

                Php = match.Php,
                Pmax = match.Pmax,
                Gold = match.Gold,
                Kills = match.Hero.Kills,
                Adrenaline = match.Hero.Adrenaline,
                MidasBonus = match.Hero.MidasBonus,
                AtkBonus = match.Hero.AtkBonus,
                DefBonus = match.Hero.DefBonus,
                SpdBonus = match.Hero.SpdBonus,
                LuckBonus = match.Hero.LuckBonus,
                AnvilBonus = match.Hero.AnvilBonus,
                DebtLeft = match.Hero.DebtLeft,
                GlassBroken = match.Hero.GlassBroken,
                SoilUsed = match.Hero.SoilUsed,
                FleshSetApplied = match.Hero.FleshSetApplied,
                StrikeTotal = match.Hero.StrikeTotal,
            };

            foreach (KeyValuePair<RelicId, int> awake in match.AwakenedIds())
            {
                if (awake.Value > 0) side.Awakened.Add(awake.Key);
            }

            return side;
        }

        /// <summary>
        /// The rival, as a duellist: a fixed statline, the relics they have drafted, and
        /// whatever their last duel left them carrying.
        /// </summary>
        private static DuelSide RivalSide(Rival rival, EnemyState foe)
        {
            DuelSide carry = rival.Carry;
            int pool = Math.Max(foe.Hp, carry != null ? carry.Pmax : 0);

            return new DuelSide
            {
                Name = rival.Name,
                IsHero = false,
                Drop = foe.Drop,
                SpeciesIndex = foe.SpeciesIndex,
                Variant = foe.Variant,
                Rank = foe.Rank,
                Items = new List<RelicId>(rival.Relics),

                BaseAtk = rival.Base.Atk,
                BaseDef = rival.Base.Def,
                BaseSpd = rival.Base.Spd,
                BaseLck = rival.Base.Lck,

                // The pool is derived from this round's scaling, but never drops below what the
                // rival was already carrying.
                Php = pool,
                Pmax = pool,

                Gold = carry != null ? carry.Gold : 0,
                Kills = carry != null ? carry.Kills : 0,
                Adrenaline = carry != null ? carry.Adrenaline : 0,
                AnvilBonus = carry != null ? carry.AnvilBonus : 0,
                DebtLeft = carry != null ? carry.DebtLeft : 0,
                GlassBroken = carry != null && carry.GlassBroken,
                SoilUsed = carry != null && carry.SoilUsed,
                FleshSetApplied = carry != null && carry.FleshSetApplied,
                StrikeTotal = carry != null ? carry.StrikeTotal : 0,
            };
        }

        /// <summary>
        /// Writes a finished duel back into the match.
        /// </summary>
        /// <remarks>
        /// Health is clamped to the pool the hero went IN with, not the one they came out with.
        /// A Bone Chalice grows the pool mid-fight, and the source tracks health against the old
        /// ceiling the whole way through before raising it at the end — so a fight that grew the
        /// pool does not also fill it.
        ///
        /// The source also carries a Bone Chalice's growth across a duel. That relic is cut, so
        /// a duellist has nowhere to hold it and nothing to put there.
        /// </remarks>
        private static void Commit(VersusMatch match, DuelSide side, CombatResult result, int poolBefore)
        {
            // The source reads health off the last event's snapshot rather than the side. The
            // two agree here — every change to health emits one — so this takes the simpler
            // route, and the clamp is what actually matters.
            match.Php = Math.Min(side.Php, poolBefore);
            match.Gold = side.Gold;
            match.Pmax = side.Pmax;

            match.Hero.Adrenaline = side.Adrenaline;
            match.Hero.Kills = side.Kills;
            match.Hero.DefBonus = side.DefBonus;
            match.Hero.StrikeTotal = side.StrikeTotal;
            match.Hero.FleshSetApplied = side.FleshSetApplied;

            // Five things a duel changed are deliberately NOT written back: the Anvil Heart's
            // own counter, a Debtor's Chain's balance, a shattered Glass Edge, a spent
            // Gravekeeper's Soil, and a Bone Chalice's growth.
            //
            // That is not a simplification, it is the source's behaviour. It writes them back
            // correctly and then overwrites them from a snapshot taken BEFORE the fight, so in
            // versus they never advance. It shows plainly in the corpus: an Anvil Heart hands
            // out its defence round after round while its counter reads zero, which means the
            // cap of ten it is supposed to stop at never arrives. Reproduced because it is what
            // the recording says, and flagged here because it is a bug rather than a rule.
        }

        /// <summary>The stretch between two rounds: the pool grows, and the hero is made whole.</summary>
        private static void BetweenRounds(VersusMatch match)
        {
            match.Pmax = JsMath.RoundToInt(match.Pmax * PoolGrowth);

            // An oath is sworn for four rounds, and then it is spent.
            if (match.Count(RelicId.DuelistsOath) > 0 &&
                !match.AwakenedIds().ContainsKey(RelicId.DuelistsOath))
            {
                match.OathCarried++;
                if (match.OathCarried >= OathLasts) match.Items.Remove(RelicId.DuelistsOath);
            }

            // The arena heals whole between rounds, however deep the last one cut.
            match.Php = match.Pmax;
            match.Round++;

            // A Blood Pact re-signs every round, so a full heal cannot wash it away.
            if (match.Count(RelicId.BloodPact) > 0)
            {
                int due = Math.Max(1, JsMath.RoundToInt(match.Pmax * 0.2));
                match.Php = Math.Max(1, match.Php - due);
            }
        }

        /// <summary>Plays a match out of a lobby that has already been made.</summary>
        public static VersusMatch Resolve(uint seed, VersusLobby lobby, IVersusChoices choices,
            IVersusObserver observer = null)
        {
            if (lobby == null) throw new ArgumentNullException(nameof(lobby));
            if (choices == null) throw new ArgumentNullException(nameof(choices));

            var match = new VersusMatch { Hall = lobby.Hall, Round = 1 };
            match.Hero.Php = lobby.HeroPool;
            match.Hero.Pmax = lobby.HeroPool;
            match.Roster.AddRange(lobby.Rivals);

            var rng = new Mulberry32(seed);
            rng.Skip(lobby.SetupDraws);

            // The opening draft is three picks rather than one, and its first offer arrived with
            // the lobby: it was dealt before the roster was, so it belongs to setting up.
            List<RelicId> offer = new List<RelicId>(lobby.OpeningOffer);
            for (int pick = 0; pick < OpeningPicks; pick++)
            {
                Draft(match, offer, choices, observer);
                if (pick < OpeningPicks - 1) offer = Draw(match, DraftChoices, rng);
            }

            EnemyState foe = NextFoe(match, match.Round, rng);
            RunEnding how = RunEnding.Died;

            while (true)
            {
                HallFor(match, match.Foe);

                Rival rival = match.Roster[match.Foe];
                DuelSide hero = HeroSide(match);
                DuelSide other = RivalSide(rival, foe);
                int poolBefore = match.Pmax;

                CombatResult fight = new DuelEngine().Resolve(hero, other, rng);
                Commit(match, hero, fight, poolBefore);
                rival.Carry = other;

                bool won = match.Php > 0;
                if (won)
                {
                    rival.Lives--;
                }
                else
                {
                    // A loss pays a consolation purse and costs a life.
                    match.Gold += RoundStipend * match.Round;
                    match.Lives--;
                    if (match.Lives <= 0)
                    {
                        if (observer != null) observer.Round(match, match.Foe, false);
                        break;
                    }

                    match.Php = match.Pmax;
                }

                Bracket(match, rng);
                RivalsDraft(match, match.Round, rng);

                // The crown is paid before the round is done, so a match that ends here ends
                // with it already in the purse.
                bool crowned = AliveRivals(match) == 0;
                if (crowned) match.Gold += CrownPurse;

                if (observer != null) observer.Round(match, match.Foe, won);

                if (crowned)
                {
                    how = RunEnding.Cleared;
                    break;
                }

                // Next round's rival is chosen HERE, at the end of this one, which is why the
                // rival met after a bazaar was scaled for the round the bazaar took.
                foe = NextFoe(match, match.Round + 1, rng);

                BetweenRounds(match);

                if (match.Round == BazaarRound)
                {
                    Bazaar(match, rng, choices, observer);
                    BetweenRounds(match);
                }

                Draft(match, Draw(match, DraftChoices, rng), choices, observer);
            }

            if (observer != null) observer.End(match, how);
            return match;
        }

        private static void Draft(VersusMatch match, List<RelicId> offer, IVersusChoices choices,
            IVersusObserver observer)
        {
            RelicId pick = choices.Draft(match, offer);
            Pickup.Take(match.Hero, match.Items, pick);
            if (observer != null) observer.Draft(match, offer, pick);
        }

        /// <summary>One deal at the counter, or none.</summary>
        private static void Bazaar(VersusMatch match, Mulberry32 rng, IVersusChoices choices,
            IVersusObserver observer)
        {
            List<RelicId> offer = ShopOffer(match, rng);
            List<int> awakenable = match.Awakenable();
            VersusDeal deal = choices.Bazaar(match, offer, awakenable);

            if (deal.Kind == VersusDealKind.Buy && offer.Contains(deal.Relic) &&
                match.Gold >= PriceOf(match, BuyPrice))
            {
                match.Gold -= PriceOf(match, BuyPrice);
                SpendHeal(match);
                Pickup.Take(match.Hero, match.Items, deal.Relic);
            }
            else if (deal.Kind == VersusDealKind.Awaken && awakenable.Contains(deal.Slot) &&
                     match.Gold >= PriceOf(match, AwakenPrice))
            {
                match.Gold -= PriceOf(match, AwakenPrice);
                SpendHeal(match);
                match.AwakenedSlots.Add(deal.Slot);

                // An awakened Hollow Idol gives back the pool it took.
                if (match.Items[deal.Slot] == RelicId.HollowIdol)
                {
                    match.Pmax += 15;
                    match.Php = Math.Min(match.Pmax, match.Php + 15);
                }
            }

            if (observer != null) observer.Bazaar(match, offer, awakenable, deal);
        }

        private static int AliveRivals(VersusMatch match)
        {
            int alive = 0;
            for (int i = 0; i < match.Roster.Count; i++)
            {
                if (match.Roster[i].Lives > 0) alive++;
            }

            return alive;
        }

    }
}
