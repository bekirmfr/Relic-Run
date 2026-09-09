using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Meta;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// The first screen a delver sees.
    /// </summary>
    /// <remarks>
    /// It says four different things depending on level, whether today's Daily is done, whether
    /// one is running, and the clock — and three of those four are states a developer rarely
    /// sits in. A delver at level two sees the title for the first hour they own the game.
    /// </remarks>
    [TestFixture]
    public class TitleCardTests
    {
        private static readonly DateTimeOffset Noon =
            new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        /// <summary>A new delver is told what to do and offered the only thing there is.</summary>
        [Test]
        public void ANewDelverIsPointedAtTheDungeons()
        {
            TitleCard card = TitleCards.Of(At(1), null, false, false, Noon);

            Assert.That(card.Play.Goes, Is.EqualTo(Screen.Levels));
            Assert.That(card.Kicker, Does.Contain("DUNGEONS"));
            Assert.That(card.Sub, Does.Contain("Clear dungeons"));

            Assert.That(card.Daily.Open, Is.False);
            Assert.That(card.Daily.Left, Is.EqualTo("LOCKED"));
            Assert.That(card.Versus.Open, Is.False);
            Assert.That(card.Versus.Right, Does.Contain("LV 5"));
        }

        /// <summary>
        /// The unlock levels in the captions are the ones that actually unlock.
        /// </summary>
        /// <remarks>
        /// Read from <see cref="Career"/> rather than typed into the strings, because a caption
        /// promising level three while the gate opens at four is a delver who reaches the level
        /// they were told and finds the mode still shut — and nothing in the game contradicts
        /// itself loudly enough to be noticed in testing.
        /// </remarks>
        [Test]
        public void TheCaptionsPromiseTheLevelsThatActuallyOpenThings()
        {
            TitleCard card = TitleCards.Of(At(1), null, false, false, Noon);

            Assert.That(card.Daily.Right, Does.Contain("LV " + Career.DailyOpensAt));
            Assert.That(card.Versus.Right, Does.Contain("LV " + Career.VersusOpensAt));
            Assert.That(card.Kicker, Does.Contain("LV " + Career.DailyOpensAt));
            Assert.That(card.Sub, Does.Contain("LV " + Career.DailyOpensAt));

            Assert.That(TitleCards.Of(At(Career.DailyOpensAt), null, false, false, Noon).Daily.Open,
                Is.True, "the level the caption promised did not open it");
            Assert.That(TitleCards.Of(At(Career.VersusOpensAt), null, true, false, Noon).Versus.Open,
                Is.True);
        }

        /// <summary>Once the Daily is open, the title offers it and counts it down.</summary>
        [Test]
        public void AnOpenDailyIsOfferedWithItsClock()
        {
            TitleCard card = TitleCards.Of(At(4), null, false, false, Noon);

            Assert.That(card.Play.StartsTheDaily, Is.True);
            Assert.That(card.Kicker, Is.EqualTo("TODAY'S DELVE · ONE TRY"));

            Assert.That(card.Left, Is.EqualTo("12:00:00"), "noon UTC is halfway through the day");
            Assert.That(card.Sub, Does.Contain(card.Left));
            Assert.That(card.Sub, Does.Contain(card.Seed));

            Assert.That(card.Daily.Left, Is.EqualTo("TODAY · 12:00:00"));
            Assert.That(card.Daily.Right, Does.Contain("1 TRY"));
        }

        /// <summary>A Daily in progress says so, rather than saying it is still to be played.</summary>
        /// <remarks>
        /// Three states, not two: untouched, being played, and finished. Collapsing the middle
        /// one tells a delver who is halfway through today's run that they have not started it.
        /// </remarks>
        [Test]
        public void ADailyBeingPlayedSaysSo()
        {
            Assert.That(TitleCards.Of(At(4), null, false, true, Noon).Daily.Right,
                Does.Contain("IN PROGRESS"));

            Assert.That(TitleCards.Of(At(4), null, true, false, Noon).Daily.Right,
                Does.Contain("DONE TODAY"));

            Assert.That(TitleCards.Of(At(4), null, false, false, Noon).Daily.Right,
                Does.Contain("1 TRY"));
        }

        /// <summary>Today's best is today's, not the best of some other day.</summary>
        [Test]
        public void TheDailyShowsTodaysBest()
        {
            SaveState save = At(4);
            save.DailyBest[DailySeed.For(Noon)] = 1420;
            save.DailyBest[DailySeed.For(Noon.AddDays(-1))] = 9999;

            Assert.That(TitleCards.Of(save, null, true, false, Noon).Daily.Right,
                Does.Contain("1420").And.Not.Contains("9999"));
        }

        /// <summary>With today's done, the title moves on to whatever is deepest.</summary>
        [Test]
        public void ADoneDailySendsTheDelverOnward()
        {
            TitleCard arena = TitleCards.Of(At(Career.VersusOpensAt), null, true, false, Noon);

            Assert.That(arena.Play.Goes, Is.EqualTo(Screen.Staging));
            Assert.That(arena.Kicker, Does.Contain("VERSUS"));
            Assert.That(arena.Sub, Does.Contain("the arena is open"));

            TitleCard halls = TitleCards.Of(At(Career.VersusOpensAt - 1), null, true, false, Noon);

            Assert.That(halls.Play.Goes, Is.EqualTo(Screen.Levels));
            Assert.That(halls.Sub, Does.Contain("back to the dungeons"));
        }

        /// <summary>The name is shown as given, and an unnamed delver is not shown a null.</summary>
        [Test]
        public void ANameIsShownAsGiven()
        {
            Assert.That(TitleCards.Of(At(1), new Preferences { Name = "Bekir" }, false, false, Noon)
                .Name, Is.EqualTo("Bekir"));

            Assert.That(TitleCards.Of(At(1), new Preferences(), false, false, Noon).Name,
                Is.Empty, "never asked, and the screen still has something to draw");

            Assert.That(TitleCards.Of(At(1), null, false, false, Noon).Name, Is.Empty);
        }

        /// <summary>A delver at the ceiling is marked as being at it.</summary>
        /// <remarks>
        /// Otherwise the bar shows a fraction of a level that does not exist, which is a bar
        /// that never fills for the delvers who have played the most.
        /// </remarks>
        [Test]
        public void TheTopLevelIsMarkedAsTheTop()
        {
            TitleCard capped = TitleCards.Of(At(Progression.MaxLevel), null, true, false, Noon);

            Assert.That(capped.Level, Is.EqualTo(Progression.MaxLevel));
            Assert.That(capped.Capped, Is.True);

            Assert.That(TitleCards.Of(At(Progression.MaxLevel - 1), null, true, false, Noon).Capped,
                Is.False);
        }

        /// <summary>
        /// Every line the title shows can actually be drawn.
        /// </summary>
        /// <remarks>
        /// These strings are literals in the source's markup: they never pass through its
        /// translation function, so they are never CLEANED either — the emoji stripping that
        /// takes the arrow off "← Back" does not reach them, and every character in them is
        /// really drawn.
        ///
        /// Which makes this the gate that matters, and makes the false argument the important
        /// half of the call. Asked with the cleaning ON, this question answers itself: the
        /// cleaner strips exactly the characters a bitmap face cannot draw, so it removes the
        /// problem and then reports that there is none. The first version of this test did
        /// precisely that, and passed while asserting something untrue.
        ///
        /// What it guards is a tofu box on the FIRST screen of the game, in every language at
        /// once, because these lines are the same in all eight.
        /// </remarks>
        [Test]
        public void EveryLineOnTheTitleCanBeDrawn()
        {
            List<int> pixel = Face("silkscreen");
            var lines = new List<string>();

            foreach (int level in new[] { 1, 2, Career.DailyOpensAt, 4, Career.VersusOpensAt, 20 })
            {
                foreach (bool done in new[] { true, false })
                {
                    foreach (bool running in new[] { true, false })
                    {
                        TitleCard card = TitleCards.Of(At(level), null, done, running, Noon);

                        lines.Add(card.Kicker);
                        lines.Add(card.Sub);
                        lines.Add(card.Daily.Left);
                        lines.Add(card.Daily.Right);
                        lines.Add(card.Versus.Left);
                        lines.Add(card.Versus.Right);
                    }
                }
            }

            Legibility read = Legibility.Of("en", "silkscreen", lines, pixel, false);

            Assert.That(read.Readable, Is.True, read.Report());

            Assert.That(read.Needs, Is.GreaterThan(30),
                "the lines arrived empty, so this checked nothing");
        }

        /// <summary>
        /// The star the source shows is not shown, because nothing here could draw it.
        /// </summary>
        /// <remarks>
        /// The one departure on this screen, and it is held on its own so that putting the star
        /// back is a decision somebody makes rather than a line somebody types.
        ///
        /// The source is a web page: its own face has no star either, and the browser quietly
        /// borrows one from a system emoji font. Nothing in this port borrows — the pixel face is
        /// baked, and neither typeface the game ships covers U+2B50 — so the star would arrive as
        /// an empty box above the PLAY button.
        ///
        /// The second assertion is the argument for it. Every translated string in the game has
        /// this exact range stripped out of it on the way to the screen, by the source's own
        /// cleaner, for the same reason. The literal keeps its star only by never being asked.
        /// </remarks>
        [Test]
        public void TheStarIsDroppedBecauseNoFaceHereCanDrawIt()
        {
            const int Star = 0x2B50;

            foreach (string face in new[] { "silkscreen", "space-grotesk" })
            {
                Assert.That(Face(face), Does.Not.Contain(Star),
                    face + " covers the star after all, so this departure is no longer needed");
            }

            string right = TitleCards.Of(At(4), null, false, false, Noon).Daily.Right;

            Assert.That(right, Does.StartWith("BEST"));
            Assert.That(right, Does.Not.Contain("⭐"));

            // What the source's own translator would have done to the line, had it been asked.
            Assert.That(Locale.Clean("⭐ " + right), Is.EqualTo(right),
                "dropping the star is exactly what cleaning it would have produced");
        }

        /// <summary>The raw gate can fail, which is the only thing that makes it a gate.</summary>
        [Test]
        public void TheRawGateWouldCatchAStar()
        {
            Legibility cleaned = Legibility.Of("en", "silkscreen", new[] { "BEST 10" }, Face("silkscreen"));
            Legibility raw = Legibility.Of("en", "silkscreen", new[] { "⭐ BEST 10" }, Face("silkscreen"), false);

            Assert.That(cleaned.Readable, Is.True);
            Assert.That(Legibility.Of("en", "silkscreen", new[] { "⭐ BEST 10" }, Face("silkscreen")).Readable,
                Is.True, "with cleaning on, the star is removed before it can be missed");

            Assert.That(raw.Readable, Is.False, "the raw gate cannot see a character it should be missing");
            Assert.That(raw.Missing, Does.Contain(0x2B50));
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
