using System.Collections.Generic;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;

namespace RelicRun.Core.Run
{
    /// <summary>What a run has stopped to ask.</summary>
    public enum AskKind
    {
        /// <summary>Which relic to take from the offer.</summary>
        Draft = 0,

        /// <summary>Whether to pay for a fresh offer. Asked again after each one.</summary>
        Reroll = 1,

        /// <summary>Which of an event's choices to take.</summary>
        Event = 2,

        /// <summary>What to do with the bazaar's shelf. Asked once per deal the visit allows.</summary>
        Bazaar = 3,

        /// <summary>Whether to be brought back. Asked once per run, the moment the hero falls.</summary>
        Revive = 4,

        /// <summary>Whether to walk out at this gate rather than descend.</summary>
        CashOut = 5,

        /// <summary>
        /// A floor has been fought. Nothing is being asked.
        /// </summary>
        /// <remarks>
        /// The only stop that wants no answer, and the reason the run stops for it anyway: the
        /// fight is RESOLVED and the screen has twelve seconds of reading it out to do. The
        /// engine holding still until the caller says go is the port's second invariant —
        /// simulate, then replay — turned into control flow instead of a convention.
        /// </remarks>
        Fought = 6,
    }

    /// <summary>
    /// One answer, small enough to write down.
    /// </summary>
    /// <remarks>
    /// Every field a stop could want, and only one of them ever matters. It is a struct of four
    /// small values rather than a hierarchy because of what it is FOR: a run is resumed by
    /// replaying its answers from the start, so a run in progress is its seed and a list of
    /// these — a few dozen bytes rather than a serialised engine.
    /// </remarks>
    public struct Answer
    {
        public RelicId Pick;
        public int Choice;
        public bool Yes;
        public BazaarDeal Deal;
    }

    /// <summary>
    /// A run, stopped, holding out what it wants to know.
    /// </summary>
    /// <remarks>
    /// One class rather than seven, with the fields a given kind uses and the rest left empty.
    /// A screen switches on <see cref="Kind"/>, reads what it needs to draw, writes the answer
    /// into the same object and pumps the run — which is a shape a UI can hold in its head.
    /// </remarks>
    public sealed class Ask
    {
        public readonly AskKind Kind;

        /// <summary>Which floor is being stood on.</summary>
        public readonly int Floor;

        /// <summary>What is on offer: the draft's, the reroll's, or the bazaar's shelf.</summary>
        public readonly IReadOnlyList<RelicId> Offer;

        /// <summary>What a reroll costs.</summary>
        public readonly int Price;

        /// <summary>The event and its choices.</summary>
        public readonly DungeonEvent Event;

        /// <summary>Which inventory slots hold a copy that could be woken.</summary>
        public readonly IReadOnlyList<int> Awakenable;

        /// <summary>
        /// The pack this stop is about.
        /// </summary>
        /// <remarks>
        /// Two stops carry one and they mean opposite things: a fought floor's is what has just
        /// been beaten, and a gate's is what is waiting one floor down. Both are "the pack this
        /// stop is about", and a delver only ever sees one of them at a time — the gate's is the
        /// whole reason walking out is a decision rather than a coin toss.
        /// </remarks>
        public readonly IReadOnlyList<EnemyState> Pack;

        /// <summary>And how it went, resolved in full before a frame of it is drawn.</summary>
        public readonly CombatResult Result;

        /// <summary>The answer, written by whoever is being asked.</summary>
        public Answer Answer;

        /// <summary>
        /// What came of it, written by the run once the answer is in.
        /// </summary>
        /// <remarks>
        /// Only an event fills this, and only after it has been answered. A choice at an event is
        /// made BLIND — the hint says what it costs, never what it does, and half of them roll
        /// for it — so the sentence afterwards is the only place the run says what happened.
        ///
        /// On the stop rather than in a second stop of its own, because a second stop would be a
        /// second answer, and every run this project has recorded would have to grow one.
        /// </remarks>
        public EventOutcome Outcome;

        private Ask(AskKind kind, int floor, IReadOnlyList<RelicId> offer, int price,
            DungeonEvent ev, IReadOnlyList<int> awakenable, IReadOnlyList<EnemyState> pack,
            CombatResult result)
        {
            Kind = kind;
            Floor = floor;
            Offer = offer;
            Price = price;
            Event = ev;
            Awakenable = awakenable;
            Pack = pack;
            Result = result;
        }

        public static Ask Draft(int floor, IReadOnlyList<RelicId> offer)
        {
            return new Ask(AskKind.Draft, floor, offer, 0, null, null, null, null);
        }

        public static Ask Reroll(int floor, IReadOnlyList<RelicId> offer, int price)
        {
            return new Ask(AskKind.Reroll, floor, offer, price, null, null, null, null);
        }

        /// <summary>Named apart from the field it fills, which C# will not let it share.</summary>
        public static Ask Choosing(int floor, DungeonEvent ev)
        {
            return new Ask(AskKind.Event, floor, null, 0, ev, null, null, null);
        }

        public static Ask Bazaar(int floor, IReadOnlyList<RelicId> offer,
            IReadOnlyList<int> awakenable)
        {
            return new Ask(AskKind.Bazaar, floor, offer, 0, null, awakenable, null, null);
        }

        public static Ask Revive(int floor)
        {
            return new Ask(AskKind.Revive, floor, null, 0, null, null, null, null);
        }

        /// <param name="below">
        /// What is waiting one floor down, which the run has already rolled by the time it asks.
        /// A delver deciding whether to risk the purse is entitled to see the first of them —
        /// that is the source's own peek, and it is what makes this a decision.
        /// </param>
        public static Ask CashOut(int floor, IReadOnlyList<EnemyState> below = null)
        {
            return new Ask(AskKind.CashOut, floor, null, 0, null, null, below, null);
        }

        public static Ask Fought(int floor, IReadOnlyList<EnemyState> pack, CombatResult result)
        {
            return new Ask(AskKind.Fought, floor, null, 0, null, null, pack, result);
        }

        /// <summary>
        /// Puts the question to something that answers all of them.
        /// </summary>
        /// <remarks>
        /// The bridge between the two front doors. <see cref="DelveRun.Resolve"/> walks the same
        /// stops a screen does and hands each one straight to an <see cref="IRunChoices"/>, so
        /// there is exactly one place that knows which method answers which stop.
        /// </remarks>
        public Answer AskedOf(IRunChoices choices, RunState run)
        {
            switch (Kind)
            {
                case AskKind.Draft:
                    return new Answer { Pick = choices.Draft(run, Offer) };

                case AskKind.Reroll:
                    return new Answer { Yes = choices.Reroll(run, Offer, Price) };

                case AskKind.Event:
                    return new Answer { Choice = choices.Event(run, Event) };

                case AskKind.Bazaar:
                    return new Answer { Deal = choices.Bazaar(run, Offer, Awakenable) };

                case AskKind.Revive:
                    return new Answer { Yes = choices.Revive(run, Floor) };

                case AskKind.CashOut:
                    return new Answer { Yes = choices.CashOut(run, Floor) };

                default:
                    return new Answer();
            }
        }
    }
}
