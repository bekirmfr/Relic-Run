using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RelicRun.Core.Presentation
{
    /// <summary>One numbered step of the how-to.</summary>
    public struct HowStep
    {
        /// <summary>Its number, written to two digits.</summary>
        public string Number;

        public string Text;
    }

    /// <summary>Everything the how-to-play screen shows.</summary>
    public struct HowCard
    {
        public IReadOnlyList<HowStep> Steps;
    }

    /// <summary>
    /// How to play, taken apart.
    /// </summary>
    /// <remarks>
    /// The whole screen is ONE translated string. The source ships it as a single paragraph with
    /// line breaks in it, splits on those, strips the markup, and numbers what is left — so the
    /// number of steps is a property of the translation rather than of the game, and a language
    /// that writes four steps gets four.
    ///
    /// That is why this takes the text rather than a key. Core cannot read a locale, and the
    /// splitting is the part worth testing: a screen that split on the wrong thing would show one
    /// enormous step, and a screen that forgot to strip would show its markup to the delver.
    /// </remarks>
    public static class HowCards
    {
        /// <summary>What the source breaks its how-to on.</summary>
        /// <remarks>
        /// An HTML break, because the string is written for a web page. Every translation carries
        /// the same marker — it is punctuation in the source text rather than anything a
        /// translator chose — so splitting on it is reading the string as written.
        /// </remarks>
        public const string Break = "<br>";

        /// <summary>The steps, from one paragraph of translated text.</summary>
        public static HowCard Of(string body)
        {
            var steps = new List<HowStep>();

            if (string.IsNullOrEmpty(body)) return new HowCard { Steps = steps };

            foreach (string part in body.Split(new[] { Break }, System.StringSplitOptions.None))
            {
                string text = Bare(part).Trim();

                // A translation with a trailing break would otherwise number an empty step.
                if (text.Length == 0) continue;

                steps.Add(new HowStep
                {
                    Number = (steps.Count + 1).ToString("00", CultureInfo.InvariantCulture),
                    Text = text,
                });
            }

            return new HowCard { Steps = steps };
        }

        /// <summary>
        /// Text with its markup taken out.
        /// </summary>
        /// <remarks>
        /// The source strips rather than renders, and this follows it. The temptation is to keep
        /// the bold — the port draws with TextMeshPro, which understands the very same tags — but
        /// that would show a delver something the game has never shown anybody, and it would show
        /// it in eight languages nobody proofread it in.
        ///
        /// Walked rather than matched with a pattern, because Core has no regular expressions
        /// worth the dependency and this is a loop with one flag in it.
        /// </remarks>
        public static string Bare(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.IndexOf('<') < 0) return text;

            var built = new StringBuilder(text.Length);
            var inside = false;

            foreach (char letter in text)
            {
                if (letter == '<')
                {
                    inside = true;
                    continue;
                }

                if (letter == '>')
                {
                    inside = false;
                    continue;
                }

                if (!inside) built.Append(letter);
            }

            return built.ToString();
        }
    }
}
