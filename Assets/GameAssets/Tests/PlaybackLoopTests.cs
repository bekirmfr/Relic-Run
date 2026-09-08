using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Core.Stats;

namespace RelicRun.Tests
{
    /// <summary>
    /// Showing a fight: the pause gate, and giving up when somebody walks away.
    /// </summary>
    /// <remarks>
    /// The waiting is injected, so none of this waits for anything. A test clock answers
    /// instantly and writes down what it was asked for, which turns "does pausing work" from a
    /// question about timing — which can only be answered by watching, badly — into a question
    /// about a list.
    ///
    /// What is being tested is the part the source could not have: it coped with timers that
    /// cannot be recalled by polling every 180ms while paused and by numbering its callback
    /// chains so a superseded one would notice and return. A cancellation token replaces both,
    /// and the two things worth proving are that a paused fight stops where the delver was
    /// looking and that an abandoned one stops at all.
    /// </remarks>
    [TestFixture]
    public class PlaybackLoopTests
    {
        private static readonly IReadOnlyList<RelicId> NoRelics = new RelicId[0];
        private static readonly IReadOnlyList<StatModifier> NoMods = new StatModifier[0];

        private static CombatEvent On(int tick, CombatEventType type = CombatEventType.EnemyDamage)
        {
            var state = new CombatSnapshot(tick, 100, 50, 0, 50, 5, EnemyRank.Guard, 0, 25, 10,
                0, 0, 0, NoRelics, NoMods, default(CombatCounters));

            return new CombatEvent(type, 0, state);
        }

        /// <summary>A screen that writes down what it was told and never draws anything.</summary>
        private sealed class Notepad : IPlaybackScreen
        {
            public readonly List<string> Told = new List<string>();
            public bool Paused { get; set; }

            /// <summary>Runs after the nth thing it is told. How a delver interrupts.</summary>
            public Action<Notepad> After;

            public void Show(CombatEvent shown) { Note("show " + shown.State.Tick); }

            public void Walk(int index) { Note("walk " + index); }

            private void Note(string what)
            {
                Told.Add(what);
                if (After != null) After(this);
            }
        }

        /// <summary>A clock that does not wait, and remembers being asked to.</summary>
        private sealed class Stopwatch : IPlaybackClock
        {
            public readonly List<int> Waited = new List<int>();
            public int Gated;

            public Task Wait(int ms, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Waited.Add(ms);
                return Task.CompletedTask;
            }

            public Task Until(Func<bool> ready, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Gated++;

                // A real gate would wait. This one asks once and requires an answer, so a loop
                // that gated on something already true would spin here rather than pass quietly.
                if (!ready()) throw new InvalidOperationException("gated on something still false");

                return Task.CompletedTask;
            }
        }

        private static CombatPlayback Playing(params CombatEvent[] events)
        {
            return CombatPlayback.Of(events, false, 1, PacingRules.Shipped());
        }

        [Test]
        public async Task AFightIsShownFromStartToEnd()
        {
            var screen = new Notepad();
            var clock = new Stopwatch();

            await PlaybackLoop.Play(Playing(On(0, CombatEventType.Enter), On(1), On(3)),
                screen, clock, CancellationToken.None);

            Assert.That(screen.Told, Is.EqualTo(new[] { "walk 0", "show 0", "show 1", "show 3" }));
            Assert.That(clock.Waited, Is.EqualTo(new[] { 3350, 500, 1000, 450 }));
            Assert.That(clock.Gated, Is.Zero, "nobody paused, so nothing gated");
        }

        [Test]
        public async Task AFightWithNothingInItReturnsAtOnce()
        {
            var screen = new Notepad();
            var clock = new Stopwatch();

            await PlaybackLoop.Play(Playing(), screen, clock, CancellationToken.None);

            Assert.That(screen.Told, Is.Empty);
            Assert.That(clock.Waited, Is.Empty);
        }

        /// <summary>
        /// A paused fight waits, and comes back to the event the delver was looking at.
        /// </summary>
        /// <remarks>
        /// The gate is checked BEFORE the next step rather than after the last one, which is the
        /// difference between a delver who pauses to read a card finding it still on screen and
        /// finding the next thing already drawn over it.
        /// </remarks>
        [Test]
        public async Task PausingStopsOnTheEventTheDelverWasLookingAt()
        {
            var screen = new Notepad();
            var clock = new Stopwatch();

            // Pause as soon as the second event is drawn, and come back a moment later.
            screen.After = pad =>
            {
                if (pad.Told.Count == 2) pad.Paused = true;
                if (pad.Told.Count > 2) pad.Paused = false;
            };

            var resumed = false;
            var gate = new Resuming(screen, () => resumed = true);

            await PlaybackLoop.Play(Playing(On(0), On(1), On(2)), screen, gate,
                CancellationToken.None);

            Assert.That(resumed, Is.True, "the loop never reached the gate");
            Assert.That(screen.Told, Is.EqualTo(new[] { "show 0", "show 1", "show 2" }),
                "and nothing was drawn twice or skipped over");
        }

        /// <summary>A clock whose gate lets the delver come back.</summary>
        private sealed class Resuming : IPlaybackClock
        {
            private readonly Notepad _screen;
            private readonly Action _noticed;

            public Resuming(Notepad screen, Action noticed)
            {
                _screen = screen;
                _noticed = noticed;
            }

            public Task Wait(int ms, CancellationToken token) { return Task.CompletedTask; }

            public Task Until(Func<bool> ready, CancellationToken token)
            {
                _noticed();
                _screen.Paused = false;

                Assert.That(ready(), Is.True, "the gate did not open when the pause lifted");
                return Task.CompletedTask;
            }
        }

        /* ---------- giving up ---------- */

        /// <summary>
        /// A fight nobody is watching any more stops, and says so.
        /// </summary>
        /// <remarks>
        /// Throwing rather than returning quietly is deliberate. A caller that started a fight
        /// and got a normal return would reasonably believe it had been shown to the end, and
        /// would go on to whatever comes after a fight — which, in a delve, is the next floor.
        /// </remarks>
        [Test]
        public void AbandoningAFightStopsIt()
        {
            var screen = new Notepad();
            var clock = new Stopwatch();
            var stopping = new CancellationTokenSource();

            screen.After = pad => { if (pad.Told.Count == 2) stopping.Cancel(); };

            Assert.That(
                async () => await PlaybackLoop.Play(Playing(On(0), On(1), On(2), On(3)),
                    screen, clock, stopping.Token),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.That(screen.Told.Count, Is.EqualTo(2), "it stopped where it was told to");
        }

        [Test]
        public void AFightCancelledBeforeItStartsShowsNothing()
        {
            var screen = new Notepad();
            var clock = new Stopwatch();
            var stopping = new CancellationTokenSource();
            stopping.Cancel();

            Assert.That(
                async () => await PlaybackLoop.Play(Playing(On(0), On(1)), screen, clock,
                    stopping.Token),
                Throws.InstanceOf<OperationCanceledException>());

            Assert.That(screen.Told, Is.Empty);
        }

        /* ---------- the awkward callers ---------- */

        [Test]
        public void PlayingNothingIsRefusedRatherThanIgnored()
        {
            var screen = new Notepad();
            var clock = new Stopwatch();

            Assert.That(async () => await PlaybackLoop.Play(null, screen, clock, CancellationToken.None),
                Throws.ArgumentNullException);
            Assert.That(async () => await PlaybackLoop.Play(Playing(On(0)), null, clock, CancellationToken.None),
                Throws.ArgumentNullException);
            Assert.That(async () => await PlaybackLoop.Play(Playing(On(0)), screen, null, CancellationToken.None),
                Throws.ArgumentNullException);
        }

        /// <summary>
        /// Pressing the speed control shortens what is left, from the step after the press.
        /// </summary>
        /// <remarks>
        /// Not the wait already running. A step's hold is worked out when the step is produced,
        /// so the beat a delver is watching when they press the button plays out at the length
        /// it was given — exactly as the source behaved, where the button changed a number and
        /// the timer already scheduled was already scheduled.
        ///
        /// It is worth being precise about rather than tidying up. Cutting the current wait
        /// short would mean the press could land in the middle of a two-and-a-half second gap
        /// and skip most of it, which reads as the button having skipped an event.
        /// </remarks>
        [Test]
        public async Task TheSpeedControlShortensWhatIsLeftFromTheNextStep()
        {
            var screen = new Notepad();
            var clock = new Stopwatch();
            CombatPlayback playing = Playing(On(0), On(2), On(4), On(6));

            screen.After = pad => { if (pad.Told.Count == 2) playing.Faster(); };

            await PlaybackLoop.Play(playing, screen, clock, CancellationToken.None);

            Assert.That(clock.Waited, Is.EqualTo(new[] { 1000, 1000, 500, 225 }),
                "the second wait was decided before the button was pressed; the third was not");
        }
    }
}
