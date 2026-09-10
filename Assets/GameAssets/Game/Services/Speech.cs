using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RelicRun.Core.Content;
using RelicRun.Core.Meta;
using RelicRun.Game.Data;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace RelicRun.Game.Services
{
    /// <summary>
    /// What the game says, in the delver's own language.
    /// </summary>
    /// <remarks>
    /// Two tables and a fallback chain. <see cref="Locale"/> already knows how to resolve a key
    /// through them, fill its placeholders and clean what comes out; what is left is fetching the
    /// two files and deciding which one is the delver's — and the deciding is
    /// <see cref="Languages"/>'s, in Core, where it can be tested.
    ///
    /// English is always loaded, even for a delver reading Japanese. It is the fallback for every
    /// key a translation is missing, and a build without it shows raw keys the moment a
    /// translator falls behind — which is most of the time a language exists at all. It is
    /// fifty-nine kilobytes across all eight, so the second table costs nothing worth counting.
    ///
    /// Until this existed the screens that use translated strings could not be built at all
    /// without spelling them in English, which would have been eight screens to redo.
    /// </remarks>
    public sealed class Speech
    {
        /// <summary>What the game reads in. Never null: English until told otherwise.</summary>
        public Locale Locale { get; private set; }

        /// <summary>
        /// The book the last fetch came from, kept so a later one need not be handed it again.
        /// </summary>
        /// <remarks>
        /// Changing language is the one thing that happens AFTER startup, and it happens in a
        /// modal that has no business knowing where the game's languages are listed. Remembering
        /// the book here is what lets the modal ask for a language rather than for a file.
        /// </remarks>
        public LocaleBook Book { get; private set; }

        public Speech()
        {
            // Something legible before anything is fetched. A screen drawn during startup shows
            // its keys rather than blanks, which is ugly and diagnosable — blanks are neither.
            Locale = new Locale(LocaleBook.Fallback, new Dictionary<string, string>());
        }

        /// <summary>
        /// Picks a language and fetches it.
        /// </summary>
        /// <param name="asked">
        /// What the device asks for, best first. The platform's own answer, so that a delver who
        /// has never opened the settings still finds the game in their language.
        /// </param>
        public async Task Learn(LocaleBook book, Preferences chosen, IReadOnlyList<string> asked)
        {
            if (book == null)
            {
                Debug.LogWarning("no locale book, so the game speaks in keys");
                return;
            }

            Book = book;

            var shipped = new List<string>();
            foreach (LocaleBook.Translation one in book.Languages) shipped.Add(one.Language);

            string language = Languages.Pick(chosen != null ? chosen.Language : null, asked,
                shipped);

            Dictionary<string, string> english = await Table(book, LocaleBook.Fallback);

            Dictionary<string, string> table = language == LocaleBook.Fallback
                ? english
                : await Table(book, language);

            Locale = new Locale(language, table, english);
        }

        /// <summary>One language's strings, fetched and parsed.</summary>
        /// <remarks>
        /// A missing or unreadable language is a warning and an empty table rather than a throw.
        /// Every key then falls through to English, which is a game somebody can still play.
        ///
        /// It asks the reference for the handle it ALREADY has before asking for a new one, and
        /// that is not an optimisation. An AssetReference caches its handle on the asset that
        /// holds it, and loading one twice throws:
        ///
        ///     Attempting to load AssetReference that has already been loaded.
        ///     Handle is exposed through getter OperationHandle
        ///
        /// Which used to be impossible to reach, because the menu was the only screen that ever
        /// learned a language and the menu was loaded once. Now the fight learns one too, and
        /// the menu is loaded again after it — so the same file is asked for three times in a
        /// session, and the second and third came back as a game speaking in raw keys.
        ///
        /// And it lets go of what it loaded, which is the other half. The file is parsed into a
        /// dictionary here and the asset itself is never wanted again, so holding it is holding
        /// something for nobody — and a held handle is what makes the NEXT screen's load throw.
        /// A handle somebody else took is left alone: this releases only what it acquired.
        /// </remarks>
        private static async Task<Dictionary<string, string>> Table(LocaleBook book,
            string language)
        {
            var table = new Dictionary<string, string>();

            AssetReferenceT<TextAsset> address = book.For(language);

            if (address == null)
            {
                Debug.LogWarning("the locale book has no " + language + ", so it falls back");
                return table;
            }

            bool ours = !address.IsValid();

            AsyncOperationHandle<TextAsset> fetching = ours
                ? address.LoadAssetAsync<TextAsset>()
                : address.OperationHandle.Convert<TextAsset>();

            TextAsset asset = await fetching.Task;

            try
            {
                if (asset == null)
                {
                    Debug.LogWarning("could not fetch the strings for " + language);
                    return table;
                }

                foreach (JProperty entry in JObject.Parse(asset.text).Properties())
                {
                    table[entry.Name] = entry.Value.Value<string>();
                }

                return table;
            }
            finally
            {
                // In a finally, because a file that failed to parse was still loaded. Left
                // held, the next screen to want the same language finds the reference busy.
                if (ours && address.IsValid()) address.ReleaseAsset();
            }
        }

        /// <summary>
        /// What the device asks for, best first.
        /// </summary>
        /// <remarks>
        /// Unity reports one language rather than an ordered list, so this is a list of one. The
        /// shape is the source's — a browser hands over <c>navigator.languages</c> — and is kept
        /// because it is the shape the picker takes, and because a platform that can offer more
        /// than one should not need the picker changed to say so.
        /// </remarks>
        public static IReadOnlyList<string> Asked()
        {
            return new[] { Tag(Application.systemLanguage) };
        }

        /// <summary>
        /// Unity's own enum, as the two-letter tag the strings are filed under.
        /// </summary>
        /// <remarks>
        /// Only the eight the game ships are named. Everything else answers with something that
        /// matches nothing, and the picker falls through to English — which is the same answer a
        /// longer table would give, arrived at with less to keep correct.
        /// </remarks>
        private static string Tag(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.Arabic: return "ar";
                case SystemLanguage.Spanish: return "es";
                case SystemLanguage.French: return "fr";
                case SystemLanguage.Japanese: return "ja";
                case SystemLanguage.Russian: return "ru";
                case SystemLanguage.Turkish: return "tr";

                // Unity tells the two Chinese scripts apart and the game ships one translation,
                // so both arrive at it.
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional: return "zh";

                default: return LocaleBook.Fallback;
            }
        }
    }
}
