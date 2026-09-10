using System;
using System.Collections.Generic;
using GameLift.Popup;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The settings: a language, a sound switch, and a name.
    /// </summary>
    /// <remarks>
    /// A modal rather than a screen, and through the package's <c>PopupService</c> rather than a
    /// panel of its own — settings are reachable from wherever a delver happens to be, and a
    /// screen would have to remember where that was in order to go back.
    ///
    /// Everything it SAYS is translated. That is not incidental: the language picker is the one
    /// control in the game whose whole purpose is to be usable by somebody who cannot read the
    /// language it is currently in, which is why the tongues show their own names rather than
    /// English ones.
    /// </remarks>
    public sealed class SettingsPopup : PopupBase
    {
        /// <summary>What the popup service files this under.</summary>
        public const string Id = "settings";

        public override string PopupId
        {
            get { return Id; }
        }

        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _languageTitle;
        [SerializeField] private RectTransform _tongues;
        [SerializeField] private Button _tongue;

        [SerializeField] private TMP_Text _soundLabel;
        [SerializeField] private Button _sound;

        [SerializeField] private TMP_Text _nameLabel;
        [SerializeField] private TMP_InputField _name;

        [SerializeField] private Button _close;

        private static readonly Color Chosen = new Color(0.89f, 0.70f, 0.25f);

        private static readonly Color Plain = new Color(0.73f, 0.69f, 0.63f);

        private readonly List<Button> _picked = new List<Button>();

        /// <summary>Raised with a language's tag when the delver picks one.</summary>
        public event Action<string> Chose;

        /// <summary>Raised when the sound switch is pressed.</summary>
        public event Action Switched;

        /// <summary>
        /// Raised with whatever is in the box when the delver leaves it.
        /// </summary>
        /// <remarks>
        /// On leaving rather than on every keystroke. The name is cleaned and may be REPLACED —
        /// an empty box becomes a generated name — and doing that mid-word would rewrite what
        /// somebody is halfway through typing.
        /// </remarks>
        public event Action<string> Renamed;

        protected override void Awake()
        {
            base.Awake();

            if (_sound != null) _sound.onClick.AddListener(() => Raise(Switched));
            if (_close != null) _close.onClick.AddListener(Disappear);

            if (_name != null)
            {
                _name.onEndEdit.AddListener(typed =>
                {
                    Action<string> named = Renamed;
                    if (named != null) named(typed);
                });
            }
        }

        /// <summary>Puts the delver's settings on the screen.</summary>
        public void Show(SettingsCard card, Locale words)
        {
            Put(_title, words, "settingsTitle");
            Put(_languageTitle, words, "languageTitle");
            Put(_nameLabel, words, "nameLabel");

            // The caption says what the row is; the BUTTON says what the sound is. That split is
            // the source's, and the port had it wrong: the state was written into the caption and
            // the button was left with no text at all — an empty bar under a line reading "Sound
            // off", which is a control nobody can predict and half a control besides.
            //
            // The switch says what the sound IS rather than what pressing it would do. Also the
            // source's, and the right way round for a control that shows its own state.
            Put(_soundLabel, words, "soundLabel");

            var state = _sound != null ? _sound.GetComponentInChildren<TMP_Text>(true) : null;

            if (state != null)
            {
                state.text = words != null
                    ? words.Get(card.Muted ? "soundOff" : "soundOn")
                    : card.Muted ? "soundOff" : "soundOn";

                state.color = card.Muted ? Plain : Chosen;
            }

            if (_name != null) _name.SetTextWithoutNotify(card.Name);

            Tongues(card.Tongues);
        }

        /// <summary>
        /// The language list, spawned once and redressed.
        /// </summary>
        /// <remarks>
        /// How many there are is the locale book's business rather than this screen's, so nothing
        /// here counts them: the list grows if a translation is added and no layout has to be
        /// told.
        /// </remarks>
        private void Tongues(IReadOnlyList<Tongue> tongues)
        {
            if (_tongue == null || _tongues == null || tongues == null) return;

            while (_picked.Count < tongues.Count)
            {
                Button made = Instantiate(_tongue, _tongues);

                made.gameObject.SetActive(true);
                _picked.Add(made);
            }

            for (var i = 0; i < _picked.Count; i++)
            {
                bool used = i < tongues.Count;

                _picked[i].gameObject.SetActive(used);
                if (!used) continue;

                Tongue tongue = tongues[i];
                var label = _picked[i].GetComponentInChildren<TMP_Text>(true);

                if (label != null)
                {
                    label.text = tongue.Label;
                    label.color = tongue.Chosen ? Chosen : Plain;
                }

                // Captured per button rather than read at press time, because unlike a
                // destination that changes with the clock, a tongue's tag is what this button IS.
                string code = tongue.Code;

                _picked[i].onClick.RemoveAllListeners();
                _picked[i].onClick.AddListener(() =>
                {
                    Action<string> chose = Chose;
                    if (chose != null) chose(code);
                });
            }
        }

        private static void Put(TMP_Text text, Locale words, string key)
        {
            if (text == null) return;

            text.text = words != null ? words.Get(key) : key;
        }

        private static void Raise(Action what)
        {
            if (what != null) what();
        }
    }
}
