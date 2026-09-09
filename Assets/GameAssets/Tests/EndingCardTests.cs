using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Meta;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// The screens either side of a run: how to play, how it ended, what it earned, and where
    /// the next one is fought.
    /// </summary>
    /// <remarks>
    /// Three of the four are English literals in the source and the fourth — the how-to — is one
    /// translated paragraph the game takes apart itself. Which is which was checked in the
    /// markup rather than guessed, because two screens next to each other can differ.
    /// </remarks>
    [TestFixture]
    public class EndingCardTests
    {
        /* ---------- how to play ---------- */

        /// <summary>
        /// The whole screen is one string, split on the breaks written into it.
        /// </summary>
        /// <remarks>
        /// So the number of steps belongs to the TRANSLATION rather than to the game. Held
        /// against the shipped English, which has three — and against every other language, none
        /// of which is allowed to produce none.
        /// </remarks>
        [Test]
        public void TheHowToIsTakenApartOnItsOwnBreaks()
        {
            foreach (JToken language in (JArray)Corpus.Object("strings.json")["languages"])
            {
                string tag = language.Value<string>();
                Dictionary<string, string> table = Corpus.LocaleTable(tag);

                string body;
                if (!table.TryGetValue("howtoBody", out body)) continue;

                HowCard card = HowCards.Of(body);

                Assert.That(card.Steps, Is.Not.Empty, tag + " has no steps at all");

                for (var i = 0; i < card.Steps.Count; i++)
                {
                    HowStep step = card.Steps[i];

                    Assert.That(step.Number, Is.EqualTo((i + 1).ToString("00")), tag);
                    Assert.That(step.Text, Is.Not.Empty, tag + " step " + step.Number);

                    // The markup is stripped, not rendered. Showing a delver a <b> would be
                    // showing them something the game has never shown anybody.
                    Assert.That(step.Text, Does.Not.Contain("<"), tag + " step " + step.Number);
                    Assert.That(step.Text, Does.Not.Contain(">"), tag + " step " + step.Number);
                }
            }
        }

        /// <summary>Nothing in, nothing out — and no crash on the way.</summary>
        /// <remarks>
        /// A locale that has not arrived answers with an empty string, and this screen is drawn
        /// while one is still being fetched.
        /// </remarks>
        [Test]
        public void AnEmptyHowToIsEmptyRatherThanBroken()
        {
            foreach (string nothing in new[] { null, "", "<br>", "<br><br>", "<b></b>" })
            {
                HowCard card = HowCards.Of(nothing);

                Assert.That(card.Steps, Is.Not.Null);
                Assert.That(card.Steps, Is.Empty, "[" + nothing + "]");
            }
        }

        /// <summary>Stripping takes the tags and leaves the words.</summary>
        [Test]
        public void StrippingLeavesTheWords()
        {
            Assert.That(HowCards.Bare("<b>Each floor</b> — pick one"),
                Is.EqualTo("Each floor — pick one"));

            Assert.That(HowCards.Bare("no markup here"), Is.EqualTo("no markup here"));
            Assert.That(HowCards.Bare(""), Is.Empty);
            Assert.That(HowCards.Bare(null), Is.Empty);

            // An unclosed tag eats the rest, which is what a browser does too — and is better
            // than showing half a tag to a delver.
            Assert.That(HowCards.Bare("before <b after"), Is.EqualTo("before "));
        }

        /* ---------- the end of a run ---------- */

        /// <summary>Three endings, three different things said.</summary>
        /// <remarks>
        /// Not variations on each other: dying loses the purse, cashing out keeps it, and
        /// clearing keeps it and means something. A screen that said the same thing three ways
        /// would flatten the only three outcomes the game has.
        /// </remarks>
        [Test]
        public void EachEndingSaysSomethingDifferent()
        {
            var titles = new List<string>();
            var notes = new List<string>();

            foreach (RunEnding ending in new[]
            {
                RunEnding.Died, RunEnding.CashedOut, RunEnding.Cleared,
            })
            {
                OverCard card = OverCards.Of(new RunReward { Score = 900 }, ending, 7, 20,
                    false, null, 1d);

                Assert.That(card.Ending, Is.EqualTo(ending));
                Assert.That(card.Title, Is.Not.Empty);
                Assert.That(card.TitleUnder, Is.Not.Empty);
                Assert.That(card.Note, Is.Not.Empty);

                Assert.That(titles.Contains(card.Title + card.TitleUnder), Is.False,
                    ending + " says what another ending says");
                Assert.That(notes.Contains(card.Note), Is.False, ending + " notes a repeat");

                titles.Add(card.Title + card.TitleUnder);
                notes.Add(card.Note);
            }
        }

        /// <summary>A new best is announced, and only when it is one.</summary>
        [Test]
        public void ANewBestIsAnnouncedOnlyWhenItIsOne()
        {
            RunReward reward = new RunReward { Score = 2000 };

            OverCard beaten = OverCards.Of(reward, RunEnding.Cleared, 13, 40, true, "New best", 1d);

            Assert.That(beaten.Best, Is.True);
            Assert.That(beaten.Note, Does.StartWith("New best"));
            Assert.That(beaten.Note, Does.Contain(OverCards.KeptAll));

            OverCard ordinary = OverCards.Of(reward, RunEnding.Cleared, 13, 40, false, "New best", 1d);

            Assert.That(ordinary.Note, Is.EqualTo(OverCards.KeptAll));

            // A locale that has not arrived leaves the words missing, and the rest of the line
            // still has to read.
            OverCard wordless = OverCards.Of(reward, RunEnding.Cleared, 13, 40, true, null, 1d);

            Assert.That(wordless.Note, Is.EqualTo(OverCards.KeptAll));
        }

        /// <summary>The hall's rate is shown when there is one, and not invented when there is not.</summary>
        [Test]
        public void TheRateIsShownWhenThereIsOne()
        {
            Assert.That(OverCards.Of(new RunReward(), RunEnding.Died, 3, 4, false, null, 0.8d).Rate,
                Is.EqualTo("XP ×0.80"));

            Assert.That(OverCards.Of(new RunReward(), RunEnding.Died, 3, 4, false, null, 0d).Rate,
                Is.Null);
        }

        /* ---------- what it earned ---------- */

        /// <summary>The heading only shouts once the bar has arrived.</summary>
        /// <remarks>
        /// The bar animates across a level boundary, and the heading is what makes the moment.
        /// Shouting LEVEL UP while the bar is still travelling spends it early.
        /// </remarks>
        [Test]
        public void TheLevelUpIsAnnouncedWhenTheBarArrives()
        {
            var gains = new[] { "+5 HP" };

            Assert.That(XpCards.Of(400, 4, 0.5d, false, gains).Kicker, Is.EqualTo(XpCards.Gaining));
            Assert.That(XpCards.Of(400, 4, 0.5d, true, gains).Kicker, Is.EqualTo(XpCards.Levelled));

            // Arriving with nothing to announce is not a level-up.
            Assert.That(XpCards.Of(400, 4, 0.5d, true, new string[0]).Kicker,
                Is.EqualTo(XpCards.Gaining));
            Assert.That(XpCards.Of(400, 4, 0.5d, true, null).LevelledUp, Is.False);
        }

        /// <summary>What was earned is always signed, even when it is nothing.</summary>
        [Test]
        public void WhatWasEarnedIsAlwaysSigned()
        {
            Assert.That(XpCards.Of(0, 1, 0d, false, null).Gained, Is.EqualTo("+0"));
            Assert.That(XpCards.Of(240, 1, 0d, false, null).Gained, Is.EqualTo("+240"));
        }

        /// <summary>At the ceiling the bar is full and nothing is promised beyond it.</summary>
        [Test]
        public void TheTopLevelFillsTheBarAndPromisesNothing()
        {
            XpCard card = XpCards.Of(100, Progression.MaxLevel, 0d, false, null);

            Assert.That(card.Next, Is.EqualTo(XpCards.AtTheTop));
            Assert.That(card.Progress, Is.EqualTo(1d));

            XpCard below = XpCards.Of(100, Progression.MaxLevel - 1, 0.25d, false, null);

            Assert.That(below.Next, Does.Contain((Progression.MaxLevel).ToString()));
            Assert.That(below.Progress, Is.EqualTo(0.25d));
        }

        /* ---------- the staging hall ---------- */

        /// <summary>Only halls the delver has opened are offered.</summary>
        /// <remarks>
        /// Clamped to the frontier rather than to the catalog. Offering a hall the arena would
        /// then refuse is worse than not offering it — a delver who picks it has been told yes
        /// and then no.
        /// </remarks>
        [Test]
        public void OnlyOpenedHallsCanHostAMatch()
        {
            var save = new SaveState { Unlocked = 3 };

            StagingCard card = StagingCards.Of(save, 9, 0);

            Assert.That(card.Chosen, Is.EqualTo(Career.Frontier(save)));
            Assert.That(card.Halls.Count, Is.EqualTo(DungeonCatalog.All.Count));

            foreach (ArenaHall hall in card.Halls)
            {
                Assert.That(hall.Open, Is.EqualTo(hall.Tier <= Career.Frontier(save)),
                    "hall " + hall.Tier);

                if (hall.Chosen) Assert.That(hall.Open, Is.True, "a shut hall was chosen");
            }
        }

        /// <summary>The roster counts up while it fills, and stops when it is full.</summary>
        [Test]
        public void TheRosterSaysWhetherItIsStillFilling()
        {
            var save = new SaveState { Unlocked = 5 };

            Assert.That(StagingCards.Of(save, 0, 0).Roster, Is.EqualTo(StagingCards.Ready));
            Assert.That(StagingCards.Of(save, 0, 3).Roster, Is.EqualTo(StagingCards.Assembling + "3s"));
        }

        /// <summary>Every line these screens show can actually be drawn.</summary>
        /// <remarks>
        /// Raw, because none of these pass through the translator and so none are cleaned. The
        /// multiplication sign on the rate and the separator in the note are the two worth
        /// having checked.
        /// </remarks>
        [Test]
        public void EveryLineOnTheseScreensCanBeDrawn()
        {
            var lines = new List<string>
            {
                XpCards.Gaining, XpCards.Levelled, XpCards.AtTheTop,
                StagingCards.Ready, StagingCards.Assembling + "7s",
            };

            foreach (RunEnding ending in new[]
            {
                RunEnding.Died, RunEnding.CashedOut, RunEnding.Cleared,
            })
            {
                OverCard card = OverCards.Of(new RunReward { Score = 1 }, ending, 1, 1, true,
                    "New best", 0.8d);

                lines.Add(card.Title);
                lines.Add(card.TitleUnder);
                lines.Add(card.Note);
                lines.Add(card.Rate);
            }

            XpCard xp = XpCards.Of(240, 4, 0.5d, true, new[] { "+5 HP" });

            lines.Add(xp.Gained);
            lines.Add(xp.Level);
            lines.Add(xp.Next);

            Legibility read = Legibility.Of("en", "silkscreen", lines, Face("silkscreen"), false);

            Assert.That(read.Readable, Is.True, read.Report());

            // That the gate was given something, asked of the content rather than of a count.
            // A threshold picked to mean "not empty" is a number that breaks the day somebody
            // adds or removes a line — which it did, at exactly twenty.
            var needed = new List<int>(Legibility.Needed(lines, false));

            Assert.That(needed.Contains('×'), Is.True, "the rate's sign never reached the gate");
            Assert.That(needed.Contains('·'), Is.True, "the note's separator never reached it");
        }

        private static List<int> Face(string id)
        {
            foreach (JToken face in (JArray)Corpus.Object("fonts.json")["faces"])
            {
                if (face["id"].Value<string>() != id) continue;

                var covers = new List<int>();
                foreach (JToken point in (JArray)face["covers"]) covers.Add(point.Value<int>());

                return covers;
            }

            Assert.Fail("no face called " + id + " in the corpus");
            return null;
        }
    }
}
