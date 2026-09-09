using System.Collections.Generic;

namespace RelicRun.Core.Content
{
    /// <summary>
    /// Which language the game opens in.
    /// </summary>
    /// <remarks>
    /// Three steps, in order, and the order is the rule: what the delver chose, then what their
    /// device asks for, then English. The middle step is the one that earns its place — a delver
    /// who has never opened the settings should still find the game in their own language, and
    /// the only way to manage that is to ask the device before falling back.
    /// </remarks>
    public static class Languages
    {
        /// <summary>What the game falls back to when nothing else fits.</summary>
        /// <remarks>
        /// Not a preference: English is the language every string is written in first, and the
        /// one <see cref="Locale"/> falls back to key by key. A fallback that could be missing
        /// would be a screen of raw keys.
        /// </remarks>
        public const string Fallback = "en";

        /// <summary>
        /// The language to read in.
        /// </summary>
        /// <remarks>
        /// A chosen language that the game does not ship is ignored rather than honoured, which
        /// matters after a build drops a translation: the delver gets English rather than a
        /// screen of keys, and the setting they chose is not silently rewritten either — they
        /// still have it if the translation comes back.
        /// </remarks>
        /// <param name="chosen">What the delver picked, or null if they never have.</param>
        /// <param name="asked">
        /// What the device asks for, best first. Tags may carry a region — <c>pt-BR</c>, <c>zh-Hans</c>
        /// — and only the part before the first dash is matched, because the game ships one
        /// translation per language and a delver in Brazil should get Portuguese if it exists.
        /// </param>
        /// <param name="shipped">The languages this build actually has.</param>
        public static string Pick(string chosen, IReadOnlyList<string> asked,
            IReadOnlyCollection<string> shipped)
        {
            if (shipped == null || shipped.Count == 0) return Fallback;

            if (Has(shipped, chosen)) return chosen;

            if (asked != null)
            {
                for (var i = 0; i < asked.Count; i++)
                {
                    string tag = Base(asked[i]);
                    if (Has(shipped, tag)) return tag;
                }
            }

            return Fallback;
        }

        /// <summary>
        /// The language part of a tag, lowercased.
        /// </summary>
        /// <remarks>
        /// Lowercased with the invariant rules on purpose. A Turkish delver's device is the
        /// reason: under Turkish casing, lowering "I" gives a dotless "ı", so a tag that arrived
        /// as "IT" would become "ıt" and match nothing — Italian would be unreachable on exactly
        /// the devices most likely to be set to Turkish.
        /// </remarks>
        public static string Base(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return string.Empty;

            int dash = tag.IndexOf('-');
            string language = dash < 0 ? tag : tag.Substring(0, dash);

            return language.ToLowerInvariant();
        }

        private static bool Has(IReadOnlyCollection<string> shipped, string language)
        {
            if (string.IsNullOrEmpty(language)) return false;

            foreach (string one in shipped)
            {
                if (one == language) return true;
            }

            return false;
        }
    }
}
