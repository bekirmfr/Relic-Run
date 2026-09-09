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
    /// The dungeon list: which halls exist, which are shut, and what is down the one being read.
    /// </summary>
    /// <remarks>
    /// The screen a delver spends the longest on before they know the game, and the one that has
    /// to be right about the two things they are deciding between — how hard a hall is and what
    /// it pays. Both of those are numbers this computes.
    /// </remarks>
    [TestFixture]
    public class LevelsCardTests
    {
        /// <summary>
        /// The halls' prose is the source's, word for word.
        /// </summary>
        /// <remarks>
        /// It was never ported: the lore and the arrival blurb are English literals in the
        /// source's own markup, never passed through its translation function, and the catalog
        /// carried only the numbers. Read by <c>Tools/capture/halls.mjs</c> and asserted here,
        /// because ten paragraphs typed by hand is ten chances to drop a word — and prose is the
        /// one kind of content where a missing word reads as a deliberate one.
        ///
        /// The numbers are held against the same capture even though they are gated elsewhere.
        /// They are what proves the prose landed on the right hall: a capture that shuffled the
        /// entries would still produce ten plausible paragraphs.
        /// </remarks>
        [Test]
        public void EveryHallReadsAsTheSourceWroteIt()
        {
            var halls = (JArray)Corpus.Object("halls.json")["halls"];

            Assert.That(halls.Count, Is.EqualTo(DungeonCatalog.All.Count));

            for (var i = 0; i < halls.Count; i++)
            {
                JToken read = halls[i];
                DungeonDef hall = DungeonCatalog.All[i];
                string where = "hall " + hall.Tier;

                Assert.That(hall.Tier, Is.EqualTo(read["tier"].Value<int>()), where);
                Assert.That(hall.Name, Is.EqualTo(read["name"].Value<string>()), where);
                Assert.That(hall.Level, Is.EqualTo(read["level"].Value<int>()), where);
                Assert.That(hall.Multiplier, Is.EqualTo(read["multiplier"].Value<double>()), where);
                Assert.That(hall.GhoolemBoss, Is.EqualTo(read["ghoolemBoss"].Value<bool>()), where);

                Assert.That(hall.Blurb, Is.EqualTo(read["blurb"].Value<string>()), where);
                Assert.That(hall.Lore, Is.EqualTo(read["lore"].Value<string>()), where);
            }
        }

        /// <summary>A new delver sees ten halls and may enter exactly one of them.</summary>
        [Test]
        public void OnlyTheFirstHallIsOpenToStartWith()
        {
            LevelsCard card = LevelsCards.Of(new SaveState(), 0);

            Assert.That(card.Halls.Count, Is.EqualTo(DungeonCatalog.All.Count));

            var open = 0;

            foreach (HallTile tile in card.Halls)
            {
                if (tile.Unlocked) open++;

                Assert.That(tile.Cleared, Is.False, "nothing is cleared on a fresh save");
            }

            Assert.That(open, Is.EqualTo(1));
            Assert.That(card.Halls[0].Unlocked, Is.True);
            Assert.That(card.Halls[0].Tag, Is.EqualTo(LevelsCards.OpenTag));
        }

        /// <summary>
        /// Cleared, open and sealed are three states, and the frontier is the line between them.
        /// </summary>
        /// <remarks>
        /// The hall AT the frontier is open and not cleared — it is the one they unlocked and
        /// have not finished. Collapsing that into "cleared" would tell a delver they had beaten
        /// the hall they are standing in front of.
        /// </remarks>
        [Test]
        public void TheFrontierIsOpenAndEverythingBehindItIsCleared()
        {
            var save = new SaveState { Unlocked = 4 };

            LevelsCard card = LevelsCards.Of(save, 0);
            int frontier = Career.Frontier(save);

            foreach (HallTile tile in card.Halls)
            {
                string where = "hall " + tile.Tier;

                Assert.That(tile.Unlocked, Is.EqualTo(tile.Tier <= frontier), where);
                Assert.That(tile.Cleared, Is.EqualTo(tile.Tier < frontier), where);

                string tag = tile.Cleared ? LevelsCards.ClearedTag
                    : tile.Unlocked ? LevelsCards.OpenTag : string.Empty;

                Assert.That(tile.Tag, Is.EqualTo(tag), where);
            }

            Assert.That(card.Halls[frontier - 1].Unlocked, Is.True);
            Assert.That(card.Halls[frontier - 1].Cleared, Is.False,
                "the hall they just unlocked has not been beaten");
        }

        /// <summary>The cursor starts on the deepest hall they have opened.</summary>
        [Test]
        public void TheDeepestOpenHallIsWhereTheCursorStarts()
        {
            var save = new SaveState { Unlocked = 6 };

            Assert.That(LevelsCards.Of(save, 0).Chosen, Is.EqualTo(Career.Frontier(save)));
            Assert.That(LevelsCards.Of(save, 2).Chosen, Is.EqualTo(2), "and a choice is honoured");
        }

        /// <summary>A hall outside the catalog is clamped rather than crashing the screen.</summary>
        /// <remarks>
        /// The remembered hall comes off disk, where it may be anything — a save from a build
        /// with more halls, or a torn line. A screen that threw would be a screen that could not
        /// be opened to fix it.
        /// </remarks>
        [Test]
        public void AHallOutsideTheCatalogIsClamped()
        {
            var save = new SaveState { Unlocked = 10 };
            int count = DungeonCatalog.All.Count;

            Assert.That(LevelsCards.Of(save, 999).Chosen, Is.EqualTo(count));
            Assert.That(LevelsCards.Of(save, -5).Chosen, Is.EqualTo(Career.Frontier(save)));

            foreach (int asked in new[] { -5, 0, 1, count, 999 })
            {
                LevelsCard card = LevelsCards.Of(save, asked);

                Assert.That(card.Chosen, Is.InRange(1, count), "asked for " + asked);
                Assert.That(card.Detail.Tier, Is.EqualTo(card.Chosen));
            }
        }

        /// <summary>
        /// A sealed hall says nothing about itself, and says so.
        /// </summary>
        /// <remarks>
        /// No name, no numbers, no relics. The point of sealing it is that the delver does not
        /// know what is down there, and a panel that listed the king's hit points while calling
        /// it a mystery would be telling them anyway.
        /// </remarks>
        [Test]
        public void ASealedHallGivesNothingAway()
        {
            HallDetail shut = LevelsCards.Of(new SaveState(), 5).Detail;

            Assert.That(shut.Sealed, Is.True);
            Assert.That(shut.CanDelve, Is.False);
            Assert.That(shut.Stats, Is.Empty);
            Assert.That(shut.BossRelics, Is.Empty);

            Assert.That(shut.Title, Is.EqualTo("DUNGEON 5"));
            Assert.That(shut.Title, Does.Not.Contain(DungeonCatalog.Get(5).Name),
                "the panel names a hall the delver has not reached");

            Assert.That(shut.Lore, Does.Contain("Clear Dungeon 4"),
                "and does not say what would open it");
            Assert.That(shut.Lore, Is.Not.EqualTo(DungeonCatalog.Get(5).Lore));
        }

        /// <summary>An open hall shows its own name, prose and seven numbers.</summary>
        [Test]
        public void AnOpenHallShowsWhatIsDownIt()
        {
            var save = new SaveState { Unlocked = 3 };

            HallDetail open = LevelsCards.Of(save, 2).Detail;
            DungeonDef hall = DungeonCatalog.Get(2);

            Assert.That(open.Sealed, Is.False);
            Assert.That(open.CanDelve, Is.True);
            Assert.That(open.Title, Is.EqualTo(hall.Name.ToUpperInvariant()));
            Assert.That(open.Lore, Is.EqualTo(hall.Lore));
            Assert.That(open.BossRelics, Is.EqualTo(hall.BossRelics));

            Assert.That(open.Stats.Count, Is.EqualTo(7));

            List<string> labels = Labels(open);

            foreach (string label in new[] { "HP", "ATK", "DEF", "SPD", "SCORE", "FOR LV", "XP RATE" })
            {
                // Asked of the list rather than through Does.Contain, whose string overload is a
                // substring check in one of the two NUnits this file is compiled by.
                Assert.That(labels.Contains(label), Is.True, "no row called " + label);
            }
        }

        /// <summary>
        /// A deeper hall's king hits harder and lasts longer, and is no faster.
        /// </summary>
        /// <remarks>
        /// The source applies the hall's multiplier to hit points and attack and to nothing else.
        /// That is a design decision rather than an oversight — depth makes a hall punishing, not
        /// nimbler — and it is exactly the kind of thing a port "tidies" by scaling everything.
        /// </remarks>
        [Test]
        public void DepthScalesTheKingsHitPointsAndAttackAndNothingElse()
        {
            var save = new SaveState { Unlocked = 10 };

            HallDetail first = LevelsCards.Of(save, 1).Detail;
            HallDetail last = LevelsCards.Of(save, 10).Detail;

            Assert.That(Number(last, "HP"), Is.GreaterThan(Number(first, "HP")));
            Assert.That(Number(last, "ATK"), Is.GreaterThan(Number(first, "ATK")));

            Assert.That(Number(last, "DEF"), Is.EqualTo(Number(first, "DEF")),
                "armour is not scaled by the hall");
            Assert.That(Number(last, "SPD"), Is.EqualTo(Number(first, "SPD")),
                "speed is not scaled by the hall");
        }

        /// <summary>
        /// The two multipliers are spelled differently, and both spellings are the source's.
        /// </summary>
        /// <remarks>
        /// The score multiplier is a property of the hall and reads as one: 1.1, not 1.10. The
        /// experience rate is a rate and always shows its hundredths, because the difference
        /// between 0.8 and 0.80 on a screen full of rates is a delver wondering which they
        /// misread. Getting either the wrong way round is a number that looks fine and is not
        /// what the source shows.
        /// </remarks>
        [Test]
        public void TheTwoMultipliersAreSpelledTheWayTheSourceSpellsThem()
        {
            var save = new SaveState { Unlocked = 10 };

            Assert.That(Value(LevelsCards.Of(save, 1).Detail, "SCORE"), Is.EqualTo("×1"));
            Assert.That(Value(LevelsCards.Of(save, 2).Detail, "SCORE"), Is.EqualTo("×1.1"));
            Assert.That(Value(LevelsCards.Of(save, 3).Detail, "SCORE"), Is.EqualTo("×1.21"));

            // 1.4641 rounds to two places, and the trailing zero of 1.46 is not dropped because
            // there is no trailing zero: it is 1.46.
            Assert.That(Value(LevelsCards.Of(save, 5).Detail, "SCORE"), Is.EqualTo("×1.46"));

            // The frontier hall pays full rate, and every step back pays less.
            Assert.That(Value(LevelsCards.Of(save, 10).Detail, "XP RATE"), Is.EqualTo("×1.00"));
            Assert.That(Value(LevelsCards.Of(save, 9).Detail, "XP RATE"), Is.EqualTo("×0.80"));
            Assert.That(Value(LevelsCards.Of(save, 1).Detail, "XP RATE"), Is.EqualTo("×0.10"));
        }

        /// <summary>
        /// The preview is the same every time the screen is opened.
        /// </summary>
        /// <remarks>
        /// It is a shop window, not a fight. A preview rolled fresh each time would show a delver
        /// different numbers for the same hall depending on when they looked — and the numbers
        /// are the whole reason they are looking.
        /// </remarks>
        [Test]
        public void TheSameHallPreviewsTheSameEveryTime()
        {
            var save = new SaveState { Unlocked = 10 };

            for (var tier = 1; tier <= DungeonCatalog.All.Count; tier++)
            {
                HallDetail once = LevelsCards.Of(save, tier).Detail;
                HallDetail again = LevelsCards.Of(save, tier).Detail;

                for (var i = 0; i < once.Stats.Count; i++)
                {
                    Assert.That(again.Stats[i].Value, Is.EqualTo(once.Stats[i].Value),
                        "hall " + tier + ", " + once.Stats[i].Label);
                }
            }
        }

        /// <summary>
        /// Every line on this screen can actually be drawn.
        /// </summary>
        /// <remarks>
        /// Asked RAW, for the reason the title's are: none of these pass through the translator,
        /// so none are cleaned, and asking with the cleaning on would strip exactly the
        /// characters the question is about. The multiplication sign is the one worth having
        /// checked — it appears on two of the seven rows of every hall.
        /// </remarks>
        [Test]
        public void EveryLineOnTheDungeonListCanBeDrawn()
        {
            var lines = new List<string>();
            var save = new SaveState { Unlocked = 6 };

            for (var tier = 1; tier <= DungeonCatalog.All.Count; tier++)
            {
                LevelsCard card = LevelsCards.Of(save, tier);

                lines.Add(card.Detail.Title);
                lines.Add(card.Detail.Lore);

                foreach (HallStat stat in card.Detail.Stats)
                {
                    lines.Add(stat.Label);
                    lines.Add(stat.Value);
                }

                foreach (HallTile tile in card.Halls) lines.Add(tile.Tag);
            }

            lines.Add(LevelsCards.KingName);
            lines.Add(LevelsCards.NoRelics);

            Legibility read = Legibility.Of("en", "silkscreen", lines, Face("silkscreen"), false);

            Assert.That(read.Readable, Is.True, read.Report());
            Assert.That(read.Needs, Is.GreaterThan(30), "the lines arrived empty");
        }

        private static List<string> Labels(HallDetail detail)
        {
            var labels = new List<string>();

            foreach (HallStat stat in detail.Stats) labels.Add(stat.Label);

            return labels;
        }

        private static string Value(HallDetail detail, string label)
        {
            foreach (HallStat stat in detail.Stats)
            {
                if (stat.Label == label) return stat.Value;
            }

            Assert.Fail("no row called " + label);
            return null;
        }

        private static int Number(HallDetail detail, string label)
        {
            return int.Parse(Value(detail, label));
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
