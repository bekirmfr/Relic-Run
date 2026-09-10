using System;
using System.Collections.Generic;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;

namespace RelicRun.Core.Run
{
    /// <summary>
    /// One delve, walked a stop at a time.
    /// </summary>
    /// <remarks>
    /// The same run <see cref="DelveRun.Resolve"/> has always resolved, turned inside out. Resolve
    /// is a PULL model — it runs its loop and calls <c>choices.Draft(run, offer)</c> expecting a
    /// relic back before the method returns — and a screen cannot answer that, because the answer
    /// arrives seconds later on another frame.
    ///
    /// So the loop yields instead. It walks until it wants something, hands out an
    /// <see cref="Ask"/>, and stops until somebody answers. Resolve still exists and still has its
    /// old signature; it is now three lines over this, feeding each stop to an
    /// <see cref="IRunChoices"/>. One implementation, two front doors — which is what keeps the
    /// 516 recorded runs gating the code the screen actually runs rather than a copy of it.
    ///
    /// The arithmetic did not move. Everything about what a floor DOES is still on
    /// <see cref="DelveRun"/>; what is here is only the order things happen in, which is the part
    /// that had to become interruptible.
    ///
    /// A run in progress is its seed and <see cref="Answers"/>, and nothing else. Resuming is
    /// replaying: build another Delve from the same seed and feed it the same answers, which
    /// takes about four milliseconds for a whole run and is exactly what the corpus already
    /// proves works. There is no engine state to serialise and so no save format to version.
    /// </remarks>
    public sealed class Delve
    {
        private readonly RunSetup _setup;
        private readonly RunState _run = new RunState();
        private readonly Mulberry32 _rng;
        private readonly Mulberry32 _events;
        private readonly CombatRules _combat;
        private readonly IRunObserver _observer;
        private readonly Dictionary<int, int> _placed;
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly IEnumerator<Ask> _walk;
        private readonly List<Answer> _answers = new List<Answer>();

        private List<RelicId> _offer;
        private List<EnemyState> _nextPack;
        private bool _fell;

        /// <summary>Starts a delve, walked to its first question.</summary>
        public Delve(uint seed, RunSetup setup, IRunObserver observer = null,
            RunRules rules = null, CombatRules combat = null)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));

            _setup = setup;
            _observer = observer;
            _combat = combat ?? CombatRules.Delve();

            _run.Rules = rules ?? RunRules.Shipped();
            _run.Hero.Php = setup.Hp;
            _run.Hero.Pmax = setup.Hp;
            _run.Hero.Gold = setup.Gold;
            _run.Hero.BaseAtk = setup.Atk;
            _run.Hero.BaseDef = setup.Def;
            _run.Hero.BaseSpd = setup.Spd;
            _run.Hero.BaseLck = setup.Lck;
            _run.Items.AddRange(setup.StartKit);
            _run.Floor = 1;

            _rng = new Mulberry32(seed);
            _events = new Mulberry32(seed ^ DelveRun.EventSeedMix);
            _placed = DelveRun.PlaceEvents(_events);

            Ending = RunEnding.Died;

            // The opening offer is drawn before the first floor is walked, as the run is made.
            _offer = DelveRun.RollOffer(_run, setup.DraftChoices, _rng);

            _walk = Walk().GetEnumerator();

            Step();
        }

        /// <summary>The run as it stands. Live: it changes as the delve is walked.</summary>
        public RunState State
        {
            get { return _run; }
        }

        /// <summary>What the run is waiting on, or null once it has ended.</summary>
        public Ask Pending { get; private set; }

        /// <summary>Whether there is nothing left to ask.</summary>
        public bool Finished
        {
            get { return Pending == null; }
        }

        /// <summary>How it ended. Only meaningful once <see cref="Finished"/>.</summary>
        public RunEnding Ending { get; private set; }

        /// <summary>Which floor it ended on.</summary>
        public int EndedOn { get; private set; }

        /// <summary>
        /// Every answer given, in order.
        /// </summary>
        /// <remarks>
        /// The whole of a run in progress. Kept because a run is resumed by replaying it, not by
        /// restoring it — see the class remarks.
        /// </remarks>
        public IReadOnlyList<Answer> Answers
        {
            get { return _answers; }
        }

        /// <summary>Answers whatever is pending, and walks on to the next stop.</summary>
        /// <remarks>
        /// The answer is read off <see cref="Pending"/> rather than passed in, so a caller fills
        /// in the one field its stop uses and pumps. A stop that wants nothing — a floor that has
        /// been fought — is answered the same way, which is what gives playback the clock.
        /// </remarks>
        public void Answer()
        {
            if (Pending == null) throw new InvalidOperationException("the delve has ended");

            _answers.Add(Pending.Answer);

            Step();
        }

        /// <summary>Answers with something already decided. For a replay, and for a resume.</summary>
        public void Answer(Answer answer)
        {
            if (Pending == null) throw new InvalidOperationException("the delve has ended");

            Pending.Answer = answer;

            Answer();
        }

        private void Step()
        {
            Pending = _walk.MoveNext() ? _walk.Current : null;
        }

        /* ---------- the walk ---------- */

        /// <summary>
        /// The run's own loop, line for line as <see cref="DelveRun.Resolve"/> walked it.
        /// </summary>
        /// <remarks>
        /// The order here is load-bearing and is not obvious from reading it. The pack for the
        /// floor AHEAD is rolled the moment a fight ends, before the delver is asked whether to
        /// leave — so asking moves nothing. And the gate loop runs TWICE around the bazaar
        /// floor, which is why floor seven is walked through without a fight and why a delver who
        /// means to leave "after seven" leaves after eight.
        /// </remarks>
        private IEnumerable<Ask> Walk()
        {
            while (true)
            {
                foreach (Ask ask in Drafting()) yield return ask;

                IReadOnlyList<EnemyState> pack = _nextPack ??
                    EnemyPackGenerator.Build(_run.Floor, _rng, _setup.Dungeon);

                _nextPack = null;

                foreach (Ask ask in Fighting(pack)) yield return ask;

                if (_fell) break;

                if (_run.Floor >= DelveRun.MaxFloor)
                {
                    Ending = RunEnding.Cleared;
                    break;
                }

                _nextPack = EnemyPackGenerator.Build(_run.Floor + 1, _rng, _setup.Dungeon);

                Ask leaving = Ask.CashOut(_run.Floor);
                yield return leaving;

                if (leaving.Answer.Yes)
                {
                    Ending = RunEnding.CashedOut;
                    break;
                }

                // Walking out of a floor: the event in the gap, the gate's breather, and the
                // bazaar, which is a gate of its own and is walked out of the same way.
                while (true)
                {
                    foreach (Ask ask in Gap()) yield return ask;

                    DelveRun.Gate(_run, _setup);

                    if (_run.Floor != DelveRun.BazaarFloor) break;

                    foreach (Ask ask in Trading()) yield return ask;

                    if (_run.Rules.BazaarRollsItsOwnPack)
                    {
                        _nextPack = EnemyPackGenerator.Build(_run.Floor + 1, _rng, _setup.Dungeon);
                    }
                }

                _offer = DelveRun.RollOffer(_run, _setup.DraftChoices, _rng);
            }

            EndedOn = _run.Floor;

            if (_observer != null) _observer.End(_run, Ending, _run.Floor);
        }

        /// <summary>The draft, and however many rerolls are paid for.</summary>
        private IEnumerable<Ask> Drafting()
        {
            var rerolls = 0;
            int floor = _run.Floor;

            while (true)
            {
                int price = DelveRun.RerollPrice(_run);

                if (_run.Gold < price) break;

                Ask again = Ask.Reroll(floor, _offer, price);
                yield return again;

                if (!again.Answer.Yes) break;

                _run.Gold -= price;
                _run.Rerolls++;
                rerolls++;
                DelveRun.PayTheDebt(_run);

                // Not a slip: the shipped reroll draws its replacement from the EVENT stream.
                _offer.Clear();
                _offer.AddRange(RelicDraft.RollOffer(
                    _run.Items, _setup.DraftChoices, _events, _run.Rules, GameModes.Delve));
            }

            Ask taking = Ask.Draft(floor, _offer);
            yield return taking;

            RelicId pick = taking.Answer.Pick;

            Pickup.Take(_run, pick);

            if (_observer != null) _observer.Draft(_run, floor, _offer, rerolls, pick);
        }

        /// <summary>The event waiting in the gap beyond a floor, if one was placed there.</summary>
        private IEnumerable<Ask> Gap()
        {
            int index;

            if (!_placed.TryGetValue(_run.Floor, out index) || !_seen.Add(_run.Floor)) yield break;

            DungeonEvent ev = DungeonEvents.Get(index);

            Ask choosing = Ask.Choosing(_run.Floor, ev);
            yield return choosing;

            int choice = choosing.Answer.Choice;

            // A counter is a counter: an outcome that costs gold pays the Debt of Flesh, the
            // same as the bazaar and the reroll ladder do.
            int purse = _run.Gold;
            ev.Choices[choice].Resolve(_run, _events);
            if (_run.Gold < purse) DelveRun.PayTheDebt(_run);

            // An event can hurt, but never kill: the floor at one is what makes the Spike Trap a
            // scare rather than an ending, and it is why the run loop asks nothing about health
            // here. Only a fight can end a delve.
            _run.Php = Math.Max(1, Math.Min(_run.Pmax, _run.Php));
            _run.Hero.SpdBonus = Math.Max(DelveRun.MinSpdBonus, _run.Hero.SpdBonus);

            if (_observer != null) _observer.Event(_run, _run.Floor, index, choice);
        }

        /// <summary>The bazaar: a shelf of five, what could be woken, and the visit's business.</summary>
        private IEnumerable<Ask> Trading()
        {
            List<RelicId> offer = RelicDraft.RollOffer(
                _run.Items, DelveRun.BazaarChoices, _rng, _run.Rules, GameModes.Delve);

            // Which copies could be woken is settled on the way in, and stays settled: buying
            // something on the first deal does not put it on the awakening shelf.
            List<int> awakenable = _run.Awakenable();
            var done = 0;

            while (true)
            {
                Ask dealing = Ask.Bazaar(_run.Floor, offer, awakenable);
                yield return dealing;

                BazaarDeal deal = dealing.Answer.Deal;

                if (deal.Kind == DealKind.None) break;

                int allowed = DelveRun.DealsAllowed(_run, _setup);

                if (deal.Kind == DealKind.Awaken)
                {
                    // Only a copy the shelf actually carried. The source checks the slot holds
                    // something and is not already awake, and leans on the UI for the rest —
                    // which is fine until something that is not the UI asks.
                    if (!awakenable.Contains(deal.Slot)) break;

                    int price = DelveRun.PriceOf(_run, DelveRun.AwakenPrice);
                    if (_run.Gold < price) break;

                    RelicId woken = _run.Items[deal.Slot];
                    done++;
                    _run.Gold -= price;
                    DelveRun.PayTheDebt(_run);
                    _run.Awaken(deal.Slot);

                    // The shelf closes: one awakening a visit, whatever else the visit allows.
                    awakenable = new List<int>();

                    // The idol fills: it gave up fifteen of the pool to join every set, and
                    // waking it hands them back.
                    if (woken == RelicId.HollowIdol)
                    {
                        _run.Pmax += 15;
                        _run.Php = Math.Min(_run.Pmax, _run.Php + 15);
                    }
                }
                else
                {
                    int price = DelveRun.PriceOf(_run, DelveRun.BuyPrice);
                    if (_run.Gold < price || !offer.Contains(deal.Relic)) break;

                    done++;
                    _run.Gold -= price;
                    DelveRun.PayTheDebt(_run);
                    Pickup.Take(_run, deal.Relic);

                    // A relic that does not stack leaves the shelf; one that does stays.
                    if (!_run.Rules.Stacks(deal.Relic)) offer.RemoveAll(id => id == deal.Relic);
                }

                if (_observer != null) _observer.Deal(_run, _run.Floor, offer, awakenable, deal);
                if (done >= allowed) break;
            }

            // The walk-out closes the visit, so a watcher sees where the delver left it — the
            // shelf they did not clear as much as the deals they took.
            if (_observer != null)
            {
                _observer.Deal(_run, _run.Floor, offer, awakenable, BazaarDeal.Walk);
            }
        }

        /// <summary>The floor's fight, and the one chance to be brought back.</summary>
        /// <remarks>
        /// Sets <see cref="_fell"/> rather than returning, because an iterator cannot hand
        /// anything back through <c>return</c> and its caller is one too.
        /// </remarks>
        private IEnumerable<Ask> Fighting(IReadOnlyList<EnemyState> pack)
        {
            // Gold an earlier event promised for this floor arrives as the fight opens.
            if (_run.Pending.HasValue && _run.Pending.Value.Floor == _run.Floor)
            {
                _run.Gold += _run.Pending.Value.Gold;
                _run.Pending = null;
            }

            int ceiling = _run.Pmax;

            // A floor may get its own copy of the Flesh set's flag, and never hand it back.
            bool fleshSet = _run.Hero.FleshSetApplied;
            if (!_run.Rules.FleshSetSurvivesTheFloor) _run.Hero.FleshSetApplied = false;

            CombatResult result = new CombatEngine(_combat).ResolveFloor(_run.Hero, pack, _rng);
            _run.BreathHealed = 0;
            if (!_run.Rules.FleshSetSurvivesTheFloor) _run.Hero.FleshSetApplied = fleshSet;

            // Health is clamped to the ceiling the floor OPENED with. A Chalice that grew the
            // pool mid-fight raises the ceiling only once the fight is over, so the health it
            // bought does not arrive with it.
            _run.Php = Math.Min(_run.Php, ceiling);
            DelveRun.Crown(_run);

            if (_observer != null) _observer.Fight(_run, _run.Floor, pack, result);

            yield return Ask.Fought(_run.Floor, pack, result);

            if (_run.Php > 0)
            {
                _fell = false;
                yield break;
            }

            if (!_run.Revived)
            {
                Ask back = Ask.Revive(_run.Floor);
                yield return back;

                if (back.Answer.Yes)
                {
                    foreach (Ask ask in Reviving(pack, result)) yield return ask;

                    if (_run.Php > 0)
                    {
                        _fell = false;
                        yield break;
                    }
                }
            }

            _fell = true;
        }

        /// <summary>
        /// Brought back on half a pool, once per run, and set straight back into the fight that
        /// ended it.
        /// </summary>
        /// <remarks>
        /// The floor does not start again. The hero resumes against the foe that felled them,
        /// still carrying the wounds it took, and against whatever was behind it — with the
        /// floor's own carried state intact. A hero who fell to the last foe of the pack as it
        /// died gets a fresh pack instead, rolled for the same floor.
        /// </remarks>
        private IEnumerable<Ask> Reviving(IReadOnlyList<EnemyState> pack, CombatResult fell)
        {
            _run.Revived = true;
            _run.Php = Math.Max(1, (int)Math.Floor(_run.Pmax / 2.0));

            var remaining = new List<EnemyState>();
            for (int i = fell.FoeIndex; i < pack.Count; i++) remaining.Add(pack[i]);

            // The foe that killed the hero keeps the wounds it took getting there.
            if (remaining.Count > 0) remaining[0].Hp = Math.Max(1, fell.FoeHp);
            else remaining.AddRange(EnemyPackGenerator.Build(_run.Floor, _rng, _setup.Dungeon));

            if (_observer != null) _observer.Revived(_run, _run.Floor);

            int ceiling = _run.Pmax;
            bool fleshSet = _run.Hero.FleshSetApplied;
            if (!_run.Rules.FleshSetSurvivesTheFloor) _run.Hero.FleshSetApplied = false;

            _run.Hero.Carry = fell.Carry;
            CombatResult result = new CombatEngine(_combat).ResolveFloor(_run.Hero, remaining, _rng);
            _run.Hero.Carry = null;
            if (!_run.Rules.FleshSetSurvivesTheFloor) _run.Hero.FleshSetApplied = fleshSet;
            _run.Php = Math.Min(_run.Php, ceiling);
            DelveRun.Crown(_run);

            if (_observer != null) _observer.Fight(_run, _run.Floor, remaining, result);

            yield return Ask.Fought(_run.Floor, remaining, result);
        }
    }
}
