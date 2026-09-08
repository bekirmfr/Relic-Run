using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Core.Stats;

namespace RelicRun.Tests
{
    /// <summary>
    /// Walking a finished fight: what to show, and how long to hold it.
    /// </summary>
    /// <remarks>
    /// The stepper has no timers in it, which is the point. The source could not avoid them — a
    /// browser timer cannot be recalled once set, so a fight there was a chain of callbacks kept
    /// honest by a token, pausing meant re-scheduling a poll every 180ms, and starting a fresh
    /// fight meant bumping a counter so the old chain would notice it was stale. All of that is
    /// scaffolding for a language problem, and none of it is a rule about how a fight is shown.
    ///
    /// What is left after taking it out is small enough to state: a cursor, a hall walk that
    /// happens once, and a hold whose length comes from the fight's own clock.
    /// </remarks>
    [TestFixture]
    public class CombatPlaybackTests
    {
        private static readonly IReadOnlyList<RelicId> NoRelics = new RelicId[0];
        private static readonly IReadOnlyList<StatModifier> NoMods = new StatModifier[0];

        /// <summary>An event of a kind, on a tick. Nothing else about it matters here.</summary>
        private static CombatEvent On(int tick, CombatEventType type = CombatEventType.EnemyDamage)
        {
            var state = new CombatSnapshot(tick, 100, 50, 0, 50, 5, EnemyRank.Guard, 0, 25, 10,
                0, 0, 0, NoRelics, NoMods, default(CombatCounters));

            return new CombatEvent(type, 0, state);
        }

        private static CombatPlayback Playing(params CombatEvent[] events)
        {
            return CombatPlayback.Of(events, false, 1, PacingRules.Shipped());
        }

        [Test]
        public void AFightIsShownOneEventAtATime()
        {
            CombatPlayback playing = Playing(On(0), On(1), On(2));

            Assert.That(playing.Finished, Is.False);
            Assert.That(playing.Count, Is.EqualTo(3));

            for (int i = 0; i < 3; i++)
            {
                PlaybackStep step = playing.Next();

                Assert.That(step.Action, Is.EqualTo(PlaybackAction.Show));
                Assert.That(step.Index, Is.EqualTo(i));
            }

            Assert.That(playing.Finished, Is.True);
            Assert.That(playing.Next().Action, Is.EqualTo(PlaybackAction.Done));
        }

        [Test]
        public void AFightWithNothingInItIsAlreadyShown()
        {
            CombatPlayback playing = Playing();

            Assert.That(playing.Finished, Is.True);
            Assert.That(playing.Next().Action, Is.EqualTo(PlaybackAction.Done));
            Assert.That(playing.Next().Index, Is.EqualTo(-1), "and nothing to point at");
        }

        /// <summary>
        /// A fight is paced by its own length, decided before the first blow is drawn.
        /// </summary>
        /// <remarks>
        /// Which is why building the playback is the only sanctioned way in: a caller who handed
        /// it a pacing made from some other fight's length would show this one at the wrong speed
        /// with nothing anywhere to say so.
        /// </remarks>
        [Test]
        public void AFightIsPacedByItsOwnLength()
        {
            var brief = new CombatEvent[10];
            for (int i = 0; i < brief.Length; i++) brief[i] = On(i);

            var lengthy = new CombatEvent[40];
            for (int i = 0; i < lengthy.Length; i++) lengthy[i] = On(i);

            Assert.That(CombatPlayback.Of(brief, false, 1, PacingRules.Shipped()).Pacing.StepMs,
                Is.EqualTo(1000));
            Assert.That(CombatPlayback.Of(lengthy, false, 1, PacingRules.Shipped()).Pacing.StepMs,
                Is.EqualTo(700), "forty events is a long fight and is shown faster");
        }

        /// <summary>The hold after an event is the gap to the next event's tick.</summary>
        /// <remarks>
        /// Looking FORWARD, not back. An event is shown and then the screen waits for whatever
        /// happens next, which is what makes a chain land as one thing and a slow exchange breathe.
        /// </remarks>
        [Test]
        public void TheHoldComesFromTheNextEventsTick()
        {
            CombatPlayback playing = Playing(On(0), On(0), On(3));

            Assert.That(playing.Next().WaitMs, Is.EqualTo(450), "nothing between them, so the floor");
            Assert.That(playing.Next().WaitMs, Is.EqualTo(1500), "three ticks away");
            Assert.That(playing.Next().WaitMs, Is.EqualTo(450), "and the last has nothing to wait for");
        }

        /* ---------- the walk ---------- */

        /// <summary>
        /// A foe entering is announced twice: once to walk to them, once to show it.
        /// </summary>
        /// <remarks>
        /// The walk does not consume the event. That is why the source keeps a <c>_walked</c>
        /// index at all — the walk is what puts the cursor back, so without a memory of having
        /// walked already, the same foe would be approached forever.
        /// </remarks>
        [Test]
        public void MeetingAFoeWalksTheHallFirstAndShowsItSecond()
        {
            CombatPlayback playing = Playing(On(0, CombatEventType.Enter), On(2));

            PlaybackStep walk = playing.Next();
            Assert.That(walk.Action, Is.EqualTo(PlaybackAction.Walk));
            Assert.That(walk.Index, Is.EqualTo(0));
            Assert.That(walk.WaitMs, Is.EqualTo(3350));
            Assert.That(playing.Cursor, Is.Zero, "the walk did not consume it");

            PlaybackStep show = playing.Next();
            Assert.That(show.Action, Is.EqualTo(PlaybackAction.Show));
            Assert.That(show.Index, Is.EqualTo(0), "and now the same event is shown");
            Assert.That(playing.Cursor, Is.EqualTo(1));
        }

        /// <summary>Every foe in a pack is walked to, and each exactly once.</summary>
        [Test]
        public void EachFoeIsWalkedToOnce()
        {
            CombatPlayback playing = Playing(
                On(0, CombatEventType.Enter), On(1),
                On(2, CombatEventType.Enter), On(3));

            var actions = new List<string>();
            for (PlaybackStep step = playing.Next();
                 step.Action != PlaybackAction.Done;
                 step = playing.Next())
            {
                actions.Add(step.Action + ":" + step.Index);

                // A cursor that stops advancing turns this loop into a forever, and a test that
                // hangs is worse than one that fails: mutation runs it dozens of times with no
                // console to watch, and a wedged suite looks exactly like a slow one.
                Assert.That(actions.Count, Is.LessThan(32), "playback is not advancing");
            }

            Assert.That(actions, Is.EqualTo(new[]
            {
                "Walk:0", "Show:0", "Show:1", "Walk:2", "Show:2", "Show:3",
            }));
        }

        /// <summary>A delver who asked for less motion is not walked anywhere.</summary>
        [Test]
        public void ReducedMotionSkipsTheWalk()
        {
            CombatPlayback playing = CombatPlayback.Of(
                new[] { On(0, CombatEventType.Enter), On(1) }, true, 1, PacingRules.Shipped());

            Assert.That(playing.Next().Action, Is.EqualTo(PlaybackAction.Show));
            Assert.That(playing.Cursor, Is.EqualTo(1));
        }

        /// <summary>So is a delver who asked to get on with it.</summary>
        /// <remarks>
        /// The same effect for a different reason, which is why they are two arguments and not
        /// one. Reduced motion is a system setting about animation; skipping the intro is a
        /// delver in a hurry, and the day one of them stops suppressing the walk the other
        /// should not follow it.
        /// </remarks>
        [Test]
        public void SkippingTheIntroSkipsTheWalkToo()
        {
            CombatPlayback playing = CombatPlayback.Of(
                new[] { On(0, CombatEventType.Enter), On(1) }, false, 1, PacingRules.Shipped(),
                true);

            Assert.That(playing.Next().Action, Is.EqualTo(PlaybackAction.Show));
        }

        /// <summary>A walk of no length is no walk.</summary>
        [Test]
        public void AWalkNobodyAuthoredIsNotTaken()
        {
            var rules = PacingRules.Shipped();
            rules.WalkMs = 0;

            CombatPlayback playing = CombatPlayback.Of(
                new[] { On(0, CombatEventType.Enter) }, false, 1, rules);

            Assert.That(playing.Next().Action, Is.EqualTo(PlaybackAction.Show));
        }

        /* ---------- the speed control ---------- */

        /// <summary>Pressing the control changes the next hold, not the one already running.</summary>
        [Test]
        public void TheSpeedControlTakesEffectOnTheNextHold()
        {
            CombatPlayback playing = Playing(On(0), On(2), On(4));

            Assert.That(playing.Next().WaitMs, Is.EqualTo(1000), "two ticks at half a second");

            Assert.That(playing.Faster(), Is.EqualTo(2));
            Assert.That(playing.Next().WaitMs, Is.EqualTo(500), "the same two ticks, twice as fast");
        }

        [Test]
        public void TheSpeedControlKeepsItsPlaceInTheFight()
        {
            CombatPlayback playing = Playing(On(0), On(1), On(2));

            playing.Next();
            playing.Faster();

            Assert.That(playing.Cursor, Is.EqualTo(1), "pressing it shows nothing and skips nothing");
        }

        /* ---------- skipping ---------- */

        /// <summary>
        /// Skipping ends the showing and keeps the events.
        /// </summary>
        /// <remarks>
        /// The log still needs every one of them however fast somebody wanted through the fight,
        /// so the events are not thrown away — only the handing out of them stops.
        /// </remarks>
        [Test]
        public void SkippingEndsTheShowingAndKeepsTheEvents()
        {
            CombatPlayback playing = Playing(On(0), On(1), On(2));
            playing.Next();

            playing.Skip();

            Assert.That(playing.Finished, Is.True);
            Assert.That(playing.Next().Action, Is.EqualTo(PlaybackAction.Done));
            Assert.That(playing.Count, Is.EqualTo(3), "the fight still happened");
            Assert.That(playing.At(2).State.Tick, Is.EqualTo(2), "and can still be read");
        }

        /* ---------- a real fight ---------- */

        /// <summary>
        /// A recorded fight is walked to its end, and takes a plausible time to watch.
        /// </summary>
        /// <remarks>
        /// The synthetic cases above state the rules; this one asks whether they add up over a
        /// real event stream. A fight that took an hour to show, or ended after two events,
        /// would pass every rule above.
        /// </remarks>
        [Test]
        public void ARecordedFightIsShownInAPlausibleTime()
        {
            IReadOnlyList<CombatEvent> events = ARecordedFight();
            Assert.That(events.Count, Is.GreaterThan(10), "this fight is too short to mean anything");

            CombatPlayback playing = CombatPlayback.Of(events, false, 1, PacingRules.Shipped());

            int shown = 0;
            int walked = 0;
            long total = 0;

            for (PlaybackStep step = playing.Next();
                 step.Action != PlaybackAction.Done;
                 step = playing.Next())
            {
                if (step.Action == PlaybackAction.Show) shown++;
                else walked++;

                total += step.WaitMs;

                Assert.That(step.WaitMs, Is.GreaterThan(0), "a hold of nothing is not a hold");
                Assert.That(shown + walked, Is.LessThan(events.Count * 3 + 10),
                    "playback is not advancing");
            }

            Assert.That(shown, Is.EqualTo(events.Count), "every event was shown exactly once");
            Assert.That(walked, Is.GreaterThan(0), "a fight opens by meeting somebody");

            Assert.That(total, Is.GreaterThan(2000), "a whole fight in under two seconds");
            Assert.That(total, Is.LessThan(180000), "over three minutes to watch one fight");
        }

        /// <summary>
        /// The busiest of the first few recorded fights, resolved for its events.
        /// </summary>
        /// <remarks>
        /// The busiest rather than the first, because a two-event fight would satisfy every
        /// assertion above without exercising anything. Resolved rather than read out of the
        /// corpus JSON: the recording is the gate for the ENGINE, and what playback needs is the
        /// engine's own output, which is the same thing by the time the gate is green.
        /// </remarks>
        private static IReadOnlyList<CombatEvent> ARecordedFight()
        {
            List<Support.CorpusFight.Case> cases = Support.CorpusFight.Load("bare.json");
            Support.CorpusFight.Case busiest = null;

            for (int i = 0; i < cases.Count && i < 20; i++)
            {
                if (busiest == null || cases[i].Events.Count > busiest.Events.Count) busiest = cases[i];
            }

            var engine = new CombatEngine(CombatRules.DelveAsRecorded());
            return engine.ResolveFloor(busiest.Hero, busiest.Pack,
                new Core.Determinism.Mulberry32(busiest.FightSeed)).Events;
        }
    }
}
