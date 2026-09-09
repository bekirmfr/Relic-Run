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
    public class ScreensTests
    {
        /// <summary>A new delver's PLAY goes to the dungeons, because nothing else exists yet.</summary>
        [Test]
        public void BeforeTheDailyOpensPlayIsTheDungeons()
        {
            Play play = Screens.Featured(At(1), false);

            Assert.That(play.Goes, Is.EqualTo(Screen.Levels));
            Assert.That(play.StartsTheDaily, Is.False);

            Assert.That(Screens.Featured(At(Career.DailyOpensAt - 1), false).Goes,
                Is.EqualTo(Screen.Levels), "one level short is still short");
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
                Play play = Screens.Featured(At(level), false);

                Assert.That(play.Goes, Is.EqualTo(Screen.Run), "at level " + level);
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
            Assert.That(Screens.Featured(At(Career.VersusOpensAt), true).Goes,
                Is.EqualTo(Screen.Staging));

            Assert.That(Screens.Featured(At(Career.VersusOpensAt - 1), true).Goes,
                Is.EqualTo(Screen.Levels), "versus is not open yet, so it is the dungeons");

            Assert.That(Screens.Featured(At(20), true).StartsTheDaily, Is.False,
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
                    Play play = Screens.Featured(At(level), done);

                    Assert.That(play.Goes, Is.AnyOf(Screen.Levels, Screen.Run, Screen.Staging),
                        "level " + level + ", daily done " + done);

                    Assert.That(play.StartsTheDaily, Is.EqualTo(play.Goes == Screen.Run),
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
            Assert.That(Screens.Home(Screen.Over, 240, false), Is.EqualTo(Screen.Xp));

            Assert.That(Screens.Home(Screen.Over, 240, true), Is.EqualTo(Screen.Title),
                "it has been shown once, and once is the promise");
            Assert.That(Screens.Home(Screen.Over, 0, false), Is.EqualTo(Screen.Title),
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
            foreach (Screen from in new[]
            {
                Screen.Title, Screen.Modes, Screen.Levels, Screen.Run, Screen.Staging,
                Screen.Xp, Screen.Board, Screen.Profile, Screen.Bestiary, Screen.RelicBook,
                Screen.How,
            })
            {
                Assert.That(Screens.Home(from, 240, false), Is.EqualTo(Screen.Title),
                    "home from " + from);
            }
        }

        /// <summary>The board goes back where it was opened from.</summary>
        [Test]
        public void TheBoardClosesBackToWhereItWasOpened()
        {
            Assert.That(Screens.CloseBoard(Screen.Over), Is.EqualTo(Screen.Over));
            Assert.That(Screens.CloseBoard(Screen.Title), Is.EqualTo(Screen.Title));

            Assert.That(Screens.CloseBoard(Screen.Profile), Is.EqualTo(Screen.Title),
                "anywhere that is not the end of a run goes to the title");
        }

        /// <summary>A save that is not there does not route into a mode nobody has opened.</summary>
        [Test]
        public void NoSaveRoutesToTheDungeons()
        {
            Play play = Screens.Featured(null, false);

            Assert.That(play.Goes, Is.EqualTo(Screen.Levels));
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
