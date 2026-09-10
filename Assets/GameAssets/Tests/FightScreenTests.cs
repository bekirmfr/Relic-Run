using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Core.Stats;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// The two things a fight screen shows that are not about the blow being struck.
    /// </summary>
    /// <remarks>
    /// The row of foes says whether beating this one ends the floor, and the card a fight opens
    /// on says what is about to happen before it does. Both are read at a glance and both are
    /// wrong in ways nobody would notice for a long time — a foe struck out one event early, a
    /// boss announced as a guard — which is what makes them worth a corpus rather than a look.
    /// </remarks>
    [TestFixture]
    public class FightScreenTests
    {
        /// <summary>
        /// The row shows the whole pack, with exactly one of them underfoot.
        /// </summary>
        /// <remarks>
        /// Swept over every position a fight can be at, including the one past the end: a floor
        /// finishes with every foe fallen and nobody up, and a row that insisted on drawing
        /// somebody there would ring a corpse.
        /// </remarks>
        [Test]
        public void OneFoeIsUpAndTheFallenAreBehindIt()
        {
            IReadOnlyList<EnemyState> pack = APack(4);

            for (var done = 0; done <= pack.Count; done++)
            {
                FoeQueueCard row = FoeQueues.Of(pack, done);

                Assert.That(row.Count, Is.EqualTo(4));
                Assert.That(row.Done, Is.EqualTo(done));

                var here = 0;
                var fallen = 0;

                foreach (FoeTile tile in row.Foes)
                {
                    if (tile.Here) here++;
                    if (tile.Done) fallen++;

                    Assert.That(tile.Here && tile.Done, Is.False,
                        "a foe was both being fought and already dead");

                    Assert.That(tile.Struck, Is.EqualTo(tile.Done));
                    Assert.That(tile.Lit, Is.EqualTo(tile.Here));
                    Assert.That(tile.Dim, Is.EqualTo(!tile.Done && !tile.Here));
                }

                Assert.That(fallen, Is.EqualTo(done), "with " + done + " down");
                Assert.That(here, Is.EqualTo(done < pack.Count ? 1 : 0), "with " + done + " down");
            }
        }

        /// <summary>A floor with one foe on it has nothing to queue.</summary>
        /// <remarks>
        /// A row of one repeats the picture in the middle of the screen and spends a line of a
        /// 390-wide shell doing it. Most floors of a run are exactly that.
        /// </remarks>
        [Test]
        public void ARowOfOneIsNotDrawn()
        {
            Assert.That(FoeQueues.Of(APack(1), 0).Shown, Is.False);
            Assert.That(FoeQueues.Of(APack(2), 0).Shown, Is.True);
            Assert.That(FoeQueues.Of(null, 0).Shown, Is.False);
            Assert.That(FoeQueues.Of(null, 0).Count, Is.Zero);
        }

        /// <summary>
        /// A foe being fought is ringed apart from one that is merely a boss.
        /// </summary>
        /// <remarks>
        /// The row's whole job is answering "which one is up" without words, so the ring on the
        /// current foe has to differ from every ring a foe can wear for standing still — and a
        /// boss's steel has to survive being the one underfoot, or the two facts collapse into
        /// one colour.
        /// </remarks>
        [Test]
        public void TheFoeUnderfootIsRingedApart()
        {
            Assert.That(FoeQueues.Ringed(EnemyRank.Guard, true),
                Is.Not.EqualTo(FoeQueues.Ringed(EnemyRank.Guard, false)));

            Assert.That(FoeQueues.Ringed(EnemyRank.Boss, true),
                Is.Not.EqualTo(FoeQueues.Ringed(EnemyRank.Boss, false)));

            Assert.That(FoeQueues.Ringed(EnemyRank.King, true),
                Is.Not.EqualTo(FoeQueues.Ringed(EnemyRank.King, false)));

            // A boss keeps its steel while it is the one being fought.
            Assert.That(FoeQueues.Ringed(EnemyRank.Boss, true),
                Is.Not.EqualTo(FoeQueues.Ringed(EnemyRank.Guard, true)));

            // And anything that outranks a guard is drawn with a thicker edge standing still.
            Assert.That(FoeQueues.Of(One(EnemyRank.Boss), 1).Foes[0].Edge, Is.EqualTo(2));
            Assert.That(FoeQueues.Of(One(EnemyRank.King), 1).Foes[0].Edge, Is.EqualTo(2));
            Assert.That(FoeQueues.Of(One(EnemyRank.Guard), 1).Foes[0].Edge, Is.EqualTo(1));
        }

        /// <summary>
        /// A foe is struck out on the event that kills it, not one later.
        /// </summary>
        /// <remarks>
        /// Over a recorded fight, because this is the piece a hand-built list would agree with by
        /// construction. The count has to be right at EVERY index — playback can be paused on any
        /// one of them — and it must never run past the pack it is counting.
        /// </remarks>
        [Test]
        public void TheFallenAreCountedOnTheBlowThatFells()
        {
            IReadOnlyList<CombatEvent> fight = ARecordedFight();

            var kills = 0;

            for (var i = 0; i < fight.Count; i++)
            {
                if (fight[i].Type == CombatEventType.Kill) kills++;

                Assert.That(FoeQueues.Fallen(fight, i), Is.EqualTo(kills),
                    "at event " + i + ", a " + fight[i].Type);
            }

            Assert.That(kills, Is.GreaterThan(0), "nothing died in the fight being counted");

            // Before anything has been shown, nobody has fallen.
            Assert.That(FoeQueues.Fallen(fight, -1), Is.Zero);
            Assert.That(FoeQueues.Fallen(null, 40), Is.Zero);

            // And asking past the end is asking about the end.
            Assert.That(FoeQueues.Fallen(fight, fight.Count + 20), Is.EqualTo(kills));
        }

        /// <summary>
        /// A delver dying does not strike a foe out of the row.
        /// </summary>
        /// <remarks>
        /// Two ways a fight ends and only one of them is a kill. If a death counted, the row
        /// would show the foe that just won as beaten — on the last frame a delver sees.
        /// </remarks>
        [Test]
        public void ADelverDyingIsNotAKill()
        {
            var events = new List<CombatEvent>
            {
                An(CombatEventType.Enter),
                An(CombatEventType.Kill),
                An(CombatEventType.Enter),
                An(CombatEventType.Death),
            };

            Assert.That(FoeQueues.Fallen(events, events.Count - 1), Is.EqualTo(1));
        }

        /// <summary>
        /// The card announces the fight, where the bestiary names the thing.
        /// </summary>
        /// <remarks>
        /// The same rank, said two ways, and both are in the source. A dungeon king is a DUNGEON
        /// KING in the book a delver reads between runs and a FINAL BOSS on the screen that opens
        /// the fight — the first is a reference and the second is a threat. Written down here
        /// because the obvious tidy-up is to make them one function, and that would quietly
        /// change what the game says at its biggest moment.
        /// </remarks>
        [Test]
        public void TheCardAnnouncesAndTheBookNames()
        {
            Assert.That(IntroCards.Called(EnemyRank.King), Is.EqualTo("FINAL BOSS"));
            Assert.That(EnemyCards.Called(EnemyRank.King), Is.EqualTo("DUNGEON KING"));

            Assert.That(IntroCards.Called(EnemyRank.Elite), Is.EqualTo("HONOR GUARD"));
            Assert.That(EnemyCards.Called(EnemyRank.Elite), Is.EqualTo("ELITE GUARD"));

            // A floor boss is the one both agree on.
            Assert.That(IntroCards.Called(EnemyRank.Boss), Is.EqualTo("FLOOR BOSS"));
            Assert.That(EnemyCards.Called(EnemyRank.Boss), Is.EqualTo("FLOOR BOSS"));

            // And a guard is announced by name and nothing else.
            Assert.That(IntroCards.Called(EnemyRank.Guard), Is.Empty);
        }

        /// <summary>An ordinary guard gets no kicker; everything else does.</summary>
        [Test]
        public void OnlyWhatOutranksAGuardIsAnnounced()
        {
            Assert.That(Card(EnemyRank.Guard).Ranked, Is.False);
            Assert.That(Card(EnemyRank.Guard).Called, Is.Empty);
            Assert.That(Card(EnemyRank.Guard).Hex, Is.Empty);

            foreach (EnemyRank rank in new[] { EnemyRank.Elite, EnemyRank.Boss, EnemyRank.King })
            {
                IntroCard card = Card(rank);

                Assert.That(card.Ranked, Is.True, rank.ToString());
                Assert.That(card.Called, Is.Not.Empty, rank.ToString());
                Assert.That(card.Hex, Is.EqualTo(IntroCards.Coloured(rank)), rank.ToString());
            }

            // The three ranks that are announced are announced in three different colours.
            Assert.That(IntroCards.Coloured(EnemyRank.King),
                Is.Not.EqualTo(IntroCards.Coloured(EnemyRank.Boss)));

            Assert.That(IntroCards.Coloured(EnemyRank.Boss),
                Is.Not.EqualTo(IntroCards.Coloured(EnemyRank.Elite)));

            Assert.That(IntroCards.Coloured(EnemyRank.Elite),
                Is.Not.EqualTo(IntroCards.Coloured(EnemyRank.King)));
        }

        /// <summary>
        /// The card carries the foe's full pool and all four of its numbers.
        /// </summary>
        /// <remarks>
        /// This line is the only look a delver gets at a foe before it matters, and it is read
        /// against their own four — so all four have to be there, in the order theirs are drawn,
        /// and the health has to be the pool rather than whatever is left.
        /// </remarks>
        [Test]
        public void TheCardCarriesTheFourNumbersAndTheWholePool()
        {
            IntroCard card = IntroCards.Of(Snapshot(EnemyRank.Boss, hp: 41, max: 90));

            Assert.That(card.Stats, Is.EqualTo(
                "90 HP" + IntroCards.Between +
                "ATK 12" + IntroCards.Between +
                "DEF 3" + IntroCards.Between +
                "SPD 27" + IntroCards.Between +
                "LCK 8"));

            Assert.That(card.Species, Is.EqualTo(3));
            Assert.That(card.Variant, Is.EqualTo(1));
            Assert.That(card.NameKey, Is.EqualTo(EnemyCards.NameKey(3)));
        }

        /// <summary>Only the bottom of a run is lit.</summary>
        /// <remarks>
        /// Thirteen floors and one of them looks different. Spending the glow anywhere else is
        /// spending it everywhere.
        /// </remarks>
        [Test]
        public void OnlyTheKingIsLit()
        {
            Assert.That(Card(EnemyRank.King).Lit, Is.True);
            Assert.That(Card(EnemyRank.Boss).Lit, Is.False);
            Assert.That(Card(EnemyRank.Elite).Lit, Is.False);
            Assert.That(Card(EnemyRank.Guard).Lit, Is.False);

            Assert.That(Card(EnemyRank.King).Edge, Is.EqualTo(3));
            Assert.That(Card(EnemyRank.Boss).Edge, Is.EqualTo(3));
            Assert.That(Card(EnemyRank.Guard).Edge, Is.EqualTo(2));
        }

        /* ---------- the pieces ---------- */

        private static IntroCard Card(EnemyRank rank)
        {
            return IntroCards.Of(Snapshot(rank, 90, 90));
        }

        private static CombatSnapshot Snapshot(EnemyRank rank, int hp, int max)
        {
            return new CombatSnapshot(0, 100, hp, 0, max, 12, rank, 3, 27, 8, 1, 3, 0,
                new List<RelicId>(), new List<StatModifier>(),
                new CombatCounters(0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        }

        private static CombatEvent An(CombatEventType type)
        {
            return new CombatEvent(type, 0, Snapshot(EnemyRank.Guard, 10, 10));
        }

        private static IReadOnlyList<EnemyState> One(EnemyRank rank)
        {
            return new List<EnemyState> { new EnemyState { Rank = rank } };
        }

        private static IReadOnlyList<EnemyState> APack(int many)
        {
            var pack = new List<EnemyState>();

            for (var i = 0; i < many; i++)
            {
                pack.Add(new EnemyState
                {
                    SpeciesIndex = i,
                    Variant = i % 3,
                    Rank = i == many - 1 ? EnemyRank.Boss : EnemyRank.Guard,
                });
            }

            return pack;
        }

        private static IReadOnlyList<CombatEvent> ARecordedFight()
        {
            List<CorpusFight.Case> cases = CorpusFight.Load("mixed.json");
            CorpusFight.Case busiest = null;

            for (int i = 0; i < cases.Count && i < 40; i++)
            {
                if (busiest == null || cases[i].Events.Count > busiest.Events.Count) busiest = cases[i];
            }

            var engine = new CombatEngine(CombatRules.DelveAsRecorded());

            return engine.ResolveFloor(busiest.Hero, busiest.Pack,
                new Core.Determinism.Mulberry32(busiest.FightSeed)).Events;
        }
    }
}
