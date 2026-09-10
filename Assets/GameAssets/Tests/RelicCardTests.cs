using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// The relic card: the one screen where being wrong costs a run.
    /// </summary>
    /// <remarks>
    /// A delver opens this to decide whether a relic is worth a draft slot, and then plays fifty
    /// floors on the answer. Everything else in the interface is a report of something that has
    /// already happened; this is the only screen a decision is made from, which is why every
    /// relic is swept rather than a handful sampled.
    /// </remarks>
    [TestFixture]
    public class RelicCardTests
    {
        /// <summary>
        /// Every relic has a card at all.
        /// </summary>
        /// <remarks>
        /// The cheapest possible gate and worth having: a relic added to the catalogue without
        /// a matching line in the lore table would come back as a card of blanks, and a card of
        /// blanks looks like a relic that does nothing rather than like a missing row.
        /// </remarks>
        [Test]
        public void EveryRelicHasAName()
        {
            foreach (RelicDef relic in RelicCatalog.All)
            {
                RelicCard card = RelicCards.Of(relic.Id);

                Assert.That(card.Name, Is.Not.Null.And.Not.Empty, relic.Key + " has no name");
                Assert.That(card.Flavour, Is.Not.Null.And.Not.Empty,
                    relic.Key + " has no flavour line");
                Assert.That(card.FamilyName, Is.Not.Null.And.Not.Empty,
                    relic.Key + " has no family");
            }
        }

        /// <summary>
        /// Every relic says at least one thing about what it does.
        /// </summary>
        /// <remarks>
        /// A card with a flavour line and no rule rows is prose about a relic whose effect is
        /// invisible. The description is the fallback — some relics carry their whole effect
        /// there — so the gate is that at least one of the two exists, not that both do.
        /// </remarks>
        [Test]
        public void EveryRelicSaysWhatItDoes()
        {
            foreach (RelicDef relic in RelicCatalog.All)
            {
                RelicCard card = RelicCards.Of(relic.Id);

                bool described = card.What != null || card.WhatKey != null;

                Assert.That(described || card.Rows.Count > 0, Is.True,
                    relic.Key + " has neither a description nor a rule row");
            }
        }

        /// <summary>
        /// A relic that fires by itself says ON ACTIVATION; one that cannot says IF RELAYED.
        /// </summary>
        /// <remarks>
        /// The same sentence under two labels, and the difference is the whole point: a relic
        /// with no trigger of its own never fires unless somebody sockets a trigger onto it, so
        /// a card that said ON ACTIVATION for both would be promising an effect that cannot
        /// happen. Swept, because which relics have a native trigger is content and moves.
        /// </remarks>
        [Test]
        public void OnlyARelicWithATriggerOfItsOwnSaysOnActivation()
        {
            var relayed = 0;
            var activated = 0;

            foreach (RelicDef relic in RelicCatalog.All)
            {
                RelicCard card = RelicCards.Of(relic.Id);

                bool trigger = Has(card, RelicRowKind.Trigger);
                bool onActivation = Has(card, RelicRowKind.Activation);
                bool ifRelayed = Has(card, RelicRowKind.Relayed);

                Assert.That(onActivation && ifRelayed, Is.False,
                    relic.Key + " labels its activation two ways at once");

                if (onActivation) activated++;
                if (ifRelayed) relayed++;

                if (onActivation) Assert.That(trigger, Is.True,
                    relic.Key + " says ON ACTIVATION with nothing to activate it");

                if (ifRelayed) Assert.That(trigger, Is.False,
                    relic.Key + " says IF RELAYED and has its own trigger");
            }

            Assert.That(activated, Is.GreaterThan(0), "no relic activates by itself");
            Assert.That(relayed, Is.GreaterThan(0), "no relic waits to be relayed");
        }

        /// <summary>
        /// The awakening promise and the awakening report never both show.
        /// </summary>
        /// <remarks>
        /// One is "this WOULD do", the other "this DID". A card showing both reads as a relic
        /// that awakened twice; a card showing neither, for a relic that can awaken, silently
        /// drops the reason to spend gold on it at the bazaar.
        /// </remarks>
        [Test]
        public void AwakeningIsEitherAPromiseOrAReport()
        {
            var canAwaken = 0;

            foreach (RelicDef relic in RelicCatalog.All)
            {
                RelicCard asleep = RelicCards.Of(relic.Id);
                RelicCard awake = RelicCards.Of(relic.Id,
                    new RelicHolding { Owned = 1, Awakened = true });

                Assert.That(asleep.Awoke, Is.Null, relic.Key + " reports an awakening it has not had");
                Assert.That(awake.AwakeLine, Is.Null, relic.Key + " still promises what it already did");

                bool possible = RelicLore.Get(relic.Id).Awake != null;

                if (!possible)
                {
                    Assert.That(asleep.AwakeLine, Is.Null);
                    Assert.That(awake.Awoke, Is.Null);
                    continue;
                }

                canAwaken++;

                Assert.That(asleep.AwakeLine, Does.StartWith(RelicCards.PromisePrefix));
                Assert.That(awake.Awoke, Does.StartWith(RelicCards.AwokePrefix));
            }

            Assert.That(canAwaken, Is.EqualTo(39), "the number of relics that can awaken moved");
        }

        /// <summary>
        /// An awakened relic wears the star even when it is also socketed.
        /// </summary>
        /// <remarks>
        /// The source's precedence, and the right way round: awakening changes what a relic
        /// DOES and a socket changes when, so the larger claim gets the one character of room
        /// in front of the name.
        /// </remarks>
        [Test]
        public void TheStarBeatsTheLozenge()
        {
            RelicId any = RelicId.Whetstone;

            Assert.That(RelicCards.Of(any).Mark, Is.Empty);

            Assert.That(RelicCards.Of(any, new RelicHolding { Trigger = SocketTrigger.Kill }).Mark,
                Is.EqualTo(RelicCards.SocketedMark));

            Assert.That(RelicCards.Of(any, new RelicHolding { Awakened = true }).Mark,
                Is.EqualTo(RelicCards.AwakenedMark));

            Assert.That(
                RelicCards.Of(any, new RelicHolding
                {
                    Awakened = true,
                    Trigger = SocketTrigger.Kill,
                }).Mark,
                Is.EqualTo(RelicCards.AwakenedMark));
        }

        /// <summary>
        /// A fitted socket adds one row, named for what was fitted.
        /// </summary>
        /// <remarks>
        /// Triggers and emitters are bought from the same shelf and fitted the same way, and
        /// they are not the same thing: one decides WHEN the relic fires and the other adds
        /// something to what happens. The card labels them apart because a delver choosing a
        /// second component needs to know which half they already have.
        /// </remarks>
        [Test]
        public void ASocketNamesItself()
        {
            RelicCard bare = RelicCards.Of(RelicId.Whetstone);

            Assert.That(Has(bare, RelicRowKind.SocketTrigger), Is.False);
            Assert.That(Has(bare, RelicRowKind.SocketEmitter), Is.False);
            Assert.That(bare.SocketNote, Is.Null);

            RelicCard triggered = RelicCards.Of(RelicId.Whetstone,
                new RelicHolding { Trigger = SocketTrigger.Kill });

            Assert.That(Has(triggered, RelicRowKind.SocketTrigger), Is.True);
            Assert.That(Row(triggered, RelicRowKind.SocketTrigger),
                Does.Contain(SocketText.Of(SocketTrigger.Kill).Name));
            Assert.That(triggered.SocketNote, Does.Contain(SocketText.Of(SocketTrigger.Kill).What));

            RelicCard emitting = RelicCards.Of(RelicId.Whetstone,
                new RelicHolding { Emitter = SocketEmitter.Heal });

            Assert.That(Has(emitting, RelicRowKind.SocketEmitter), Is.True);
            Assert.That(Row(emitting, RelicRowKind.SocketEmitter),
                Does.Contain(SocketText.Of(SocketEmitter.Heal).Name));
        }

        /// <summary>
        /// The socket row is the last one, under whatever the relic does by itself.
        /// </summary>
        /// <remarks>
        /// Order is content here. What a relic does natively is what it will do in every run;
        /// what a socket adds is true of one copy in one run, and putting it above the native
        /// rows would read as the relic's own behaviour.
        /// </remarks>
        [Test]
        public void WhatWasFittedComesLast()
        {
            RelicCard card = RelicCards.Of(RelicId.CoinMagnet,
                new RelicHolding { Owned = 1, Trigger = SocketTrigger.Gold });

            Assert.That(card.Rows.Count, Is.GreaterThan(1));
            Assert.That(card.Rows[card.Rows.Count - 1].Kind,
                Is.EqualTo(RelicRowKind.SocketTrigger));
        }

        /// <summary>
        /// How many are held is only said past one.
        /// </summary>
        /// <remarks>
        /// "Held 1 time" is noise on every card a delver opens mid-run. The line exists to warn
        /// that a second copy may be doing nothing — nineteen relics do not stack — so it turns
        /// up exactly when there IS a second copy.
        /// </remarks>
        [Test]
        public void OneCopyIsNotWorthMentioning()
        {
            Assert.That(RelicCards.Of(RelicId.Whetstone).ShowsOwned, Is.False);

            Assert.That(RelicCards.Of(RelicId.Whetstone, new RelicHolding { Owned = 1 }).ShowsOwned,
                Is.False);

            RelicCard two = RelicCards.Of(RelicId.Whetstone, new RelicHolding { Owned = 2 });

            Assert.That(two.ShowsOwned, Is.True);
            Assert.That(two.Owned, Is.EqualTo(2));
        }

        /// <summary>
        /// A wheel needs both halves.
        /// </summary>
        /// <remarks>
        /// The same rule the book applies, checked here too because the two screens work it out
        /// separately and a delver moves between them in one press. A relic that reacts and
        /// emits nothing is not a wheel; drawing the arrow for it would claim a conversion that
        /// never happens.
        /// </remarks>
        [Test]
        public void OnlyARelicThatConvertsHasAWheel()
        {
            var wheels = 0;

            foreach (RelicDef relic in RelicCatalog.All)
            {
                RelicCard card = RelicCards.Of(relic.Id);

                bool both = relic.Reacts.Length > 0 && relic.Emits.Length > 0;

                if (!both)
                {
                    Assert.That(card.Wheel, Is.Null, relic.Key + " has half a wheel");
                    continue;
                }

                wheels++;

                Assert.That(card.Wheel, Is.Not.Null, relic.Key + " converts and shows no wheel");
                Assert.That(card.Wheel.Length, Is.EqualTo(2));
                Assert.That(card.Wheel[0], Is.EqualTo(relic.Reacts[0]));
                Assert.That(card.Wheel[1], Is.EqualTo(relic.Emits[0]));
            }

            Assert.That(wheels, Is.GreaterThan(0), "no relic converts anything");
        }

        /// <summary>
        /// The card and the book describe a relic the same way.
        /// </summary>
        /// <remarks>
        /// Two screens, one press apart, both carrying the translated/English split by hand. If
        /// they ever disagree the card is the one a decision is made on, and the disagreement
        /// would be invisible: both would show a name, and one of them would be the wrong one.
        /// </remarks>
        [Test]
        public void TheCardAndTheBookAgree()
        {
            RelicBookCard book = RelicBookCards.Of();

            foreach (BookEntry entry in book.Relics)
            {
                RelicCard card = RelicCards.Of(entry.Relic);

                Assert.That(card.Name, Is.EqualTo(entry.Name));
                Assert.That(card.NameKey, Is.EqualTo(entry.NameKey));
                Assert.That(card.What, Is.EqualTo(entry.What));
                Assert.That(card.WhatKey, Is.EqualTo(entry.WhatKey));
                Assert.That(card.Family, Is.EqualTo(entry.Kind));
            }
        }

        /// <summary>
        /// Exactly one of the two ways of describing a relic is set.
        /// </summary>
        /// <remarks>
        /// The split is the source's: nineteen relics are in the locale tables and thirty-one
        /// carry English of their own, and the source reads its English table FIRST, so a relic
        /// in both never shows its translation. A card with both set would have to choose, and
        /// whichever it chose would be right half the time.
        /// </remarks>
        [Test]
        public void ARelicIsDescribedOneWayOrTheOther()
        {
            var translated = 0;

            foreach (RelicDef relic in RelicCatalog.All)
            {
                RelicCard card = RelicCards.Of(relic.Id);

                Assert.That(card.Name, Is.Not.Null.And.Not.Empty);

                if (card.NameKey != null)
                {
                    translated++;
                    Assert.That(card.WhatKey, Is.Not.Null, relic.Key + " has a translated name and an English description");
                    Assert.That(card.What, Is.Null);
                }
                else
                {
                    Assert.That(card.What, Is.Not.Null.And.Not.Empty, relic.Key + " has neither key nor English");
                    Assert.That(card.WhatKey, Is.Null);
                }
            }

            Assert.That(translated, Is.EqualTo(19), "the translated share of the relic table moved");
        }

        /// <summary>
        /// Every label and colour the card can produce exists.
        /// </summary>
        /// <remarks>
        /// The labels are English literals and there is nothing to hold them against, so what is
        /// checked is that a kind added to the enum cannot reach the screen unlabelled — an
        /// unlabelled row is a sentence with no idea what it is claiming.
        /// </remarks>
        [Test]
        public void EveryRowKindIsLabelledAndColoured()
        {
            foreach (RelicRowKind kind in System.Enum.GetValues(typeof(RelicRowKind)))
            {
                Assert.That(RelicCards.Label(kind), Is.Not.Null.And.Not.Empty, kind + " has no label");

                string hex = RelicCards.Hex(kind);

                Assert.That(hex, Does.StartWith("#"), kind + " has no colour");
                Assert.That(hex.Length, Is.EqualTo(7));
            }
        }

        /// <summary>
        /// The two awakening lines do not read the same.
        /// </summary>
        /// <remarks>
        /// One says WOULD and the other says DID, and they are shown in the same place on the
        /// same card. Checked against their literal text rather than against each other, because
        /// a mutation that made one equal to the other passed every test that compared them to
        /// themselves — which is what the constants are for and exactly how they can lie.
        /// </remarks>
        [Test]
        public void APromiseDoesNotReadLikeAReport()
        {
            Assert.That(RelicCards.PromisePrefix, Is.EqualTo("✦ AWAKENED: "));
            Assert.That(RelicCards.AwokePrefix, Is.EqualTo("✦ AWAKENED — "));
            Assert.That(RelicCards.PromisePrefix, Is.Not.EqualTo(RelicCards.AwokePrefix));

            Assert.That(RelicCards.AwakenedMark, Is.EqualTo("✦ "));
            Assert.That(RelicCards.SocketedMark, Is.EqualTo("◆ "));
            Assert.That(RelicCards.AwakenedMark, Is.Not.EqualTo(RelicCards.SocketedMark));
        }

        /// <summary>
        /// No two row labels read the same.
        /// </summary>
        /// <remarks>
        /// ON ACTIVATION and IF RELAYED are the pair that matters — the rows carry identical
        /// text and mean different things — but the gate is over all six, because two labels
        /// that read alike make a card that cannot be read at all, whichever two they are.
        /// </remarks>
        [Test]
        public void NoTwoRowsAreLabelledTheSame()
        {
            var seen = new List<string>();

            foreach (RelicRowKind kind in System.Enum.GetValues(typeof(RelicRowKind)))
            {
                string label = RelicCards.Label(kind);

                Assert.That(seen.Contains(label), Is.False, label + " labels two different rows");
                seen.Add(label);
            }

            Assert.That(RelicCards.Label(RelicRowKind.Relayed), Is.EqualTo("▸ IF RELAYED"));
            Assert.That(RelicCards.Label(RelicRowKind.Activation), Is.EqualTo("ON ACTIVATION"));
        }

        /// <summary>
        /// One socket fits one row, whichever half it is.
        /// </summary>
        /// <remarks>
        /// A copy carries one component. Two socket rows would read as two components fitted,
        /// which is not a thing that can happen and would have a delver looking for the second
        /// one in their inventory.
        /// </remarks>
        [Test]
        public void OneSocketIsOneRow()
        {
            RelicCard bare = RelicCards.Of(RelicId.CoinMagnet);
            int native = bare.Rows.Count;

            Assert.That(native, Is.GreaterThan(0), "the relic this checks with says nothing");

            RelicCard fitted = RelicCards.Of(RelicId.CoinMagnet,
                new RelicHolding { Trigger = SocketTrigger.Gold });

            Assert.That(fitted.Rows.Count, Is.EqualTo(native + 1), "a socket added other than one row");
            Assert.That(Sockets(fitted), Is.EqualTo(1));

            for (var i = 0; i < native; i++)
            {
                Assert.That(fitted.Rows[i].Kind, Is.EqualTo(bare.Rows[i].Kind),
                    "a socket rearranged what the relic does by itself");
            }
        }

        /// <summary>
        /// A copy with both halves somehow set is read as the trigger.
        /// </summary>
        /// <remarks>
        /// Malformed rather than meaningful — a copy carries one component — so what is pinned
        /// is that the answer is DEFINITE. A tie-break that shifted with the wind would show two
        /// different cards for the same broken state and make the bug behind it unfindable.
        /// </remarks>
        [Test]
        public void AMalformedHoldingStillAnswersTheSameWay()
        {
            RelicCard card = RelicCards.Of(RelicId.Whetstone, new RelicHolding
            {
                Trigger = SocketTrigger.Kill,
                Emitter = SocketEmitter.Heal,
            });

            Assert.That(Sockets(card), Is.EqualTo(1));
            Assert.That(Has(card, RelicRowKind.SocketTrigger), Is.True);
            Assert.That(card.SocketNote, Does.Contain(SocketText.Of(SocketTrigger.Kill).Name));
        }

        /// <summary>The flavour line is quoted, and a missing one is nothing rather than "".</summary>
        [Test]
        public void TheFlavourLineIsQuoted()
        {
            RelicCard card = RelicCards.Of(RelicId.Whetstone);

            Assert.That(card.Flavour, Does.StartWith("“"));
            Assert.That(card.Flavour, Does.EndWith("”"));
            Assert.That(card.Flavour, Does.Contain(RelicLore.Get(RelicId.Whetstone).Flavour));
        }

        /// <summary>How many of the card's rows describe something fitted to it.</summary>
        private static int Sockets(RelicCard card)
        {
            var many = 0;

            foreach (RelicRow row in card.Rows)
            {
                if (row.Kind == RelicRowKind.SocketTrigger) many++;
                if (row.Kind == RelicRowKind.SocketEmitter) many++;
            }

            return many;
        }

        /// <summary>
        /// Nothing the card writes is a character the build cannot draw.
        /// </summary>
        /// <remarks>
        /// A gap here is silent and looks deliberate: TMP substitutes an empty box, and a row of
        /// boxes reads as a font that has not loaded rather than as a character nobody bundled.
        /// It was found the expensive way — the mode footer was ported with the source's pickaxe
        /// and crossed swords, which the browser drew from an emoji font and this build cannot.
        ///
        /// Three marks are outside the pixel face and stay: they draw through the same system
        /// fallback the CJK and Arabic faces are there for, and they are the source's own
        /// characters rather than something chosen here. They are LISTED, so a fourth one cannot
        /// be added without somebody deciding it in this file.
        /// </remarks>
        [Test]
        public void EveryCharacterTheCardWritesCanBeDrawn()
        {
            var borrowed = new List<int> { 0x2726, 0x25C6, 0x25B8 };

            var covers = new List<int>();

            foreach (JToken face in (JArray)Corpus.Object("fonts.json")["faces"])
            {
                if (face["id"].Value<string>() != "silkscreen") continue;

                foreach (JToken point in (JArray)face["covers"]) covers.Add(point.Value<int>());
            }

            Assert.That(covers, Is.Not.Empty, "no pixel face in the corpus");

            var written = new List<string>
            {
                RelicCards.AwakenedMark,
                RelicCards.SocketedMark,
                RelicCards.AwokePrefix,
                RelicCards.PromisePrefix,
                RelicCards.DelveWord,
                RelicCards.VersusWord,
                SetCards.Between,
                SetCards.Suffix,
                SetCards.IdolNote,
            };

            foreach (RelicRowKind kind in System.Enum.GetValues(typeof(RelicRowKind)))
            {
                written.Add(RelicCards.Label(kind));
            }

            foreach (string line in written)
            {
                foreach (char letter in line)
                {
                    if (covers.Contains(letter)) continue;
                    if (borrowed.Contains(letter)) continue;

                    Assert.Fail("the pixel face cannot draw U+" + ((int)letter).ToString("X4") +
                                " in \"" + line + "\", and it is not one of the three marks " +
                                "known to fall through to the system");
                }
            }
        }

        private static bool Has(RelicCard card, RelicRowKind kind)
        {
            return Row(card, kind) != null;
        }

        private static string Row(RelicCard card, RelicRowKind kind)
        {
            foreach (RelicRow row in card.Rows)
            {
                if (row.Kind == kind) return row.Text;
            }

            return null;
        }
    }
}
