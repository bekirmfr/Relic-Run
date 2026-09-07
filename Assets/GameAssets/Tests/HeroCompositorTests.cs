using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// Phase 8 gate: <c>Tools/corpus/hero.json</c> — a delver, composed pixel by pixel.
    /// </summary>
    /// <remarks>
    /// A hero is a stack of twelve layers, each a grid whose characters are role keys. Composing
    /// one means stamping the stack in paint order, resolving the shadow and highlight modifiers
    /// against whatever is already beneath them, outlining the finished silhouette, and laying
    /// the lot over the backdrop.
    ///
    /// The modifiers are why this cannot be twelve sprites drawn on top of one another, and why
    /// it is worth a gate of its own: a shadow pixel means "whatever is under this, one tone
    /// darker", so the answer depends on the order everything was stamped in.
    ///
    /// A TONED PIXEL IS COMPARED ON ITS MEANING. The source names each one with a synthetic
    /// character handed out as it goes, which is an artefact of the order it happened to compose
    /// in — a port that composed differently would pick different characters and still be right.
    /// The recording carries a table saying what each character means, and this resolves it.
    /// </remarks>
    [TestFixture]
    public class HeroCompositorTests
    {
        private static HeroPack _pack;
        private static JObject _corpus;

        [OneTimeSetUp]
        public void LoadThePack()
        {
            _corpus = Corpus.Object("hero.json");
            _pack = Corpus.HeroPack();
        }

        [Test]
        public void ThePackLoadsAsRecorded()
        {
            Assert.That(_pack.Size, Is.EqualTo(_corpus["size"].Value<int>()));

            var stack = new List<string>();
            foreach (JToken t in (JArray)_corpus["stack"]) stack.Add(t.Value<string>());
            Assert.That(_pack.Stack, Is.EqualTo(stack), "the paint order is the pack's own");

            foreach (JProperty state in ((JObject)_corpus["states"]).Properties())
            {
                HeroStateDef def = _pack.State(state.Name);
                Assert.That(def.Frames, Is.EqualTo(state.Value["frames"].Value<int>()), state.Name);
                Assert.That(def.Ms, Is.EqualTo(state.Value["ms"].Value<int>()), state.Name);
                Assert.That(def.Mode, Is.EqualTo(state.Value["mode"].Value<string>()), state.Name);
            }
        }

        [Test]
        public void RecordedHeroesComposeExactly()
        {
            var cases = (JArray)_corpus["composed"];
            Assert.That(cases.Count, Is.GreaterThan(0), "hero corpus is empty");

            var failures = new List<string>();
            int pixels = 0;

            foreach (JToken token in cases)
            {
                string id = token["id"].Value<string>();

                var worn = new Dictionary<string, string>();
                foreach (JProperty slot in ((JObject)token["combo"]).Properties())
                {
                    worn[slot.Name] = slot.Value.Value<string>();
                }

                ComposedHero got = HeroCompositor.Compose(_pack, worn, token["state"].Value<string>());
                var frames = (JArray)token["frames"];

                string wrong =
                    Same(id, "frame count", frames.Count, got.Frames.Count) ??
                    Same(id, "frame length", token["ms"].Value<int>(), got.Ms) ??
                    Same(id, "playback mode", token["mode"].Value<string>(), got.Mode);

                if (wrong != null) { failures.Add(wrong); continue; }

                for (int f = 0; f < frames.Count && wrong == null; f++)
                {
                    wrong = DiffFrame(id, f, (JObject)frames[f], got, ref pixels);
                }

                if (wrong != null) failures.Add(wrong);
            }

            Assert.That(failures, Is.Empty,
                failures.Count + " of " + cases.Count + " compositions diverge:\n  " +
                string.Join("\n  ", failures.GetRange(0, Math.Min(6, failures.Count))));

            Assert.That(pixels, Is.GreaterThan(100000),
                "only " + pixels + " pixels were compared, which is too few to mean anything");
        }

        private static string DiffFrame(string id, int frame, JObject recorded, ComposedHero got,
            ref int pixels)
        {
            var rows = (JArray)recorded["rows"];
            var tones = (JObject)recorded["tones"];

            for (int y = 0; y < rows.Count; y++)
            {
                string row = rows[y].Value<string>();
                for (int x = 0; x < row.Length; x++)
                {
                    ComposedPixel want = Resolve(row[x], tones);
                    ComposedPixel mine = got.At(frame, x, y);
                    pixels++;

                    if (want.Role != mine.Role || want.Tones != mine.Tones)
                    {
                        return id + " frame " + frame + " at " + x + "," + y + ": recorded " +
                               want + ", composed " + mine;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Turns a recorded character into what it means: a role, and the tones over it.
        /// </summary>
        /// <remarks>
        /// A tone can name another tone as its source — a shadow laid on a shadow — so this
        /// unwinds until it reaches a real role key, collecting the modifiers as it goes. They
        /// come off outermost first, so the list is reversed to the order they were applied in.
        /// </remarks>
        private static ComposedPixel Resolve(char ch, JObject tones)
        {
            var applied = new List<char>();
            int guard = 0;

            while (guard++ < 16)
            {
                JToken tone = tones[ch.ToString()];
                if (tone == null) break;

                applied.Add(tone["mode"].Value<string>()[0]);
                ch = tone["src"].Value<string>()[0];
            }

            applied.Reverse();
            return new ComposedPixel(ch, new string(applied.ToArray()));
        }

        private static string Same(string id, string what, int recorded, int got)
        {
            return recorded == got ? null : id + ": " + what + " recorded " + recorded + ", got " + got;
        }

        private static string Same(string id, string what, string recorded, string got)
        {
            return recorded == got ? null : id + ": " + what + " recorded " + recorded + ", got " + got;
        }

        /// <summary>
        /// A composed delver can be coloured, and a toned pixel really is darker than a plain one.
        /// </summary>
        /// <remarks>
        /// The corpus pins what each pixel MEANS; this pins that the meaning turns into paint.
        /// A shadow over a colour has to come out darker than the colour, or the modifiers are
        /// being resolved the wrong way round — which is the one mistake the meanings alone
        /// would not catch.
        /// </remarks>
        [Test]
        public void AToneDarkensAndAHighlightLightens()
        {
            Dictionary<char, Rgb> palette = HeroPalette.Build();
            Rgb plain = HeroCompositor.Colour(new ComposedPixel('T'), palette);

            Rgb shadowed = HeroCompositor.Colour(new ComposedPixel('T', "D"), palette);
            Rgb deeper = HeroCompositor.Colour(new ComposedPixel('T', "Du"), palette);
            Rgb lit = HeroCompositor.Colour(new ComposedPixel('T', "L"), palette);

            Assert.That(Brightness(shadowed), Is.LessThan(Brightness(plain)), "a shadow darkens");
            Assert.That(Brightness(deeper), Is.LessThan(Brightness(shadowed)),
                "and a second shadow darkens it again");
            Assert.That(Brightness(lit), Is.GreaterThan(Brightness(plain)), "a highlight lightens");
        }

        private static int Brightness(Rgb c) { return c.R + c.G + c.B; }

        /// <summary>
        /// A modifier landing on nothing paints itself; landing on a bare modifier, it replaces it.
        /// </summary>
        /// <remarks>
        /// Both are the source's, and both are easy to get wrong in a way the corpus might not
        /// happen to reach. The first is what lets a shadow be drawn deliberately on its own;
        /// the second is what stops two shadows meeting in empty space and compounding.
        /// </remarks>
        [Test]
        public void AModifierOnNothingPaintsItself()
        {
            var pack = new HeroPack { Size = 3 };
            pack.Stack.Add("a");
            pack.Stack.Add("b");
            pack.States["static"] = new HeroStateDef { Frames = 1, Ms = 100, Mode = "once" };

            pack.Add(Layer("a", "one", new[] { "T..", "...", "..." }));
            pack.Add(Layer("b", "two", new[] { "D.D", "...", "..." }));

            ComposedHero hero = HeroCompositor.Compose(pack,
                new Dictionary<string, string> { { "a", "one" }, { "b", "two" } }, "static");

            Assert.That(hero.At(0, 0, 0).ToString(), Is.EqualTo("T+D"),
                "a shadow over a real pixel tones it");
            Assert.That(hero.At(0, 2, 0).ToString(), Is.EqualTo("D"),
                "and over nothing it is simply painted");
        }

        [Test]
        public void AModifierOverABareModifierReplacesIt()
        {
            var pack = new HeroPack { Size = 3 };
            pack.Stack.Add("a");
            pack.Stack.Add("b");
            pack.States["static"] = new HeroStateDef { Frames = 1, Ms = 100, Mode = "once" };

            pack.Add(Layer("a", "one", new[] { "D..", "...", "..." }));
            pack.Add(Layer("b", "two", new[] { "u..", "...", "..." }));

            ComposedHero hero = HeroCompositor.Compose(pack,
                new Dictionary<string, string> { { "a", "one" }, { "b", "two" } }, "static");

            Assert.That(hero.At(0, 0, 0).ToString(), Is.EqualTo("u"),
                "two shadows meeting in empty space do not compound");
        }

        /// <summary>The outline traces solid pixels, and neither shadows nor the backdrop.</summary>
        [Test]
        public void TheOutlineTracesOnlySolidPixels()
        {
            var pack = new HeroPack { Size = 5 };
            pack.Stack.Add("bg");
            pack.Stack.Add("a");
            pack.States["static"] = new HeroStateDef { Frames = 1, Ms = 100, Mode = "once" };

            pack.Add(Layer("bg", "flat", new[] { "ZZZZZ", "ZZZZZ", "ZZZZZ", "ZZZZZ", "ZZZZZ" }));
            pack.Add(Layer("a", "dot", new[] { ".....", ".....", "..T..", ".....", "....D" }));

            ComposedHero hero = HeroCompositor.Compose(pack,
                new Dictionary<string, string> { { "bg", "flat" }, { "a", "dot" } }, "static");

            Assert.That(hero.At(0, 2, 1).Role, Is.EqualTo(HeroPalette.OutlineKey), "above it");
            Assert.That(hero.At(0, 1, 2).Role, Is.EqualTo(HeroPalette.OutlineKey), "beside it");
            Assert.That(hero.At(0, 3, 3).Role, Is.EqualTo('Z'),
                "a diagonal neighbour is not traced, and the backdrop shows through");
            Assert.That(hero.At(0, 3, 4).Role, Is.EqualTo('Z'),
                "a lone shadow casts no outline of its own");
        }

        /// <summary>
        /// A shadow over an already shadowed pixel deepens it, rather than replacing the tone.
        /// </summary>
        /// <remarks>
        /// The shipped pack never does this — nothing in eleven thousand recorded rows lays one
        /// modifier over another on a real pixel — so the corpus cannot say whether tones stack
        /// or only the last one survives. They stack, and in the order they were laid on, which
        /// is what makes a fold under a cape darker than the cape's own shading.
        /// </remarks>
        [Test]
        public void TonesStackInTheOrderTheyWereLaidOn()
        {
            var pack = new HeroPack { Size = 2 };
            pack.Stack.Add("a");
            pack.Stack.Add("b");
            pack.Stack.Add("c");
            pack.States["static"] = new HeroStateDef { Frames = 1, Ms = 100, Mode = "once" };

            pack.Add(Layer("a", "one", new[] { "T.", ".." }));
            pack.Add(Layer("b", "two", new[] { "D.", ".." }));
            pack.Add(Layer("c", "three", new[] { "l.", ".." }));

            ComposedHero hero = HeroCompositor.Compose(pack, new Dictionary<string, string>
            {
                { "a", "one" }, { "b", "two" }, { "c", "three" },
            }, "static");

            Assert.That(hero.At(0, 0, 0).ToString(), Is.EqualTo("T+Dl"),
                "both tones survive, shadow first because it was laid on first");

            // And they resolve that way too: the shadow darkens, then the highlight lifts it
            // part of the way back — which is not the same colour as either one alone.
            Dictionary<char, Rgb> palette = HeroPalette.Build();
            Rgb both = HeroCompositor.Colour(hero.At(0, 0, 0), palette);
            Rgb shadowOnly = HeroCompositor.Colour(new ComposedPixel('T', "D"), palette);
            Rgb reversed = HeroCompositor.Colour(new ComposedPixel('T', "lD"), palette);

            Assert.That(both.ToString(), Is.Not.EqualTo(shadowOnly.ToString()));
            Assert.That(both.ToString(), Is.Not.EqualTo(reversed.ToString()),
                "the order the tones went on changes the colour that comes out");
        }

        /// <summary>
        /// A blank in a layer is a hole, not paint.
        /// </summary>
        /// <remarks>
        /// The grids use a full stop for nothing, but the format allows a space too and the
        /// shipped pack happens never to use one. If a space were treated as paint, a garment
        /// authored with them would punch its own silhouette out of the body underneath.
        /// </remarks>
        [Test]
        public void ASpaceIsNothingJustAsAFullStopIs()
        {
            var pack = new HeroPack { Size = 2 };
            pack.Stack.Add("a");
            pack.Stack.Add("b");
            pack.States["static"] = new HeroStateDef { Frames = 1, Ms = 100, Mode = "once" };

            pack.Add(Layer("a", "one", new[] { "TT", ".." }));
            pack.Add(Layer("b", "two", new[] { " K", ".." }));

            ComposedHero hero = HeroCompositor.Compose(pack,
                new Dictionary<string, string> { { "a", "one" }, { "b", "two" } }, "static");

            Assert.That(hero.At(0, 0, 0).Role, Is.EqualTo('T'), "the space left the outfit alone");
            Assert.That(hero.At(0, 1, 0).Role, Is.EqualTo('K'), "and the skin beside it went down");
        }

        /// <summary>
        /// A grid smaller than the canvas is padded with nothing, not cropped into place.
        /// </summary>
        /// <remarks>
        /// Every row in the shipped pack is already the full width, so the corpus never exercises
        /// this. A hand-authored part need not be, and the format explicitly allows it — a short
        /// row means the artist stopped drawing, not that the canvas shrank.
        /// </remarks>
        [Test]
        public void AShortGridIsPaddedRatherThanCropped()
        {
            var pack = new HeroPack { Size = 4 };
            pack.Stack.Add("a");
            pack.States["static"] = new HeroStateDef { Frames = 1, Ms = 100, Mode = "once" };

            pack.Add(Layer("a", "one", new[] { "TT", "T" }));

            string[] rows = pack.Frame("a", "one", "static", 0);
            Assert.That(rows.Length, Is.EqualTo(4), "every row of the canvas is there");
            Assert.That(rows[0], Is.EqualTo("TT.."));
            Assert.That(rows[1], Is.EqualTo("T..."));
            Assert.That(rows[3], Is.EqualTo("...."), "the rows that were never drawn are empty");
        }

        /// <summary>
        /// The compositor dresses exactly what it was handed, and applies no defaults of its own.
        /// </summary>
        /// <remarks>
        /// The pack carries a default for some slots, and it is the Changing Room's business
        /// rather than the compositor's — a lobby rolling a rival's appearance chooses every
        /// slot itself, and a delver drawn deliberately bare must stay bare.
        /// </remarks>
        [Test]
        public void NothingIsWornThatWasNotAskedFor()
        {
            Assert.That(_pack.Defaults, Is.Not.Empty, "the pack has defaults, or this proves nothing");

            var bare = new Dictionary<string, string>();
            foreach (string slot in _pack.Stack)
            {
                if (slot != HeroPack.BaseSlot) bare[slot] = HeroPack.Nothing;
            }

            ComposedHero asked = HeroCompositor.Compose(_pack, bare, "idle");
            ComposedHero silent = HeroCompositor.Compose(_pack, new Dictionary<string, string>(), "idle");

            for (int f = 0; f < asked.Frames.Count; f++)
            {
                for (int i = 0; i < asked.Frames[f].Length; i++)
                {
                    Assert.That(silent.Frames[f][i].ToString(),
                        Is.EqualTo(asked.Frames[f][i].ToString()),
                        "saying nothing and saying \"nothing\" have to mean the same thing");
                }
            }
        }

        private static HeroPart Layer(string slot, string id, string[] rows)
        {
            var part = new HeroPart(slot, id);
            part.Frames["static"] = new List<string[]> { rows };
            return part;
        }
    }
}
