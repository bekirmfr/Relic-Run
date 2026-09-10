using System;
using System.Threading;
using System.Threading.Tasks;
using RelicRun.Core.Combat;

namespace RelicRun.Core.Presentation
{
    /// <summary>Whatever is drawing the fight.</summary>
    /// <remarks>
    /// Two things to do and one question to answer, which is the whole of what a screen owes the
    /// loop. Nothing here returns anything: a view that had to be waited on would be deciding
    /// the pace, and the pace is decided already.
    /// </remarks>
    public interface IPlaybackScreen
    {
        /// <summary>
        /// Draw what this event says happened.
        /// </summary>
        /// <param name="index">
        /// Where in the fight it is. Handed over because the loop knows it and the screen would
        /// otherwise have to search for it — and two events of a fight can be identical in every
        /// field, so a search would sometimes find the wrong one. A gauge timed off the wrong
        /// blow winds up against the wrong blow.
        /// </param>
        void Show(int index, CombatEvent shown);

        /// <summary>Set off down the hall to meet the foe entering at this index.</summary>
        void Walk(int index);

        /// <summary>
        /// Announce the foe entering at this index, the walking now being over.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Walk"/> so the approach is visible. A screen that raised its
        /// card at the same moment it set off would cover its own hall for the whole walk.
        /// </remarks>
        void Meet(int index);

        /// <summary>Whether the delver has stopped to look at something.</summary>
        bool Paused { get; }

        /// <summary>
        /// Whether the delver has asked to get on with it.
        /// </summary>
        /// <remarks>
        /// The opposite of <see cref="Paused"/> and asked in only one place: while the card that
        /// announces a foe is up. That card is the one wait in a fight a delver is invited to cut
        /// short — everything else is the fight's own clock, and skipping THAT is what the speed
        /// control is for.
        ///
        /// Asked rather than raised, so a screen with no button answers false forever and the
        /// card simply runs its three seconds.
        /// </remarks>
        bool Impatient { get; }
    }

    /// <summary>Where the waiting happens.</summary>
    /// <remarks>
    /// Injected so the loop can be tested without any waiting at all. A test clock answers
    /// instantly and writes down what it was asked for, which turns "does this pause correctly"
    /// from a question about timing into a question about a list.
    /// </remarks>
    public interface IPlaybackClock
    {
        /// <summary>Waits this many milliseconds.</summary>
        Task Wait(int ms, CancellationToken token);

        /// <summary>
        /// Waits this many milliseconds, unless something becomes true first.
        /// </summary>
        /// <remarks>
        /// A countdown with a way out, which is what a card carrying both a timer and a button
        /// is. Unlike <see cref="Until"/> this cannot wait forever — a delver who never presses
        /// anything still gets on with the fight, which is the behaviour the source's own
        /// three-second auto-start has.
        /// </remarks>
        Task Wait(int ms, Func<bool> cut, CancellationToken token);

        /// <summary>Waits until something becomes true. The pause gate.</summary>
        Task Until(Func<bool> ready, CancellationToken token);
    }

    /// <summary>
    /// Shows a fight, one step at a time, until it is over or somebody stops it.
    /// </summary>
    /// <remarks>
    /// This is what the source's timer chain becomes. There, a fight was a run of
    /// <c>setTimeout</c> callbacks: pausing re-scheduled a poll every 180 milliseconds until the
    /// delver came back, and starting a new fight bumped a token so the old chain would notice
    /// it had been superseded and quietly return. Both were ways of coping with a timer that
    /// cannot be recalled.
    ///
    /// A cancellation token can. So there is no token counter, no polling, and no single-flight
    /// dance — a fight that is abandoned is a cancelled await, and a paused fight is a loop
    /// sitting on a gate rather than one waking up five times a second to ask again.
    /// </remarks>
    public static class PlaybackLoop
    {
        /// <summary>
        /// Runs a fight to its end.
        /// </summary>
        /// <remarks>
        /// Cancellation is checked before each step and inside every wait, so abandoning a fight
        /// costs at most one held frame. It throws rather than returning quietly, because a
        /// caller that started a fight and got a normal return would reasonably believe it had
        /// been shown.
        /// </remarks>
        public static async Task Play(CombatPlayback playing, IPlaybackScreen screen,
            IPlaybackClock clock, CancellationToken token)
        {
            if (playing == null) throw new ArgumentNullException("playing");
            if (screen == null) throw new ArgumentNullException("screen");
            if (clock == null) throw new ArgumentNullException("clock");

            while (true)
            {
                token.ThrowIfCancellationRequested();

                // Asked before the step, not after, so a delver who pauses mid-wait stops on the
                // event they were looking at rather than one further on.
                if (screen.Paused) await clock.Until(() => !screen.Paused, token);

                PlaybackStep step = playing.Next();
                if (step.Action == PlaybackAction.Done) return;

                if (step.Action == PlaybackAction.Walk)
                {
                    screen.Walk(step.Index);
                }
                else if (step.Action == PlaybackAction.Meet)
                {
                    screen.Meet(step.Index);

                    // The one wait a delver may cut short. A card announcing a foe is something
                    // to READ, so it holds until it has been read or until the three seconds the
                    // source gives it run out — whichever comes first.
                    await clock.Wait(step.WaitMs, () => screen.Impatient, token);
                    continue;
                }
                else
                {
                    screen.Show(step.Index, playing.At(step.Index));
                }

                await clock.Wait(step.WaitMs, token);
            }
        }
    }
}
