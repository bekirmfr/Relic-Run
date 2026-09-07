using System;

namespace RelicRun.Core.Content
{
    /// <summary>A colour, as the wardrobe deals in them: eight bits a channel.</summary>
    public readonly struct Rgb
    {
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;

        public Rgb(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        /// <summary>Reads <c>#RRGGBB</c>. The leading hash is optional.</summary>
        public static Rgb Parse(string hex)
        {
            if (hex == null) throw new ArgumentNullException(nameof(hex));

            int at = hex.Length > 0 && hex[0] == '#' ? 1 : 0;
            if (hex.Length - at != 6)
            {
                throw new FormatException("a colour is six hex digits, not \"" + hex + "\"");
            }

            return new Rgb(
                (byte)((Digit(hex[at]) << 4) | Digit(hex[at + 1])),
                (byte)((Digit(hex[at + 2]) << 4) | Digit(hex[at + 3])),
                (byte)((Digit(hex[at + 4]) << 4) | Digit(hex[at + 5])));
        }

        private static int Digit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            throw new FormatException("not a hex digit: " + c);
        }

        /// <summary>
        /// Writes <c>#rrggbb</c>, in lower case.
        /// </summary>
        /// <remarks>
        /// Lower case because that is what the source's own writer produces, and the corpus
        /// compares the strings. The reader accepts either, since the hand-written tables it
        /// came from are upper case.
        /// </remarks>
        public override string ToString()
        {
            return "#" + Hex(R) + Hex(G) + Hex(B);
        }

        private static string Hex(byte v)
        {
            const string digits = "0123456789abcdef";
            return new string(new[] { digits[v >> 4], digits[v & 15] });
        }
    }

    /// <summary>
    /// The colour maths behind a delver's appearance.
    /// </summary>
    /// <remarks>
    /// A hero is drawn as a stack of images whose pixels are not colours but ROLE KEYS — one
    /// letter saying "this pixel is hair" or "this is the dark side of the outfit". A palette
    /// turns keys into colours and the shader looks them up, which is what lets the Changing
    /// Room recolour a delver live without touching a sprite.
    ///
    /// A family gives one base colour and derives the other two from it, through HSL and back:
    /// the dark side has its lightness multiplied down, its hue rotated a little and its
    /// saturation pushed up, and the light side is the same in reverse.
    ///
    /// It is float arithmetic landing on a byte, so it is gated by <c>palette.json</c> rather
    /// than trusted. Rounding goes through <see cref="Determinism.JsMath"/>, because a value
    /// landing exactly on a half is the one place the two languages disagree by default.
    /// </remarks>
    public static class HeroColour
    {
        /// <summary>Every channel multiplied, and clamped. The flat shade.</summary>
        public static Rgb Shade(Rgb colour, double multiplier)
        {
            return new Rgb(Channel(colour.R * multiplier), Channel(colour.G * multiplier),
                Channel(colour.B * multiplier));
        }

        /// <summary>
        /// Lightness multiplied, hue rotated by degrees, saturation scaled.
        /// </summary>
        /// <remarks>
        /// Lightness and saturation are clamped to the unit range BEFORE the conversion back,
        /// which is what stops a multiplier over one from wrapping into a different colour. The
        /// hue is not clamped but wrapped, because a hue is an angle.
        /// </remarks>
        public static Rgb ApplyShade(Rgb colour, double lightness, double hueDegrees,
            double saturation)
        {
            double h, s, l;
            ToHsl(colour, out h, out s, out l);

            double toned = Clamp01(l * lightness);
            double sat = Clamp01(s * saturation);
            return FromHsl(h + hueDegrees / 360.0, sat, toned);
        }

        /// <summary>Hue, saturation and lightness, each in the unit range.</summary>
        public static void ToHsl(Rgb colour, out double h, out double s, out double l)
        {
            double r = colour.R / 255.0, g = colour.G / 255.0, b = colour.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));

            l = (max + min) / 2.0;
            h = 0;
            s = 0;

            if (max == min) return;

            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);

            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;

            h /= 6;
        }

        /// <summary>Back to a colour. The hue wraps; saturation and lightness are taken as given.</summary>
        public static Rgb FromHsl(double h, double s, double l)
        {
            h -= Math.Floor(h);

            if (s == 0)
            {
                byte flat = Channel(l * 255);
                return new Rgb(flat, flat, flat);
            }

            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;

            return new Rgb(
                Channel(Hue(p, q, h + 1.0 / 3.0) * 255),
                Channel(Hue(p, q, h) * 255),
                Channel(Hue(p, q, h - 1.0 / 3.0) * 255));
        }

        private static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        /// <summary>
        /// One channel: rounded as the source rounds, then held inside a byte.
        /// </summary>
        /// <remarks>
        /// The rounding is what matters. JavaScript rounds a half toward positive infinity and
        /// .NET rounds it to even by default, so 0.5 becomes 1 in one language and 0 in the
        /// other — a whole level of a channel, on exactly the values a palette lands on.
        /// </remarks>
        private static byte Channel(double value)
        {
            double rounded = Determinism.JsMath.Round(value);
            if (rounded <= 0) return 0;
            if (rounded >= 255) return 255;
            return (byte)rounded;
        }

        private static double Clamp01(double v)
        {
            return v < 0 ? 0 : v > 1 ? 1 : v;
        }
    }
}
