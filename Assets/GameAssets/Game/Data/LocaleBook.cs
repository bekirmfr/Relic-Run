using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using UnityEngine;

namespace RelicRun.Game.Data
{
    /// <summary>
    /// Every language the game is written in, one text asset each.
    /// </summary>
    /// <remarks>
    /// Separate assets rather than one file, because a delver reading Japanese has no use for
    /// the other seven and Addressables can only leave behind what it can address separately.
    ///
    /// English is required. Not out of preference — <see cref="Locale"/> falls back to it for
    /// any key a translation is missing, so a build without it would show raw keys the moment a
    /// translator fell behind, which is most of the time a language exists at all.
    /// </remarks>
    [CreateAssetMenu(menuName = "Relic Run/Locales", fileName = "Locales")]
    public sealed class LocaleBook : ScriptableObject
    {
        /// <summary>The language every other one falls back to.</summary>
        public const string Fallback = "en";

        [Serializable]
        public struct Translation
        {
            [Tooltip("Locale code, e.g. en, fr, ja.")]
            public string Language;

            [Tooltip("The strings for that language, as JSON.")]
            public TextAsset Strings;

            public Translation(string language, TextAsset strings)
            {
                Language = language;
                Strings = strings;
            }
        }

        [SerializeField] private Translation[] _languages = new Translation[0];

        public IReadOnlyList<Translation> Languages { get { return _languages; } }

        /// <summary>The strings for a language, or null when it is not one of them.</summary>
        public TextAsset For(string language)
        {
            for (int i = 0; i < _languages.Length; i++)
            {
                if (_languages[i].Language == language) return _languages[i].Strings;
            }

            return null;
        }

        /// <summary>
        /// Whether the book is usable: English is here, and nothing is half-filled.
        /// </summary>
        /// <remarks>
        /// The needed list is the book's own contents plus English, which is the one id it is not
        /// allowed to decide for itself. There is no catalog of languages to check against —
        /// which languages exist is a shipping decision and not a fact about the game — so this
        /// audit can only catch a language declared and not delivered, one declared twice, and
        /// the fallback missing. All three have happened to somebody.
        /// </remarks>
        public BindingAudit Audit()
        {
            var needed = new List<string> { Fallback };
            var bound = new List<Binding>(_languages.Length);

            for (int i = 0; i < _languages.Length; i++)
            {
                string language = _languages[i].Language;
                if (!needed.Contains(language)) needed.Add(language);

                bound.Add(new Binding(language, _languages[i].Strings != null));
            }

            return BindingAudit.Of("locales", needed, bound);
        }

        /// <summary>Replaces everything this book holds. The importer's one way in.</summary>
        public void Rebind(IList<Translation> languages)
        {
            var kept = new Translation[languages == null ? 0 : languages.Count];
            for (int i = 0; i < kept.Length; i++) kept[i] = languages[i];

            _languages = kept;
        }
    }
}
