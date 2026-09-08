using NUnit.Framework;
using RelicRun.Core.Presentation;

namespace RelicRun.Tests
{
    /// <summary>
    /// How long a fight takes to watch.
    /// </summary>
    /// <remarks>
    /// Ported by hand from the source's <c>evDelay</c> and the setup around it, because there is
    /// nothing to record: the timings live inside a React component's methods rather than in a
    /// named declaration the lifter can slice, and a recording of them would be a recording of
    /// how fast one machine ran on one afternoon.
    ///
    /// So the numbers are stated and the rules are asserted. The one that matters most is that
    /// playback follows the fight's OWN clock — every event carries the tick it happened on, and
    /// the wait between two events is how many ticks passed, not one beat each. That is the
    /// difference between a flurry reading as a flurry and a fight reading as a metronome.
    /// </remarks>
    [TestFixture]
    public class PacingTests
    {
        private static Pacing Fight(int events, int speed = 1, bool reduced = false,
            PacingRules rules = null)
        {
            return Pacing.For(events, reduced, speed, rules ?? PacingRules.Shipped());
        }

        [Test]
        public void AShortFightOpensAtASecondABeat()
        {
            Pacing pacing = Fight(10);

            Assert.That(pacing.StepMs, Is.EqualTo(1000));
            Assert.That(pacing.PerTickMs, Is.EqualTo(500));
            Assert.That(pacing.Speed, Is.EqualTo(1));
        }

        /// <summary>
        /// A long fight is shown faster from its first blow, not from the middle.
        /// </summary>
        /// <remarks>
        /// The count decides the beat before anything is drawn. A fight that accelerated as it
        /// went would read as the game catching up with itself.
        /// </remarks>
        [Test]
        public void ALongFightOpensFaster()
        {
            Assert.That(Fight(34).StepMs, Is.EqualTo(1000), "thirty-four is not yet long");
            Assert.That(Fight(35).StepMs, Is.EqualTo(700), "thirty-five is");

            Assert.That(Fight(34).PerTickMs, Is.EqualTo(500));
            Assert.That(Fight(35).PerTickMs, Is.EqualTo(350));
        }

        [Test]
        public void TheSpeedControlDividesTheBeat()
        {
            Assert.That(Fight(10, 1).StepMs, Is.EqualTo(1000));
            Assert.That(Fight(10, 2).StepMs, Is.EqualTo(500));
            Assert.That(Fight(10, 4).StepMs, Is.EqualTo(250));
        }

        [Test]
        public void TheSpeedControlCyclesAndWraps()
        {
            Assert.That(Fight(10, 1).NextSpeed(), Is.EqualTo(2));
            Assert.That(Fight(10, 2).NextSpeed(), Is.EqualTo(4));
            Assert.That(Fight(10, 4).NextSpeed(), Is.EqualTo(1), "the fastest wraps to the slowest");
        }

        /// <summary>
        /// Pressing the control three times lands where starting there would.
        /// </summary>
        /// <remarks>
        /// Derived from the full-speed figures each time rather than from the current ones.
        /// Halving a number that has already been halved and rounded drifts, and a delver who
        /// cycled back to ×1 would find the fight slightly the wrong speed with nothing to
        /// explain it.
        /// </remarks>
        [Test]
        public void CyclingRoundReturnsToWhereItStarted()
        {
            Pacing pacing = Fight(40);
            int step = pacing.StepMs;
            int perTick = pacing.PerTickMs;

            pacing = pacing.At(pacing.NextSpeed());
            pacing = pacing.At(pacing.NextSpeed());
            pacing = pacing.At(pacing.NextSpeed());

            Assert.That(pacing.Speed, Is.EqualTo(1));
            Assert.That(pacing.StepMs, Is.EqualTo(step));
            Assert.That(pacing.PerTickMs, Is.EqualTo(perTick));
        }

        /* ---------- the waits ---------- */

        /// <summary>
        /// The wait between two events is how far apart their ticks are.
        /// </summary>
        [Test]
        public void TheWaitFollowsTheFightsOwnClock()
        {
            Pacing pacing = Fight(10);

            Assert.That(pacing.Between(0, 1), Is.EqualTo(500), "one tick is half a second");
            Assert.That(pacing.Between(0, 2), Is.EqualTo(1000));
            Assert.That(pacing.Between(10, 13), Is.EqualTo(1500), "and it is the gap, not the tick");
        }

        /// <summary>Two events on the same tick land together.</summary>
        /// <remarks>
        /// This is most of a chain: a relic firing off a strike happens on the same tick as the
        /// strike, and a delver should see them as one thing happening rather than as two.
        /// </remarks>
        [Test]
        public void EventsOnOneTickGetTheShortestGap()
        {
            Pacing pacing = Fight(10);

            Assert.That(pacing.Between(7, 7), Is.EqualTo(pacing.ShortestMs));
            Assert.That(pacing.ShortestMs, Is.EqualTo(450), "forty-five hundredths of a beat");
        }

        /// <summary>A tick that goes backwards is a pause, not a crash.</summary>
        /// <remarks>
        /// It should not happen and nothing in the corpus does it. Answering rather than
        /// asserting is deliberate: a wrong answer here costs half a second of screen time, and
        /// an exception costs a fight nobody can replay.
        /// </remarks>
        [Test]
        public void ATickThatGoesBackwardsGetsTheShortestGapToo()
        {
            Pacing pacing = Fight(10);
            Assert.That(pacing.Between(9, 4), Is.EqualTo(pacing.ShortestMs));
        }

        /// <summary>
        /// A long wait is capped, because nothing is happening during it.
        /// </summary>
        /// <remarks>
        /// A slow delver against a slow foe can leave twenty ticks between blows. At half a
        /// second a tick that is ten seconds of an empty screen, which reads as the game having
        /// hung.
        /// </remarks>
        [Test]
        public void ALongSilenceIsCapped()
        {
            Pacing pacing = Fight(10);

            Assert.That(pacing.Between(0, 5), Is.EqualTo(2500), "just under the cap");
            Assert.That(pacing.Between(0, 6), Is.EqualTo(2600), "and this would have been 3000");
            Assert.That(pacing.Between(0, 40), Is.EqualTo(2600));
        }

        /// <summary>The last event has nothing to wait for and holds for the shortest gap.</summary>
        [Test]
        public void TheLastEventHoldsForTheShortestGap()
        {
            Pacing pacing = Fight(10);
            Assert.That(pacing.Last(), Is.EqualTo(pacing.ShortestMs));
        }

        /* ---------- reduced motion ---------- */

        /// <summary>
        /// A delver who asked for less motion gets a flat forty milliseconds and no choices.
        /// </summary>
        /// <remarks>
        /// Including the speed control, which does nothing at all here. Somebody asking their
        /// system for reduced motion is not asking to pick how much less: they want it over
        /// with, and the source obliges by ignoring the multiplier in the one place it would
        /// have applied.
        /// </remarks>
        [Test]
        public void ReducedMotionIgnoresEverythingIncludingTheSpeedControl()
        {
            foreach (int speed in new[] { 1, 2, 4 })
            {
                Pacing pacing = Fight(10, speed, true);

                Assert.That(pacing.Between(0, 1), Is.EqualTo(40));
                Assert.That(pacing.Between(0, 30), Is.EqualTo(40), "even a long silence");
                Assert.That(pacing.Between(3, 3), Is.EqualTo(40));
                Assert.That(pacing.Last(), Is.EqualTo(40));
            }
        }

        /// <summary>
        /// The beat and the clock say so too, not only the waits.
        /// </summary>
        /// <remarks>
        /// Both are public and nothing inside <see cref="Pacing"/> reads them under reduced
        /// motion — the waits answer before they get that far. Asserted anyway, because a caller
        /// asking a reduced-motion fight how long a beat is should be told forty rather than a
        /// thousand, and mutation found that nothing was holding them to it.
        /// </remarks>
        [Test]
        public void ReducedMotionShowsInTheBeatAndTheClockAsWell()
        {
            Pacing pacing = Fight(10, 1, true);

            Assert.That(pacing.StepMs, Is.EqualTo(40), "a reduced beat is the reduced number");
            Assert.That(pacing.PerTickMs, Is.Zero, "and the fight's clock is not followed at all");

            Assert.That(Fight(400, 1, true).StepMs, Is.EqualTo(40), "long or short");
            Assert.That(Fight(400, 1, true).PerTickMs, Is.Zero);
        }

        /// <summary>Reduced motion is reduced whether or not the fight was a long one.</summary>
        [Test]
        public void ReducedMotionDoesNotCareHowLongTheFightWas()
        {
            Assert.That(Fight(10, 1, true).Between(0, 4), Is.EqualTo(40));
            Assert.That(Fight(400, 1, true).Between(0, 4), Is.EqualTo(40));
        }

        /* ---------- the rounding ---------- */

        /// <summary>
        /// The shortest gap is rounded JavaScript's way, and there is a case where it shows.
        /// </summary>
        /// <remarks>
        /// At the fastest speed on a short fight the beat is 250 and forty-five hundredths of it
        /// is 112.5 exactly. JavaScript rounds a half upward and gets 113; .NET rounds a half to
        /// the nearest even and gets 112. One millisecond, in the one place the arithmetic lands
        /// on a half — which is exactly how this class of bug always looks.
        /// </remarks>
        [Test]
        public void TheShortestGapRoundsAsJavaScriptDoes()
        {
            Pacing fastest = Fight(10, 4);

            Assert.That(fastest.StepMs, Is.EqualTo(250));
            Assert.That(fastest.ShortestMs, Is.EqualTo(113),
                "112.5 rounds up, and .NET left alone would have said 112");
        }

        [Test]
        public void TheBeatRoundsAsJavaScriptDoes()
        {
            // 700 at double speed is 350 exactly; the long fight's per-tick figure is the half.
            Pacing busy = Fight(100, 4);

            Assert.That(busy.StepMs, Is.EqualTo(175));
            Assert.That(busy.PerTickMs, Is.EqualTo(88), "87.5 rounds up");
        }

        /// <summary>
        /// A beat somebody authored rounds JavaScript's way too.
        /// </summary>
        /// <remarks>
        /// None of the shipped numbers reaches a half where the two roundings disagree — 87.5
        /// goes to 88 either way, because .NET rounds a half to the nearest EVEN and 88 is even.
        /// So with the numbers as they stand, dividing a duration the wrong way is invisible, and
        /// mutation said so.
        ///
        /// The numbers are authored, though. A person who sets a beat of 450 and presses the
        /// control twice lands on 112.5, where the two disagree, and would find their fight one
        /// millisecond out for a reason nothing in the Inspector could explain.
        /// </remarks>
        [Test]
        public void ABeatNobodyHasChosenYetRoundsTheSameWay()
        {
            var authored = PacingRules.Shipped();
            authored.StepMs = 450;
            authored.PerTickMs = 225;

            Pacing pacing = Fight(10, 4, false, authored);

            Assert.That(pacing.StepMs, Is.EqualTo(113), "112.5 rounds up; .NET would say 112");
            Assert.That(pacing.PerTickMs, Is.EqualTo(56), "56.25 is not a half and rounds down");

            authored.PerTickMs = 450;
            Assert.That(Fight(10, 4, false, authored).PerTickMs, Is.EqualTo(113),
                "and the clock rounds the same way the beat does");
        }

        /* ---------- the change from the source ---------- */

        /// <summary>
        /// The shipped rules let the speed control shorten the long waits. The source did not.
        /// </summary>
        /// <remarks>
        /// The source divides both figures by the speed when a fight opens and then recomputes
        /// only the beat when the button is pressed. So a delver pressing it mid-fight shortened
        /// the gaps between events on the same tick and changed nothing about the long waits,
        /// which are most of what they were trying to skip.
        ///
        /// Both are kept so the difference is one line rather than a memory.
        /// </remarks>
        [Test]
        public void PressingTheControlNowShortensTheLongWaitsToo()
        {
            Pacing shipped = Fight(10).At(4);
            Pacing recorded = Fight(10, 1, false, PacingRules.AsRecorded()).At(4);

            Assert.That(shipped.StepMs, Is.EqualTo(recorded.StepMs), "the beat agrees either way");

            Assert.That(shipped.PerTickMs, Is.EqualTo(125), "a tick is worth a quarter as much");
            Assert.That(recorded.PerTickMs, Is.EqualTo(500), "the source left this alone");

            Assert.That(shipped.Between(0, 4), Is.EqualTo(500));
            Assert.That(recorded.Between(0, 4), Is.EqualTo(2000),
                "four ticks still cost two seconds however fast the delver asked for it");
        }

        /// <summary>Starting at a speed and cycling to it agree under the shipped rules.</summary>
        /// <remarks>
        /// They do not under the source's, which is the clearest statement of what was wrong
        /// with it: the same fight at the same speed played at two different paces depending on
        /// whether the delver had touched the button.
        /// </remarks>
        [Test]
        public void StartingFastAndBecomingFastAgreeNow()
        {
            Pacing started = Fight(10, 4);
            Pacing became = Fight(10).At(4);

            Assert.That(became.StepMs, Is.EqualTo(started.StepMs));
            Assert.That(became.PerTickMs, Is.EqualTo(started.PerTickMs));

            Pacing sourceBecame = Fight(10, 1, false, PacingRules.AsRecorded()).At(4);
            Pacing sourceStarted = Fight(10, 4, false, PacingRules.AsRecorded());

            Assert.That(sourceBecame.PerTickMs, Is.Not.EqualTo(sourceStarted.PerTickMs),
                "which is the disagreement the shipped rules remove");
        }

        /* ---------- the awkward inputs ---------- */

        [Test]
        public void AFightWithNoEventsIsStillPaced()
        {
            Pacing pacing = Fight(0);

            Assert.That(pacing.StepMs, Is.EqualTo(1000));
            Assert.That(pacing.Last(), Is.EqualTo(450));
        }

        [Test]
        public void ASpeedNobodyOffersIsNotACrash()
        {
            Assert.That(Fight(10, 0).Speed, Is.EqualTo(1), "zero would divide by nothing");
            Assert.That(Fight(10, -3).Speed, Is.EqualTo(1));

            Pacing odd = Fight(10, 3);
            Assert.That(odd.StepMs, Is.EqualTo(333), "and a speed off the list still works");
            Assert.That(odd.NextSpeed(), Is.EqualTo(1), "though the control cannot find it");
        }

        [Test]
        public void NoRulesAtAllMeansTheShippedOnes()
        {
            Pacing pacing = Pacing.For(10, false, 1, null);
            Assert.That(pacing.StepMs, Is.EqualTo(PacingRules.Shipped().StepMs));
        }
    }
}
