using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Meta;
using RelicRun.Core.Presentation;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// The four screens a delver reads rather than plays: the board, the profile, the bestiary
    /// and the relic book.
    /// </summary>
    /// <remarks>
    /// They share a shape and one hazard. Every word on them is TRANSLATED — unlike the title and
    /// the mode picker, which the source writes as literals — so what these cards carry is
    /// numbers and KEYS, and a key that names nothing shows the key. That failure is visible, but
    /// it is visible to a delver rather than to anybody here, which is why the keys are held
    /// against the shipped strings below.
    /// </remarks>
    [TestFixture]
    public class ReferenceCardTests
    {
        /* ---------- the board ---------- */

        /// <summary>A delver who has not played sees the empty board, not an empty list.</summary>
        /// <remarks>
        /// Two different screens in the source, and the distinction is worth keeping: an empty
        /// list is a bug that looks like a design, and a screen that says "no runs yet" is a
        /// design that cannot be mistaken for one.
        /// </remarks>
        [Test]
        public void AnUnplayedBoardSaysSoRatherThanShowingNothing()
        {
            BoardCard card = BoardCards.Of(new SaveState(), "Bekir");

            Assert.That(card.Empty, Is.True);
            Assert.That(card.Rows, Is.Empty);
        }

        /// <summary>Runs are ranked from one, and the rank is two digits wide.</summary>
        /// <remarks>
        /// Padded so the column does not jog when the board passes nine — a list that reflows as
        /// it fills is one a delver reads as broken.
        /// </remarks>
        [Test]
        public void RunsAreRankedFromOneInTwoDigits()
        {
            SaveState save = Played(12);

            BoardCard card = BoardCards.Of(save, null);

            Assert.That(card.Rows.Count, Is.EqualTo(BoardCards.Keeps), "the board is a top ten");
            Assert.That(card.Rows[0].Rank, Is.EqualTo("01"));
            Assert.That(card.Rows[9].Rank, Is.EqualTo("10"));

            foreach (BoardRow row in card.Rows) Assert.That(row.Rank.Length, Is.EqualTo(2));
        }

        /// <summary>
        /// A name nobody gave is not a name, and never belongs to anybody.
        /// </summary>
        /// <remarks>
        /// Two failures in one. An empty name shown as empty is a blank row; and if an unnamed
        /// row could be "mine", every anonymous run on the board would light up as the delver's
        /// own the moment they cleared their own name.
        /// </remarks>
        [Test]
        public void AnUnnamedRunIsAnonymousAndNobodys()
        {
            var save = new SaveState();

            save.Scores.Add(new ScoreRow { Score = 900, Name = null });
            save.Scores.Add(new ScoreRow { Score = 800, Name = string.Empty });
            save.Scores.Add(new ScoreRow { Score = 700, Name = PreferenceCodec.AutoNamed + "0042" });
            save.Scores.Add(new ScoreRow { Score = 600, Name = "Bekir" });

            BoardCard anyone = BoardCards.Of(save, string.Empty);

            for (var i = 0; i < 3; i++)
            {
                Assert.That(anyone.Rows[i].Name, Is.Null, "row " + i + " is not anonymous");
                Assert.That(anyone.Rows[i].Mine, Is.False, "row " + i + " was claimed by somebody");
            }

            Assert.That(anyone.Rows[3].Name, Is.EqualTo("Bekir"));

            BoardCard mine = BoardCards.Of(save, "Bekir");

            Assert.That(mine.Rows[3].Mine, Is.True);
            Assert.That(mine.Rows[0].Mine, Is.False);
        }

        /* ---------- the profile ---------- */

        /// <summary>Every achievement is listed, earned or not.</summary>
        /// <remarks>
        /// All of them from the first launch. An achievement nobody can see is one nobody plays
        /// toward, and the source writes the locked ones out with their conditions.
        /// </remarks>
        [Test]
        public void EveryAchievementIsListedWhetherOrNotItIsWon()
        {
            ProfileCard fresh = ProfileCards.Of(new SaveState(), null);

            Assert.That(fresh.Badges.Count, Is.EqualTo(Achievements.All.Count));
            Assert.That(fresh.Won, Is.Zero);

            foreach (Badge badge in fresh.Badges)
            {
                Assert.That(badge.Earned, Is.False);
                Assert.That(badge.Name, Is.Not.Null.And.Not.Empty);
                Assert.That(badge.What, Is.Not.Null.And.Not.Empty, badge.Key);
            }
        }

        /// <summary>What has been won is counted, and counted right.</summary>
        [Test]
        public void WhatHasBeenWonIsCounted()
        {
            var save = new SaveState { Runs = 1, Clears = 1 };

            ProfileCard card = ProfileCards.Of(save, null);

            var earned = 0;
            foreach (Badge badge in card.Badges)
            {
                if (badge.Earned) earned++;
            }

            Assert.That(card.Won, Is.EqualTo(earned), "the tally disagrees with the badges");
            Assert.That(card.Won, Is.GreaterThan(0), "a finished run earns at least First Blood");
        }

        /// <summary>
        /// A delver one point short of a level is not told they are there.
        /// </summary>
        /// <remarks>
        /// Rounded DOWN. Ninety-nine per cent that stays ninety-nine for a while is honest; a
        /// hundred per cent that is not a level is a delver waiting for something that already
        /// happened, or did not.
        /// </remarks>
        [Test]
        public void ProgressRoundsDownSoNinetyNineIsNeverAHundred()
        {
            for (var xp = 0; xp < 40000; xp += 137)
            {
                var save = new SaveState { Xp = xp };
                ProfileCard card = ProfileCards.Of(save, null);

                if (card.Capped) continue;

                Assert.That(card.ToNext, Is.InRange(0, 99), "at " + xp + " experience");
            }
        }

        /// <summary>At the ceiling there is no next level, and the card says so.</summary>
        [Test]
        public void TheTopLevelHasNoNextLevel()
        {
            var save = new SaveState { Xp = 10000000 };

            ProfileCard card = ProfileCards.Of(save, null);

            Assert.That(card.Capped, Is.True);
            Assert.That(card.ToNext, Is.EqualTo(ProfileCards.AtTheTop));
        }

        /* ---------- the bestiary ---------- */

        /// <summary>An unmet species gives away neither its name nor its card.</summary>
        /// <remarks>
        /// The fog is the point. What is NOT hidden is that it exists: the tally says thirteen
        /// from the first launch, so a delver knows how much they have not seen.
        /// </remarks>
        [Test]
        public void AnUnmetSpeciesGivesNothingAway()
        {
            BestiaryCard card = BestiaryCards.Of(new SaveState());

            Assert.That(card.Total, Is.EqualTo(EnemyCatalog.All.Count));
            Assert.That(card.Met, Is.Zero);
            Assert.That(card.Count, Is.EqualTo("0 / " + EnemyCatalog.All.Count + BestiaryCards.Found));

            foreach (BestiaryEntry foe in card.Foes)
            {
                Assert.That(foe.Met, Is.False);
                Assert.That(foe.NameKey, Is.Null, "species " + foe.Species + " named itself");
                Assert.That(foe.Lore, Is.Null, "species " + foe.Species + " told its story");
            }
        }

        /// <summary>A species that has been met shows its name and its card, and only it.</summary>
        [Test]
        public void MeetingASpeciesRevealsThatSpeciesOnly()
        {
            var save = new SaveState();
            save.Seen.Add(3);

            BestiaryCard card = BestiaryCards.Of(save);

            Assert.That(card.Met, Is.EqualTo(1));
            Assert.That(card.Count, Does.StartWith("1 / "));

            foreach (BestiaryEntry foe in card.Foes)
            {
                bool expected = foe.Species == 3;

                Assert.That(foe.Met, Is.EqualTo(expected), "species " + foe.Species);
                Assert.That(foe.Lore != null, Is.EqualTo(expected), "species " + foe.Species);
            }

            Assert.That(card.Foes[3].NameKey, Is.EqualTo("en3"));
            Assert.That(card.Foes[3].Lore, Does.Contain(EnemyCatalog.Get(3).Lore));
        }

        /// <summary>The bestiary's prose is the source's, word for word.</summary>
        /// <remarks>
        /// Read by <c>Tools/capture/foes.mjs</c> and asserted here. Thirteen lines typed by hand
        /// is thirteen chances to drop a word, and a bestiary entry with a word missing still
        /// reads as a bestiary entry.
        /// </remarks>
        [Test]
        public void EverySpeciesReadsAsTheSourceWroteIt()
        {
            var lore = (JArray)Corpus.Object("foes.json")["lore"];

            Assert.That(lore.Count, Is.EqualTo(EnemyCatalog.All.Count));

            for (var i = 0; i < lore.Count; i++)
            {
                Assert.That(EnemyCatalog.Get(i).Lore, Is.EqualTo(lore[i].Value<string>()),
                    "species " + i);
            }
        }

        /* ---------- the relic book ---------- */

        /// <summary>Every relic is in the book, and nothing is hidden.</summary>
        [Test]
        public void EveryRelicIsInTheBook()
        {
            RelicBookCard card = RelicBookCards.Of();

            Assert.That(card.Total, Is.EqualTo(RelicCatalog.All.Count));
            Assert.That(card.Relics.Count, Is.EqualTo(RelicCatalog.All.Count));
        }

        /// <summary>
        /// Every relic is described exactly one way.
        /// </summary>
        /// <remarks>
        /// Either it carries English of its own or it names a key, and never both or neither.
        /// Both would be two descriptions with nothing deciding between them; neither is a relic
        /// with a blank where its rules should be — on the screen a delver uses to decide what to
        /// pick up.
        /// </remarks>
        [Test]
        public void EveryRelicIsDescribedExactlyOneWay()
        {
            foreach (BookEntry entry in RelicBookCards.Of().Relics)
            {
                bool english = !string.IsNullOrEmpty(entry.What);
                bool translated = !string.IsNullOrEmpty(entry.WhatKey);

                Assert.That(english || translated, Is.True, entry.Relic + " says nothing");
                Assert.That(english && translated, Is.False, entry.Relic + " says two things");

                // The two halves have to agree: a relic whose NAME is translated has a
                // translated description, and one that carries its own name carries its own words.
                Assert.That(translated, Is.EqualTo(!string.IsNullOrEmpty(entry.NameKey)),
                    entry.Relic + " is named one way and described the other");
            }
        }

        /// <summary>A wheel needs both halves or it is not a wheel.</summary>
        [Test]
        public void AWheelTurnsOneThingIntoAnother()
        {
            var wheels = 0;

            foreach (BookEntry entry in RelicBookCards.Of().Relics)
            {
                if (entry.Wheel == null) continue;

                wheels++;
                Assert.That(entry.Wheel.Length, Is.EqualTo(2), entry.Relic.ToString());
            }

            Assert.That(wheels, Is.GreaterThan(0), "no relic turns anything into anything");
        }

        /* ---------- the keys these screens hand over ---------- */

        /// <summary>
        /// Every key these cards carry names a string the game actually ships.
        /// </summary>
        /// <remarks>
        /// The hazard the whole fixture exists for. These screens hand over keys rather than
        /// words, and <see cref="Locale"/> answers an unknown key with the key itself — which is
        /// deliberate and right, because a visible <c>it_marrow_d</c> is diagnosable and a blank
        /// is not. But it is only diagnosable by somebody LOOKING, and nothing else in the port
        /// would ever fail.
        ///
        /// Held against English, which is what every other language falls back to. A key missing
        /// there is missing everywhere.
        /// </remarks>
        [Test]
        public void EveryKeyTheseScreensUseNamesAShippedString()
        {
            Dictionary<string, string> english = Corpus.LocaleTable("en");
            var missing = new List<string>();

            for (var i = 0; i < EnemyCatalog.All.Count; i++)
            {
                string key = BestiaryCards.NameKey(i);
                if (!english.ContainsKey(key)) missing.Add(key);
            }

            foreach (BookEntry entry in RelicBookCards.Of().Relics)
            {
                if (!string.IsNullOrEmpty(entry.NameKey) && !english.ContainsKey(entry.NameKey))
                {
                    missing.Add(entry.NameKey);
                }

                if (!string.IsNullOrEmpty(entry.WhatKey) && !english.ContainsKey(entry.WhatKey))
                {
                    missing.Add(entry.WhatKey);
                }
            }

            foreach (string key in new[] { "lbTitle", "lbLocal", "lbAnon", "lbMeta", "lbEmpty" })
            {
                if (!english.ContainsKey(key)) missing.Add(key);
            }

            Assert.That(missing, Is.Empty,
                "these screens name strings the game does not ship: " + string.Join(", ", missing));
        }

        private static SaveState Played(int runs)
        {
            var save = new SaveState();

            for (var i = 0; i < runs; i++)
            {
                save.Scores.Add(new ScoreRow
                {
                    Score = 1000 - i * 10, Floor = 13 - i % 13, Kills = 40 - i, Name = "Bekir",
                });
            }

            return save;
        }
    }
}
