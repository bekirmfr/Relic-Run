using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// Re-encoding a composed hero as indices, so the colours can change without recomposing.
    /// </summary>
    /// <remarks>
    /// Not a port. The source game drew straight to CSS box-shadows and never built anything
    /// like this, so there is no recording to diff against — but there IS a recording of what
    /// every composition contains, and this has to survive a round trip through it unchanged.
    /// That is the gate: index all ninety-three recorded heroes, read every pixel back, and
    /// demand the same pixel that went in.
    ///
    /// A lossless re-encoding sounds like it cannot go wrong, which is exactly why it is worth
    /// asking. The two ways it can are both quiet: two different pixels sharing an index, which
    /// paints a shadow in the colour of the thing beside it; and the table running past a byte,
    /// which would show up as an outfit rendering in noise.
    /// </remarks>
    [TestFixture]
    public class HeroIndexTests
    {
        private static HeroPack _pack;
        private static JObject _corpus;

        [OneTimeSetUp]
        public void LoadThePack()
        {
            _corpus = Corpus.Object("hero.json");
            _pack = Corpus.HeroPack();
        }

        private static IEnumerable<ComposedHero> RecordedHeroes(JArray cases)
        {
            foreach (JToken token in cases)
            {
                var worn = new Dictionary<string, string>();
                foreach (JProperty slot in ((JObject)token["combo"]).Properties())
                {
                    worn[slot.Name] = slot.Value.Value<string>();
                }

                yield return HeroCompositor.Compose(_pack, worn, token["state"].Value<string>());
            }
        }

        /// <summary>Every pixel of every recorded hero comes back out as it went in.</summary>
        [Test]
        public void EveryRecordedHeroSurvivesTheRoundTrip()
        {
            var cases = (JArray)_corpus["composed"];
            Assert.That(cases.Count, Is.GreaterThan(0), "hero corpus is empty");

            var failures = new List<string>();
            int pixels = 0;
            int i = 0;

            foreach (ComposedHero hero in RecordedHeroes(cases))
            {
                string id = cases[i++]["id"].Value<string>();
                HeroIndex indexed = HeroIndex.Of(hero);

                if (indexed.Frames.Count != hero.Frames.Count)
                {
                    failures.Add(id + ": " + hero.Frames.Count + " frames became " +
                                 indexed.Frames.Count);
                    continue;
                }

                // Carried across rather than recomputed. How long a frame is held and whether
                // the state loops are the compositor's answers, and a renderer reading this
                // grid has nowhere else to ask.
                Assert.That(indexed.Size, Is.EqualTo(hero.Size), id);
                Assert.That(indexed.Ms, Is.EqualTo(hero.Ms), id + " changed how long a frame lasts");
                Assert.That(indexed.Mode, Is.EqualTo(hero.Mode), id + " changed how it plays");

                for (int frame = 0; frame < hero.Frames.Count; frame++)
                {
                    for (int y = 0; y < hero.Size; y++)
                    {
                        for (int x = 0; x < hero.Size; x++)
                        {
                            ComposedPixel want = hero.At(frame, x, y);
                            ComposedPixel got = indexed.At(frame, x, y);
                            pixels++;

                            if (want.Role == got.Role && want.Tones == got.Tones) continue;

                            failures.Add(id + " frame " + frame + " at " + x + "," + y +
                                         ": composed " + want + ", indexed " + got);
                            frame = hero.Frames.Count;
                            y = hero.Size;
                            break;
                        }
                    }
                }
            }

            Assert.That(failures, Is.Empty,
                failures.Count + " of " + cases.Count + " heroes lose pixels:\n  " +
                string.Join("\n  ", failures.GetRange(0, Math.Min(6, failures.Count))));

            Assert.That(pixels, Is.GreaterThan(100000),
                "only " + pixels + " pixels were compared, which is too few to mean anything");
        }

        /// <summary>
        /// Nothing is index zero, in every hero, whether or not it has a transparent pixel.
        /// </summary>
        /// <remarks>
        /// A hero whose backdrop covers the frame has no empty pixel anywhere in it. Numbering
        /// from what a hero happens to contain would give that one a different meaning for zero
        /// than every other, and a renderer cannot ask.
        /// </remarks>
        [Test]
        public void NothingIsAlwaysIndexZero()
        {
            var cases = (JArray)_corpus["composed"];
            int covered = 0;

            foreach (ComposedHero hero in RecordedHeroes(cases))
            {
                HeroIndex indexed = HeroIndex.Of(hero);

                Assert.That(indexed.Entries[HeroIndex.Nothing].IsEmpty, Is.True);

                bool empty = false;
                foreach (byte[] frame in indexed.Frames)
                {
                    foreach (byte at in frame)
                    {
                        if (at == HeroIndex.Nothing) empty = true;
                    }
                }

                if (!empty) covered++;
            }

            Assert.That(covered, Is.GreaterThan(0),
                "no recorded hero fills its frame, so this proves nothing — the reserved entry " +
                "needs a hero with a backdrop to be worth reserving");
        }

        /// <summary>No two entries mean the same thing.</summary>
        /// <remarks>
        /// The failure this rules out is a shadow and the thing beside it sharing an index, and
        /// so sharing a colour. It would look like a shading mistake rather than a bug.
        /// </remarks>
        [Test]
        public void NoColourIsListedTwice()
        {
            foreach (ComposedHero hero in RecordedHeroes((JArray)_corpus["composed"]))
            {
                HeroIndex indexed = HeroIndex.Of(hero);

                var seen = new HashSet<string>();
                foreach (ComposedPixel entry in indexed.Entries)
                {
                    Assert.That(seen.Add(entry.Role + entry.Tones), Is.True,
                        entry + " is in the table twice");
                }
            }
        }

        /// <summary>
        /// The busiest delver the wardrobe can dress still fits in a byte, with room to spare.
        /// </summary>
        /// <remarks>
        /// The number is reported rather than merely bounded. Fifty-five of two hundred and
        /// fifty-six is comfortable; a change that took it to two hundred would still pass this
        /// test and would be worth knowing about before it took it to two hundred and sixty.
        /// </remarks>
        [Test]
        public void TheBusiestHeroFitsInAByte()
        {
            int most = 0;
            string worst = "";
            var cases = (JArray)_corpus["composed"];
            int i = 0;

            foreach (ComposedHero hero in RecordedHeroes(cases))
            {
                string id = cases[i++]["id"].Value<string>();
                int count = HeroIndex.Of(hero).Entries.Count;

                if (count <= most) continue;
                most = count;
                worst = id;
            }

            Assert.That(most, Is.LessThan(HeroIndex.Limit),
                worst + " needs " + most + " colours");
            Assert.That(most, Is.GreaterThan(20), "only " + most + " colours is suspiciously few");

            Assert.That(most, Is.LessThan(HeroIndex.Limit / 2),
                "the busiest hero (" + worst + ") now needs " + most + " of " + HeroIndex.Limit +
                " colours, which is closer to the ceiling than this was designed for");
        }

        /// <summary>The same hero indexes the same way every time.</summary>
        /// <remarks>
        /// Numbering by first sighting is only useful if "first" is fixed. A table built from a
        /// dictionary's enumeration order would pass every other test here and still hand out
        /// different numbers on a different run, which would break a grid the moment it outlived
        /// the table it was made with.
        /// </remarks>
        [Test]
        public void IndexingIsStable()
        {
            foreach (ComposedHero hero in RecordedHeroes((JArray)_corpus["composed"]))
            {
                HeroIndex once = HeroIndex.Of(hero);
                HeroIndex again = HeroIndex.Of(hero);

                Assert.That(again.Entries.Count, Is.EqualTo(once.Entries.Count));

                for (int i = 0; i < once.Entries.Count; i++)
                {
                    Assert.That(again.Entries[i].Role, Is.EqualTo(once.Entries[i].Role));
                    Assert.That(again.Entries[i].Tones, Is.EqualTo(once.Entries[i].Tones));
                }

                for (int f = 0; f < once.Frames.Count; f++)
                {
                    Assert.That(again.Frames[f], Is.EqualTo(once.Frames[f]));
                }
            }
        }

        /// <summary>The table resolves to the colours the compositor would have painted.</summary>
        [Test]
        public void TheColoursAreTheOnesTheCompositorWouldPaint()
        {
            Dictionary<char, Rgb> palette = HeroPalette.Build();

            foreach (ComposedHero hero in RecordedHeroes((JArray)_corpus["composed"]))
            {
                HeroIndex indexed = HeroIndex.Of(hero);
                Rgb[] colours = indexed.Colours(palette);

                Assert.That(colours.Length, Is.EqualTo(indexed.Entries.Count));

                for (int i = 0; i < colours.Length; i++)
                {
                    Assert.That(colours[i],
                        Is.EqualTo(HeroCompositor.Colour(indexed.Entries[i], palette)),
                        indexed.Entries[i] + " resolves differently through the table");
                }
            }
        }

        /// <summary>
        /// A pixel toned twice is not the same colour as one toned once.
        /// </summary>
        /// <remarks>
        /// No shipped delver reaches this. The deepest tone stack in the entire wardrobe is ONE,
        /// across all ninety-three recorded compositions — which means a table keyed on a pixel's
        /// first tone alone passes every other test in this file, and mutation found exactly
        /// that. The compositor stacks tones without limit though, and each is laid over the last
        /// as transparent paint, so a doubly shadowed pixel really is darker than a singly
        /// shadowed one. The day a part is drawn with a fold in it, sharing an index would paint
        /// one in the other's colour.
        ///
        /// The order matters too, but only across FAMILIES. Two shadows in either order mix to
        /// the same colour, because both are the same paint; a shadow under a highlight is not
        /// the same as a highlight under a shadow, because the second one covers more of the
        /// first than the first covers of it.
        /// </remarks>
        [Test]
        public void APixelTonedTwiceIsItsOwnColour()
        {
            var hero = new ComposedHero { Size = 2, Ms = 10, Mode = "once" };
            hero.Frames.Add(new[]
            {
                new ComposedPixel('W'),
                new ComposedPixel('W', "D"),
                new ComposedPixel('W', "DD"),
                new ComposedPixel('W', "DL"),
            });

            HeroIndex indexed = HeroIndex.Of(hero);
            Assert.That(indexed.Entries.Count, Is.EqualTo(5), "four pixels and the reserved empty");

            Dictionary<char, Rgb> palette = HeroPalette.Build();
            Rgb[] colours = indexed.Colours(palette);

            Assert.That(colours[2], Is.Not.EqualTo(colours[1]),
                "a second shadow over the first changed nothing");
            Assert.That(colours[3], Is.Not.EqualTo(colours[2]),
                "a highlight over a shadow is the same as a second shadow");
        }

        /// <summary>Tones of different families do not commute.</summary>
        [Test]
        public void AShadowUnderAHighlightIsNotAHighlightUnderAShadow()
        {
            var hero = new ComposedHero { Size = 2, Ms = 10, Mode = "once" };
            hero.Frames.Add(new[]
            {
                new ComposedPixel('W', "DL"),
                new ComposedPixel('W', "LD"),
                new ComposedPixel(HeroCompositor.Empty),
                new ComposedPixel(HeroCompositor.Empty),
            });

            HeroIndex indexed = HeroIndex.Of(hero);
            Assert.That(indexed.Entries.Count, Is.EqualTo(3));

            Rgb[] colours = indexed.Colours(HeroPalette.Build());
            Assert.That(colours[2], Is.Not.EqualTo(colours[1]),
                "the order the paint went on is part of what a pixel is");
        }

        /// <summary>
        /// A hero with more colours than a byte can count is refused rather than wrapped.
        /// </summary>
        /// <remarks>
        /// Nothing the shipped wardrobe can assemble comes close, so this is built by hand. The
        /// alternative to refusing is a silent <c>(byte)</c> wrap, which paints the two hundred
        /// and fifty-seventh colour as the first one — transparent — and puts a hole in a delver.
        /// </remarks>
        [Test]
        public void AHeroWithTooManyColoursIsRefused()
        {
            var crowded = new ComposedHero { Size = 16, Ms = 100, Mode = "once" };
            var frame = new ComposedPixel[256];

            for (int i = 0; i < frame.Length; i++)
            {
                // 255 distinct roles plus the reserved empty entry is exactly one too many.
                frame[i] = new ComposedPixel((char)('Ā' + i));
            }

            crowded.Frames.Add(frame);

            Assert.That(() => HeroIndex.Of(crowded), Throws.InvalidOperationException);
        }

        /// <summary>Exactly 255 colours and the reserved entry is the last hero that fits.</summary>
        [Test]
        public void TwoHundredAndFiftyFiveColoursIsStillAllowed()
        {
            var full = new ComposedHero { Size = 16, Ms = 100, Mode = "once" };
            var frame = new ComposedPixel[256];

            for (int i = 0; i < 255; i++) frame[i] = new ComposedPixel((char)('Ā' + i));
            frame[255] = new ComposedPixel(HeroCompositor.Empty);

            full.Frames.Add(frame);

            HeroIndex indexed = HeroIndex.Of(full);
            Assert.That(indexed.Entries.Count, Is.EqualTo(HeroIndex.Limit));
            Assert.That(indexed.Frames[0][255], Is.EqualTo(HeroIndex.Nothing));
            Assert.That(indexed.Frames[0][254], Is.EqualTo(255));
        }
    }
}
