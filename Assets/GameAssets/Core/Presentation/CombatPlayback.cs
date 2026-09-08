using System;
using System.Collections.Generic;
using RelicRun.Core.Combat;

namespace RelicRun.Core.Presentation
{
    /// <summary>What playback wants done next.</summary>
    public enum PlaybackAction
    {
        /// <summary>Nothing is left. The fight has been shown.</summary>
        Done,

        /// <summary>Show the event at <see cref="PlaybackStep.Index"/>, then hold.</summary>
        Show,

        /// <summary>Walk the hall to meet the foe that is about to enter, then hold.</summary>
        Walk,
    }

    /// <summary>One instruction: do this, then wait that long.</summary>
    public readonly struct PlaybackStep
    {
        public readonly PlaybackAction Action;

        /// <summary>The event this concerns, or <c>-1</c> when there is none.</summary>
        public readonly int Index;

        /// <summary>How long to hold afterwards, in milliseconds.</summary>
        public readonly int WaitMs;

        public PlaybackStep(PlaybackAction action, int index, int waitMs)
        {
            Action = action;
            Index = index;
            WaitMs = waitMs;
        }

        public override string ToString()
        {
            return Action == PlaybackAction.Done
                ? "done"
                : Action.ToString().ToLowerInvariant() + " " + Index + " for " + WaitMs + "ms";
        }
    }

    /// <summary>
    /// Walks a finished fight, saying what to show and how long to hold it.
    /// </summary>
    /// <remarks>
    /// The fight is over before any of this runs. Nothing here can change an outcome; it decides
    /// only the order and the timing of the showing, which is why it is ordinary C# with no
    /// timers in it at all. Whoever drives it does the waiting — a coroutine, a UniTask, a test
    /// that waits for nothing.
    ///
    /// That is the whole shape of the port's answer to the source's timer dance. There, a fight
    /// was a chain of <c>setTimeout</c> callbacks kept honest by a token, because a browser timer
    /// cannot be recalled once set: pausing meant re-scheduling a poll every 180ms, and starting
    /// a new fight meant bumping a counter so the old chain would notice it was stale and quietly
    /// stop. Here the caller holds a cancellation token and simply stops awaiting. Nothing polls,
    /// nothing goes stale, and pausing is not modelled here at all — a paused caller is a caller
    /// that has not asked for the next step yet.
    /// </remarks>
    public sealed class CombatPlayback
    {
        private readonly IReadOnlyList<CombatEvent> _events;
        private readonly bool _walks;

        private int _cursor;
        private int _walked = -1;

        /// <summary>The pacing, which changes when the delver presses the speed control.</summary>
        public Pacing Pacing { get; private set; }

        /// <summary>How far in the fight has been shown.</summary>
        public int Cursor { get { return _cursor; } }

        public int Count { get { return _events == null ? 0 : _events.Count; } }

        public bool Finished { get { return _cursor >= Count; } }

        /// <param name="skipIntro">
        /// Whether the delver has asked to get on with it. Suppresses the hall walk, as reduced
        /// motion does — for a different reason and with the same effect.
        /// </param>
        public CombatPlayback(IReadOnlyList<CombatEvent> events, Pacing pacing, bool skipIntro = false)
        {
            if (pacing == null) throw new ArgumentNullException("pacing");

            _events = events;
            Pacing = pacing;
            _walks = !pacing.Reduced && !skipIntro && pacing.Rules.WalkMs > 0;
        }

        /// <summary>
        /// The pacing a fight opens at, from its own length.
        /// </summary>
        /// <remarks>
        /// A convenience, and the only sanctioned way to build one: the event count decides the
        /// beat, and a caller that passed a pacing built from a different fight's length would be
        /// showing this one at the wrong speed with nothing to say so.
        /// </remarks>
        public static CombatPlayback Of(IReadOnlyList<CombatEvent> events, bool reduced, int speed,
            PacingRules rules, bool skipIntro = false)
        {
            int count = events == null ? 0 : events.Count;
            return new CombatPlayback(events, Pacing.For(count, reduced, speed, rules), skipIntro);
        }

        /// <summary>Presses the speed control. Takes effect on the next wait, not the current one.</summary>
        public int Faster()
        {
            Pacing = Pacing.At(Pacing.NextSpeed());
            return Pacing.Speed;
        }

        /// <summary>
        /// What to do next, and how long to hold afterwards.
        /// </summary>
        /// <remarks>
        /// A foe entering is announced TWICE. The first time the cursor reaches it, playback
        /// walks the hall to meet them and does not advance; the second time it shows the event
        /// and moves on. That is the source's own arrangement and it is why <c>_walked</c> exists
        /// there — without it the walk would repeat forever, because the walk is what puts the
        /// cursor back.
        ///
        /// The hold after an event is the gap to the NEXT event's tick, so the timing of a fight
        /// is the fight's own. The last event has nothing to look ahead to and holds for the
        /// shortest gap.
        /// </remarks>
        public PlaybackStep Next()
        {
            if (Finished) return new PlaybackStep(PlaybackAction.Done, -1, 0);

            int at = _cursor;

            if (_walks && _walked != at && _events[at].Type == CombatEventType.Enter)
            {
                _walked = at;
                return new PlaybackStep(PlaybackAction.Walk, at, Pacing.Rules.WalkMs);
            }

            _cursor = at + 1;

            int wait = _cursor < Count
                ? Pacing.Between(_events[at].State.Tick, _events[_cursor].State.Tick)
                : Pacing.Last();

            return new PlaybackStep(PlaybackAction.Show, at, wait);
        }

        /// <summary>The event a step is about.</summary>
        public CombatEvent At(int index) { return _events[index]; }

        /// <summary>
        /// Throws the rest away and calls the fight shown.
        /// </summary>
        /// <remarks>
        /// What a delver skipping to the outcome does. The events are still there to be read —
        /// the log needs all of them however fast somebody wanted through it — but nothing more
        /// will be handed out to show.
        /// </remarks>
        public void Skip() { _cursor = Count; }
    }
}
