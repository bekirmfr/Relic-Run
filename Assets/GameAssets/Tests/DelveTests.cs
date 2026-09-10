using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Run;

namespace RelicRun.Tests
{
    /// <summary>
    /// A delve walked a stop at a time, which is how a screen walks one.
    /// </summary>
    /// <remarks>
    /// The corpus already covers most of this without meaning to: <c>DelveRun.Resolve</c> is now
    /// three lines over <see cref="Delve"/>, so 516 recorded runs replaying event for event is
    /// 516 proofs that answering stop by stop gives the same run. What it does NOT cover is the
    /// two things only an interactive caller does — stopping in the middle, and coming back.
    /// </remarks>
    [TestFixture]
    public class DelveTests
    {
        /// <summary>
        /// A run in progress is its seed and its answers, and nothing else.
        /// </summary>
        /// <remarks>
        /// The whole of how a delve survives the app closing. There is no engine state to write
        /// down: a saved run is one number and a short list, and resuming is building another
        /// delve and telling it what was already decided.
        ///
        /// Which is only true if replaying is EXACT, so that is what this asserts — not that the
        /// resumed run looks similar, but that it is standing on the same floor with the same
        /// health, the same purse and the same relics in the same order.
        /// </remarks>
        [Test]
        public void ARunIsResumedByReplayingIt()
        {
            foreach (uint seed in new uint[] { 7u, 4242u, 90210u })
            {
                var walked = new Delve(seed, Strong());
                var stops = 0;

                // Far enough in to have drafted, fought, and met whatever the gaps held.
                while (!walked.Finished && stops < 25)
                {
                    Decide(walked);
                    stops++;
                }

                Assert.That(walked.Finished, Is.False, "the fixture ran out of run to walk");
                Assert.That(walked.Answers.Count, Is.EqualTo(stops));

                var resumed = new Delve(seed, Strong());

                foreach (Answer answer in walked.Answers) resumed.Answer(answer);

                Assert.That(resumed.State.Floor, Is.EqualTo(walked.State.Floor), "seed " + seed);
                Assert.That(resumed.State.Php, Is.EqualTo(walked.State.Php));
                Assert.That(resumed.State.Pmax, Is.EqualTo(walked.State.Pmax));
                Assert.That(resumed.State.Gold, Is.EqualTo(walked.State.Gold));
                Assert.That(resumed.State.Hero.Kills, Is.EqualTo(walked.State.Hero.Kills));
                Assert.That(resumed.State.Items, Is.EqualTo(walked.State.Items));

                // And it is asking the same question, which is what makes it a resume rather
                // than a coincidence.
                Assert.That(resumed.Pending.Kind, Is.EqualTo(walked.Pending.Kind));
                Assert.That(resumed.Pending.Floor, Is.EqualTo(walked.Pending.Floor));
            }
        }

        /// <summary>
        /// Resuming from every point gives the same run, not just from one.
        /// </summary>
        /// <remarks>
        /// A resume that worked at twenty-five stops and not at twenty-four would be a save that
        /// corrupts a run depending on when somebody closed the app, which is the worst possible
        /// shape for this bug to take.
        /// </remarks>
        [Test]
        public void ResumingWorksFromEveryStop()
        {
            var whole = new Delve(88u, Strong());
            var answers = new List<Answer>();

            while (!whole.Finished)
            {
                Decide(whole);
                answers.Add(whole.Answers[whole.Answers.Count - 1]);
            }

            Assert.That(answers.Count, Is.GreaterThan(20), "the fixture was too short to matter");

            for (var upTo = 0; upTo <= answers.Count; upTo++)
            {
                var resumed = new Delve(88u, Strong());

                for (var i = 0; i < upTo; i++) resumed.Answer(answers[i]);

                if (upTo < answers.Count)
                {
                    Assert.That(resumed.Finished, Is.False, "ended early at stop " + upTo);
                    continue;
                }

                Assert.That(resumed.Finished, Is.True, "did not end where the original did");
                Assert.That(resumed.Ending, Is.EqualTo(whole.Ending));
                Assert.That(resumed.EndedOn, Is.EqualTo(whole.EndedOn));
                Assert.That(resumed.State.Gold, Is.EqualTo(whole.State.Gold));
                Assert.That(resumed.State.Items, Is.EqualTo(whole.State.Items));
            }
        }

        /// <summary>
        /// A fought floor stops the run, holding the fight it just resolved.
        /// </summary>
        /// <remarks>
        /// The stop that wants no answer, and the reason the whole thing is an iterator rather
        /// than a thread: the engine has resolved a floor and the screen has twelve seconds of
        /// reading it out to do, and nothing happens until the screen says so. Simulate, then
        /// replay, as control flow.
        /// </remarks>
        [Test]
        public void AFoughtFloorHandsOverTheWholeFight()
        {
            var delve = new Delve(1234u, Strong());
            var fights = 0;

            while (!delve.Finished && fights < 3)
            {
                if (delve.Pending.Kind == AskKind.Fought)
                {
                    fights++;

                    Assert.That(delve.Pending.Pack, Is.Not.Null.And.Not.Empty,
                        "a floor was fought against nobody");

                    Assert.That(delve.Pending.Result, Is.Not.Null);
                    Assert.That(delve.Pending.Result.Events, Is.Not.Null.And.Not.Empty,
                        "there is nothing to read out");

                    Assert.That(delve.Pending.Floor, Is.EqualTo(delve.State.Floor));
                }

                Decide(delve);
            }

            Assert.That(fights, Is.EqualTo(3), "the fixture never got three floors in");
        }

        /// <summary>
        /// Every stop is asked on the floor it belongs to, and the draft comes before the fight.
        /// </summary>
        /// <remarks>
        /// The order a screen will be drawing, checked once so that a stage machine written
        /// against it is written against something true. A draft that arrived after its fight
        /// would be offering a relic for a floor already walked.
        ///
        /// The reroll is the one that surprises. It is not asked on the first floor, because the
        /// delver has no gold and the run does not offer what cannot be paid for — so a screen
        /// that expects a reroll before every draft will wait for one that never comes.
        /// </remarks>
        [Test]
        public void TheDraftComesBeforeTheFightItIsFor()
        {
            var delve = new Delve(555u, Strong());
            var seen = new List<string>();

            while (!delve.Finished && seen.Count < 8)
            {
                seen.Add(delve.Pending.Kind + "@" + delve.Pending.Floor);
                Decide(delve);
            }

            // No reroll on the first floor: an empty purse is never asked to spend.
            Assert.That(seen[0], Is.EqualTo("Draft@1"));
            Assert.That(seen[1], Is.EqualTo("Fought@1"));
            Assert.That(seen[2], Is.EqualTo("CashOut@1"));

            // And one on the second, now that the first floor has paid out.
            Assert.That(seen[3], Is.EqualTo("Reroll@2"));
            Assert.That(seen[4], Is.EqualTo("Draft@2"));
            Assert.That(seen[5], Is.EqualTo("Fought@2"));
        }

        /// <summary>Answering a delve that has ended is a mistake worth hearing about.</summary>
        [Test]
        public void ADelveThatHasEndedCannotBeAnsweredAgain()
        {
            var delve = new Delve(3u, Frail());

            while (!delve.Finished) Decide(delve);

            Assert.Throws<System.InvalidOperationException>(() => delve.Answer());
        }

        /// <summary>Answers the pending stop the way a plain delver would.</summary>
        private static void Decide(Delve delve)
        {
            Ask ask = delve.Pending;

            switch (ask.Kind)
            {
                case AskKind.Draft:
                    ask.Answer = new Answer { Pick = ask.Offer[0] };
                    break;

                case AskKind.Event:
                    ask.Answer = new Answer { Choice = 0 };
                    break;

                case AskKind.Bazaar:
                    ask.Answer = new Answer { Deal = BazaarDeal.Walk };
                    break;

                default:
                    ask.Answer = new Answer();
                    break;
            }

            delve.Answer();
        }

        private static RunSetup Strong()
        {
            return new RunSetup
            {
                Hp = 5000,
                Atk = 400,
                Def = 40,
                Spd = 60,
                Lck = 10,
                Breath = 5,
                DraftChoices = 2,
                BazaarDeals = 1,
                Dungeon = DungeonConfig.ForTier(1),
                StartKit = new List<RelicId>(),
            };
        }

        private static RunSetup Frail()
        {
            return new RunSetup
            {
                Hp = 20,
                Atk = 1,
                Def = 0,
                Spd = 10,
                Lck = 0,
                Breath = 0,
                DraftChoices = 2,
                BazaarDeals = 1,
                Dungeon = DungeonConfig.ForTier(10),
                StartKit = new List<RelicId>(),
            };
        }
    }
}
