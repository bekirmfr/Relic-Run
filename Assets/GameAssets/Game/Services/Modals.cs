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

        /// <summary>
        /// The relic pictures, or null.
        /// </summary>
        /// <remarks>
        /// Optional on purpose. A relic card without its icon still says everything a decision
        /// is made on; a card that refused to open because a sprite sheet was missing would take
        /// the decision away entirely.
        /// </remarks>
        private readonly Data.RelicIconBook _icons;

        /// <summary>The foe pictures, or null. Optional for the same reason the relics are.</summary>
        private readonly Data.EnemyBook _foes;

        /// <summary>Raised when something changed that the screen behind should redraw for.</summary>
        public event Action Changed;

        public Modals(IPopupService popups, SaveVault vault, Speech speech,
            Data.RelicIconBook icons, Data.EnemyBook foes)
        {
            _popups = popups;
            _vault = vault;
            _speech = speech;
            _icons = icons;
            _foes = foes;
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

        /// <summary>Opens one relic's card, for a delver holding nothing.</summary>
        /// <remarks>
        /// The relic book's way in, and the only way in until there is a run to hold anything.
        /// </remarks>
        public void Relic(RelicId relic)
        {
            Relic(relic, new RelicHolding());
        }

        /// <summary>Opens one relic's card, against what is held of it.</summary>
        public void Relic(RelicId relic, RelicHolding held)
        {
            RelicPopup popup = Open<RelicPopup>("relic card");

            if (popup == null) return;

            // The family chip goes one card deeper rather than replacing this one. The source
            // swaps them, because it keeps a single modal in its state; stacking is better and
            // costs nothing — closing the set card puts the delver back on the relic they were
            // reading, which is where they were going to look next anyway.
            popup.Opened += family => Set(family);

            popup.Show(RelicCards.Of(relic, held), Words, Picture(relic));
        }

        /// <summary>
        /// Opens a species dossier, as the bestiary opens it.
        /// </summary>
        /// <remarks>
        /// Every number a question mark, which is the honest answer: a rat in the first hall and
        /// a rat in the eighth are one species and not one fight.
        /// </remarks>
        public void Foe(int species)
        {
            Foe(EnemyCards.Dossier(species), species, 0);
        }

        /// <summary>Opens the card for THIS foe, as it stands in a fight.</summary>
        public void Foe(Core.Combat.EnemyState foe, int left)
        {
            if (foe == null) return;

            Foe(EnemyCards.Live(foe, left), foe.SpeciesIndex, foe.Variant);
        }

        private void Foe(EnemyCard card, int species, int variant)
        {
            EnemyPopup popup = Open<EnemyPopup>("foe card");

            if (popup == null) return;

            popup.Show(card, Words, _foes != null ? _foes.For(species, variant) : null);
        }

        /// <summary>
        /// Opens the supporter pack.
        /// </summary>
        /// <remarks>
        /// Reachable from the title, the profile and the result screen — three more places than
        /// any other modal, which is the source saying what it is for.
        ///
        /// What the buy button does is deliberately NOT decided here yet. A purchase is a store
        /// transaction with a receipt and a restore path, and the one thing this must never do is
        /// grant the pack because a button was pressed: that is a game that hands out a paid
        /// unlock to anybody who opens the modal. So the press is routed at the seam and says so
        /// in the log until <c>IPurchasingService</c> is wired, which is its own piece of work.
        /// </remarks>
        public void Supporter()
        {
            SupporterPopup popup = Open<SupporterPopup>("supporter pack");

            if (popup == null) return;

            popup.Bought += Buy;

            popup.Show(SupporterCards.Of(Chosen(), Earned()), Words);
        }

        /// <summary>
        /// Asks the store for the pack.
        /// </summary>
        /// <remarks>
        /// Nothing is granted here. The store answers a purchase asynchronously and its callback
        /// is what applies <see cref="Keep"/> — pressing a button is a REQUEST, and treating it
        /// as a receipt is how a paid unlock becomes free.
        /// </remarks>
        private void Buy()
        {
            Debug.LogWarning("the supporter pack cannot be bought yet: no purchasing service is " +
                             "wired, so nothing was charged and nothing was granted");
        }

        /// <summary>
        /// Grants the pack, once a store has actually said so.
        /// </summary>
        /// <remarks>
        /// Public because the caller is a purchase callback that does not exist yet, and a
        /// restore path that will call the same thing. Idempotent on purpose: a restore on a
        /// device that already has it must not hand over another three hundred sparks.
        /// </remarks>
        public void Keep()
        {
            if (_vault == null) return;
            if (_vault.Chosen.Supporter) return;

            _vault.Chosen.Supporter = true;
            _vault.Earned.Sparks += SupporterCards.Sparks;

            _vault.CommitChoices();
            _vault.CommitProgress();

            Told();
        }

        /// <summary>Opens a family's set card, for a delver holding nothing.</summary>
        public void Set(RelicKind family)
        {
            Set(family, null, CombatMode.Delve);
        }

        /// <summary>Opens a family's set card, against a hand.</summary>
        public void Set(RelicKind family, System.Collections.Generic.IReadOnlyList<RelicId> hand,
            CombatMode mode)
        {
            SetPopup popup = Open<SetPopup>("set card");

            if (popup == null) return;

            popup.Show(SetCards.Of(family, hand, mode), Words);
        }

        /// <summary>
        /// Makes one popup and shows it now, or says which one could not be made.
        /// </summary>
        /// <remarks>
        /// Both failures are silent otherwise, and both are the same mistake made in two places:
        /// a scene that never reached the application scope has no popup service at all, and a
        /// popup left out of PopupSettings is simply not created. The names in the log are what
        /// tells them apart without a debugger.
        ///
        /// Shown NOW, which is the whole reason this method takes an argument the service calls
        /// <c>forceShow</c>. Left to itself the service QUEUES a second popup behind the first:
        /// it is created, hidden at zero alpha, put at the bottom of the canvas, and shown only
        /// when whatever is already open closes. Pressing the family chip on a relic card
        /// therefore did nothing visible at all, and then produced a set card a moment after the
        /// delver dismissed the relic — an answer to a question they had stopped asking.
        ///
        /// Forcing it suspends the card underneath instead, and the service restores that card
        /// when this one closes. So a delver reading a relic, opening its family, and closing
        /// the family is back on the relic — which is where they were going to look next anyway.
        ///
        /// Appearing is the service's job once it is doing this, so nothing here calls
        /// <c>Appear</c>: the popup is created, shown, and only then filled in, all before the
        /// frame is drawn.
        /// </remarks>
        private T Open<T>(string what) where T : PopupBase
        {
            if (_popups == null)
            {
                Debug.LogWarning("nothing can open a popup, so the " + what + " cannot be read");
                return null;
            }

            T popup = _popups.Create<T>(true);

            if (popup == null)
            {
                Debug.LogError("the " + what + " is not registered with the popup service");
            }

            return popup;
        }

        /// <summary>A relic's picture, or null when there is no book of them.</summary>
        private UnityEngine.Sprite Picture(RelicId relic)
        {
            return _icons != null ? _icons.For(relic) : null;
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

        /// <summary>What the delver chose, or an empty set of choices.</summary>
        private Preferences Chosen()
        {
            return _vault != null ? _vault.Chosen : new Preferences();
        }

        /// <summary>What the delver earned, or an empty save.</summary>
        private Core.Meta.SaveState Earned()
        {
            return _vault != null ? _vault.Earned : new Core.Meta.SaveState();
        }

        private void Dress(SettingsPopup popup)
        {
            popup.Show(SettingsCards.Of(Chosen(), Shipped(), Words.Language), Words);
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
