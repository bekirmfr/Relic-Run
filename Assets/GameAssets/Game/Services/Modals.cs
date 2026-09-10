using System;
using GameLift.Popup;
using RelicRun.Core.Content;
using RelicRun.Core.Meta;
using RelicRun.Core.Presentation;
using RelicRun.Game.Presentation;
using UnityEngine;

namespace RelicRun.Game.Services
{
    /// <summary>
    /// The modals, opened and answered.
    /// </summary>
    /// <remarks>
    /// A modal is not a screen and is deliberately not a <see cref="MetaPanel"/>. Settings are
    /// reachable from wherever a delver happens to be, and a panel would have to remember where
    /// that was in order to go back — the board already keeps one piece of history for exactly
    /// that reason and one is enough.
    ///
    /// This is where a popup's ANSWER is applied, rather than in the popup itself. A popup that
    /// wrote to the save would be a screen with an opinion about the save's shape; this way the
    /// popup raises what happened and one place decides what it means — including the part that
    /// is not obvious, which is that changing a language means fetching one.
    /// </remarks>
    public sealed class Modals
    {
        private readonly IPopupService _popups;
        private readonly SaveVault _vault;
        private readonly Speech _speech;

        /// <summary>Raised when something changed that the screen behind should redraw for.</summary>
        public event Action Changed;

        public Modals(IPopupService popups, SaveVault vault, Speech speech)
        {
            _popups = popups;
            _vault = vault;
            _speech = speech;
        }

        /// <summary>Whether anything can be opened at all.</summary>
        /// <remarks>
        /// Asked rather than assumed, so a caller can leave a button out instead of showing one
        /// that does nothing. The service is missing only when the application scope could not be
        /// reached, which is the same failure that leaves the save empty.
        /// </remarks>
        public bool Ready
        {
            get { return _popups != null; }
        }

        /// <summary>Opens the settings.</summary>
        public void Settings()
        {
            if (_popups == null)
            {
                Debug.LogWarning("nothing can open a popup, so the settings cannot be reached");
                return;
            }

            SettingsPopup popup = _popups.Create<SettingsPopup>();

            if (popup == null)
            {
                Debug.LogError("the settings popup is not registered with the popup service");
                return;
            }

            popup.Chose += Speak;
            popup.Switched += Hush;
            popup.Renamed += Rename;

            Dress(popup);
        }

        /// <summary>
        /// Opens the welcome, if this delver has never been asked their name.
        /// </summary>
        /// <remarks>
        /// Asked of the save rather than of a flag. A delver who was asked and cleared the box is
        /// not asked again — they answered, and the answer was nothing.
        /// </remarks>
        /// <param name="rolled">A number for the suggested name. The caller owns the randomness.</param>
        public void WelcomeIfNew(int rolled)
        {
            if (_popups == null || _vault == null) return;
            if (!_vault.Chosen.NeverAsked) return;

            WelcomePopup popup = _popups.Create<WelcomePopup>();

            if (popup == null)
            {
                Debug.LogError("the welcome popup is not registered with the popup service");
                return;
            }

            popup.Named += Rename;

            popup.Show(Naming.Auto(Words.Get("autoNameWord"), rolled), Words);
            popup.Appear();
        }

        /// <summary>Picks a language, then fetches it.</summary>
        /// <remarks>
        /// The fetch is what makes this belong here rather than in the popup. Choosing is instant
        /// and loading is not, and everything already drawn has to be told once the words arrive —
        /// which is what <see cref="Changed"/> is for.
        /// </remarks>
        private async void Speak(string language)
        {
            if (_vault == null || _speech == null) return;

            _vault.Chosen.Language = language;
            _vault.CommitChoices();

            try
            {
                await _speech.Learn(_speech.Book, _vault.Chosen, Speech.Asked());
            }
            catch (Exception broken)
            {
                Debug.LogWarning("could not fetch " + language + ": " + broken.Message);
            }

            Told();
        }

        private void Hush()
        {
            if (_vault == null) return;

            _vault.Chosen.Muted = !_vault.Chosen.Muted;
            _vault.CommitChoices();

            Told();
        }

        /// <summary>
        /// Takes a typed name, cleans it, and makes one up if nothing is left.
        /// </summary>
        /// <remarks>
        /// The cleaning is Core's and is gated there. What is decided here is the one thing Core
        /// cannot: an empty box becomes a GENERATED name rather than an empty one, which is the
        /// source's answer and the reason a delver always has something to show on the board.
        /// </remarks>
        private void Rename(string typed)
        {
            if (_vault == null) return;

            string cleaned = Naming.Clean(typed);

            if (Naming.Missing(cleaned))
            {
                cleaned = Naming.Auto(Words.Get("autoNameWord"),
                    UnityEngine.Random.Range(0, 10000));
            }

            _vault.Chosen.Name = cleaned;
            _vault.CommitChoices();

            Told();
        }

        private void Dress(SettingsPopup popup)
        {
            Preferences chosen = _vault != null ? _vault.Chosen : new Preferences();

            popup.Show(SettingsCards.Of(chosen, Shipped(), Words.Language), Words);
            popup.Appear();
        }

        /// <summary>What the build actually ships, which is the book's contents.</summary>
        private System.Collections.Generic.IReadOnlyList<string> Shipped()
        {
            var codes = new System.Collections.Generic.List<string>();

            if (_speech == null || _speech.Book == null) return codes;

            foreach (Data.LocaleBook.Translation one in _speech.Book.Languages)
            {
                codes.Add(one.Language);
            }

            return codes;
        }

        /// <summary>
        /// What the game says, or a locale that answers every key with the key.
        /// </summary>
        /// <remarks>
        /// Never null. A modal opened before the strings arrive shows its keys — ugly, and
        /// diagnosable at a glance, which a screen of blanks is not.
        /// </remarks>
        private Locale Words
        {
            get
            {
                if (_speech != null && _speech.Locale != null) return _speech.Locale;

                return _keys ?? (_keys = new Locale(Data.LocaleBook.Fallback,
                    new System.Collections.Generic.Dictionary<string, string>()));
            }
        }

        private Locale _keys;

        private void Told()
        {
            Action changed = Changed;
            if (changed != null) changed();
        }
    }
}
