using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Meta;
using RelicRun.Core.Presentation;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// The mode picker, and the banners it shares with the title.
    /// </summary>
    /// <remarks>
    /// Three ways to play and one question: which of them may this delver use. The answer is
    /// wrong in exactly one direction that matters — offering something that then refuses — and
    /// a delver who presses a mode and gets nothing has no way to tell that from a broken button.
    /// </remarks>
    [TestFixture]
    public class ModesCardTests
    {
        private static readonly DateTimeOffset Noon =
            new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// The title and the mode picker say the same thing about the same mode.
        /// </summary>
        /// <remarks>
        /// The reason the banners are built in one place. The source writes them out twice,
        /// identically, and two copies become two answers the first time somebody edits one — on
        /// two screens a delver moves between in a second.
        ///
        /// Swept across every state rather than sampled, because the states are where they would
        /// drift: a screen that agreed on "locked" and disagreed on "in progress" would look
        /// right in every screenshot anybody took.
        /// </remarks>
        [Test]
        public void BothScreensSayTheSameThingAboutBothModes()
        {
            foreach (int level in new[] { 1, 2, Career.DailyOpensAt, 4, Career.VersusOpensAt, 20 })
            {
                foreach (bool done in new[] { true, false })
                {
                    foreach (bool running in new[] { true, false })
                    {
                        SaveState save = At(level);
                        string where = "level " + level + ", done " + done + ", running " + running;

                        TitleCard title = TitleCards.Of(save, null, done, running, Noon);
                        ModesCard modes = ModesCards.Of(save, done, running, Noon);

                        Same(title.Daily, modes.Daily, where + " — daily");
                        Same(title.Versus, modes.Versus, where + " — versus");
                    }
                }
            }
        }

        /// <summary>A new delver may delve, and may do nothing else.</summary>
        [Test]
        public void ANewDelverHasOnlyTheDungeons()
        {
            ModesCard card = ModesCards.Of(new SaveState(), false, false, Noon);

            Assert.That(card.CanDaily, Is.False);
            Assert.That(card.CanVersus, Is.False);

            Assert.That(card.Daily.Open, Is.False);
            Assert.That(card.Daily.Tag, Is.EqualTo(ModeLines.Locked));
            Assert.That(card.Versus.Tag, Is.EqualTo(ModeLines.Locked));

            Assert.That(card.Dungeons, Does.Contain("1"), "the dungeons are never locked");
        }

        /// <summary>
        /// Open is not the same as playable.
        /// </summary>
        /// <remarks>
        /// The distinction the whole screen turns on. A delver at level four who has played
        /// today's Daily sees an OPEN banner and a button that refuses — because the Daily is one
        /// run a day, not because they are too junior. Collapsing the two would either grey out a
        /// mode they have earned or offer one that cannot start.
        /// </remarks>
        [Test]
        public void AnOpenModeIsNotAlwaysAPlayableOne()
        {
            SaveState save = At(4);

            ModesCard fresh = ModesCards.Of(save, false, false, Noon);

            Assert.That(fresh.Daily.Open, Is.True);
            Assert.That(fresh.CanDaily, Is.True);
            Assert.That(fresh.Daily.Tag, Is.EqualTo(ModeLines.Floors));

            ModesCard played = ModesCards.Of(save, true, false, Noon);

            Assert.That(played.Daily.Open, Is.True, "they are still senior enough");
            Assert.That(played.CanDaily, Is.False, "and today's is gone");
            Assert.That(played.Daily.Tag, Is.EqualTo(ModeLines.Done));

            ModesCard midway = ModesCards.Of(save, false, true, Noon);

            Assert.That(midway.CanDaily, Is.False, "one is already running");
            Assert.That(midway.Daily.Tag, Is.EqualTo("IN PROGRESS"));
        }

        /// <summary>The arena opens on the level it says it opens on.</summary>
        [Test]
        public void TheArenaOpensWhenItSaysItDoes()
        {
            Assert.That(ModesCards.Of(At(Career.VersusOpensAt - 1), true, false, Noon).CanVersus,
                Is.False);

            ModesCard open = ModesCards.Of(At(Career.VersusOpensAt), true, false, Noon);

            Assert.That(open.CanVersus, Is.True);
            Assert.That(open.Versus.Open, Is.True);
            Assert.That(open.Versus.Tag, Is.Not.EqualTo(ModeLines.Locked));

            Assert.That(ModesCards.Of(At(1), false, false, Noon).Versus.Right,
                Does.Contain("LV " + Career.VersusOpensAt),
                "and a shut arena says which level would open it");
        }

        /// <summary>The dungeons tile says how deep the delver has reached.</summary>
        [Test]
        public void TheDungeonsSayHowDeepTheDelverHasReached()
        {
            var save = new SaveState { Unlocked = 6 };

            Assert.That(ModesCards.Of(save, false, false, Noon).Dungeons,
                Is.EqualTo(ModesCards.Deepest + Career.Frontier(save)));
        }

        /// <summary>
        /// The tick the source shows is not shown, because no face here can draw it.
        /// </summary>
        /// <remarks>
        /// The second time this exact thing has come up, and it is held on its own for the same
        /// reason the title's star is: putting it back should be a decision somebody makes rather
        /// than a character somebody types.
        /// </remarks>
        [Test]
        public void TheTickIsDroppedBecauseNoFaceHereCanDrawIt()
        {
            const int Tick = 0x2713;

            foreach (string face in new[] { "silkscreen", "space-grotesk" })
            {
                Assert.That(Face(face).Contains(Tick), Is.False,
                    face + " covers the tick after all, so this departure is no longer needed");
            }

            Assert.That(ModeLines.Done, Is.EqualTo("DONE"));
            Assert.That(ModeLines.Done, Does.Not.Contain("✓"));
        }

        /// <summary>Every line the mode picker shows can actually be drawn.</summary>
        /// <remarks>
        /// Asked RAW, because none of these pass through the translator and so none are cleaned.
        /// Asking with the cleaning on would strip exactly the characters the question is about.
        /// </remarks>
        [Test]
        public void EveryLineOnTheModePickerCanBeDrawn()
        {
            var lines = new List<string>();

            for (var level = 1; level <= 20; level++)
            {
                foreach (bool done in new[] { true, false })
                {
                    foreach (bool running in new[] { true, false })
                    {
                        ModesCard card = ModesCards.Of(At(level), done, running, Noon);

                        lines.Add(card.Dungeons);
                        lines.Add(card.Left);

                        foreach (ModeLine line in new[] { card.Daily, card.Versus })
                        {
                            lines.Add(line.Left);
                            lines.Add(line.Right);
                            lines.Add(line.Tag);
                        }
                    }
                }
            }

            Legibility read = Legibility.Of("en", "silkscreen", lines, Face("silkscreen"), false);

            Assert.That(read.Readable, Is.True, read.Report());

            // The gate has to have been given something. A code-point count is the wrong shape
            // for that — this screen shouts in capitals and needs a small alphabet, and a
            // threshold picked to be "clearly not empty" is a number that fails the day somebody
            // shortens a caption. So the question is asked of the lines, and of one character
            // that is definitely on the screen.
            Assert.That(lines.Count, Is.GreaterThan(100));
            Assert.That(new List<int>(Legibility.Needed(lines, false)).Contains('·'), Is.True,
                "the separator every banner uses did not reach the gate");
        }

        private static void Same(ModeLine title, ModeLine modes, string where)
        {
            Assert.That(modes.Open, Is.EqualTo(title.Open), where);
            Assert.That(modes.Left, Is.EqualTo(title.Left), where);
            Assert.That(modes.Right, Is.EqualTo(title.Right), where);
            Assert.That(modes.Tag, Is.EqualTo(title.Tag), where);
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

        /// <summary>A save whose delver is at the given level.</summary>
        private static SaveState At(int level)
        {
            var save = new SaveState();

            while (save.Level < level && save.Xp < 10000000) save.Xp += 100;

            Assert.That(save.Level, Is.EqualTo(level), "could not build a delver at level " + level);

            return save;
        }
    }
}
