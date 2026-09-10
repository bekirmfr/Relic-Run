using System;
using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Core.Stats;

namespace RelicRun.Tests
{
    /// <summary>
    /// A foe's card, in the two situations that share almost nothing.
    /// </summary>
    /// <remarks>
    /// From the bestiary it is a species dossier and every number is a question mark. From a
    /// fight it is this foe and every number is real. The same card, and the difference is the
    /// whole design: a rat in the first hall and a rat in the eighth are one species and not one
    /// fight, so a dossier that printed a hall's numbers would be wrong about the other nine.
    /// </remarks>
    [TestFixture]
    public class EnemyCardTests
    {
        /// <summary>Every species has a dossier, with a name key and a line of its own.</summary>
        [Test]
        public void EverySpeciesHasADossier()
        {
            Assert.That(EnemyCatalog.All.Count, Is.EqualTo(13), "the bestiary changed size");

            var lines = new List<string>();

            foreach (EnemyDef def in EnemyCatalog.All)
            {
                EnemyCard card = EnemyCards.Dossier(def.Index);

                Assert.That(card.NameKey, Is.EqualTo("en" + def.Index));
                Assert.That(card.Lore, Is.Not.Null.And.Not.Empty, def.Key + " has no line");
                Assert.That(card.Lore, Does.Contain(def.Lore), def.Key + " was given another's");

                Assert.That(lines.Contains(card.Lore), Is.False,
                    def.Key + " shares its line with another species");

                lines.Add(card.Lore);
            }
        }

        /// <summary>
        /// A dossier admits it does not know.
        /// </summary>
        /// <remarks>
        /// All four numbers, not some of them. A card that filled in speed and left HP blank
        /// would read as a species whose HP is unknowable rather than as a screen that has no
        /// fight to read from.
        /// </remarks>
        [Test]
        public void ADossierKnowsNoNumbers()
        {
            foreach (EnemyDef def in EnemyCatalog.All)
            {
                EnemyCard card = EnemyCards.Dossier(def.Index);

                Assert.That(card.Measured, Is.False);
                Assert.That(card.Stats.Count, Is.EqualTo(4));

                foreach (FoeStat stat in card.Stats)
                {
                    Assert.That(stat.Value, Is.EqualTo(EnemyCards.Unmeasured),
                        def.Key + " claims to know its " + stat.Label);
                }

                Assert.That(card.Abilities, Is.EqualTo(EnemyCards.Varies));
                Assert.That(card.Relics.Count, Is.Zero);
                Assert.That(card.Kicker, Is.EqualTo(EnemyCards.Encountered));

                // The plainest column. A dossier drawn in a boss's armour would say this is what
                // one always looks like.
                Assert.That(card.Variant, Is.Zero, def.Key + " is drawn as something it may not be");
            }
        }

        /// <summary>
        /// A live foe's card reports what it has left, and never a negative.
        /// </summary>
        /// <remarks>
        /// The engine keeps the working pool and lets it go below zero on the killing blow, so
        /// the card is asked to show a number the fight is briefly holding. "-4 / 30" on the one
        /// frame a delver can open the card is the sort of thing that gets reported as a bug in
        /// the combat maths.
        /// </remarks>
        [Test]
        public void ALiveCardShowsWhatIsLeft()
        {
            var foe = new EnemyState
            {
                SpeciesIndex = 3,
                Hp = 30,
                MaxHp = 30,
                Atk = 7,
                Spd = 22,
                Armor = 2,
                Rank = EnemyRank.Guard,
            };

            EnemyCard half = EnemyCards.Live(foe, 12);

            Assert.That(half.Measured, Is.True);
            Assert.That(Stat(half, "HP"), Is.EqualTo("12 / 30"));
            Assert.That(Stat(half, "ATK"), Is.EqualTo("7"));
            Assert.That(Stat(half, "SPD"), Is.EqualTo("22"));
            Assert.That(Stat(half, "ARMOR"), Is.EqualTo("2"));

            EnemyCard dead = EnemyCards.Live(foe, -4);

            Assert.That(Stat(dead, "HP"), Is.EqualTo("0 / 30"), "a foe was shown at less than nothing");
        }

        /// <summary>
        /// A foe whose ceiling was never set is measured against what it started with.
        /// </summary>
        /// <remarks>
        /// MaxHp is normally equal to Hp and the only thing that moves it is an awakened Vampire
        /// Tooth draining the ceiling. A foe built without one would otherwise read "18 / 0",
        /// which is a bar that cannot be drawn.
        /// </remarks>
        [Test]
        public void AFoeWithNoCeilingUsesItsOwnHp()
        {
            var foe = new EnemyState { SpeciesIndex = 0, Hp = 18, MaxHp = 0 };

            Assert.That(Stat(EnemyCards.Live(foe, 18), "HP"), Is.EqualTo("18 / 18"));
        }

        /// <summary>
        /// Rank names the card and colours it, and the four ranks do not agree.
        /// </summary>
        /// <remarks>
        /// It is the first thing a delver reads and the only warning that this fight is not the
        /// last one. Two ranks sharing a name would make a king look like a guard at the moment
        /// it matters most; the two that DO share a colour — boss and king, both gold — share it
        /// on purpose, because both mean "this one ends the floor".
        /// </remarks>
        [Test]
        public void EveryRankIsNamedAndNoTwoAgree()
        {
            var seen = new List<string>();

            foreach (EnemyRank rank in Enum.GetValues(typeof(EnemyRank)))
            {
                if (rank == EnemyRank.None) continue;

                string called = EnemyCards.Called(rank);

                Assert.That(called, Is.Not.Null.And.Not.Empty, rank + " has no name");
                Assert.That(seen.Contains(called), Is.False, called + " names two ranks");

                seen.Add(called);

                Assert.That(EnemyCards.Coloured(rank), Does.StartWith("#"));
                Assert.That(EnemyCards.Coloured(rank).Length, Is.EqualTo(7));
            }

            Assert.That(EnemyCards.Called(EnemyRank.King), Is.EqualTo("DUNGEON KING"));
            Assert.That(EnemyCards.Called(EnemyRank.Boss), Is.EqualTo("FLOOR BOSS"));
            Assert.That(EnemyCards.Called(EnemyRank.Elite), Is.EqualTo("ELITE GUARD"));
            Assert.That(EnemyCards.Called(EnemyRank.Guard), Is.EqualTo("GUARD"));

            Assert.That(EnemyCards.Coloured(EnemyRank.King),
                Is.EqualTo(EnemyCards.Coloured(EnemyRank.Boss)),
                "a king and a boss are both the end of a floor and are both gold");

            Assert.That(EnemyCards.Coloured(EnemyRank.Elite),
                Is.Not.EqualTo(EnemyCards.Coloured(EnemyRank.Guard)));
        }

        /// <summary>
        /// What a foe's relics DO, rather than an empty bracket after each name.
        /// </summary>
        /// <remarks>
        /// The source reads a passive off a table that has none, so its abilities row came out as
        /// a list of names each followed by "()" — saying nothing, twice, since the row under it
        /// already lists the names. The passives are read from the table they are actually in.
        /// </remarks>
        [Test]
        public void ItsRelicsSayWhatTheyDo()
        {
            var foe = new EnemyState
            {
                SpeciesIndex = 5,
                Hp = 40,
                MaxHp = 40,
                Relics = new List<RelicId> { RelicId.IronSkin, RelicId.Whetstone },
            };

            EnemyCard card = EnemyCards.Live(foe, 40);

            Assert.That(card.Relics.Count, Is.EqualTo(2));
            Assert.That(card.Abilities, Does.Contain(RelicLore.Get(RelicId.IronSkin).Passive));
            Assert.That(card.Abilities, Does.Contain(RelicLore.Get(RelicId.Whetstone).Passive));
            Assert.That(card.Abilities, Does.Contain(EnemyCards.Between));
            Assert.That(card.Abilities, Does.Not.Contain("()"));
        }

        /// <summary>
        /// A foe carrying nothing says so, and so does one whose relics are all silent.
        /// </summary>
        /// <remarks>
        /// Null and empty are different to the engine and the same to a reader. A relic with no
        /// passive contributes nothing to the row, so a foe carrying only those has an empty row
        /// rather than a missing one — and an empty row is what the "none" line is for.
        /// </remarks>
        [Test]
        public void AFoeWithNothingToSaySaysSo()
        {
            var bare = new EnemyState { SpeciesIndex = 1, Hp = 10, MaxHp = 10 };

            Assert.That(EnemyCards.Live(bare, 10).Abilities, Is.EqualTo(EnemyCards.Nothing));
            Assert.That(EnemyCards.Live(bare, 10).Relics.Count, Is.Zero);

            var empty = new EnemyState
            {
                SpeciesIndex = 1,
                Hp = 10,
                MaxHp = 10,
                Relics = new List<RelicId>(),
            };

            Assert.That(EnemyCards.Live(empty, 10).Abilities, Is.EqualTo(EnemyCards.Nothing));

            RelicId silent = Silent();

            var quiet = new EnemyState
            {
                SpeciesIndex = 1,
                Hp = 10,
                MaxHp = 10,
                Relics = new List<RelicId> { silent },
            };

            EnemyCard card = EnemyCards.Live(quiet, 10);

            Assert.That(card.Abilities, Is.EqualTo(EnemyCards.Nothing),
                "a relic with no passive left an empty entry in the row");

            // It is still CARRIED, and the row under says so. The two rows answer different
            // questions and only one of them came up empty.
            Assert.That(card.Relics.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// A silent relic beside a talkative one leaves no gap in the row.
        /// </summary>
        /// <remarks>
        /// The case a mutation found: skipping the empty entry and appending an empty one read
        /// the same as long as the foe carried ONE relic. With two, the second is a separator
        /// with nothing after it — a sentence that trails off, which reads as text that failed to
        /// load rather than as a relic that does nothing by itself.
        /// </remarks>
        [Test]
        public void ASilentRelicLeavesNoTrailingSeparator()
        {
            var foe = new EnemyState
            {
                SpeciesIndex = 2,
                Hp = 20,
                MaxHp = 20,
                Relics = new List<RelicId> { RelicId.IronSkin, Silent() },
            };

            EnemyCard card = EnemyCards.Live(foe, 20);

            Assert.That(card.Relics.Count, Is.EqualTo(2), "both are carried");
            Assert.That(card.Abilities, Is.EqualTo(RelicLore.Get(RelicId.IronSkin).Passive),
                "the row picked up something from a relic with nothing to say");
            Assert.That(card.Abilities, Does.Not.EndWith(EnemyCards.Between));
        }

        /// <summary>
        /// The card and the bestiary name the same species the same way.
        /// </summary>
        /// <remarks>
        /// The list and the card it opens are one press apart and work the key out separately.
        /// A mismatch would show a name on the list and a raw key on the card, or worse, another
        /// species' name — and both screens would look like they were working.
        /// </remarks>
        [Test]
        public void TheListAndTheCardAgree()
        {
            var save = new Core.Meta.SaveState();

            for (var i = 0; i < EnemyCatalog.All.Count; i++) save.Seen.Add(i);

            BestiaryCard list = BestiaryCards.Of(save);

            foreach (BestiaryEntry entry in list.Foes)
            {
                EnemyCard card = EnemyCards.Dossier(entry.Species);

                Assert.That(card.NameKey, Is.EqualTo(entry.NameKey));
                Assert.That(card.Lore, Is.EqualTo(entry.Lore));
            }
        }

        /// <summary>A relic that has nothing to say about itself.</summary>
        private static RelicId Silent()
        {
            foreach (RelicDef relic in RelicCatalog.All)
            {
                RelicLoreDef lore = RelicLore.Get(relic.Id);

                if (lore != null && lore.Passive == null) return relic.Id;
            }

            Assert.Fail("every relic has a passive, so this case cannot be reached");
            return RelicId.Whetstone;
        }

        private static string Stat(EnemyCard card, string label)
        {
            foreach (FoeStat stat in card.Stats)
            {
                if (stat.Label == label) return stat.Value;
            }

            Assert.Fail("the card has no " + label);
            return null;
        }
    }
}
