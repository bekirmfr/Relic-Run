using System;
using System.Collections.Generic;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;

namespace RelicRun.Core.Run
{
    /// <summary>What the bazaar was asked to do.</summary>
    public enum DealKind
    {
        None = 0,
        Awaken = 1,
        Buy = 2,
    }

    /// <summary>One piece of business at the bazaar. A visit may hold more than one.</summary>
    public struct BazaarDeal
    {
        public DealKind Kind;

        /// <summary>The relic being bought.</summary>
        public RelicId Relic;

        /// <summary>The inventory slot being awakened — the copy, not the relic.</summary>
        public int Slot;

        public static readonly BazaarDeal Walk = new BazaarDeal { Kind = DealKind.None };

        public static BazaarDeal Awaken(int slot)
        {
            return new BazaarDeal { Kind = DealKind.Awaken, Slot = slot };
        }

        public static BazaarDeal Buy(RelicId id)
        {
            return new BazaarDeal { Kind = DealKind.Buy, Relic = id };
        }
    }

    /// <summary>
    /// Everything a run asks of whoever is playing it.
    /// </summary>
    /// <remarks>
    /// A player answers these through the UI; a test answers them from a recording. Neither is
    /// a rule, which is why none of it lives in the run itself.
    /// </remarks>
    public interface IRunChoices
    {
        /// <summary>Which of an event's choices to take.</summary>
        int Event(RunState run, DungeonEvent ev);

        /// <summary>Whether to pay for a fresh offer. Asked again after each reroll.</summary>
        bool Reroll(RunState run, IReadOnlyList<RelicId> offer, int price);

        /// <summary>Which relic to take.</summary>
        RelicId Draft(RunState run, IReadOnlyList<RelicId> offer);

        /// <summary>
        /// What to do with the bazaar's shelf, asked once per deal the visit allows. Awakenable
        /// slots are inventory indices; returning <see cref="BazaarDeal.Walk"/> ends the visit.
        /// </summary>
        BazaarDeal Bazaar(RunState run, IReadOnlyList<RelicId> offer, IReadOnlyList<int> awakenable);

        /// <summary>
        /// Whether to be brought back. Asked once per run, the moment the hero falls; what it
        /// costs — sparks, an advertisement, nothing at all — is the caller's business.
        /// </summary>
        bool Revive(RunState run, int floor);

        /// <summary>Whether to walk out at this gate rather than descend.</summary>
        bool CashOut(RunState run, int floor);
    }

    /// <summary>Told what a run did, as it happens.</summary>
    public interface IRunObserver
    {
        void Event(RunState run, int floor, int index, int choice);

        /// <summary>
        /// One piece of bazaar business, after it has been done — and once more with
        /// <see cref="BazaarDeal.Walk"/> as the delver leaves, whatever they bought.
        /// </summary>
        void Deal(RunState run, int floor, IReadOnlyList<RelicId> offer,
            IReadOnlyList<int> awakenable, BazaarDeal deal);

        void Draft(RunState run, int floor, IReadOnlyList<RelicId> offer, int rerolls, RelicId pick);

        void Fight(RunState run, int floor, IReadOnlyList<EnemyState> pack, CombatResult result);

        void Revived(RunState run, int floor);

        void End(RunState run, RunEnding ending, int floor);
    }

    /// <summary>How a run is set up before the first floor.</summary>
    /// <remarks>
    /// The shipped game derives every one of these from the delver's LEVEL, which is why
    /// <see cref="ForLevel"/> exists and why nothing else needs to know the progression table.
    /// The fields stay open so a test can state a run's shape instead of hunting for a level
    /// that produces it.
    /// </remarks>
    public sealed class RunSetup
    {
        public int Hp = 100;
        public int Gold;
        public int Atk = 5;
        public int Def;
        public int Spd = 25;
        public int Lck = 10;

        /// <summary>Health restored between floors.</summary>
        public int Breath = 5;

        /// <summary>Relics offered at each draft. Two, and a third from level ten.</summary>
        public int DraftChoices = 2;

        /// <summary>Pieces of business one bazaar visit allows. One, and a second from level fifteen.</summary>
        public int BazaarDeals = 1;

        /// <summary>Which dungeon is being delved. Its boss carries that hall's relics.</summary>
        public DungeonConfig Dungeon;

        /// <summary>Relics the hero starts holding.</summary>
        public IReadOnlyList<RelicId> StartKit = new List<RelicId>();

        /// <summary>The run a delver of this level starts.</summary>
        public static RunSetup ForLevel(int level)
        {
            LevelBonuses b = Progression.Bonuses(level);
            return new RunSetup
            {
                Hp = 100 + b.Hp,
                Gold = b.Gold,
                Atk = 5 + b.Atk,
                Def = b.Def,
                Spd = 25 + b.Spd,
                Lck = 10 + b.Lck,
                Breath = 5 + b.Breath,
                DraftChoices = 2 + b.DraftChoices,
                BazaarDeals = 1 + b.BazaarDeals,
            };
        }
    }

    /// <summary>
    /// A delve, floor by floor.
    /// </summary>
    /// <remarks>
    /// A floor is drafted, fought, and then walked out of: the draft, the fight, the event that
    /// may sit in the gap beyond it, and the breather at the next gate. The order matters more
    /// than it looks — the pack for the floor AHEAD is rolled the moment a fight ends, before
    /// that floor's offer is drawn — and it is the order the shipped game walks, which is not
    /// the order its Balance Lab walks.
    ///
    /// Two random streams, never mixed. The fight stream draws packs, offers and combat; the
    /// EVENT stream places the between-floor events and rolls their gambles. Keeping them apart
    /// is what makes a run's events the same whatever happens in its fights, and it is why the
    /// event seed is derived from the run seed rather than taken from the same generator.
    ///
    /// A paid reroll draws its replacement offer from the EVENT stream. That is the source's,
    /// not a tidy-up: rerollDraft passes the event generator where every other offer passes
    /// none. It means paying for a reroll shifts the events that follow.
    /// </remarks>
    public static class DelveRun
    {
        public const int MaxFloor = 13;
        public const int BazaarFloor = 7;

        /// <summary>The bazaar's two prices, before a Merchant's Thumb.</summary>
        public const int AwakenPrice = 60;
        public const int BuyPrice = 40;
        public const int BazaarChoices = 5;

        /// <summary>What the Hoard-King's floor pays out on top of his drop.</summary>
        public const int ClearPurse = 50;

        /// <summary>Floors a Duelist's Oath survives before it shatters.</summary>
        public const int OathLasts = 4;

        /// <summary>Floors an event can land in the gap beyond.</summary>
        private static readonly int[] EventGaps = { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

        /// <summary>
        /// How far an event may drag speed down before the floor holds. The Ghost's locket and
        /// the Spike Trap cost five each and a failed robbery three more, so it is reached.
        /// </summary>
        /// <remarks>
        /// The source floors defence at -2 as well. That is not ported: only the unclaimed
        /// chest costs any defence, one point, and no event repeats within a run, so nothing
        /// can reach -2 and the clamp could never fire. NoRunCanLoseEnoughDefenceToNeedAFloor
        /// asserts that is still true, so adding an event that costs defence says so rather
        /// than quietly removing a guard that used to be there.
        /// </remarks>
        internal const int MinSpdBonus = -10;

        /// <summary>Derives the event stream's seed from the run's. Knuth's golden ratio.</summary>
        public const uint EventSeedMix = 0x9E3779B9;

        /// <param name="combat">
        /// The fight's rules. Defaults to what the game ships; the corpus gate passes
        /// <see cref="CombatRules.DelveAsRecorded"/>, because a recorded run was fought before
        /// an awakened Hollow Idol started backing the family the delver leans on.
        /// </param>
        public static RunState Resolve(uint seed, RunSetup setup, IRunChoices choices,
            IRunObserver observer = null, RunRules rules = null, CombatRules combat = null)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            if (choices == null) throw new ArgumentNullException(nameof(choices));

            var delve = new Delve(seed, setup, observer, rules, combat);

            while (!delve.Finished)
            {
                delve.Answer(delve.Pending.AskedOf(choices, delve.State));
            }

            return delve.State;
        }

        /// <summary>
        /// The breather taken at a gate, which a Second Stomach makes count double.
        /// </summary>
        /// <remarks>
        /// There is no breather before the first floor, because there is no gate before it. An
        /// awakened Ox Heart adds two on top, and an awakened Second Stomach grows the pool by
        /// one every time — the only relic in the game that gets bigger for walking.
        /// </remarks>
        public static int Breather(RunSetup setup, RunState run)
        {
            int amount = setup.Breath;
            for (int i = run.Count(RelicId.SecondStomach); i > 0; i--) amount *= 2;
            if (run.Has(RelicId.OxHeart) && run.IsAwake(RelicId.OxHeart)) amount += 2;
            return amount;
        }

        /// <summary>
        /// What an awakened Second Stomach makes of a breather that would have been wasted.
        /// </summary>
        /// <remarks>
        /// A doubled breather is worth nothing to a delver who is already close to full, and
        /// worth less the more Stomachs they hold — the relic gets weaker exactly as you invest
        /// in it. Awakened, the surplus is not thrown away: it stretches the pool instead, by a
        /// point per copy held, and the breather then fills what it just made.
        ///
        /// The source's own answer here was a flat point of max HP at every gate, awake or not,
        /// and it could never fire: a Second Stomach does not stack there, and the bazaar only
        /// wakes what stacks. This is the shipped rule that takes its place, and it is why the
        /// relic is worth a second copy.
        /// </remarks>
        public static int Stretch(RunState run, int breath, int room)
        {
            if (!run.Has(RelicId.SecondStomach) || !run.IsAwake(RelicId.SecondStomach)) return 0;

            int wasted = breath - room;
            if (wasted <= 0) return 0;
            return Math.Min(wasted, run.Count(RelicId.SecondStomach));
        }

        /// <summary>How many pieces of business one visit allows.</summary>
        /// <remarks>
        /// Read BEFORE a deal is applied, which is what stops a visit from awakening a
        /// Merchant's Thumb and then spending the second deal that awakening would have paid
        /// for. The Thumb has to already be awake when the delver walks in.
        ///
        /// Which, in the source, it never can be: a Thumb does not stack, and the bazaar only
        /// wakes what stacks — so the awakened half of this is a rule that exists and can never
        /// fire, exactly like the Debt of Flesh's awakened rate before the port let it stack.
        /// See <see cref="RunRules.Stacks"/>.
        /// </remarks>
        public static int DealsAllowed(RunState run, RunSetup setup)
        {
            bool thumb = run.Has(RelicId.MerchantsThumb) && run.IsAwake(RelicId.MerchantsThumb);
            return (thumb ? 2 : 1) + (setup.BazaarDeals - 1);
        }

        /// <summary>
        /// Scatters two to four events through the gaps beyond floors 2 to 11.
        /// </summary>
        /// <remarks>
        /// Both shuffles and the count come off the event stream in this order, before a single
        /// floor is walked, so where the events land never depends on how the fights go.
        /// </remarks>
        public static Dictionary<int, int> PlaceEvents(Mulberry32 events)
        {
            int[] gaps = Shuffle((int[])EventGaps.Clone(), events);

            var indices = new int[DungeonEvents.All.Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            indices = Shuffle(indices, events);

            int count = 2 + (int)Math.Floor(events.Next() * 3);

            var placed = new Dictionary<int, int>();
            for (int i = 0; i < count; i++) placed[gaps[i]] = indices[i];
            return placed;
        }

        /// <summary>A Fisher-Yates shuffle, walked from the top down as the source walks it.</summary>
        private static int[] Shuffle(int[] a, Mulberry32 rng)
        {
            for (int i = a.Length - 1; i > 0; i--)
            {
                int j = (int)Math.Floor(rng.Next() * (i + 1));
                int t = a[i];
                a[i] = a[j];
                a[j] = t;
            }

            return a;
        }

        internal static List<RelicId> RollOffer(RunState run, int choices, Mulberry32 rng)
        {
            return RelicDraft.RollOffer(run.Items, choices, rng, run.Rules, GameModes.Delve);
        }

        /// <summary>
        /// The gate at the end of a floor: an oath spent, a breather taken, and the next floor.
        /// </summary>
        internal static void Gate(RunState run, RunSetup setup)
        {
            // Read once, before the oath shatters. Slots are positional, so losing a relic out
            // of the middle of the tray moves every awakening behind it — and the source reads
            // the awakened map before that can happen.
            bool oathAwake = run.IsAwake(RelicId.DuelistsOath);
            bool stomachAwake = run.IsAwake(RelicId.SecondStomach);
            int breath = Breather(setup, run);

            // The oath is sworn for four floors and no more.
            if (run.Has(RelicId.DuelistsOath) && !oathAwake)
            {
                run.OathCarried++;
                if (run.OathCarried >= OathLasts)
                {
                    run.Drop(RelicId.DuelistsOath);
                }
            }

            Rest(run, breath, stomachAwake);
            run.Floor++;
        }

        /// <summary>
        /// The rest itself: stretch what would spill, then take the breather.
        /// </summary>
        /// <remarks>
        /// Apart from <see cref="Gate"/> so the arithmetic can be asked directly. The ORDER is
        /// the whole of it — the room is read before the stretch, or the stretch would be
        /// measuring the space it just made and every gate would grow the pool.
        /// </remarks>
        public static void Rest(RunState run, int breath, bool stomachAwake)
        {
            if (stomachAwake) run.Pmax += Stretch(run, breath, run.Pmax - run.Php);

            run.BreathHealed = Math.Min(breath, run.Pmax - run.Php);
            run.Php += run.BreathHealed;

            if (run.Php > run.Pmax)
            {
                run.BreathHealed -= run.Php - run.Pmax;
                run.Php = run.Pmax;
            }
        }

        /// <summary>What a counter charges. A Merchant's Thumb shaves a fifth off it.</summary>
        public static int PriceOf(RunState run, int list)
        {
            double price = list * (run.Has(RelicId.MerchantsThumb) ? run.Rules.ThumbDiscount : 1.0);
            return JsMath.RoundToInt(price);
        }

        /// <summary>What the next reroll costs: it doubles each time, and a Thumb shaves a fifth.</summary>
        public static int RerollPrice(RunState run)
        {
            double price = 10 * Math.Pow(2, run.Rerolls);
            if (run.Has(RelicId.MerchantsThumb)) price *= run.Rules.ThumbDiscount;
            return JsMath.RoundToInt(price);
        }

        /// <summary>
        /// What a Debt of Flesh pays out when gold leaves the purse at a counter, in health.
        /// </summary>
        /// <remarks>
        /// The awakened rate is unreachable in the source: a relic is only awakened at the
        /// bazaar, which offers none that do not stack, and there a Debt of Flesh does not. It
        /// stacks under this port's own rules, which is what makes the awakened half real —
        /// see <see cref="RunRules"/>.
        /// </remarks>
        public static int DebtPayment(RunState run)
        {
            if (!run.Has(RelicId.DebtOfFlesh)) return 0;
            return run.IsAwake(RelicId.DebtOfFlesh) ? 20 : 10;
        }

        /// <summary>
        /// The purse for taking the Hoard-King, paid the moment the last floor is survived.
        /// </summary>
        /// <remarks>
        /// It lands with the fight rather than with the score, which is why a delver who dies on
        /// floor thirteen and is brought back still collects it: the crown is paid for clearing
        /// the floor, not for the manner of it.
        /// </remarks>
        internal static void Crown(RunState run)
        {
            if (run.Php > 0 && run.Floor >= MaxFloor) run.Gold += ClearPurse;
        }

        internal static void PayTheDebt(RunState run)
        {
            run.Php = Math.Min(run.Pmax, run.Php + DebtPayment(run));
        }
    }
}
