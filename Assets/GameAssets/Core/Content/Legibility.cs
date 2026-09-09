using System.Collections.Generic;
using System.Text;

namespace RelicRun.Core.Content
{
    /// <summary>
    /// Whether a face can draw what a language says.
    /// </summary>
    /// <remarks>
    /// The port ships two pixel faces and eight languages, and those two facts are in tension. A
    /// face drawn on a five-by-seven grid holds a few hundred glyphs; Japanese alone needs four
    /// hundred characters, and Chinese four hundred and forty-five. A build where three of the
    /// eight languages render as empty boxes is not a thing to find out from a screenshot on
    /// somebody else's phone.
    ///
    /// What has to be legible is what reaches the SCREEN, not what is in the table. Half the
    /// buttons in this game are written with an icon in front of them and
    /// <see cref="Locale.Clean"/> strips every one, so the arrows and pictograms in the source
    /// strings are not a font's problem — and a check that counted them would demand emoji from
    /// a font drawn in 2001.
    /// </remarks>
    public sealed class Legibility
    {
        /// <summary>The language this was asked about.</summary>
        public readonly string Language;

        /// <summary>The face this was asked about.</summary>
        public readonly string Face;

        /// <summary>How many distinct characters the language needs.</summary>
        public readonly int Needs;

        /// <summary>The code points the face has no glyph for, ascending.</summary>
        public readonly IReadOnlyList<int> Missing;

        public Legibility(string language, string face, int needs, IReadOnlyList<int> missing)
        {
            Language = language;
            Face = face;
            Needs = needs;
            Missing = missing;
        }

        /// <summary>Whether every character the language uses has a glyph.</summary>
        public bool Readable { get { return Missing.Count == 0; } }

        /// <summary>How much of the language the face can draw, in hundredths.</summary>
        public int Percent
        {
            get { return Needs == 0 ? 100 : 100 * (Needs - Missing.Count) / Needs; }
        }

        /// <summary>
        /// Every code point a set of strings needs once it has been cleaned, ascending.
        /// </summary>
        /// <remarks>
        /// Cleaned first, and by the same call the game uses, so this cannot drift from what is
        /// actually drawn. Code points rather than chars: the stripped ranges reach above the
        /// basic plane, and a font either has a glyph for a character or it does not — half a
        /// surrogate pair is not a question anybody can answer.
        /// </remarks>
        public static IReadOnlyList<int> Needed(IEnumerable<string> lines)
        {
            return Needed(lines, true);
        }

        /// <summary>
        /// Every code point a set of strings needs, ascending.
        /// </summary>
        /// <remarks>
        /// The cleaning is not optional for translated strings and must not be applied to lines
        /// that never go through the translator. The source writes some of its screens as string
        /// literals — the whole of the title's captions among them — and a literal never passes
        /// through <c>t()</c>, so it never gets cleaned and every character in it is really drawn.
        ///
        /// Asking about those with the cleaning on is worse than not asking: the cleaner strips
        /// exactly the characters a bitmap face cannot draw, so the check removes the problem it
        /// was called to find and then reports that there is none.
        /// </remarks>
        /// <param name="cleaned">
        /// Whether these lines reach the screen through <see cref="Locale.Clean"/>. True for
        /// anything the translator hands back; false for a literal.
        /// </param>
        public static IReadOnlyList<int> Needed(IEnumerable<string> lines, bool cleaned)
        {
            var points = new List<int>();
            var seen = new HashSet<int>();

            if (lines == null) return points;

            foreach (string line in lines)
            {
                string shown = cleaned ? Locale.Clean(line) : line ?? string.Empty;

                for (int i = 0; i < shown.Length; i++)
                {
                    bool paired = char.IsHighSurrogate(shown[i]) && i + 1 < shown.Length &&
                                  char.IsLowSurrogate(shown[i + 1]);

                    int point = paired ? char.ConvertToUtf32(shown[i], shown[i + 1]) : shown[i];
                    if (paired) i++;

                    if (seen.Add(point)) points.Add(point);
                }
            }

            points.Sort();
            return points;
        }

        /// <summary>What one face makes of one language.</summary>
        public static Legibility Of(string language, string face, IEnumerable<string> lines,
            IEnumerable<int> covers)
        {
            return Of(language, face, lines, covers, true);
        }

        /// <summary>What one face makes of one set of lines, cleaned or literal.</summary>
        public static Legibility Of(string language, string face, IEnumerable<string> lines,
            IEnumerable<int> covers, bool cleaned)
        {
            var glyphs = new HashSet<int>();
            if (covers != null)
            {
                foreach (int point in covers) glyphs.Add(point);
            }

            IReadOnlyList<int> needed = Needed(lines, cleaned);
            var missing = new List<int>();

            for (int i = 0; i < needed.Count; i++)
            {
                if (!glyphs.Contains(needed[i])) missing.Add(needed[i]);
            }

            return new Legibility(language, face, needed.Count, missing);
        }

        /// <summary>What is missing, written out. Empty when nothing is.</summary>
        public string Report()
        {
            if (Readable) return "";

            var said = new StringBuilder(Face)
                .Append(" cannot read ").Append(Language).Append(": ")
                .Append(Missing.Count).Append(" of ").Append(Needs)
                .Append(" characters have no glyph (");

            for (int i = 0; i < Missing.Count && i < 8; i++)
            {
                if (i > 0) said.Append(' ');
                said.Append("U+").Append(Missing[i].ToString("X4"));
            }

            if (Missing.Count > 8) said.Append(" and ").Append(Missing.Count - 8).Append(" more");

            return said.Append(')').ToString();
        }
    }
}
