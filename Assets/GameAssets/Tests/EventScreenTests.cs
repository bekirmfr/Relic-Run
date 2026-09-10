using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Run;

namespace RelicRun.Tests
{
    /// <summary>
    /// The event between two floors: a blind choice, and the sentence that says what it did.
    /// </summary>
    /// <remarks>
    /// The only place in a run where a delver decides without being shown the odds. A hint says
    /// what a choice COSTS — never what it does — and nine of the twenty-six roll for it, so the
    /// line afterwards is the whole payoff and the only place the run explains itself.
    ///
    /// The prose and the logic live apart on purpose: one is generated from the source drop and
    /// the other is hand-ported against the corpus. These are the tests that stop them drifting.
    /// </remarks>
    [TestFixture]
    public class EventScreenTests
    {
        /// <summary>
        /// Every event has as many labels as it has choices, and in the same order.
        /// </summary>
        /// <remarks>
        /// The failure this exists for is silent and expensive: a label describing a different
        /// choice than the one it sits on. A delver would press "Walk away" and pay sixty gold,
        /// and nothing anywhere would report it.
        /// </remarks>
        [Test]
        public void EveryChoiceHasItsOwnLabel()
        {
            Assert.That(EventText.All.Count, Is.EqualTo(DungeonEvents.All.Count),
                "the prose and the logic disagree about how many events there are");

            for (var i = 0; i < DungeonEvents.All.Count; i++)
            {
                DungeonEvent ev = DungeonEvents.All[i];
                EventTextDef said = EventText.Get(i);

                Assert.That(said.Choices.Count, Is.EqualTo(ev.Choices.Count),
                    "event " + i + " (" + said.Title + ") has " + ev.Choices.Count +
                    " choices and " + said.Choices.Count + " labels");

                foreach (EventChoiceText choice in said.Choices)
                {
                    Assert.That(choice.Label, Is.Not.Empty, "a choice of event " + i + " is unlabelled");
                    Assert.That(choice.Hint, Is.Not.Null, "a hint was null rather than empty");
                }

                Assert.That(said.Title, Is.Not.Empty);
                Assert.That(said.Text, Is.Not.Empty);
                Assert.That(said.Kicker, Is.Not.Empty);
            }
        }

        /// <summary>
        /// A choice with a price shows the delver that price before they press it.
        /// </summary>
        /// <remarks>
        /// In the LABEL or in the hint, either will do — the Imp's Dice says "Bet 15 gold" on the
        /// button and spends its hint on the odds instead. What matters is that the number is
        /// somewhere a delver reads, because the affordability gate is silent: a choice they
        /// cannot pay for simply goes dim.
        ///
        /// This started out asserting the hint said GOLD, which the dice broke immediately.
        /// </remarks>
        [Test]
        public void APricedChoiceShowsItsPrice()
        {
            var priced = 0;

            for (var i = 0; i < DungeonEvents.All.Count; i++)
            {
                DungeonEvent ev = DungeonEvents.All[i];
                EventTextDef said = EventText.Get(i);

                for (var c = 0; c < ev.Choices.Count; c++)
                {
                    int cost = ev.Choices[c].Cost;

                    string label = said.Choices[c].Label;
                    string hint = said.Choices[c].Hint;
                    string shown = label + " " + hint;

                    if (cost > 0)
                    {
                        priced++;

                        Assert.That(shown, Does.Contain(cost.ToString()),
                            "event " + i + " charges " + cost + " for \"" + label +
                            "\" and says so nowhere: hint reads \"" + hint + "\"");

                        continue;
                    }

                    // And nothing free claims to cost gold. A hint may still say COSTS 20 HP —
                    // the Pale Merchant's second offer is paid in blood, not coin.
                    bool claims = hint.Contains("COSTS") && hint.Contains("GOLD");

                    Assert.That(claims, Is.False,
                        "event " + i + " choice \"" + label + "\" is free but its hint reads \"" +
                        hint + "\"");
                }
            }

            Assert.That(priced, Is.GreaterThan(0), "no event charges for anything");
        }

        /// <summary>
        /// Every choice of every event says something afterwards.
        /// </summary>
        /// <remarks>
        /// Swept over every branch, both ways, by rolling each choice with a hundred seeds. Nine
        /// of them fork on a roll and a fork with nothing to say would be a screen that went
        /// blank on exactly the outcomes worth reading.
        /// </remarks>
        [Test]
        public void EveryOutcomeHasWordsForWhatHappened()
        {
            for (var i = 0; i < DungeonEvents.All.Count; i++)
            {
                DungeonEvent ev = DungeonEvents.All[i];

                for (var c = 0; c < ev.Choices.Count; c++)
                {
                    for (var seed = 1u; seed <= 100u; seed++)
                    {
                        var run = new RunState { Gold = 500 };

                        EventOutcome came = ev.Choices[c].Resolve(run, new Mulberry32(seed));

                        Assert.That(came.Text, Is.Not.Null.And.Not.Empty,
                            "event " + i + " choice " + c + " said nothing on seed " + seed);
                    }
                }
            }
        }

        /// <summary>
        /// A relic granted is carried, and a sentence that names one has somewhere to put it.
        /// </summary>
        /// <remarks>
        /// Core does not translate and nineteen of the fifty relics have names that are
        /// translated, so an outcome cannot spell one out — it leaves a hole and hands over the
        /// id. A hole with no relic behind it would print the word <c>{relic}</c> at a delver;
        /// a relic with no hole would hand one over without saying so.
        /// </remarks>
        [Test]
        public void AGrantedRelicHasAHoleToGoIn()
        {
            var granted = 0;

            for (var i = 0; i < DungeonEvents.All.Count; i++)
            {
                DungeonEvent ev = DungeonEvents.All[i];

                for (var c = 0; c < ev.Choices.Count; c++)
                {
                    for (var seed = 1u; seed <= 40u; seed++)
                    {
                        var run = new RunState { Gold = 500 };

                        EventOutcome came = ev.Choices[c].Resolve(run, new Mulberry32(seed));

                        bool hole = came.Text.Contains("{relic}");
                        bool gave = came.Relic != RelicId.None;

                        Assert.That(hole, Is.EqualTo(gave),
                            "event " + i + " choice " + c + " on seed " + seed + ": \"" +
                            came.Text + "\" with relic " + came.Relic);

                        if (gave) granted++;
                    }
                }
            }

            Assert.That(granted, Is.GreaterThan(0), "no event ever handed over a relic");
        }

        /// <summary>
        /// An event can hurt, but it can never kill.
        /// </summary>
        /// <remarks>
        /// The rule that makes the Spike Trap a scare rather than an ending. Every branch of
        /// every choice is rolled at one health and the delver has to still be standing — which
        /// is why the run loop asks nothing about health between floors, and why only a fight
        /// can end a delve.
        ///
        /// This is where a test about "there is always a safe way out" ended up, because there
        /// is NOT: the Rusted Spike Trap offers two bad choices and no third, and the thief will
        /// rob a delver who fights him for free. The floor at one is the real promise.
        /// </remarks>
        [Test]
        public void AnEventCanHurtButNeverKill()
        {
            for (var i = 0; i < DungeonEvents.All.Count; i++)
            {
                for (var c = 0; c < DungeonEvents.All[i].Choices.Count; c++)
                {
                    for (var seed = 1u; seed <= 60u; seed++)
                    {
                        var delve = new Delve(seed, new RunSetup());

                        var walked = false;

                        while (!delve.Finished && !walked)
                        {
                            Ask stop = delve.Pending;

                            if (stop.Kind == AskKind.Event)
                            {
                                // The floor is put to one first, so every choice is taken by
                                // somebody who cannot afford to lose anything at all.
                                delve.State.Php = 1;

                                int pick = c < stop.Event.Choices.Count ? c : 0;

                                delve.Answer(new Answer { Choice = pick });

                                Assert.That(delve.State.Php, Is.GreaterThan(0),
                                    "event " + stop.Event.Index + " choice " + pick +
                                    " killed a delver on seed " + seed);

                                walked = true;
                                continue;
                            }

                            delve.Answer(Plainly(stop));
                        }
                    }
                }
            }
        }

        /// <summary>What a delver with no screen would answer.</summary>
        private static Answer Plainly(Ask stop)
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
