using NUnit.Framework;
using RelicRun.Core.Meta;
using RelicRun.Core.Presentation;

namespace RelicRun.Tests
{
    /// <summary>
    /// Where the buttons go.
    /// </summary>
    /// <remarks>
    /// Three rules, and all three are the kind that look obvious written down and are wrong in
    /// exactly one combination. A routing bug does not crash: it leaves a delver on the wrong
    /// screen, or walks them past the only announcement of something they earned.
    /// </remarks>
    [TestFixture]
    public class PagesTests
    {
        /// <summary>A new delver's PLAY goes to the dungeons, because nothing else exists yet.</summary>
        [Test]
        public void BeforeTheDailyOpensPlayIsTheDungeons()
        {
            Play play = Pages.Featured(At(1), false);

            Assert.That(play.Goes, Is.EqualTo(Page.Levels));
            Assert.That(play.StartsTheDaily, Is.False);

            Assert.That(Pages.Featured(At(Career.DailyOpensAt - 1), false).Goes,
                Is.EqualTo(Page.Levels), "one level short is still short");
        }

        /// <summary>
        /// From level three, PLAY is today's Daily — while today's is still there.
        /// </summary>
        /// <remarks>
        /// Ahead of versus even for a delver who has both, because the Daily is the thing that
        /// expires. A delver sent to the arena first loses today's run to the clock.
        /// </remarks>
        [Test]
        public void TheDailyComesFirstWhileItIsStillThere()
        {
            foreach (int level in new[] { Career.DailyOpensAt, 4, Career.VersusOpensAt, 12, 20 })
            {
                Play play = Pages.Featured(At(level), false);

                Assert.That(play.Goes, Is.EqualTo(Page.Run), "at level " + level);
                Assert.That(play.StartsTheDaily, Is.True, "at level " + level);
            }
        }

        /// <summary>
        /// Once today's Daily is done, PLAY is the arena — or the dungeons, if the arena is shut.
        /// </summary>
        /// <remarks>
        /// The combination that carries the whole rule. Both delvers here have finished today's
        /// Daily and they are sent to different places, so a router that had dropped either the
        /// level test or the done test would send one of them wrong.
        /// </remarks>
        [Test]
        public void OnceTheDailyIsDonePlayIsTheDeepestThingOpen()
        {
            Assert.That(Pages.Featured(At(Career.VersusOpensAt), true).Goes,
                Is.EqualTo(Page.Staging));

            Assert.That(Pages.Featured(At(Career.VersusOpensAt - 1), true).Goes,
                Is.EqualTo(Page.Levels), "versus is not open yet, so it is the dungeons");

            Assert.That(Pages.Featured(At(20), true).StartsTheDaily, Is.False,
                "today's is done and cannot be started twice");
        }

        /// <summary>The featured button always goes somewhere.</summary>
        /// <remarks>
        /// Swept rather than sampled, because the rule has two inputs and four branches, and the
        /// failure it guards is one combination falling through to nothing at all.
        /// </remarks>
        [Test]
        public void ThereIsAlwaysSomewhereToPlay()
        {
            for (var level = 1; level <= 20; level++)
            {
                foreach (bool done in new[] { true, false })
                {
                    Play play = Pages.Featured(At(level), done);

                    // Spelled as a chain of Or rather than with Is.AnyOf, which Unity's older
                    // NUnit does not have. Two runners, two NUnits — see docs/testing.md.
                    Assert.That(play.Goes,
                        Is.EqualTo(Page.Levels).Or.EqualTo(Page.Run).Or.EqualTo(Page.Staging),
                        "level " + level + ", daily done " + done);

                    Assert.That(play.StartsTheDaily, Is.EqualTo(play.Goes == Page.Run),
                        "only the Daily lands on the run screen from the title");
                }
            }
        }

        /// <summary>
        /// A run that earned experience shows it before going home.
        /// </summary>
        /// <remarks>
        /// The experience screen is the only place a level-up is announced. The level is banked
        /// either way, so skipping it costs nothing a delver can measure — which is exactly why
        /// nothing would ever report it.
        /// </remarks>
        [Test]
        public void ExperienceIsShownOnTheWayHome()
        {
            Assert.That(Pages.Home(Page.Over, 240, false), Is.EqualTo(Page.Xp));

            Assert.That(Pages.Home(Page.Over, 240, true), Is.EqualTo(Page.Title),
                "it has been shown once, and once is the promise");
            Assert.That(Pages.Home(Page.Over, 0, false), Is.EqualTo(Page.Title),
                "a run that earned nothing has nothing to announce");
        }

        /// <summary>Home from anywhere else is home.</summary>
        /// <remarks>
        /// Including from the experience screen itself, which is what stops a delver who earned
        /// experience being handed back to it every time they try to leave.
        /// </remarks>
        [Test]
        public void HomeFromAnywhereElseIsTheTitle()
        {
            foreach (Page from in new[]
            {
                Page.Title, Page.Modes, Page.Levels, Page.Run, Page.Staging,
                Page.Xp, Page.Board, Page.Profile, Page.Bestiary, Page.RelicBook,
                Page.How,
            })
            {
                Assert.That(Pages.Home(from, 240, false), Is.EqualTo(Page.Title),
                    "home from " + from);
            }
        }

        /// <summary>The board goes back where it was opened from.</summary>
        [Test]
        public void TheBoardClosesBackToWhereItWasOpened()
        {
            Assert.That(Pages.CloseBoard(Page.Over), Is.EqualTo(Page.Over));
            Assert.That(Pages.CloseBoard(Page.Title), Is.EqualTo(Page.Title));

            Assert.That(Pages.CloseBoard(Page.Profile), Is.EqualTo(Page.Title),
                "anywhere that is not the end of a run goes to the title");
        }

        /// <summary>A save that is not there does not route into a mode nobody has opened.</summary>
        [Test]
        public void NoSaveRoutesToTheDungeons()
        {
            Play play = Pages.Featured(null, false);

            Assert.That(play.Goes, Is.EqualTo(Page.Levels));
            Assert.That(play.StartsTheDaily, Is.False);
        }

        /// <summary>A save whose delver is at the given level.</summary>
        /// <remarks>
        /// Built from experience rather than by setting a level, because the level IS the
        /// experience — <see cref="SaveState.Level"/> is derived and has no setter. A fixture
        /// that could set it directly would be testing a state the game cannot reach.
        /// </remarks>
        private static SaveState At(int level)
        {
            var save = new SaveState();

            while (save.Level < level && save.Xp < 10000000) save.Xp += 100;

            Assert.That(save.Level, Is.EqualTo(level), "could not build a delver at level " + level);

            return save;
        }
    }
}
