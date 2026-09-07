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
        private const int MinSpdBonus = -10;

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

            var run = new RunState();
            run.Rules = rules ?? RunRules.Shipped();
            combat = combat ?? CombatRules.Delve();
            run.Hero.Php = setup.Hp;
            run.Hero.Pmax = setup.Hp;
            run.Hero.Gold = setup.Gold;
            run.Hero.BaseAtk = setup.Atk;
            run.Hero.BaseDef = setup.Def;
            run.Hero.BaseSpd = setup.Spd;
            run.Hero.BaseLck = setup.Lck;
            run.Items.AddRange(setup.StartKit);
            run.Floor = 1;

            var rng = new Mulberry32(seed);
            var events = new Mulberry32(seed ^ EventSeedMix);
            Dictionary<int, int> placed = PlaceEvents(events);
            var seen = new HashSet<int>();

            // The opening offer is drawn before the first floor is walked, as the run is made.
            List<RelicId> offer = RollOffer(run, setup.DraftChoices, rng);
            List<EnemyState> nextPack = null;
            RunEnding ending = RunEnding.Died;

            while (true)
            {
                Draft(run, setup, offer, rng, events, choices, observer);

                IReadOnlyList<EnemyState> pack =
                    nextPack ?? EnemyPackGenerator.Build(run.Floor, rng, setup.Dungeon);
                nextPack = null;

                bool fell = Fight(run, pack, rng, choices, observer, setup, combat);
                if (fell) break;

                if (run.Floor >= MaxFloor)
                {
                    ending = RunEnding.Cleared;
                    break;
                }

                nextPack = EnemyPackGenerator.Build(run.Floor + 1, rng, setup.Dungeon);

                if (choices.CashOut(run, run.Floor))
                {
                    ending = RunEnding.CashedOut;
                    break;
                }

                // Walking out of a floor: the event in the gap, the gate's breather, and the
                // bazaar, which is a gate of its own and is walked out of the same way.
                while (true)
                {
                    Gap(run, placed, seen, events, choices, observer);
                    Gate(run, setup);

                    if (run.Floor != BazaarFloor) break;

                    Bazaar(run, rng, choices, observer, setup);
                    if (run.Rules.BazaarRollsItsOwnPack)
                    {
                        nextPack = EnemyPackGenerator.Build(run.Floor + 1, rng, setup.Dungeon);
                    }
                }

                offer = RollOffer(run, setup.DraftChoices, rng);
            }

            if (observer != null) observer.End(run, ending, run.Floor);
            return run;
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

        private static List<RelicId> RollOffer(RunState run, int choices, Mulberry32 rng)
        {
            return RelicDraft.RollOffer(run.Items, choices, rng, run.Rules, GameModes.Delve);
        }

        /// <summary>The event waiting in the gap beyond a floor, if one was placed there.</summary>
        private static void Gap(RunState run, Dictionary<int, int> placed, HashSet<int> seen,
            Mulberry32 events, IRunChoices choices, IRunObserver observer)
        {
            int index;
            if (!placed.TryGetValue(run.Floor, out index) || !seen.Add(run.Floor)) return;

            DungeonEvent ev = DungeonEvents.Get(index);
            int choice = choices.Event(run, ev);

            // A counter is a counter: an outcome that costs gold pays the Debt of Flesh, the
            // same as the bazaar and the reroll ladder do.
            int purse = run.Gold;
            ev.Choices[choice].Resolve(run, events);
            if (run.Gold < purse) PayTheDebt(run);

            // An event can hurt, but never kill: the floor at one is what makes the Spike Trap
            // a scare rather than an ending, and it is why the run loop asks nothing about
            // health here. Only a fight can end a delve.
            //
            // The source floors the purse at zero here too. That is not ported: an outcome that
            // costs gold declares the cost, and a choice whose cost the purse cannot cover is
            // never offered, so nothing can spend a delver into the red.
            // NoEventCanSpendMoreGoldThanItAsksFor asserts that is still true.
            run.Php = Math.Max(1, Math.Min(run.Pmax, run.Php));
            run.Hero.SpdBonus = Math.Max(MinSpdBonus, run.Hero.SpdBonus);

            if (observer != null) observer.Event(run, run.Floor, index, choice);
        }

        /// <summary>
        /// The gate at the end of a floor: an oath spent, a breather taken, and the next floor.
        /// </summary>
        private static void Gate(RunState run, RunSetup setup)
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

        /// <summary>
        /// The bazaar: a shelf of five, a list of copies that could be woken, and as many
        /// pieces of business as the visit allows. No fight happens on this floor.
        /// </summary>
        /// <remarks>
        /// One deal a visit, two with an AWAKENED Merchant's Thumb — the unawakened one only
        /// discounts — and one more again for a delver past level fifteen. A discount is read
        /// from merely holding a Thumb, and both prices take it.
        /// </remarks>
        private static void Bazaar(RunState run, Mulberry32 rng, IRunChoices choices,
            IRunObserver observer, RunSetup setup)
        {
            List<RelicId> offer = RelicDraft.RollOffer(
                run.Items, BazaarChoices, rng, run.Rules, GameModes.Delve);

            // Which copies could be woken is settled on the way in, and stays settled: buying
            // something on the first deal does not put it on the awakening shelf.
            List<int> awakenable = run.Awakenable();
            int done = 0;

            while (true)
            {
                BazaarDeal deal = choices.Bazaar(run, offer, awakenable);
                if (deal.Kind == DealKind.None) break;

                int allowed = DealsAllowed(run, setup);

                if (deal.Kind == DealKind.Awaken)
                {
                    // Only a copy the shelf actually carried. The source checks the slot holds
                    // something and is not already awake, and leans on the UI for the rest —
                    // which is fine until something that is not the UI asks.
                    if (!awakenable.Contains(deal.Slot)) break;

                    int price = PriceOf(run, AwakenPrice);
                    if (run.Gold < price) break;

                    RelicId woken = run.Items[deal.Slot];
                    done++;
                    run.Gold -= price;
                    PayTheDebt(run);
                    run.Awaken(deal.Slot);

                    // The shelf closes: one awakening a visit, whatever else the visit allows.
                    awakenable = new List<int>();

                    // The idol fills: it gave up fifteen of the pool to join every set, and
                    // waking it hands them back. Unreachable in the source for the same reason
                    // as the Thumb and the Stomach above — a Hollow Idol does not stack.
                    if (woken == RelicId.HollowIdol)
                    {
                        run.Pmax += 15;
                        run.Php = Math.Min(run.Pmax, run.Php + 15);
                    }
                }
                else
                {
                    int price = PriceOf(run, BuyPrice);
                    if (run.Gold < price || !offer.Contains(deal.Relic)) break;

                    done++;
                    run.Gold -= price;
                    PayTheDebt(run);
                    Pickup.Take(run, deal.Relic);

                    // A relic that does not stack leaves the shelf; one that does stays.
                    if (!run.Rules.Stacks(deal.Relic)) offer.RemoveAll(id => id == deal.Relic);
                }

                if (observer != null) observer.Deal(run, run.Floor, offer, awakenable, deal);
                if (done >= allowed) break;
            }

            // The walk-out closes the visit, so a watcher sees where the delver left it — the
            // shelf they did not clear as much as the deals they took.
            if (observer != null)
            {
                observer.Deal(run, run.Floor, offer, awakenable, BazaarDeal.Walk);
            }
        }

        /// <summary>What a counter charges. A Merchant's Thumb shaves a fifth off it.</summary>
        public static int PriceOf(RunState run, int list)
        {
            double price = list * (run.Has(RelicId.MerchantsThumb) ? run.Rules.ThumbDiscount : 1.0);
            return JsMath.RoundToInt(price);
        }

        /// <summary>The draft, and however many rerolls are paid for.</summary>
        private static void Draft(RunState run, RunSetup setup, List<RelicId> offer,
            Mulberry32 rng, Mulberry32 events, IRunChoices choices, IRunObserver observer)
        {
            int rerolls = 0;
            int floor = run.Floor;

            while (true)
            {
                int price = RerollPrice(run);
                if (run.Gold < price || !choices.Reroll(run, offer, price)) break;

                run.Gold -= price;
                run.Rerolls++;
                rerolls++;
                PayTheDebt(run);

                // Not a slip: the shipped reroll draws its replacement from the EVENT stream.
                offer.Clear();
                offer.AddRange(RelicDraft.RollOffer(
                    run.Items, setup.DraftChoices, events, run.Rules, GameModes.Delve));
            }

            RelicId pick = choices.Draft(run, offer);
            Pickup.Take(run, pick);

            if (observer != null) observer.Draft(run, floor, offer, rerolls, pick);
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
        private static void Crown(RunState run)
        {
            if (run.Php > 0 && run.Floor >= MaxFloor) run.Gold += ClearPurse;
        }

        private static void PayTheDebt(RunState run)
        {
            run.Php = Math.Min(run.Pmax, run.Php + DebtPayment(run));
        }

        /// <summary>
        /// The floor's fight, and the one chance to be brought back. True if the hero stayed down.
        /// </summary>
        private static bool Fight(RunState run, IReadOnlyList<EnemyState> pack, Mulberry32 rng,
            IRunChoices choices, IRunObserver observer, RunSetup setup, CombatRules combat)
        {
            // Gold an earlier event promised for this floor arrives as the fight opens.
            if (run.Pending.HasValue && run.Pending.Value.Floor == run.Floor)
            {
                run.Gold += run.Pending.Value.Gold;
                run.Pending = null;
            }

            int ceiling = run.Pmax;

            // A floor may get its own copy of the Flesh set's flag, and never hand it back.
            bool fleshSet = run.Hero.FleshSetApplied;
            if (!run.Rules.FleshSetSurvivesTheFloor) run.Hero.FleshSetApplied = false;

            CombatResult result = new CombatEngine(combat).ResolveFloor(run.Hero, pack, rng);
            run.BreathHealed = 0;
            if (!run.Rules.FleshSetSurvivesTheFloor) run.Hero.FleshSetApplied = fleshSet;

            // Health is clamped to the ceiling the floor OPENED with. A Chalice that grew the
            // pool mid-fight raises the ceiling only once the fight is over, so the health it
            // bought does not arrive with it. That is the source's order, and it costs a
            // Bottomless Chalice its first floor of growth every time.
            run.Php = Math.Min(run.Php, ceiling);
            Crown(run);

            if (observer != null) observer.Fight(run, run.Floor, pack, result);
            if (run.Php > 0) return false;

            if (!run.Revived && choices.Revive(run, run.Floor))
            {
                Revive(run, pack, result, rng, observer, setup, combat);
                if (run.Php > 0) return false;
            }

            return true;
        }

        /// <summary>
        /// Brought back on half a pool, once per run, and set straight back into the fight that
        /// ended it.
        /// </summary>
        /// <remarks>
        /// The floor does not start again. The hero resumes against the foe that felled them,
        /// still carrying the wounds it took, and against whatever was behind it — with the
        /// floor's own carried state intact, so an Anvil Heart's bonus and a Sentinel's defence
        /// survive the death that interrupted them. A hero who fell to the last foe of the pack
        /// as it died gets a fresh pack instead, rolled for the same floor.
        /// </remarks>
        private static void Revive(RunState run, IReadOnlyList<EnemyState> pack,
            CombatResult fell, Mulberry32 rng, IRunObserver observer, RunSetup setup,
            CombatRules combat)
        {
            run.Revived = true;
            run.Php = Math.Max(1, (int)Math.Floor(run.Pmax / 2.0));

            var remaining = new List<EnemyState>();
            for (int i = fell.FoeIndex; i < pack.Count; i++) remaining.Add(pack[i]);

            // The foe that killed the hero keeps the wounds it took getting there.
            if (remaining.Count > 0) remaining[0].Hp = Math.Max(1, fell.FoeHp);
            else remaining.AddRange(EnemyPackGenerator.Build(run.Floor, rng, setup.Dungeon));

            if (observer != null) observer.Revived(run, run.Floor);

            int ceiling = run.Pmax;
            bool fleshSet = run.Hero.FleshSetApplied;
            if (!run.Rules.FleshSetSurvivesTheFloor) run.Hero.FleshSetApplied = false;

            run.Hero.Carry = fell.Carry;
            CombatResult result = new CombatEngine(combat).ResolveFloor(run.Hero, remaining, rng);
            run.Hero.Carry = null;
            if (!run.Rules.FleshSetSurvivesTheFloor) run.Hero.FleshSetApplied = fleshSet;
            run.Php = Math.Min(run.Php, ceiling);
            Crown(run);

            if (observer != null) observer.Fight(run, run.Floor, remaining, result);
        }
    }
}
