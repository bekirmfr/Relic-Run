using System.Collections.Generic;
using RelicRun.Core.Meta;

namespace RelicRun.Core.Presentation
{
    /// <summary>One language a delver may read the game in.</summary>
    public struct Tongue
    {
        /// <summary>The tag it is filed under: two letters, lowercase.</summary>
        public string Code;

        /// <summary>What it calls itself.</summary>
        public string Label;

        /// <summary>Whether it is the one being read now.</summary>
        public bool Chosen;
    }

    /// <summary>Everything the settings modal shows.</summary>
    public struct SettingsCard
    {
        /// <summary>Every language this build ships, in the order the book lists them.</summary>
        public IReadOnlyList<Tongue> Tongues;

        /// <summary>Whether the game is silent.</summary>
        public bool Muted;

        /// <summary>What the delver calls themselves, ready to put in the box.</summary>
        public string Name;

        /// <summary>Whether the supporter pack has been bought, which hides its offer.</summary>
        public bool Supporter;
    }

    /// <summary>
    /// The settings, worked out.
    /// </summary>
    /// <remarks>
    /// Three things a delver can change and one they can only have bought. Everything the modal
    /// SAYS is translated — its title, both sound labels, the name caption — so none of it is
    /// here; what is here is which languages exist, which is current, and what is in the box.
    /// </remarks>
    public static class SettingsCards
    {
        /// <summary>
        /// What each language calls itself.
        /// </summary>
        /// <remarks>
        /// The port supplies these. The source reads them from a global its markup never defines,
        /// so it falls back to a table of one — a language picker showing "en" and nothing else —
        /// which is a placeholder rather than a decision to copy.
        ///
        /// Endonyms rather than English names, because the delver most in need of this list is
        /// the one who cannot read the language it is currently in. "日本語" is findable by
        /// somebody who reads Japanese; "Japanese" is not. The three scripts the pixel face
        /// cannot draw are exactly the three the borrowed system faces exist for.
        /// </remarks>
        public static readonly IReadOnlyDictionary<string, string> Endonyms =
            new Dictionary<string, string>
            {
                { "ar", "العربية" },
                { "en", "English" },
                { "es", "Español" },
                { "fr", "Français" },
                { "ja", "日本語" },
                { "ru", "Русский" },
                { "tr", "Türkçe" },
                { "zh", "中文" },
            };

        /// <summary>The settings, for this delver, with these languages shipped.</summary>
        /// <param name="shipped">
        /// What the build actually has, in the order to show them. Asked for rather than taken
        /// from <see cref="Endonyms"/>, because that table is what languages are CALLED and the
        /// book is what the build contains — a list built from the names would offer a language
        /// with no strings behind it.
        /// </param>
        /// <param name="reading">The language in use, which may not be the one chosen.</param>
        public static SettingsCard Of(Preferences prefs, IReadOnlyList<string> shipped,
            string reading)
        {
            var tongues = new List<Tongue>();

            if (shipped != null)
            {
                foreach (string code in shipped)
                {
                    string label;

                    tongues.Add(new Tongue
                    {
                        Code = code,

                        // A language with no endonym shows its tag. Ugly and honest: the delver
                        // can still pick it, and whoever shipped it can see what is missing.
                        Label = Endonyms.TryGetValue(code, out label) ? label : code,
                        Chosen = code == reading,
                    });
                }
            }

            return new SettingsCard
            {
                Tongues = tongues,
                Muted = prefs != null && prefs.Muted,
                Name = prefs != null && prefs.Name != null ? prefs.Name : string.Empty,
                Supporter = prefs != null && prefs.Supporter,
            };
        }
    }
}
