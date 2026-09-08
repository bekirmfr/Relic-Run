using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Run;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// The join between the generated content and the project's assets.
    /// </summary>
    /// <remarks>
    /// Everything else in Core is gated by replaying recorded JavaScript, because everything else
    /// in Core is a port. This is not: nothing in the browser game bound a relic to a Unity
    /// sprite, so there is no recording to diff against and the rule has to be stated instead.
    ///
    /// It is stated here rather than in the Editor on purpose. The audit itself is arithmetic
    /// over two lists of strings and runs in microseconds outside Unity; only the lists it is
    /// handed need the Editor. Splitting it that way is what keeps a wrong answer about missing
    /// art a second away rather than a domain reload away.
    /// </remarks>
    [TestFixture]
    public class ContentBindingTests
    {
        private static IEnumerable<Binding> Bound(params string[] ids)
        {
            var bound = new List<Binding>();
            foreach (string id in ids) bound.Add(new Binding(id, true));
            return bound;
        }

        [Test]
        public void ACompleteBindingPasses()
        {
            BindingAudit audit = BindingAudit.Of("relics", new[] { "iron", "boots" },
                Bound("iron", "boots"));

            Assert.That(audit.Passed, Is.True, audit.Report());
            Assert.That(audit.Report(), Is.Empty, "and says nothing when there is nothing to say");
        }

        [Test]
        public void AnIdNobodyBoundIsMissing()
        {
            BindingAudit audit = BindingAudit.Of("relics", new[] { "iron", "boots" }, Bound("iron"));

            Assert.That(audit.Missing, Is.EqualTo(new[] { "boots" }));
            Assert.That(audit.Passed, Is.False);
            Assert.That(audit.Report(), Does.Contain("boots"));
        }

        /// <summary>An entry with an empty slot behind it, which is a hole that looks handled.</summary>
        [Test]
        public void AnIdBoundToNothingIsBlankRatherThanMissing()
        {
            var bound = new List<Binding> { new Binding("iron", true), new Binding("boots", false) };
            BindingAudit audit = BindingAudit.Of("relics", new[] { "iron", "boots" }, bound);

            Assert.That(audit.Blank, Is.EqualTo(new[] { "boots" }));
            Assert.That(audit.Missing, Is.Empty, "somebody made a row for it, so it is not missing");
        }

        [Test]
        public void AnIdNothingWillAskForIsUnknown()
        {
            BindingAudit audit = BindingAudit.Of("relics", new[] { "iron" }, Bound("iron", "cutfromgame"));

            Assert.That(audit.Unknown, Is.EqualTo(new[] { "cutfromgame" }));
            Assert.That(audit.Missing, Is.Empty);
            Assert.That(audit.Passed, Is.False, "complete is not the same as exact");
        }

        [Test]
        public void AnIdBoundTwiceIsDoubled()
        {
            BindingAudit audit = BindingAudit.Of("relics", new[] { "iron" }, Bound("iron", "iron"));

            Assert.That(audit.Doubled, Is.EqualTo(new[] { "iron" }));
            Assert.That(audit.Missing, Is.Empty, "it is bound, twice over");
        }

        /// <summary>Three copies is one problem, not two.</summary>
        [Test]
        public void AnIdBoundThreeTimesIsNamedOnce()
        {
            BindingAudit audit = BindingAudit.Of("relics", new[] { "iron" }, Bound("iron", "iron", "iron"));

            Assert.That(audit.Doubled, Is.EqualTo(new[] { "iron" }));
        }

        /// <summary>An unknown id bound twice is unknown once and doubled once, not unknown twice.</summary>
        [Test]
        public void AnUnknownIdBoundTwiceIsNamedOnceInEachList()
        {
            BindingAudit audit = BindingAudit.Of("relics", new[] { "iron" }, Bound("ghost", "ghost"));

            Assert.That(audit.Unknown, Is.EqualTo(new[] { "ghost" }));
            Assert.That(audit.Doubled, Is.EqualTo(new[] { "ghost" }));
        }

        [Test]
        public void AnExcusedIdMayBeAbsent()
        {
            BindingAudit audit = BindingAudit.Of("relics", new[] { "iron", "debtflesh" },
                Bound("iron"), new[] { "debtflesh" });

            Assert.That(audit.Passed, Is.True, audit.Report());
            Assert.That(audit.Missing, Is.Empty);
        }

        /// <summary>
        /// An excuse that has stopped being true is a failure of its own.
        /// </summary>
        /// <remarks>
        /// Both halves matter. Without the first, an icon could be drawn for a relic and the
        /// exception would sit there forever, so the next relic to lose its art would be silently
        /// forgiven by a line written about a different one. Without the second, a relic could be
        /// cut and its excuse would outlive it.
        /// </remarks>
        [Test]
        public void AnExcuseThatIsNoLongerTrueIsItselfAFailure()
        {
            BindingAudit drawn = BindingAudit.Of("relics", new[] { "iron", "debtflesh" },
                Bound("iron", "debtflesh"), new[] { "debtflesh" });

            Assert.That(drawn.Stale, Is.EqualTo(new[] { "debtflesh" }), "the art arrived");
            Assert.That(drawn.Passed, Is.False);

            BindingAudit cut = BindingAudit.Of("relics", new[] { "iron" }, Bound("iron"),
                new[] { "debtflesh" });

            Assert.That(cut.Stale, Is.EqualTo(new[] { "debtflesh" }), "the relic left");
            Assert.That(cut.Passed, Is.False);
        }

        /// <summary>An excused id bound to an EMPTY slot is still an expired excuse.</summary>
        /// <remarks>
        /// Somebody made a row for it, which means somebody meant to fill it. Staying quiet here
        /// would forgive the one case where a person is halfway through the work.
        /// </remarks>
        [Test]
        public void AnExcusedIdWithAnEmptyRowIsStale()
        {
            var bound = new List<Binding> { new Binding("debtflesh", false) };
            BindingAudit audit = BindingAudit.Of("relics", new[] { "debtflesh" }, bound,
                new[] { "debtflesh" });

            Assert.That(audit.Stale, Is.EqualTo(new[] { "debtflesh" }));
        }

        /// <summary>Reports read down the catalog, not down the alphabet.</summary>
        [Test]
        public void TheReportKeepsTheCallersOrder()
        {
            BindingAudit audit = BindingAudit.Of("halls",
                new[] { "hall-hoard", "hall-moss", "hall-tomb" }, Bound("hall-moss"));

            Assert.That(audit.Missing, Is.EqualTo(new[] { "hall-hoard", "hall-tomb" }));
        }

        /// <summary>A catalog that named the same id twice is not two problems.</summary>
        [Test]
        public void ANeedListThatRepeatsItselfReportsOnce()
        {
            BindingAudit audit = BindingAudit.Of("relics", new[] { "iron", "iron" },
                new List<Binding>());

            Assert.That(audit.Missing, Is.EqualTo(new[] { "iron" }));
        }

        /// <summary>The report names every failure it found, and counts what it truncates.</summary>
        [Test]
        public void TheReportNamesWhatBrokeAndCountsTheRest()
        {
            var needed = new List<string>();
            for (int i = 0; i < 20; i++) needed.Add("relic" + i);

            BindingAudit audit = BindingAudit.Of("relics", needed, Bound("stranger"));
            string report = audit.Report();

            Assert.That(report, Does.StartWith("relics"));
            Assert.That(report, Does.Contain("20 unbound"));
            Assert.That(report, Does.Contain("relic0"));
            Assert.That(report, Does.Contain("and 8 more"), "it names twelve and counts the rest");
            Assert.That(report, Does.Not.Contain("relic19"));
            Assert.That(report, Does.Contain("1 bound but never asked for"));
        }

        /// <summary>Nothing bound at all is a failure, not a crash.</summary>
        [Test]
        public void AnEmptyBindingIsAFailureRatherThanAnException()
        {
            BindingAudit audit = BindingAudit.Of("relics", new[] { "iron" }, null);

            Assert.That(audit.Missing, Is.EqualTo(new[] { "iron" }));

            BindingAudit nothing = BindingAudit.Of("relics", null, null);
            Assert.That(nothing.Passed, Is.True, "and a binding of nothing to nothing is complete");
        }

        /* ---------- the lists a binding is audited against ---------- */

        [Test]
        public void EveryRelicIsAskedForExactlyOnce()
        {
            Assert.That(ContentIds.Relics.Count, Is.EqualTo(RelicCatalog.All.Count));
            Assert.That(ContentIds.Relics, Is.Unique);

            var keys = new HashSet<string>();
            for (int i = 0; i < RelicCatalog.All.Count; i++) keys.Add(RelicCatalog.All[i].Key);

            foreach (string id in ContentIds.Relics)
            {
                Assert.That(keys.Contains(id), Is.True, id + " is not a relic key");
            }
        }

        /// <summary>
        /// The one relic with no icon, read off the sheet rather than typed here.
        /// </summary>
        /// <remarks>
        /// The count is asserted so that a second relic quietly losing its cell fails here
        /// instead of being waved through by a list that only ever says "some are excused".
        /// </remarks>
        [Test]
        public void OnlyDebtOfFleshIsDrawnWithoutAnIcon()
        {
            Assert.That(ContentIds.RelicsWithoutIcons, Is.EqualTo(new[] { "debtflesh" }));
            Assert.That(RelicArt.Get(RelicId.DebtOfFlesh).OnTheSheet, Is.False);
            Assert.That(RelicArt.Get(RelicId.DebtOfFlesh).Glyph, Is.EqualTo("heart"),
                "which is what it draws instead");
        }

        /// <summary>Every excuse names something that is actually asked for.</summary>
        [Test]
        public void TheRelicBindingAuditsCleanWhenEveryIconIsBound()
        {
            var bound = new List<Binding>();
            foreach (string id in ContentIds.Relics)
            {
                if (id != "debtflesh") bound.Add(new Binding(id, true));
            }

            BindingAudit audit = BindingAudit.Of("relics", ContentIds.Relics, bound,
                ContentIds.RelicsWithoutIcons);

            Assert.That(audit.Passed, Is.True, audit.Report());
        }

        [Test]
        public void EveryIconCellIsInsideTheSheetAndUsedOnce()
        {
            var cells = new HashSet<int>();

            foreach (RelicArtDef art in RelicArt.All)
            {
                if (!art.OnTheSheet) continue;

                Assert.That(art.Column, Is.InRange(0, RelicArt.SheetColumns - 1), art.Id.ToString());
                Assert.That(art.Row, Is.InRange(0, RelicArt.SheetRows - 1), art.Id.ToString());
                Assert.That(cells.Add(art.Row * RelicArt.SheetColumns + art.Column), Is.True,
                    art.Id + " shares a cell with another relic");
            }

            Assert.That(cells.Count, Is.EqualTo(RelicCatalog.All.Count - 1));
        }

        /// <summary>Every relic has a glyph, whether or not it needs one.</summary>
        [Test]
        public void EveryRelicHasAGlyphToFallBackTo()
        {
            Assert.That(RelicArt.All.Count, Is.EqualTo(RelicCatalog.All.Count));

            foreach (RelicArtDef art in RelicArt.All)
            {
                Assert.That(art.Glyph, Is.Not.Null.And.Not.Empty, art.Id.ToString());
                Assert.That(RelicArt.Get(art.Id), Is.SameAs(art));
            }
        }

        /// <summary>
        /// The sheets are the size the tables say they are.
        /// </summary>
        /// <remarks>
        /// This is the only thing in the port that can catch a re-exported sheet. Every other
        /// number about the art is generated from the source's own tables, so the tables agree
        /// with themselves however wrong they are; the PNG is the one witness that was not
        /// generated from the same place. A sheet one column wider slices every icon after the
        /// first slightly off, which reads as a drawing mistake rather than an arithmetic one.
        /// </remarks>
        [Test]
        public void TheSheetsAreTheSizeTheTablesSayTheyAre()
        {
            int width, height;

            Corpus.SheetSize("relic-icons.png", out width, out height);
            Assert.That(width, Is.EqualTo(RelicArt.SheetColumns * RelicArt.SheetCell),
                "the icon sheet is " + width + "px across, which is not " +
                RelicArt.SheetColumns + " cells of " + RelicArt.SheetCell);
            Assert.That(height, Is.EqualTo(RelicArt.SheetRows * RelicArt.SheetCell));

            Corpus.SheetSize("enemies-hoard.png", out width, out height);
            Assert.That(width, Is.EqualTo(EnemyCatalog.SheetColumns * EnemyCatalog.SheetCell),
                "one column per rank, and no more");
            Assert.That(height, Is.EqualTo(EnemyCatalog.SheetRows * EnemyCatalog.SheetCell),
                "one row per species, and no more");
        }

        [Test]
        public void EveryHallIsAskedForByTheStemOfItsBackdrop()
        {
            Assert.That(ContentIds.Halls.Count, Is.EqualTo(DungeonCatalog.All.Count));
            Assert.That(ContentIds.Halls, Is.Unique);
            Assert.That(ContentIds.Halls[0], Is.EqualTo("hall-hoard"));

            foreach (string id in ContentIds.Halls)
            {
                Assert.That(id, Does.StartWith("hall-"));
                Assert.That(id, Does.Not.Contain("."), "no extension");
                Assert.That(id, Does.Not.Contain("/"), "and no folder");
            }
        }

        /// <summary>
        /// The illustration table and the rules table agree about how many events there are.
        /// </summary>
        /// <remarks>
        /// They are generated and hand-ported respectively, which is precisely why this is worth
        /// asking: an event added to one and not the other would show the wrong picture for every
        /// event after it, and every single one of them would still be a legal event.
        /// </remarks>
        [Test]
        public void EveryEventHasItsOwnIllustration()
        {
            Assert.That(ContentIds.Events.Count, Is.EqualTo(DungeonEvents.All.Count));
            Assert.That(ContentIds.Events, Is.Unique);

            for (int i = 0; i < DungeonEvents.All.Count; i++)
            {
                Assert.That(EventArt.Get(i), Does.StartWith("event-"), DungeonEvents.All[i].Key);
            }
        }

        [Test]
        public void EverySpeciesIsAskedForAtEveryRankItIsDrawnAt()
        {
            Assert.That(EnemyCatalog.Ranks.Count, Is.EqualTo(EnemyCatalog.SheetColumns));
            Assert.That(EnemyCatalog.All.Count, Is.EqualTo(EnemyCatalog.SheetRows));

            Assert.That(ContentIds.Enemies.Count,
                Is.EqualTo(EnemyCatalog.All.Count * EnemyCatalog.Ranks.Count));
            Assert.That(ContentIds.Enemies, Is.Unique);

            Assert.That(ContentIds.Enemies[0], Is.EqualTo("rat/guard"));
            Assert.That(ContentIds.Enemies[2], Is.EqualTo("rat/boss"));
            Assert.That(ContentIds.Enemies[3], Is.EqualTo("bat/guard"));
        }

        /// <summary>
        /// The sheet row is not the bestiary index, and the catalog is where that is written down.
        /// </summary>
        /// <remarks>
        /// Not one of the thirteen agrees, so a port that assumed the two were the same would
        /// draw the wrong creature every single time — and every one of them would still be a
        /// legal creature, which is why nothing but a person looking at the screen would catch it.
        /// </remarks>
        [Test]
        public void NoSpeciesIsDrawnOnTheRowItsIndexWouldSuggest()
        {
            var rows = new HashSet<int>();
            int agree = 0;

            for (int i = 0; i < EnemyCatalog.All.Count; i++)
            {
                EnemyDef species = EnemyCatalog.All[i];

                Assert.That(species.Index, Is.EqualTo(i), "position is the index");
                Assert.That(species.Key, Is.Not.Null.And.Not.Empty);
                Assert.That(species.SheetRow, Is.InRange(0, EnemyCatalog.SheetRows - 1));
                Assert.That(rows.Add(species.SheetRow), Is.True, species.Key + " shares a row");

                if (species.Index == species.SheetRow) agree++;
            }

            Assert.That(agree, Is.Zero, "the artist drew them in an order all of their own");
        }

        /// <summary>The species the fight names all exist in the table the art is found by.</summary>
        [Test]
        public void TheBestiaryCoversEverySpeciesTheDelveCanField()
        {
            Assert.That(EnemyCatalog.Get(EnemyPackGenerator.KingSpecies).Key, Is.EqualTo("crown"));
            Assert.That(EnemyCatalog.Get(EnemyPackGenerator.GhoolemSpecies).Key, Is.EqualTo("ghoolem"));

            for (int floor = 1; floor <= EnemyPackGenerator.MaxFloor; floor++)
            {
                if (floor == EnemyPackGenerator.BazaarFloor) continue;

                var setup = RunSetup.ForLevel(1);
                foreach (Core.Combat.EnemyState enemy in
                         EnemyPackGenerator.Build(floor, new Core.Determinism.Mulberry32((uint)floor), setup.Dungeon))
                {
                    Assert.That(enemy.SpeciesIndex, Is.InRange(0, EnemyCatalog.All.Count - 1),
                        "floor " + floor + " fields a species with no art");
                }
            }
        }

        [Test]
        public void AnArtPathIsBoundUnderItsStem()
        {
            Assert.That(ContentIds.Stem("assets/hall-hoard.png"), Is.EqualTo("hall-hoard"));
            Assert.That(ContentIds.Stem("hall-hoard.png"), Is.EqualTo("hall-hoard"));
            Assert.That(ContentIds.Stem("hall-hoard"), Is.EqualTo("hall-hoard"));
            Assert.That(ContentIds.Stem("a/b/c.d.png"), Is.EqualTo("c.d"), "only the last dot");
            Assert.That(ContentIds.Stem("a\\b.png"), Is.EqualTo("b"), "and either kind of slash");
            Assert.That(ContentIds.Stem(".gitignore"), Is.EqualTo(".gitignore"),
                "a leading dot is a name, not an extension");
            Assert.That(ContentIds.Stem(""), Is.Empty);
            Assert.That(ContentIds.Stem(null), Is.Empty);
        }
    }
}
