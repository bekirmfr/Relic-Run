using System;
using System.Collections.Generic;

namespace RelicRun.Core.Presentation
{
    /// <summary>The four shapes the source's oscillator can make.</summary>
    public enum Wave
    {
        Square,
        Sawtooth,
        Triangle,
        Sine,
    }

    /// <summary>
    /// One of the game's noises, as the numbers that make it.
    /// </summary>
    /// <remarks>
    /// The source has no audio FILES. Every sound is an oscillator and a gain envelope, thirty
    /// lines of WebAudio, and the eight blips are eight rows of arguments to it. So there is
    /// nothing to import and nothing to redraw — the honest port is to run the same synthesis and
    /// keep the same numbers, which is what this is.
    ///
    /// The envelope is the part worth reading twice. Both the pitch and the gain ramp
    /// EXPONENTIALLY, not linearly, and WebAudio cannot ramp exponentially to zero — which is why
    /// the source ends its gain at 0.0001 rather than at nothing. Ported as written: a linear
    /// fade sounds like a note being turned down, and an exponential one sounds like a note
    /// stopping, and every blip here is short enough that the difference is the whole character.
    /// </remarks>
    public readonly struct Blip
    {
        /// <summary>What the game calls it. This is the name the audio service plays by.</summary>
        public readonly string Name;

        public readonly double Hz;

        public readonly double Seconds;

        public readonly Wave Shape;

        public readonly double Gain;

        /// <summary>
        /// Where the pitch slides to by the end, or zero for a note that holds.
        /// </summary>
        /// <remarks>
        /// Floored at 40Hz by the source, and kept: an exponential ramp toward nothing never
        /// arrives, and a ramp toward a frequency below hearing spends the note's whole length
        /// getting somewhere nobody can hear.
        /// </remarks>
        public readonly double Slide;

        public Blip(string name, double hz, double seconds, Wave shape, double gain,
            double slide = 0d)
        {
            Name = name;
            Hz = hz;
            Seconds = seconds;
            Shape = shape;
            Gain = gain;
            Slide = slide;
        }

        /// <summary>The lowest a slide may reach, which is the source's floor.</summary>
        public const double Floor = 40d;

        /// <summary>Where an exponential gain ramp ends, since it cannot end at nothing.</summary>
        public const double Silence = 0.0001d;

        /// <summary>
        /// The samples, rendered.
        /// </summary>
        /// <remarks>
        /// Pure, so the maths can be gated without a speaker. An envelope is exactly the sort of
        /// thing that sounds "fine" while being wrong — a click at the end, a note that never
        /// quite stops — and those are audible for one frame and obvious in an array.
        /// </remarks>
        public float[] Render(int rate)
        {
            if (rate <= 0) return new float[0];

            var count = (int)Math.Round(Seconds * rate);
            if (count < 1) count = 1;

            var samples = new float[count];
            double phase = 0d;

            for (var i = 0; i < count; i++)
            {
                double at = i / (double)rate;
                double part = Seconds <= 0d ? 1d : at / Seconds;

                double hz = Ramp(Hz, Slide > 0d ? Math.Max(Floor, Slide) : Hz, part);
                double gain = Ramp(Gain, Silence, part);

                samples[i] = (float)(Shaped(phase) * gain);

                phase += hz / rate;
                if (phase >= 1d) phase -= Math.Floor(phase);
            }

            return samples;
        }

        /// <summary>
        /// An exponential ramp from one value to another, which is what WebAudio does.
        /// </summary>
        /// <remarks>
        /// Not a lerp. An exponential ramp moves quickly at first and slowly at the end, which is
        /// how both pitch and loudness are actually heard — a linear fade over a twentieth of a
        /// second sounds like a click, because most of its travel happens where the ear is least
        /// able to follow it.
        /// </remarks>
        public static double Ramp(double from, double to, double part)
        {
            if (part <= 0d) return from;
            if (part >= 1d) return to;
            if (from <= 0d || to <= 0d) return from + (to - from) * part;

            return from * Math.Pow(to / from, part);
        }

        /// <summary>One cycle of the shape, from a phase in [0, 1).</summary>
        private double Shaped(double phase)
        {
            switch (Shape)
            {
                case Wave.Square: return phase < 0.5d ? 1d : -1d;
                case Wave.Sawtooth: return 2d * phase - 1d;
                case Wave.Triangle: return 4d * Math.Abs(phase - 0.5d) - 1d;
                default: return Math.Sin(phase * 2d * Math.PI);
            }
        }

        public override string ToString()
        {
            return Name + " " + Hz + "Hz " + Seconds + "s " + Shape;
        }
    }

    /// <summary>
    /// The eight noises the game makes.
    /// </summary>
    /// <remarks>
    /// Every number is the source's, read off its <c>SFX</c> object. Six of them answer combat
    /// events and are chosen by <see cref="FightSound"/>; the other two belong to the interface —
    /// a tap for a press, and a fanfare for a run that ended well.
    ///
    /// Kept as data for the same reason the worn kit is: adding a ninth should be a row, and the
    /// alternative is a switch somewhere that has to be found and edited.
    /// </remarks>
    public static class Blips
    {
        public const string Tap = "tap";
        public const string Hit = "hit";
        public const string Hurt = "hurt";
        public const string Gold = "gold";
        public const string Heal = "heal";
        public const string Kill = "kill";
        public const string Death = "death";
        public const string Fanfare = "fanfare";

        public static readonly IReadOnlyList<Blip> All = new[]
        {
            new Blip(Tap, 440d, 0.05d, Wave.Square, 0.04d),
            new Blip(Hit, 220d, 0.07d, Wave.Square, 0.05d, 110d),
            new Blip(Hurt, 150d, 0.12d, Wave.Sawtooth, 0.05d, 70d),
            new Blip(Gold, 880d, 0.06d, Wave.Triangle, 0.045d, 1320d),
            new Blip(Heal, 660d, 0.10d, Wave.Sine, 0.05d, 990d),
            new Blip(Kill, 330d, 0.16d, Wave.Square, 0.055d, 120d),
            new Blip(Death, 180d, 0.50d, Wave.Sawtooth, 0.06d, 45d),

            // The fanfare is four notes 110ms apart in the source, played by four timers. Here it
            // is one clip of the four in sequence, because a clip cannot be four things — and a
            // sound the game plays once, on an ending, has no reason to stay four.
            new Blip(Fanfare, 523d, 0.14d, Wave.Triangle, 0.05d),
        };

        /// <summary>Which noise a combat event makes, by the name the audio service knows.</summary>
        /// <remarks>
        /// The mapping is here rather than in the widget so that a screen asks for "the sound of
        /// this event" and not for a filename. <see cref="FightSound.None"/> is most events, and
        /// silence is the answer rather than a missing clip.
        /// </remarks>
        public static string Named(FightSound sound)
        {
            switch (sound)
            {
                case FightSound.Gold: return Gold;
                case FightSound.Hurt: return Hurt;
                case FightSound.Hit: return Hit;
                case FightSound.Heal: return Heal;
                case FightSound.Kill: return Kill;
                case FightSound.Death: return Death;
                default: return null;
            }
        }
    }
}
