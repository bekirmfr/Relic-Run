using System.Globalization;
using System.Text;

namespace RelicRun.Core.Presentation
{
    /// <summary>
    /// What a delver may call themselves.
    /// </summary>
    /// <remarks>
    /// The only field in the game a person types, which makes it the only one that arrives
    /// hostile. It is shown on the board, written into the save, and carried on every run — so
    /// what gets past this is what everything downstream has to survive.
    ///
    /// The rule is the source's, character for character: strip the control range and the two
    /// angle brackets, trim, keep sixteen. The brackets are there because the source draws names
    /// into a web page; this port draws them into a text mesh, where they are harmless. They are
    /// stripped anyway. A save is portable between the two, and a name that is safe in one and
    /// not the other is a name that stops being safe when somebody writes an export.
    /// </remarks>
    public static class Naming
    {
        /// <summary>The most characters a name may keep. The source's sixteen.</summary>
        /// <remarks>
        /// Counted in CHARS, as the source counts them, so a name of sixteen emoji is cut where
        /// JavaScript would cut it. That is not the prettiest rule available — it can split a
        /// surrogate pair — and it is the one that keeps the two builds agreeing about what a
        /// delver is called. <see cref="Clean"/> never leaves half a pair behind, whether the cut
        /// made the orphan or the delver pasted one in.
        /// </remarks>
        public const int Longest = 16;

        /// <summary>What separates an auto-name from its number.</summary>
        public const char Tally = '#';

        /// <summary>
        /// A typed name, made safe to keep.
        /// </summary>
        /// <remarks>
        /// May come back EMPTY, and that is an answer rather than a failure: the source gives a
        /// delver who clears the box a generated name, and the deciding happens where the word
        /// for "delver" can be looked up. This does the stripping and nothing else.
        /// </remarks>
        public static string Clean(string typed)
        {
            if (string.IsNullOrEmpty(typed)) return string.Empty;

            var kept = new StringBuilder(typed.Length);

            for (var i = 0; i < typed.Length; i++)
            {
                char letter = typed[i];

                // The C0 control range and the two brackets. Everything else a person can type
                // is theirs to type — accents, scripts this build cannot even draw, emoji.
                if (letter <= '\u001f') continue;
                if (letter == '<' || letter == '>') continue;

                // Half a character is not a character. A pair is kept together; a surrogate
                // arriving without its partner is dropped here rather than downstream, where it
                // draws as nothing, compares as something, and survives a save round trip
                // looking like text.
                //
                // This used to happen only to names long enough to be CUT, which made the
                // promise true of some names and not others. Mutation testing found the gap by
                // widening the length check and surviving: a name of exactly sixteen took the
                // other path, and the two paths disagreed about an orphan that arrived already
                // in the input.
                if (char.IsHighSurrogate(letter))
                {
                    if (i + 1 >= typed.Length || !char.IsLowSurrogate(typed[i + 1])) continue;

                    kept.Append(letter).Append(typed[i + 1]);
                    i++;
                    continue;
                }

                if (char.IsLowSurrogate(letter)) continue;

                kept.Append(letter);
            }

            string trimmed = kept.ToString().Trim();

            if (trimmed.Length <= Longest) return trimmed;

            string cut = trimmed.Substring(0, Longest);

            // Sixteen chars can land between the two halves of one character. Nothing above let
            // an ORPHAN through, so this is only ever the pair the cut itself split.
            if (char.IsHighSurrogate(cut[cut.Length - 1])) cut = cut.Substring(0, cut.Length - 1);

            return cut.Trim();
        }

        /// <summary>Whether a cleaned name is no name at all, and one has to be made up.</summary>
        public static bool Missing(string cleaned)
        {
            return string.IsNullOrEmpty(cleaned);
        }

        /// <summary>
        /// A name for a delver who did not give one.
        /// </summary>
        /// <remarks>
        /// The word is translated and the number is rolled, so both are handed in: Core neither
        /// picks a language nor owns a random source, and a generated name that differed between
        /// two runs of the same seed would be the one thing on the board nobody could reproduce.
        /// </remarks>
        /// <param name="word">The local word for a delver, already translated.</param>
        /// <param name="number">A roll from 0 to 9999. Written to four digits.</param>
        public static string Auto(string word, int number)
        {
            if (string.IsNullOrEmpty(word)) word = "Delver";

            int within = number % 10000;
            if (within < 0) within = -within;

            return word + Tally + within.ToString("0000", CultureInfo.InvariantCulture);
        }
    }
}
