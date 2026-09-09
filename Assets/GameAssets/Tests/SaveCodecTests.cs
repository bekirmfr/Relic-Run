using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RelicRun.Core.Meta;

namespace RelicRun.Tests
{
    /// <summary>
    /// A delver's progression, written out and read back.
    /// </summary>
    /// <remarks>
    /// Every one of these guards the same class of failure and it is a quiet one: a save that
    /// looks fine and has lost something. Nothing throws, nothing logs, the screens all draw —
    /// and the delver's gold is back to zero next time they open the game.
    /// </remarks>
    [TestFixture]
    public class SaveCodecTests
    {
        /// <summary>
        /// Every field of the save is carried by the codec.
        /// </summary>
        /// <remarks>
        /// The test this file exists for. A round-trip test cannot find a field that neither
        /// side writes — such a field round-trips perfectly, because it is default on both ends —
        /// so the only thing that catches "added a field, forgot the codec" is asking the type
        /// itself what fields it has.
        ///
        /// It fails on the day somebody adds one, which is the day it is cheap to fix, rather
        /// than on the day a delver notices their runs are not being counted.
        /// </remarks>
        [Test]
        public void EveryFieldOfTheSaveIsCarried()
        {
            var carried = new List<string>(SaveCodec.Fields);

            foreach (FieldInfo field in typeof(SaveState).GetFields(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.That(carried, Does.Contain(field.Name),
                    "SaveState." + field.Name + " is not in SaveCodec — it will not be saved, " +
                    "and nothing else will say so");
            }

            foreach (string name in carried)
            {
                Assert.That(typeof(SaveState).GetField(name), Is.Not.Null,
                    "SaveCodec claims to carry " + name + ", which SaveState does not have");
            }
        }

        /// <summary>A full save survives the trip unchanged.</summary>
        [Test]
        public void AFullSaveComesBackWholeIntact()
        {
            SaveState before = Full();

            Loaded loaded = SaveCodec.Read(SaveCodec.Write(before));

            Assert.That(loaded.Damaged, Is.Zero, "the codec could not read its own output");
            Assert.That(loaded.Version, Is.EqualTo(SaveCodec.Version));

            Same(before, loaded.Save);
        }

        /// <summary>Writing twice gives the same text twice.</summary>
        /// <remarks>
        /// So that two saves can be diffed. A save whose lines moved between writes would show
        /// every field as changed, and reading a diff of two saves is how a progression bug is
        /// found in the first place.
        /// </remarks>
        [Test]
        public void TheSameSaveWritesTheSameText()
        {
            SaveState save = Full();

            Assert.That(SaveCodec.Write(save), Is.EqualTo(SaveCodec.Write(save)));

            Assert.That(SaveCodec.Write(SaveCodec.Read(SaveCodec.Write(save)).Save),
                Is.EqualTo(SaveCodec.Write(save)), "a round trip changed the text");
        }

        /// <summary>
        /// A name a delver can actually type survives it.
        /// </summary>
        /// <remarks>
        /// Every one of these is something a person can put in the name box, and every one of
        /// them breaks a format that does not escape: the separator moves the name into a field
        /// that wants a number, and a newline splits one record into two. Both damage the row
        /// they are in and neither is a hypothetical.
        /// </remarks>
        [Test]
        public void ANameSurvivesWhateverIsInIt()
        {
            foreach (string name in new[]
            {
                "Bekir", "", " ", "a|b", "line\nbreak", "back\\slash", "carriage\rreturn",
                "\\n", "\\\\", "|||", "trailing\\", "\\p", "Ünlü Kâşif", "🗡️ delver",
                "a b c   d", "tab\there",

                // The one that is not like the others. A carriage return in the MIDDLE of a name
                // survives an escaper that ignores it, because nothing splits on it — so a
                // mutant that stopped escaping it lived through every case above. At the END it
                // does not: the reader trims a trailing return off every line, on the way to
                // reading a file written on Windows, and the name comes back a character short.
                "ends with\r", "\r", "two\r\r",
            })
            {
                var save = new SaveState();
                save.Scores.Add(new ScoreRow { Score = 1, Name = name });
                save.Arts.Add(name);

                Loaded loaded = SaveCodec.Read(SaveCodec.Write(save));

                Assert.That(loaded.Damaged, Is.Zero, "[" + name + "] damaged the save");
                Assert.That(loaded.Save.Scores[0].Name, Is.EqualTo(name), "as a name");
                Assert.That(loaded.Save.Arts[0], Is.EqualTo(name), "as an art key");
            }
        }

        /// <summary>An escape survives on its own, either way round.</summary>
        [Test]
        public void EscapingIsReversible()
        {
            foreach (string text in new[] { "", "|", "\\", "\n", "\r", "\\|", "|\\", "\\\\|" })
            {
                Assert.That(Lines.Plain(Lines.Escaped(text)), Is.EqualTo(text));
            }

            Assert.That(Lines.Escaped("a|b"), Does.Not.Contain("|"),
                "an escaped separator is still a separator, so the line splits in the wrong place");
            Assert.That(Lines.Escaped("a\nb"), Does.Not.Contain("\n"),
                "an escaped newline is still a newline, so one record becomes two");
        }

        /// <summary>
        /// Text this version's encoder could never have written still comes back whole.
        /// </summary>
        /// <remarks>
        /// Three mutants survived everything above, and all three for the same reason: they live
        /// in branches a ROUND TRIP cannot reach, because the encoder never produces the input
        /// that reaches them. That does not make them dead code — it makes them the two cases
        /// where the text did not come from this encoder:
        ///
        /// A file truncated mid-write ends on a lone backslash, and a save written by a later
        /// version carries escapes this one has not been taught. Both are read by this decoder,
        /// and in both the wrong answer is to quietly change what a delver typed — a name that
        /// loses a character every time an old build opens it.
        ///
        /// So they are asked of the decoder directly rather than through a trip that cannot
        /// deliver them.
        /// </remarks>
        [Test]
        public void TextThisVersionCouldNotHaveWrittenIsStillKept()
        {
            Assert.That(Lines.Plain("cut off here\\"), Is.EqualTo("cut off here\\"),
                "a file truncated mid-escape should lose nothing but the truncation");

            Assert.That(Lines.Plain("\\"), Is.EqualTo("\\"));

            Assert.That(Lines.Plain("a\\qb"), Is.EqualTo("a\\qb"),
                "an escape from a later version should come back both characters whole");

            Assert.That(Lines.Plain("\\p\\q\\n"), Is.EqualTo("|\\q\n"),
                "and the ones this version does know still resolve around it");
        }

        /// <summary>Nothing saved is a first run, not a failure.</summary>
        /// <remarks>
        /// The commonest case there is — every delver has it once — and the one where throwing
        /// would be worst, because it happens before there is anything to lose and stops the
        /// game opening at all.
        /// </remarks>
        [Test]
        public void NothingSavedIsAFreshDelver()
        {
            foreach (string nothing in new[] { null, "", "\n", "   \n\n" })
            {
                Loaded loaded = SaveCodec.Read(nothing);

                Assert.That(loaded.Save, Is.Not.Null);
                Assert.That(loaded.Save.Runs, Is.Zero);
                Assert.That(loaded.Save.Unlocked, Is.EqualTo(1), "the first hall is always open");
            }
        }

        /// <summary>
        /// A torn save keeps what is left and says how much went.
        /// </summary>
        /// <remarks>
        /// The two wrong answers are refusing the file — which loses a profile to one bad line —
        /// and skipping quietly, which loses it and says nothing. This keeps every line that
        /// parsed and counts the rest, so a caller has something to log.
        /// </remarks>
        [Test]
        public void ATornSaveKeepsWhatItCanAndCountsWhatItCannot()
        {
            // The two short rows are both here on purpose. Three fields is short enough that any
            // length check catches it; five is one field from whole, and a check that asked for
            // "enough fields" rather than "these fields" would read it and then reach past the
            // end of it.
            Loaded loaded = SaveCodec.Read(
                "v 1\ngold 120\ngold-but-not\nxp notanumber\nruns 4\nscore 1|2|3\n" +
                "score 9|8|7|6|5\n");

            Assert.That(loaded.Save.Gold, Is.EqualTo(120));
            Assert.That(loaded.Save.Runs, Is.EqualTo(4), "a bad line took a good one with it");
            Assert.That(loaded.Save.Xp, Is.Zero);
            Assert.That(loaded.Save.Scores, Is.Empty, "half a row is not a row");

            Assert.That(loaded.Damaged, Is.EqualTo(4), "damage was not reported");
        }

        /// <summary>
        /// A key from a later version is not damage.
        /// </summary>
        /// <remarks>
        /// A delver who plays on a newer build and comes back to an older one should lose the
        /// thing the older build cannot understand, and nothing else — and should not be told
        /// their save is broken, because it is not.
        /// </remarks>
        [Test]
        public void AKeyFromTheFutureIsIgnoredRatherThanRefused()
        {
            Loaded loaded = SaveCodec.Read("v 9\ngold 30\nprestige 4\nwardrobe hat|red\n");

            Assert.That(loaded.Damaged, Is.Zero);
            Assert.That(loaded.Save.Gold, Is.EqualTo(30));
            Assert.That(loaded.Version, Is.EqualTo(9), "the version is what a migration keys on");
        }

        /// <summary>The first hall cannot be taken away.</summary>
        /// <remarks>
        /// Zero is what an absent or unreadable value leaves behind, and a delver with zero halls
        /// unlocked cannot start a run at all — a save that loads into a game with no way in.
        /// </remarks>
        [Test]
        public void TheFirstHallIsAlwaysOpen()
        {
            Assert.That(SaveCodec.Read("unlocked 0\n").Save.Unlocked, Is.EqualTo(1));
            Assert.That(SaveCodec.Read("unlocked -3\n").Save.Unlocked, Is.EqualTo(1));

            Assert.That(SaveCodec.Read("unlocked 6\n").Save.Unlocked, Is.EqualTo(6),
                "and a delver who earned six keeps six");
        }

        /// <summary>A day's seed is unsigned, and the big ones are real.</summary>
        /// <remarks>
        /// Daily seeds run past the top of a signed integer, so a codec that read them as
        /// <c>int</c> would refuse roughly half of all days — and only ever on those days.
        /// </remarks>
        [Test]
        public void ADaySeedSurvivesAboveTheSignedCeiling()
        {
            var save = new SaveState();
            save.DailyBest[4294967295u] = 1200;
            save.DailyDone.Add(3000000000u);

            Loaded loaded = SaveCodec.Read(SaveCodec.Write(save));

            Assert.That(loaded.Damaged, Is.Zero);
            Assert.That(loaded.Save.DailyBestFor(4294967295u), Is.EqualTo(1200));
            Assert.That(loaded.Save.DailyDone, Does.Contain(3000000000u));
        }

        private static SaveState Full()
        {
            var save = new SaveState
            {
                Runs = 41, Clears = 6, GoldLife = 5120, Gold = 340, Best = 2870,
                Xp = 18400, Unlocked = 5, VsCrowns = 3, Sparks = 2,
            };

            save.Scores.Add(new ScoreRow
            {
                Score = 2870, Floor = 13, Kills = 62, Relics = 9,
                Name = "Bekir", Stamp = 1757000000000L,
            });
            save.Scores.Add(new ScoreRow
            {
                Score = 1120, Floor = 8, Kills = 30, Relics = 5, Name = "", Stamp = 0L,
            });

            save.Arts.Add("hall-hoard.png");
            save.Arts.Add("hall-moss.png");

            save.DailyBest[20250101u] = 900;
            save.DailyBest[20250102u] = 1400;
            save.DailyDone.Add(20250101u);

            save.Seen.Add(0);
            save.Seen.Add(17);

            return save;
        }

        private static void Same(SaveState before, SaveState after)
        {
            Assert.That(after.Runs, Is.EqualTo(before.Runs));
            Assert.That(after.Clears, Is.EqualTo(before.Clears));
            Assert.That(after.GoldLife, Is.EqualTo(before.GoldLife));
            Assert.That(after.Gold, Is.EqualTo(before.Gold));
            Assert.That(after.Best, Is.EqualTo(before.Best));
            Assert.That(after.Xp, Is.EqualTo(before.Xp));
            Assert.That(after.Unlocked, Is.EqualTo(before.Unlocked));
            Assert.That(after.VsCrowns, Is.EqualTo(before.VsCrowns));
            Assert.That(after.Sparks, Is.EqualTo(before.Sparks));

            Assert.That(after.Scores.Count, Is.EqualTo(before.Scores.Count));

            for (var i = 0; i < before.Scores.Count; i++)
            {
                ScoreRow was = before.Scores[i];
                ScoreRow now = after.Scores[i];

                Assert.That(now.Score, Is.EqualTo(was.Score), "row " + i);
                Assert.That(now.Floor, Is.EqualTo(was.Floor), "row " + i);
                Assert.That(now.Kills, Is.EqualTo(was.Kills), "row " + i);
                Assert.That(now.Relics, Is.EqualTo(was.Relics), "row " + i);
                Assert.That(now.Name, Is.EqualTo(was.Name), "row " + i);
                Assert.That(now.Stamp, Is.EqualTo(was.Stamp), "row " + i);
            }

            Assert.That(after.Arts, Is.EqualTo(before.Arts));
            Assert.That(after.Seen, Is.EquivalentTo(before.Seen));
            Assert.That(after.DailyDone, Is.EquivalentTo(before.DailyDone));

            foreach (KeyValuePair<uint, int> day in before.DailyBest)
            {
                Assert.That(after.DailyBestFor(day.Key), Is.EqualTo(day.Value),
                    "daily " + day.Key);
            }

            Assert.That(after.DailyBest.Count, Is.EqualTo(before.DailyBest.Count));
        }
    }
}
