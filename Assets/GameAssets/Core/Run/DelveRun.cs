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

    /// <summary>One visit's business at the bazaar.</summary>
    public struct BazaarDeal
    {
        public DealKind Kind;
        public RelicId Relic;

        public static readonly BazaarDeal Walk = new BazaarDeal { Kind = DealKind.None };

        public static BazaarDeal Awaken(RelicId id)
        {
            return new BazaarDeal { Kind = DealKind.Awaken, Relic = id };
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

        /// <summary>What to do with the bazaar's offer.</summary>
        BazaarDeal Bazaar(RunState run, IReadOnlyList<RelicId> offer);

        /// <summary>
        /// Whether to be brought back. Asked once per run, the moment the hero falls; what it
        /// costs — sparks, an advertisement, nothing at all — is the caller's business.
        /// </summary>
        bool Revive(RunState run, int floor);
    }

    /// <summary>Told what a run did, as it happens.</summary>
    public interface IRunObserver
    {
        void Event(RunState run, int floor, int index, int choice);

        void Bazaar(RunState run, int floor, IReadOnlyList<RelicId> offer, BazaarDeal deal);

        void Draft(RunState run, int floor, IReadOnlyList<RelicId> offer, int rerolls, RelicId pick);

        void Fight(RunState run, int floor, IReadOnlyList<EnemyState> pack, CombatResult result);

        void Revived(RunState run, int floor);

        void End(RunState run, bool dead, int floor);
    }

    /// <summary>How a run is set up before the first floor.</summary>
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

        public int DraftChoices = 3;

        /// <summary>Relics the hero starts holding.</summary>
        public IReadOnlyList<RelicId> StartKit = new List<RelicId>();
    }

    /// <summary>
    /// A delve, floor by floor.
    /// </summary>
    /// <remarks>
    /// Two random streams, never mixed. The fight stream draws packs and resolves combat; the
    /// EVENT stream places the between-floor events and rolls their gambles. Keeping them apart
    /// is what makes a run's events the same whatever happens in its fights, and it is why the
    /// event seed is derived from the run seed rather than taken from the same generator.
    /// </remarks>
    public static class DelveRun
    {
        public const int MaxFloor = 13;
        public const int BazaarFloor = 7;

        /// <summary>The bazaar's two prices: awakening a relic, and buying one.</summary>
        public const int AwakenPrice = 60;
        public const int BuyPrice = 40;
        public const int BazaarChoices = 5;

        /// <summary>Floors an event can land between.</summary>
        private static readonly int[] EventGaps = { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

        /// <summary>How far an event may drag a stat down before the floor holds.</summary>
        /// <remarks>
        /// Only the speed floor is ever reached: the Ghost's locket and the Spike Trap cost
        /// five each and a failed robbery three more, which is thirteen. Nothing can spend more
        /// than one point of defence in a run — only the unclaimed chest costs any, and no
        /// event repeats — so the defence floor is unreachable. It is ported because the source
        /// has it, not because it can bite.
        /// </remarks>
        private const int MinSpdBonus = -10;
        private const int MinDefBonus = -2;

        /// <summary>Derives the event stream's seed from the run's. Knuth's golden ratio.</summary>
        public const uint EventSeedMix = 0x9E3779B9;

        public static RunState Resolve(uint seed, RunSetup setup, IRunChoices choices,
            IRunObserver observer = null, RunRules rules = null)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            if (choices == null) throw new ArgumentNullException(nameof(choices));

            var run = new RunState();
            run.Rules = rules ?? RunRules.Shipped();
            run.Hero.Php = setup.Hp;
            run.Hero.Pmax = setup.Hp;
            run.Hero.Gold = setup.Gold;
            run.Hero.BaseAtk = setup.Atk;
            run.Hero.BaseDef = setup.Def;
            run.Hero.BaseSpd = setup.Spd;
            run.Hero.BaseLck = setup.Lck;
            run.Items.AddRange(setup.StartKit);

            var rng = new Mulberry32(seed);
            var events = new Mulberry32(seed ^ EventSeedMix);
            Dictionary<int, int> placed = PlaceEvents(events);

            bool dead = false;
            int deadAt = 0;

            for (int floor = 1; floor <= MaxFloor && !dead; floor++)
            {
                run.Floor = floor;

                run.Php = Math.Min(run.Pmax, run.Php + Breather(setup, run, floor));

                int index;
                if (placed.TryGetValue(floor, out index))
                {
                    DungeonEvent ev = DungeonEvents.Get(index);
                    int choice = choices.Event(run, ev);
                    ev.Choices[choice].Resolve(run, events);

                    // An event can hurt, but only so far.
                    run.Hero.SpdBonus = Math.Max(MinSpdBonus, run.Hero.SpdBonus);
                    run.Hero.DefBonus = Math.Max(MinDefBonus, run.Hero.DefBonus);
                    run.Pmax = Math.Max(1, run.Pmax);
                    run.Php = Math.Min(run.Pmax, run.Php);

                    if (observer != null) observer.Event(run, floor, index, choice);

                    if (run.Php <= 0)
                    {
                        dead = true;
                        deadAt = floor;
                        break;
                    }
                }

                // Gold an earlier event promised for this floor.
                if (run.Pending.HasValue && run.Pending.Value.Floor == floor)
                {
                    run.Gold += run.Pending.Value.Gold;
                    run.Pending = null;
                }

                if (floor == BazaarFloor)
                {
                    Bazaar(run, floor, rng, choices, observer);
                    continue;
                }

                Draft(run, floor, setup, rng, choices, observer);
                Fight(run, floor, rng, choices, observer, ref dead, ref deadAt);
            }

            if (observer != null) observer.End(run, dead, dead ? deadAt : 0);
            return run;
        }

        /// <summary>
        /// The breather taken BETWEEN floors, which a Second Stomach makes count double.
        /// </summary>
        /// <remarks>
        /// There is no breather before the first floor, because there is no floor before it to
        /// have come up from. That the hero also happens to start whole is a coincidence of the
        /// starting state rather than the reason, which is why this answers the question
        /// directly instead of leaning on the health being full.
        /// </remarks>
        public static int Breather(RunSetup setup, RunState run, int floor)
        {
            if (floor <= 1) return 0;
            return setup.Breath * (run.Has(RelicId.SecondStomach) ? 2 : 1);
        }

        /// <summary>
        /// Scatters two to four events through the gaps between floors 2 and 11.
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

        /// <summary>The bazaar: one deal per visit, and no fight on this floor.</summary>
        private static void Bazaar(RunState run, int floor, Mulberry32 rng, IRunChoices choices,
            IRunObserver observer)
        {
            List<RelicId> offer = RelicDraft.RollOffer(run.Items, BazaarChoices, rng, run.Rules);
            BazaarDeal deal = choices.Bazaar(run, offer);

            // The shelf only ever offers a relic that stacks, and never a copy already awake.
            if (deal.Kind == DealKind.Awaken && run.CanAwaken(deal.Relic))
            {
                run.Awaken(deal.Relic);
                run.Gold -= AwakenPrice;
                PayTheDebt(run);
            }
            else if (deal.Kind == DealKind.Buy)
            {
                run.Gold -= BuyPrice;
                Pickup.Take(run, deal.Relic);
                PayTheDebt(run);
            }

            if (observer != null) observer.Bazaar(run, floor, offer, deal);
        }

        /// <summary>The draft, and however many rerolls are paid for.</summary>
        private static void Draft(RunState run, int floor, RunSetup setup, Mulberry32 rng,
            IRunChoices choices, IRunObserver observer)
        {
            List<RelicId> offer = RelicDraft.RollOffer(run.Items, setup.DraftChoices, rng, run.Rules);
            int rerolls = 0;

            while (true)
            {
                int price = RerollPrice(run);
                if (!choices.Reroll(run, offer, price)) break;

                run.Gold -= price;
                run.Rerolls++;
                rerolls++;
                PayTheDebt(run);
                offer = RelicDraft.RollOffer(run.Items, setup.DraftChoices, rng, run.Rules);
            }

            RelicId pick = choices.Draft(run, offer);
            Pickup.Take(run, pick);

            if (observer != null) observer.Draft(run, floor, offer, rerolls, pick);
        }

        /// <summary>What the next reroll costs: it doubles each time, and a Thumb shaves a fifth.</summary>
        public static int RerollPrice(RunState run)
        {
            double price = 10 * Math.Pow(2, run.Rerolls);
            if (run.Has(RelicId.MerchantsThumb)) price *= 0.8;
            return JsMath.RoundToInt(price);
        }

        /// <summary>
        /// A Debt of Flesh pays out in health every time gold leaves the purse at a counter.
        /// </summary>
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

        private static void PayTheDebt(RunState run)
        {
            run.Php = Math.Min(run.Pmax, run.Php + DebtPayment(run));
        }

        /// <summary>The floor's pack, the fight, and the one chance to be brought back.</summary>
        private static void Fight(RunState run, int floor, Mulberry32 rng, IRunChoices choices,
            IRunObserver observer, ref bool dead, ref int deadAt)
        {
            // No dungeon: these packs carry no boss relics and take no dungeon multiplier,
            // which is what the run corpus records. Pack COMPOSITION is gated separately.
            IReadOnlyList<EnemyState> pack = EnemyPackGenerator.Build(floor, rng, null);
            CombatResult result = new CombatEngine().ResolveFloor(run.Hero, pack, rng);

            if (observer != null) observer.Fight(run, floor, pack, result);
            if (run.Php > 0) return;

            if (!run.Revived && choices.Revive(run, floor))
            {
                Revive(run, floor, pack, result, rng, observer);
                if (run.Php > 0) return;
            }

            dead = true;
            deadAt = floor;
        }

        /// <summary>
        /// Brought back on half a pool, once per run, and set straight back into the fight that
        /// ended it.
        /// </summary>
        /// <remarks>
        /// The floor does not start again. The hero resumes against the foe that felled them,
        /// still carrying the wounds it took, and against whatever was behind it — with the
        /// floor's own carried state intact, so an Anvil Heart's bonus and a Sentinel's defence
        /// survive the death that interrupted them.
        /// </remarks>
        private static void Revive(RunState run, int floor, IReadOnlyList<EnemyState> pack,
            CombatResult fell, Mulberry32 rng, IRunObserver observer)
        {
            run.Revived = true;
            run.Php = Math.Max(1, (int)Math.Floor(run.Pmax / 2.0));

            var remaining = new List<EnemyState>();
            for (int i = fell.FoeIndex; i < pack.Count; i++) remaining.Add(pack[i]);

            // The foe that killed the hero keeps the wounds it took getting there.
            if (remaining.Count > 0) remaining[0].Hp = Math.Max(1, fell.FoeHp);

            if (observer != null) observer.Revived(run, floor);

            run.Hero.Carry = fell.Carry;
            CombatResult result = new CombatEngine().ResolveFloor(run.Hero, remaining, rng);
            run.Hero.Carry = null;

            if (observer != null) observer.Fight(run, floor, remaining, result);
        }
    }
}
