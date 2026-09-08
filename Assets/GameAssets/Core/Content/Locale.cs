using System.Collections.Generic;
using System.Text;

namespace RelicRun.Core.Content
{
    /// <summary>
    /// What a delver reads, in their own language.
    /// </summary>
    /// <remarks>
    /// Resolving a string is more than a lookup. A translation that is missing a key falls back
    /// to English, and English falling back means showing the KEY — so a string nobody wrote is
    /// visible in testing rather than an empty button. Placeholders are then filled, and the
    /// result is cleaned: emoji stripped, runs of spaces collapsed, ends trimmed.
    ///
    /// The cleaning is the part that surprises. "← Back" is written with an arrow and reaches
    /// the screen as "Back", because the arrow is inside the stripped range — a hundred and
    /// forty-four of the twelve hundred shipped strings change on their way out. The game's
    /// buttons are written with icons in front of them and none of those icons is ever drawn.
    ///
    /// Values are passed as strings on purpose. A number's spelling depends on a culture, and
    /// deciding that is the caller's business — Core has no opinion and no way to have one.
    /// </remarks>
    public sealed class Locale
    {
        /// <summary>The language this reads in, as a locale code.</summary>
        public readonly string Language;

        private readonly IReadOnlyDictionary<string, string> _table;
        private readonly IReadOnlyDictionary<string, string> _fallback;

        /// <param name="fallback">
        /// English, normally. A locale IS its own fallback when none is given, which is what
        /// English itself wants.
        /// </param>
        public Locale(string language, IReadOnlyDictionary<string, string> table,
            IReadOnlyDictionary<string, string> fallback = null)
        {
            Language = language;
            _table = table ?? new Dictionary<string, string>();
            _fallback = fallback ?? _table;
        }

        /// <summary>The string for a key, cleaned and ready to show.</summary>
        public string Get(string key)
        {
            return Get(key, null);
        }

        /// <summary>The string for a key with its placeholders filled.</summary>
        public string Get(string key, IReadOnlyDictionary<string, string> values)
        {
            string text;
            if (!_table.TryGetValue(key, out text) && !_fallback.TryGetValue(key, out text))
            {
                // Not a mistake to hide: a key with nothing behind it shows as itself.
                text = key;
            }

            if (values != null)
            {
                foreach (KeyValuePair<string, string> value in values)
                {
                    text = text.Replace("{" + value.Key + "}", value.Value ?? "");
                }
            }

            return Clean(text);
        }

        /// <summary>Whether this locale has a string of its own for a key.</summary>
        public bool Has(string key) { return _table.ContainsKey(key); }

        /// <summary>
        /// Strips the decoration and tidies the spacing.
        /// </summary>
        /// <remarks>
        /// Walked by code point rather than matched by a pattern. The stripped ranges reach
        /// above the basic plane, where a regular expression would be working on halves of
        /// surrogate pairs, and the two languages disagree about what that means.
        /// </remarks>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return Trim(Collapse(Strip(text)));
        }

        /// <summary>Drops every decoration code point, surrogate pairs included.</summary>
        private static string Strip(string text)
        {
            var kept = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                bool paired = char.IsHighSurrogate(text[i]) && i + 1 < text.Length &&
                              char.IsLowSurrogate(text[i + 1]);

                int point = paired ? char.ConvertToUtf32(text[i], text[i + 1]) : text[i];

                if (!IsDecoration(point))
                {
                    kept.Append(text[i]);
                    if (paired) kept.Append(text[i + 1]);
                }

                if (paired) i++;
            }

            return kept.ToString();
        }

        /// <summary>
        /// Turns every run of two or more spaces or tabs into a single space.
        /// </summary>
        /// <remarks>
        /// After the stripping, not before — a decoration between two spaces leaves them
        /// adjacent, and closing that gap is most of what this is for. A LONE space or tab is
        /// left exactly as it was, which is why this counts before it replaces.
        /// </remarks>
        private const char Tab = '\t';

        private static string Collapse(string text)
        {
            var kept = new StringBuilder(text.Length);
            int run = 0;

            // One past the end, so a run that reaches the end is still flushed.
            for (int i = 0; i <= text.Length; i++)
            {
                if (i < text.Length && (text[i] == ' ' || text[i] == Tab))
                {
                    run++;
                    continue;
                }

                if (run == 1) kept.Append(text[i - 1]);
                else if (run > 1) kept.Append(' ');
                run = 0;

                if (i < text.Length) kept.Append(text[i]);
            }

            return kept.ToString();
        }

        /// <summary>Trims the ends, as the source's own trim does.</summary>
        private static string Trim(string text)
        {
            return text.Trim();
        }

        /// <summary>
        /// Whether a code point is decoration rather than text.
        /// </summary>
        /// <remarks>
        /// Emoji, arrows, technical symbols, dingbats, the variation selectors and the zero-width
        /// joiner. The ranges are the source's exactly; widening or narrowing one changes which
        /// of the shipped strings arrive with an icon still attached.
        /// </remarks>
        private static bool IsDecoration(int point)
        {
            return (point >= 0x1F000 && point <= 0x1FAFF) ||
                   (point >= 0x2190 && point <= 0x21FF) ||
                   (point >= 0x2300 && point <= 0x23FF) ||
                   (point >= 0x2500 && point <= 0x27BF) ||
                   (point >= 0x2B00 && point <= 0x2BFF) ||
                   point == 0xFE0E || point == 0xFE0F || point == 0x200D;
        }
    }
}
