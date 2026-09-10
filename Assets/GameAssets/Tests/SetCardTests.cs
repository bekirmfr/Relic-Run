using System;
using System.Collections.Generic;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;

namespace RelicRun.Tests
{
    /// <summary>
    /// The set card: what collecting a family is worth, and whether it has been.
    /// </summary>
    /// <remarks>
    /// Reached from a relic's own card, so a delver looking at one relic is one press from what
    /// six more of its kind would buy. From the relic book nothing is held and every step reads
    /// as off, which is the card doing its other job — showing what a family is worth BEFORE
    /// committing to it.
    /// </remarks>
    [TestFixture]
    public class SetCardTests
    {
        /// <summary>
        /// Every family has a card, with steps in order.
        /// </summary>
        /// <remarks>
        /// Ascending order is not decoration: the card is read top to bottom as "then, then",
        /// and a family whose steps arrived unsorted would show the deepest bonus as the easiest
        /// one to reach.
        /// </remarks>
        [Test]
        public void EveryFamilyHasStepsInOrder()
        {
            foreach (RelicKind family in Enum.GetValues(typeof(RelicKind)))
            {
                SetCard card = SetCards.Of(family);

                Assert.That(card.Name, Is.Not.Null.And.Not.Empty, family + " has no name");
                Assert.That(card.Hex, Does.StartWith("#"), family + " has no colour");
                Assert.That(card.Steps.Count, Is.GreaterThan(0), family + " has no steps");

                for (var i = 1; i < card.Steps.Count; i++)
                {
                    Assert.That(card.Steps[i].Needs, Is.GreaterThan(card.Steps[i - 1].Needs),
                        family + " lists its steps out of order");
                }

                foreach (SetStep step in card.Steps)
                {
                    Assert.That(step.What, Is.Not.Null.And.Not.Empty,
                        family + " has a step that says nothing");
                    Assert.That(step.Tag, Is.EqualTo("(" + step.Needs + ")"));
                }
            }
        }

        /// <summary>
        /// Seven families climb to seven; Curse stops at three.
        /// </summary>
        /// <remarks>
        /// Curse is the odd one out, and the card is built to show it rather than to assume
        /// three tiers everywhere. Pinned because a generator that quietly padded Curse to three
        /// steps would invent two bonuses the engine has never heard of.
        /// </remarks>
        [Test]
        public void CurseIsTheShortOne()
        {
            Assert.That(SetCards.Of(RelicKind.Curse).Steps.Count, Is.EqualTo(1));

            foreach (RelicKind family in Enum.GetValues(typeof(RelicKind)))
            {
                if (family == RelicKind.Curse) continue;

                SetCard card = SetCards.Of(family);

                Assert.That(card.Steps.Count, Is.EqualTo(3), family + " does not have three steps");
                Assert.That(card.Steps[0].Needs, Is.EqualTo(3));
                Assert.That(card.Steps[1].Needs, Is.EqualTo(5));
                Assert.That(card.Steps[2].Needs, Is.EqualTo(7));
            }
        }

        /// <summary>
        /// Nothing held means nothing lit.
        /// </summary>
        /// <remarks>
        /// The relic book's case, and the one where being wrong is loudest: a card opened from
        /// the book that showed a step as reached would be claiming a bonus for a delver who is
        /// not even in a run.
        /// </remarks>
        [Test]
        public void AnEmptyHandLightsNothing()
        {
            foreach (RelicKind family in Enum.GetValues(typeof(RelicKind)))
            {
                SetCard card = SetCards.Of(family);

                Assert.That(card.Held, Is.EqualTo(0));
                Assert.That(card.Idol, Is.False);
                Assert.That(card.Members.Count, Is.EqualTo(0));

                foreach (SetStep step in card.Steps) Assert.That(step.On, Is.False);
            }
        }

        /// <summary>
        /// A step switches on at exactly the count it names, and not one short.
        /// </summary>
        /// <remarks>
        /// The whole arithmetic of the card in one sweep. An off-by-one here is the difference
        /// between a delver taking a seventh Guard relic and a delver taking something useful,
        /// and it is invisible in a screenshot.
        /// </remarks>
        [Test]
        public void AStepLightsAtItsOwnNumber()
        {
            foreach (RelicKind family in Enum.GetValues(typeof(RelicKind)))
            {
                for (var carrying = 0; carrying <= 8; carrying++)
                {
                    SetCard card = SetCards.Of(family, Hand(family, carrying), CombatMode.Delve);

                    Assert.That(card.Held, Is.EqualTo(carrying));

                    foreach (SetStep step in card.Steps)
                    {
                        Assert.That(step.On, Is.EqualTo(carrying >= step.Needs),
                            family + " at " + carrying + " got its " + step.Needs + " step wrong");
                    }
                }
            }
        }

        /// <summary>
        /// Duplicates count and are listed once.
        /// </summary>
        /// <remarks>
        /// The two halves of the same sentence. Three Whetstones ARE three toward the Edge set —
        /// that is most of how a set is ever finished — and the members line naming Whetstone
        /// three times would waste the only room the card has for what is actually in there.
        /// </remarks>
        [Test]
        public void CopiesCountOnceEachAndAreNamedOnce()
        {
            var hand = new List<RelicId>
            {
                RelicId.Whetstone,
                RelicId.Whetstone,
                RelicId.Whetstone,
                RelicId.MidasBlade,
            };

            SetCard card = SetCards.Of(RelicKind.Edge, hand, CombatMode.Delve);

            Assert.That(card.Held, Is.EqualTo(4));
            Assert.That(card.Steps[0].On, Is.True);
            Assert.That(card.Members.Count, Is.EqualTo(2));
            Assert.That(Names(card, RelicId.Whetstone), Is.True);
            Assert.That(Names(card, RelicId.MidasBlade), Is.True);
        }

        /// <summary>
        /// The heading says what the card is, not just which family.
        /// </summary>
        /// <remarks>
        /// Held against the literal rather than against the constant, which is the trick a
        /// mutation taught: every assertion that built the expected heading OUT of the constant
        /// went on passing when the constant was emptied, and every set card would have been
        /// headed EDGE — the family, and not what collecting it is worth.
        /// </remarks>
        [Test]
        public void TheHeadingSaysWhatTheCardIs()
        {
            Assert.That(SetCards.Suffix, Is.EqualTo(" SET"));
            Assert.That(SetCards.Between, Is.EqualTo(" · "));
            Assert.That(SetCards.IdolNote, Is.EqualTo(" (Hollow Idol +1)"));

            Assert.That(SetCards.Of(RelicKind.Edge).Name, Is.EqualTo("EDGE SET"));
            Assert.That(SetCards.Of(RelicKind.Curse).Name, Is.EqualTo("CURSE SET"));
        }

        /// <summary>Only relics of the family are named, whatever else is in hand.</summary>
        [Test]
        public void ARelicOfAnotherFamilyIsNotAMember()
        {
            var hand = new List<RelicId> { RelicId.Whetstone, RelicId.IronSkin };

            SetCard edge = SetCards.Of(RelicKind.Edge, hand, CombatMode.Delve);

            Assert.That(edge.Held, Is.EqualTo(1));
            Assert.That(Names(edge, RelicId.IronSkin), Is.False);
        }

        /// <summary>
        /// The Hollow Idol pads every family in a delve, and none in a duel.
        /// </summary>
        /// <remarks>
        /// The one place the card is deliberately NOT the source. The source adds the idol
        /// unconditionally; the port's rules add it only in a delve, and a card that copied the
        /// source would tell a duellist they had a tier the engine will never grant. This asks
        /// the same table the engine asks, so the two cannot drift.
        /// </remarks>
        [Test]
        public void TheIdolPadsADelveAndNotADuel()
        {
            var hand = new List<RelicId>
            {
                RelicId.Whetstone,
                RelicId.MidasBlade,
                RelicId.HollowIdol,
            };

            SetCard delve = SetCards.Of(RelicKind.Edge, hand, CombatMode.Delve);

            Assert.That(delve.Held, Is.EqualTo(3), "the idol did not pad a delve");
            Assert.That(delve.Idol, Is.True);
            Assert.That(delve.Steps[0].On, Is.True);
            Assert.That(delve.CountText, Does.Contain(SetCards.IdolNote));

            SetCard duel = SetCards.Of(RelicKind.Edge, hand, CombatMode.Versus);

            Assert.That(duel.Held, Is.EqualTo(2), "the idol padded a duel");
            Assert.That(duel.Idol, Is.False);
            Assert.That(duel.Steps[0].On, Is.False);
            Assert.That(duel.CountText, Does.Not.Contain(SetCards.IdolNote));
        }

        /// <summary>
        /// Toward its own family, the idol counts twice.
        /// </summary>
        /// <remarks>
        /// It is a Curse relic, so the Curse count has it before the padding is added. That is
        /// the source's arithmetic and it is kept rather than corrected — Curse has one step and
        /// it is +1 ATK, so what the double count buys is a bonus the delver already has. Pinned
        /// because it looks exactly like a bug and would otherwise be "fixed" by somebody
        /// reading only the code.
        /// </remarks>
        [Test]
        public void TheIdolCountsTwiceTowardItsOwnFamily()
        {
            var hand = new List<RelicId> { RelicId.HollowIdol };

            SetCard curse = SetCards.Of(RelicKind.Curse, hand, CombatMode.Delve);

            Assert.That(curse.Held, Is.EqualTo(2));
            Assert.That(curse.Members.Count, Is.EqualTo(1));

            SetCard guard = SetCards.Of(RelicKind.Guard, hand, CombatMode.Delve);

            Assert.That(guard.Held, Is.EqualTo(1));
            Assert.That(guard.Members.Count, Is.EqualTo(0));
        }

        /// <summary>The count line pluralises, and says when an idol is propping it up.</summary>
        [Test]
        public void TheCountLineReadsLikeEnglish()
        {
            Assert.That(SetCards.Of(RelicKind.Edge).CountText, Is.EqualTo("0 relics"));

            Assert.That(
                SetCards.Of(RelicKind.Edge, new List<RelicId> { RelicId.Whetstone },
                    CombatMode.Delve).CountText,
                Is.EqualTo("1 relic"));

            Assert.That(
                SetCards.Of(RelicKind.Edge,
                    new List<RelicId> { RelicId.Whetstone, RelicId.MidasBlade },
                    CombatMode.Delve).CountText,
                Is.EqualTo("2 relics"));

            Assert.That(
                SetCards.Of(RelicKind.Edge, new List<RelicId> { RelicId.HollowIdol },
                    CombatMode.Delve).CountText,
                Is.EqualTo("1 relic (Hollow Idol +1)"));
        }

        /// <summary>
        /// The family a relic's card names is the family whose card that chip opens.
        /// </summary>
        /// <remarks>
        /// The chip is the only way into this screen outside a run, so a mismatch would send a
        /// delver to the wrong family's bonuses while the relic in front of them said otherwise.
        /// </remarks>
        [Test]
        public void TheChipOnARelicOpensItsOwnFamily()
        {
            foreach (RelicDef relic in RelicCatalog.All)
            {
                RelicCard card = RelicCards.Of(relic.Id);
                SetCard set = SetCards.Of(card.Family);

                // The whole heading rather than its start: a suffix that went missing would
                // leave every set card headed EDGE, which reads as the family and not as what
                // collecting it is worth.
                Assert.That(set.Name, Is.EqualTo(card.FamilyName + " SET"),
                    relic.Key + " points elsewhere");
                Assert.That(set.Hex, Is.EqualTo(card.FamilyHex), relic.Key + " changes colour on the way");
            }
        }

        /// <summary>Whether the members line names this relic. IReadOnlyList has no Contains.</summary>
        private static bool Names(SetCard card, RelicId relic)
        {
            foreach (RelicId one in card.Members)
            {
                if (one == relic) return true;
            }

            return false;
        }

        /// <summary>A hand holding the given number of relics of one family.</summary>
        private static List<RelicId> Hand(RelicKind family, int many)
        {
            var hand = new List<RelicId>();

            foreach (RelicDef relic in RelicCatalog.All)
            {
                if (hand.Count >= many) break;

                // The idol is skipped: it counts toward every family, so a hand built from it
                // would test the padding rather than the plain count these cases are about.
                if (relic.Id == RelicId.HollowIdol) continue;
                if (relic.Kind != family) continue;

                hand.Add(relic.Id);
            }

            while (hand.Count < many && hand.Count > 0) hand.Add(hand[0]);

            Assert.That(hand.Count, Is.EqualTo(many),
                "could not build a hand of " + many + " " + family + " relics");

            return hand;
        }
    }
}
