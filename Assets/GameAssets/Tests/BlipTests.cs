using System;
using NUnit.Framework;
using RelicRun.Core.Presentation;

namespace RelicRun.Tests
{
    /// <summary>
    /// The eight noises, as arithmetic.
    /// </summary>
    /// <remarks>
    /// The source ships no audio files: every sound is an oscillator and an envelope, and the
    /// eight blips are eight rows of arguments. So the port synthesises rather than imports, and
    /// synthesis is exactly the sort of thing that sounds "fine" while being wrong — a click at
    /// the end, a note that never quite stops, a slide that goes the wrong way. Those are audible
    /// for one frame and obvious in an array.
    /// </remarks>
    [TestFixture]
    public class BlipTests
    {
        private const int Rate = 44100;

        private static Blip Find(string name)
        {
            foreach (Blip blip in Blips.All)
            {
                if (blip.Name == name) return blip;
            }

            Assert.Fail("no blip called " + name);
            return default(Blip);
        }

        /// <summary>All eight are here, and every one of them is named.</summary>
        /// <remarks>
        /// Eight because the source has eight. Six answer combat and two belong to the interface,
        /// and a ninth appearing without a row here would be a sound nothing could play.
        /// </remarks>
        [Test]
        public void ThereAreEightNoisesAndEachHasAName()
        {
            // Through the interface, not the Count constraint: this list is an array, and the
            // two runners disagree about reflecting for a property an array does not have. The
            // note in docs/testing.md is two commits old and I wrote this anyway.
            Assert.That(Blips.All.Count, Is.EqualTo(8));

            foreach (Blip blip in Blips.All)
            {
                Assert.That(blip.Name, Is.Not.Null.And.Not.Empty);
                Assert.That(blip.Hz, Is.GreaterThan(0d), blip.Name);
                Assert.That(blip.Seconds, Is.GreaterThan(0d), blip.Name);
                Assert.That(blip.Gain, Is.GreaterThan(0d), blip.Name);
            }
        }

        /// <summary>A clip is as long as it says it is.</summary>
        [Test]
        public void AClipIsAsLongAsItsDuration()
        {
            Assert.That(Find(Blips.Tap).Render(Rate).Length, Is.EqualTo((int)(0.05d * Rate)));
            Assert.That(Find(Blips.Death).Render(Rate).Length, Is.EqualTo((int)(0.50d * Rate)));

            Assert.That(Find(Blips.Death).Render(Rate).Length,
                Is.GreaterThan(Find(Blips.Tap).Render(Rate).Length),
                "a death should outlast a button press by a long way");
        }

        /// <summary>
        /// Every note starts loud and ends silent.
        /// </summary>
        /// <remarks>
        /// The envelope, which is the whole character of a blip this short. A note that ended at
        /// full volume would click; one that started silent would never be heard at all.
        /// </remarks>
        [Test]
        public void ANoteFadesToNothing()
        {
            foreach (Blip blip in Blips.All)
            {
                float[] samples = blip.Render(Rate);

                Assert.That(Loudest(samples, 0, samples.Length / 8), Is.GreaterThan(0.001f),
                    blip.Name + " never gets going");

                Assert.That(Loudest(samples, samples.Length * 7 / 8, samples.Length),
                    Is.LessThan(0.01f), blip.Name + " is still sounding when it ends, so it clicks");
            }
        }

        /// <summary>Nothing clips, so nothing crackles.</summary>
        /// <remarks>
        /// The source's gains are all around a twentieth, so there is a lot of headroom — but a
        /// sample beyond one is a hard-edged crackle rather than a loud note, and it is the kind
        /// of thing a gain change would introduce silently.
        /// </remarks>
        [Test]
        public void NothingExceedsFullScale()
        {
            foreach (Blip blip in Blips.All)
            {
                foreach (float sample in blip.Render(Rate))
                {
                    Assert.That(Math.Abs(sample), Is.LessThanOrEqualTo(1f), blip.Name);
                }
            }
        }

        /// <summary>
        /// A ramp is exponential, not straight.
        /// </summary>
        /// <remarks>
        /// The difference is audible and is most of what a blip sounds like: an exponential ramp
        /// moves quickly at first and slowly at the end, which is how both pitch and loudness are
        /// actually heard. Halfway through, a straight line would be at the midpoint; this is
        /// below it.
        /// </remarks>
        [Test]
        public void ARampBendsRatherThanRunningStraight()
        {
            double half = Blip.Ramp(880d, 220d, 0.5d);

            Assert.That(half, Is.EqualTo(440d).Within(1e-9), "the geometric mean, not the average");
            Assert.That(half, Is.LessThan((880d + 220d) / 2d));

            Assert.That(Blip.Ramp(880d, 220d, 0d), Is.EqualTo(880d));
            Assert.That(Blip.Ramp(880d, 220d, 1d), Is.EqualTo(220d));
        }

        /// <summary>A slide never falls below hearing.</summary>
        /// <remarks>
        /// The source floors it at forty. Below that a ramp spends the note's whole length
        /// travelling somewhere nobody can hear, and toward zero it never arrives at all.
        /// </remarks>
        [Test]
        public void ASlideStopsAtTheFloor()
        {
            var low = new Blip("low", 200d, 0.1d, Wave.Sine, 0.05d, 1d);

            Assert.That(low.Render(Rate), Is.Not.Empty, "a slide below the floor should still play");
            Assert.That(Blip.Floor, Is.EqualTo(40d));
        }

        /// <summary>
        /// Every shape is actually a different shape.
        /// </summary>
        /// <remarks>
        /// Four waveforms that all rendered the same would be four sounds that were one sound,
        /// and nothing about the game would look wrong.
        /// </remarks>
        [Test]
        public void TheFourShapesDiffer()
        {
            var seen = new System.Collections.Generic.List<float[]>();

            foreach (Wave shape in new[] { Wave.Square, Wave.Sawtooth, Wave.Triangle, Wave.Sine })
            {
                float[] rendered = new Blip("x", 440d, 0.02d, shape, 0.5d).Render(Rate);

                foreach (float[] already in seen)
                {
                    Assert.That(Same(already, rendered), Is.False, shape + " renders as another shape");
                }

                seen.Add(rendered);
            }
        }

        /// <summary>Six events make a noise; the rest are silent.</summary>
        /// <remarks>
        /// Silence is the answer for most events, and it is an answer rather than a gap. A screen
        /// that asked for a clip on every event would be a screen looking up names that do not
        /// exist twenty times a fight.
        /// </remarks>
        [Test]
        public void OnlySixEventsMakeANoise()
        {
            Assert.That(Blips.Named(FightSound.None), Is.Null);

            foreach (FightSound sound in new[]
            {
                FightSound.Gold, FightSound.Hurt, FightSound.Hit,
                FightSound.Heal, FightSound.Kill, FightSound.Death,
            })
            {
                string named = Blips.Named(sound);

                Assert.That(named, Is.Not.Null, sound + " makes no noise");
                Assert.That(Find(named).Name, Is.EqualTo(named), sound + " names a blip that is absent");
            }
        }

        private static float Loudest(float[] samples, int from, int to)
        {
            var most = 0f;

            for (int i = Math.Max(0, from); i < Math.Min(samples.Length, to); i++)
            {
                float size = Math.Abs(samples[i]);
                if (size > most) most = size;
            }

            return most;
        }

        private static bool Same(float[] a, float[] b)
        {
            if (a.Length != b.Length) return false;

            for (var i = 0; i < a.Length; i++)
            {
                if (Math.Abs(a[i] - b[i]) > 1e-6f) return false;
            }

            return true;
        }
    }
}
