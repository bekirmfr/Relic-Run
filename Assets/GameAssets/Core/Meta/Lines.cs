using System.Collections.Generic;
using System.Text;

namespace RelicRun.Core.Meta
{
    /// <summary>One record: a key, and everything after the first space.</summary>
    public struct Record
    {
        public string Key;
        public string Value;
    }

    /// <summary>What a piece of saved text turned out to hold.</summary>
    public sealed class Reading
    {
        public readonly List<Record> Records = new List<Record>();

        /// <summary>Lines that were not records at all.</summary>
        public int Damaged;
    }

    /// <summary>
    /// The shape everything saved is written in.
    /// </summary>
    /// <remarks>
    /// A line per record, <c>key value</c>, split on the FIRST space. Not JSON: Core has no
    /// engine and no parser, and hand-rolling a JSON reader to hold a dozen integers and a name
    /// would be more code to get wrong than the thing it holds.
    ///
    /// It lives on its own because the game saves in two places — progression and settings — and
    /// the one thing that must not differ between them is the ESCAPING. Two copies of an escaper
    /// is two chances for a name that survives one file and is quietly mangled by the other, and
    /// the delver whose name has a backslash in it would be the only person who ever found out.
    /// </remarks>
    public static class Lines
    {
        /// <summary>Writes one record.</summary>
        public static void Put(StringBuilder text, string key, string value)
        {
            text.Append(key).Append(' ').Append(value).Append('\n');
        }

        /// <summary>
        /// Every record in a piece of text, and a count of what was not one.
        /// </summary>
        /// <remarks>
        /// Never throws. A torn file is a delver with some of their save back, which beats a
        /// screen that cannot open — and the count is what stops "some of it back" being silent.
        /// Blank lines are neither: they are skipped without being counted, because a trailing
        /// newline is not damage.
        /// </remarks>
        public static Reading Of(string text)
        {
            var reading = new Reading();

            if (string.IsNullOrEmpty(text)) return reading;

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0) continue;

                int space = line.IndexOf(' ');
                if (space <= 0)
                {
                    reading.Damaged++;
                    continue;
                }

                // Everything after the first space, which may be nothing at all: a delver who
                // never set a name has an empty one, and an empty value is a value. Refusing it
                // would report damage on the commonest row the board has.
                reading.Records.Add(new Record
                {
                    Key = line.Substring(0, space),
                    Value = line.Substring(space + 1),
                });
            }

            return reading;
        }

        /// <summary>
        /// Text, made safe to put on a line.
        /// </summary>
        /// <remarks>
        /// A delver types their own name and the board shows it back, so every character has to
        /// survive: a newline would split one record into two, and the separator would move the
        /// name into a field that expects a number. Both are things a delver can do on purpose,
        /// and neither should be able to damage anybody's save — including their own.
        /// </remarks>
        public static string Escaped(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var built = new StringBuilder(text.Length);

            foreach (char letter in text)
            {
                switch (letter)
                {
                    case '\\': built.Append("\\\\"); break;
                    case '\n': built.Append("\\n"); break;
                    case '\r': built.Append("\\r"); break;
                    case '|': built.Append("\\p"); break;
                    default: built.Append(letter); break;
                }
            }

            return built.ToString();
        }

        /// <summary>
        /// Text, back from a line.
        /// </summary>
        /// <remarks>
        /// Two of the branches here cannot be reached by reading back something this escaper
        /// wrote, and they are not dead for that. They are for text that arrived some other way:
        /// a file truncated mid-write, which ends on half an escape, and a save written by a
        /// LATER version, which carries escapes this one has not been taught. Both are read here,
        /// and in both the wrong answer is to quietly change what the delver typed — a name that
        /// loses a character every time an old build opens it.
        /// </remarks>
        public static string Plain(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.IndexOf('\\') < 0) return text;

            var built = new StringBuilder(text.Length);

            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '\\' || i == text.Length - 1)
                {
                    built.Append(text[i]);
                    continue;
                }

                i++;

                switch (text[i])
                {
                    case '\\': built.Append('\\'); break;
                    case 'n': built.Append('\n'); break;
                    case 'r': built.Append('\r'); break;
                    case 'p': built.Append('|'); break;

                    default: built.Append('\\').Append(text[i]); break;
                }
            }

            return built.ToString();
        }
    }
}
