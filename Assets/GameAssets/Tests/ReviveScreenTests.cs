using NUnit.Framework;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;

namespace RelicRun.Tests
{
    /// <summary>
    /// The one second chance a run gets.
    /// </summary>
    /// <remarks>
    /// Offered once per delve and never again, so this is the last screen most runs ever show.
    /// What it must not get wrong is the arithmetic: a delver deciding whether to spend their
    /// whole balance is deciding on one number, and being told the wrong one costs them a run
    /// they could have kept.
    /// </remarks>
    [TestFixture]
    public class ReviveScreenTests
    {
        /// <summary>
        /// Afterwards means what is LEFT when it can be paid, and what is MISSING when it cannot.
        /// </summary>
        /// <remarks>
        /// One field carrying two meanings, which is worth pinning because it is exactly the kind
        /// of thing that reads fine and is backwards. The sign is never negative either way — the
        /// screen says which of the two it is in words.
        /// </remarks>
        [Test]
        public void AfterwardsIsWhatIsLeftOrWhatIsMissing()
        {
            ReviveCard rich = ReviveCards.Of(5, 400);

            Assert.That(rich.Afford, Is.True);
            Assert.That(rich.After, Is.EqualTo(400 - ReviveCards.Cost));

            ReviveCard poor = ReviveCards.Of(5, 90);

            Assert.That(poor.Afford, Is.False);
            Assert.That(poor.After, Is.EqualTo(ReviveCards.Cost - 90));

            Assert.That(poor.After, Is.GreaterThan(0), "being short read as a negative balance");
        }

        /// <summary>Exactly the price is enough.</summary>
        /// <remarks>
        /// The off-by-one that would sting most: a delver who has saved precisely a hundred and
        /// fifty sparks and is told they cannot afford the thing they saved for.
        /// </remarks>
        [Test]
        public void ExactlyEnoughIsEnough()
        {
            ReviveCard exact = ReviveCards.Of(3, ReviveCards.Cost);

            Assert.That(exact.Afford, Is.True);
            Assert.That(exact.After, Is.Zero);

            Assert.That(ReviveCards.Of(3, ReviveCards.Cost - 1).Afford, Is.False);
        }

        /// <summary>
        /// A run offers to bring the delver back exactly once.
        /// </summary>
        /// <remarks>
        /// The rule the screen is built on and the reason it has no "are you sure": the engine
        /// asks only while <c>Revived</c> is false, so a delver who rises and falls again is not
        /// asked a second time. Walked rather than asserted on the flag, because what matters is
        /// the number of ASKS a screen would have to answer.
        /// </remarks>
        [Test]
        public void ARunOffersToRaiseYouOnce()
        {
            var offers = 0;
            var risen = 0;

            // Deep enough to die in, and every fight taken rather than walked out of.
            for (var seed = 1u; seed <= 40u && risen < 1; seed++)
            {
                var delve = new Delve(seed, new RunSetup());

                var here = 0;

                while (!delve.Finished)
                {
                    Ask stop = delve.Pending;

                    if (stop.Kind == AskKind.Revive)
                    {
                        here++;
                        offers++;

                        delve.Answer(new Answer { Yes = true });
                        continue;
                    }

                    delve.Answer(Dully(stop));
                }

                Assert.That(here, Is.LessThanOrEqualTo(1),
                    "seed " + seed + " was offered a second life twice");

                if (here == 1) risen++;
            }

            Assert.That(offers, Is.GreaterThan(0), "no run in forty seeds ever fell");
        }

        /// <summary>Turning it down ends the run.</summary>
        /// <remarks>
        /// The answer a screen sends when ACCEPT DEATH is pressed is the empty one — no
        /// <c>Yes</c> — and this is what makes that the right thing to send.
        /// </remarks>
        [Test]
        public void AcceptingDeathEndsTheRun()
        {
            for (var seed = 1u; seed <= 40u; seed++)
            {
                var delve = new Delve(seed, new RunSetup());

                var fell = false;

                while (!delve.Finished)
                {
                    Ask stop = delve.Pending;

                    if (stop.Kind == AskKind.Revive)
                    {
                        fell = true;

                        delve.Answer(new Answer());
                        continue;
                    }

                    delve.Answer(Dully(stop));
                }

                if (!fell) continue;

                Assert.That(delve.Ending, Is.EqualTo(RunEnding.Died));
                return;
            }

            Assert.Fail("no run in forty seeds ever fell");
        }

        /// <summary>What a delver with no screen would answer, never walking out.</summary>
        private static Answer Dully(Ask stop)
        {
            switch (stop.Kind)
            {
                case AskKind.Draft:
                case AskKind.Reroll:
                    return new Answer { Pick = stop.Offer[0] };

                case AskKind.Bazaar:
                    return new Answer { Deal = BazaarDeal.Walk };

                default:
                    return new Answer();
            }
        }
    }
}
