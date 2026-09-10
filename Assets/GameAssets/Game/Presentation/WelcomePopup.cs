using System;
using GameLift.Popup;
using RelicRun.Core.Content;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The first thing a delver ever sees: what shall we call you.
    /// </summary>
    /// <remarks>
    /// Shown once, on the launch where the save has no name at all — which is what
    /// <c>Preferences.NeverAsked</c> exists to answer. A delver who was asked and cleared the box
    /// is NOT asked again: they said nothing on purpose, and a game that kept asking would be one
    /// refusing to take an answer.
    ///
    /// The box opens already filled. The source generates a name before showing this, so nobody
    /// is ever forced to invent one to get past it — the button says "enter the depths" rather
    /// than "save", and pressing it straight away is a complete answer.
    /// </remarks>
    public sealed class WelcomePopup : PopupBase
    {
        /// <summary>What the popup service files this under.</summary>
        public const string Id = "welcome";

        public override string PopupId
        {
            get { return Id; }
        }

        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _sub;
        [SerializeField] private TMP_InputField _name;
        [SerializeField] private Button _begin;

        /// <summary>Raised with whatever is in the box when the delver goes in.</summary>
        public event Action<string> Named;

        protected override void Awake()
        {
            base.Awake();

            if (_begin != null) _begin.onClick.AddListener(Begin);
        }

        /// <summary>Puts the welcome on the screen, with a name already in the box.</summary>
        /// <param name="suggested">
        /// A generated name. Filled in rather than left blank so the button is a complete answer
        /// on its own — an empty box with a button under it reads as a form that must be filled.
        /// </param>
        public void Show(string suggested, Locale words)
        {
            Put(_title, words, "welcomeTitle");
            Put(_sub, words, "welcomeSub");

            if (_begin != null)
            {
                var label = _begin.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = words != null ? words.Get("welcomeBegin") : "welcomeBegin";
            }

            if (_name != null) _name.SetTextWithoutNotify(suggested);
        }

        private void Begin()
        {
            Action<string> named = Named;

            if (named != null) named(_name != null ? _name.text : string.Empty);

            Disappear();
        }

        private static void Put(TMP_Text text, Locale words, string key)
        {
            if (text == null) return;

            text.text = words != null ? words.Get(key) : key;
        }
    }
}
