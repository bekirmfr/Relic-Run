using NUnit.Framework;
using RelicRun.Core.Meta;
using RelicRun.Core.Presentation;

namespace RelicRun.Tests
{
    /// <summary>
    /// The supporter pack: the one card that asks for money.
    /// </summary>
    /// <remarks>
    /// Almost everything on it is a locale key, which is the point — a delver deciding whether
    /// to pay should be reading their own language, and the source translates every word of this
    /// modal and not one word of the relic card. So what is worth holding here is the small part
    /// that is not words: which face of the card shows, and the one number the pack moves.
    /// </remarks>
    [TestFixture]
    public class SupporterCardTests
    {
        /// <summary>
        /// The three perks are keys, in the source's order, and none of them is a sentence.
        /// </summary>
        /// <remarks>
        /// A key that had been replaced by English at some point would still show SOMETHING on
        /// screen — itself — so nothing about the running game would look wrong. That is exactly
        /// the failure Locale's "answer an unknown key with the key" behaviour makes invisible.
        /// </remarks>
        [Test]
        public void EveryPerkIsAPairOfKeys()
        {
            Assert.That(SupporterCards.Perks.Count, Is.EqualTo(3));

            var titles = new[] { "gildedTitle", "noAdsPerk", "startSparks" };

            for (var i = 0; i < SupporterCards.Perks.Count; i++)
            {
                Perk perk = SupporterCards.Perks[i];

                Assert.That(perk.TitleKey, Is.EqualTo(titles[i]));
                Assert.That(perk.WhatKey, Is.EqualTo(titles[i] + "D"));

                Assert.That(perk.TitleKey, Does.Not.Contain(" "),
                    perk.TitleKey + " looks like a sentence rather than a key");
            }
        }

        /// <summary>
        /// Buying it hands over three hundred sparks, once.
        /// </summary>
        /// <remarks>
        /// The card says what the delver would HAVE rather than what they would gain, so the
        /// arithmetic is visible in the one place a mistake in it would be paid for. Somebody who
        /// already owns the pack gains nothing further: it is bought once, and a card offering
        /// another three hundred is an offer that cannot be taken.
        /// </remarks>
        [Test]
        public void ThePackIsBoughtOnce()
        {
            Assert.That(SupporterCards.Sparks, Is.EqualTo(300));

            var save = new SaveState { Sparks = 40 };

            SupporterCard offered = SupporterCards.Of(new Preferences(), save);

            Assert.That(offered.Owned, Is.False);
            Assert.That(offered.Grants, Is.EqualTo(300));
            Assert.That(offered.Afterwards, Is.EqualTo(340));

            SupporterCard kept = SupporterCards.Of(new Preferences { Supporter = true }, save);

            Assert.That(kept.Owned, Is.True);
            Assert.That(kept.Afterwards, Is.EqualTo(40), "an owned pack offered its sparks again");
        }

        /// <summary>A card asked about nothing at all still has its three perks.</summary>
        /// <remarks>
        /// The modal is reachable before the save is, and a card of blanks in a screen asking for
        /// money is the worst possible moment for the game to look broken.
        /// </remarks>
        [Test]
        public void ACardWithNoSaveStillOffersThePack()
        {
            SupporterCard card = SupporterCards.Of(null, null);

            Assert.That(card.Owned, Is.False);
            Assert.That(card.Perks.Count, Is.EqualTo(3));
            Assert.That(card.Afterwards, Is.EqualTo(SupporterCards.Sparks));
        }
    }
}
