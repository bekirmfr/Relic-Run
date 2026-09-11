using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;

namespace RelicRun.Tests
{
    /// <summary>
    /// The two relic descriptions that cannot say themselves.
    /// </summary>
    /// <remarks>
    /// Forty-eight of the fifty are a fixed sentence. Two are not, and both reached a delver as
    /// raw markup: <c>(now +{n})</c> on the Midas Blade and <c>[[LCK]]</c> on the Weighted Dice.
    /// A screen showing a delver the word "{n}" is a screen telling them the game is broken.
    /// </remarks>
    [TestFixture]
    public class RelicWordsTests
    {
        /// <summary>
        /// The Midas Blade's number is the one the engine will actually give it.
        /// </summary>
        /// <remarks>
        /// The whole point of computing it rather than reading it out of the sentence. This is
        /// the SAME arithmetic <c>Pickup</c> performs when the blade is taken, so a delver
        /// reading "+3" and then getting +3 is not a coincidence.
        /// </remarks>
        [Test]
        public void TheBladeIsWorthWhatThePurseSays()
        {
            for (var gold = 0; gold <= 500; gold += 7)
            {
                var run = new RunState();
                run.Gold = gold;

                int promised = RelicWords.Live(RelicId.MidasBlade, gold);

                Pickup.Take(run, RelicId.MidasBlade);

                Assert.That(run.Hero.MidasBonus, Is.EqualTo(promised),
                    "the card promised " + promised + " at " + gold + " gold");
            }
        }

        /// <summary>An empty purse still sharpens the blade once.</summary>
        /// <remarks>
        /// The floor is one, not zero — a blade taken with nothing in the purse is still worth an
        /// attack. Worth pinning because the obvious division gives zero and reads fine.
        /// </remarks>
        [Test]
        public void AnEmptyPurseStillBuysOne()
        {
            Assert.That(RelicWords.Live(RelicId.MidasBlade, 0), Is.EqualTo(1));
            Assert.That(RelicWords.Live(RelicId.MidasBlade, RelicWords.MidasGold - 1), Is.EqualTo(1));
            Assert.That(RelicWords.Live(RelicId.MidasBlade, RelicWords.MidasGold), Is.EqualTo(1));
            Assert.That(RelicWords.Live(RelicId.MidasBlade, RelicWords.MidasGold * 2), Is.EqualTo(2));
        }

        /// <summary>Every other relic says it needs nothing filling in.</summary>
        /// <remarks>
        /// Swept, so that a relic given a live number later is noticed here rather than on a
        /// screen — and so that nothing is quietly handed a <c>-1</c> to print.
        /// </remarks>
        [Test]
        public void OnlyOneRelicHasALiveNumber()
        {
            var live = 0;

            foreach (RelicDef relic in RelicCatalog.All)
            {
                bool lives = RelicWords.Lives(relic.Id);

                if (lives) live++;

                RelicTextDef text = RelicText.Get(relic.Id);

                // And the claim matches the text: a description carrying {n} must say it does.
                bool carries = text != null && text.What != null && text.What.Contains("{n}");

                if (text != null && text.What != null)
                {
                    Assert.That(lives || !carries, Is.True,
                        RelicCatalog.KeyOf(relic.Id) + " carries {n} and nobody fills it");
                }

                Assert.That(RelicWords.Live(relic.Id, 500), lives ? Is.GreaterThan(0) : Is.EqualTo(-1));
            }

            Assert.That(live, Is.EqualTo(1));
        }

        /// <summary>A stat chip is unwrapped and painted with whatever the screen paints with.</summary>
        /// <remarks>
        /// The markup belongs to the caller, because Core does not know what a screen draws with.
        /// What it knows is where the chip starts and stops.
        /// </remarks>
        [Test]
        public void ChipsAreUnwrappedAndPainted()
        {
            Assert.That(RelicWords.Chips("crit = [[LCK]].", "<b>", "</b>"),
                Is.EqualTo("crit = <b>LCK</b>."));

            Assert.That(RelicWords.Chips("[[ATK]] and [[DEF]]", "{", "}"),
                Is.EqualTo("{ATK} and {DEF}"));

            Assert.That(RelicWords.Chips("nothing here", "<b>", "</b>"),
                Is.EqualTo("nothing here"));
        }

        /// <summary>
        /// A description with a stray bracket keeps it, rather than losing the sentence.
        /// </summary>
        /// <remarks>
        /// A content bug should stay visible. Swallowing the rest of the line to tidy it away
        /// would turn a typo somebody can see into a missing sentence nobody can explain.
        /// </remarks>
        [Test]
        public void AStrayBracketIsLeftAlone()
        {
            Assert.That(RelicWords.Chips("crit = [[LCK and more", "<b>", "</b>"),
                Is.EqualTo("crit = [[LCK and more"));

            Assert.That(RelicWords.Chips("[[LCK]] then [[broken", "<b>", "</b>"),
                Is.EqualTo("<b>LCK</b> then [[broken"));

            Assert.That(RelicWords.Chips(null, "<b>", "</b>"), Is.Null);
            Assert.That(RelicWords.Chips("", "<b>", "</b>"), Is.Empty);
        }

        /// <summary>
        /// Every relic description survives being said, with nothing left unfilled.
        /// </summary>
        /// <remarks>
        /// The sweep that would have caught both of these before a delver did. Nothing a screen
        /// shows should still contain a brace or a double bracket once it has been through the
        /// two things that exist to remove them.
        /// </remarks>
        [Test]
        public void NoDescriptionReachesAScreenStillWrapped()
        {
            foreach (RelicDef relic in RelicCatalog.All)
            {
                RelicTextDef text = RelicText.Get(relic.Id);

                if (text == null || text.What == null) continue;

                string said = text.What;

                if (RelicWords.Lives(relic.Id))
                {
                    said = said.Replace("{n}",
                        RelicWords.Live(relic.Id, 260).ToString());
                }

                said = RelicWords.Chips(said, "<b>", "</b>");

                Assert.That(said, Does.Not.Contain("{"),
                    RelicCatalog.KeyOf(relic.Id) + " still has a placeholder in it");

                Assert.That(said, Does.Not.Contain("[["),
                    RelicCatalog.KeyOf(relic.Id) + " still has a chip in it");
            }
        }
    }
}
