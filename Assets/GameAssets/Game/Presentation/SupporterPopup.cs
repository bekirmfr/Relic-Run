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
    /// The supporter pack: what backing the game buys, and what it costs.
    /// </summary>
    /// <remarks>
    /// The only modal in the game whose every word is translated, and the only one that asks for
    /// money — which are related. A delver deciding whether to pay should be reading their own
    /// language, and the source agrees: title, blurb, all three perks, the price, the thank-you
    /// and the small print are locale keys without exception.
    ///
    /// It shows one of two faces. Not yet bought, it offers; already bought, it thanks and
    /// nothing on it can be pressed except the way out. That is not politeness — a card still
    /// offering a one-time purchase to somebody who has already made it is an offer that cannot
    /// be taken, and looks like a game that has lost the receipt.
    /// </remarks>
    public sealed class SupporterPopup : PopupBase
    {
        /// <summary>What the popup service files this under.</summary>
        public const string Id = "supporter";

        public override string PopupId
        {
            get { return Id; }
        }

        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _sub;

        [Tooltip("The perks. One is spawned per perk the pack has.")]
        [SerializeField] private RectTransform _perks;

        [SerializeField] private TMP_Text _perk;

        [SerializeField] private TMP_Text _price;
        [SerializeField] private TMP_Text _thanks;
        [SerializeField] private Button _buy;
        [SerializeField] private TMP_Text _fine;
        [SerializeField] private Button _close;

        private static readonly Color Gold = new Color(0.89f, 0.70f, 0.25f);

        private static readonly Color Plain = new Color(0.73f, 0.69f, 0.63f);

        private static readonly Color Faint = new Color(0.40f, 0.36f, 0.31f);

        private static readonly Color Kept = new Color(0.49f, 0.60f, 0.42f);

        private readonly List<TMP_Text> _lines = new List<TMP_Text>();

        /// <summary>Raised when the delver asks to buy it.</summary>
        public event Action Bought;

        protected override void Awake()
        {
            base.Awake();

            if (_close != null) _close.onClick.AddListener(Disappear);

            if (_buy != null)
            {
                _buy.onClick.AddListener(() =>
                {
                    Action bought = Bought;
                    if (bought != null) bought();
                });
            }
        }

        /// <summary>Puts the offer, or the thank-you, on the screen.</summary>
        public void Show(SupporterCard card, Locale words)
        {
            Put(_title, words, "supporterPack", Gold);
            Put(_sub, words, "supSub", Faint);
            Put(_price, words, "supPrice", Plain);
            Put(_fine, words, "supFine", Faint);

            Perks(card.Perks, words);

            // Exactly one of the two shows. Both hidden would be a card that says what the pack
            // does and never what to do about it; both shown would offer a one-time purchase to
            // somebody holding the receipt.
            if (_thanks != null)
            {
                Put(_thanks, words, "supOwnedNote", Kept);
                _thanks.gameObject.SetActive(card.Owned);
            }

            if (_buy != null)
            {
                var label = _buy.GetComponentInChildren<TMP_Text>(true);

                if (label != null) label.text = Say(words, "supBuy");

                _buy.gameObject.SetActive(!card.Owned);
            }

            if (_close != null)
            {
                var label = _close.GetComponentInChildren<TMP_Text>(true);

                // "Maybe later" while it is still an offer; once it is bought there is nothing to
                // be later about, so the way out says what it is.
                if (label != null) label.text = Say(words, card.Owned ? "close" : "maybeLater");
            }
        }

        /// <summary>The perks, spawned once and refilled after.</summary>
        private void Perks(IReadOnlyList<Perk> perks, Locale words)
        {
            if (_perk == null || _perks == null || perks == null) return;

            while (_lines.Count < perks.Count)
            {
                TMP_Text made = Instantiate(_perk, _perks);

                made.gameObject.SetActive(true);
                _lines.Add(made);
            }

            for (var i = 0; i < _lines.Count; i++)
            {
                bool used = i < perks.Count;

                _lines[i].gameObject.SetActive(used);

                if (!used) continue;

                // The name and its line together, because three of these share the card and a
                // stacked pair would need six rows to say what six words say.
                _lines[i].text = Say(words, perks[i].TitleKey) + "  " + Say(words, perks[i].WhatKey);
                _lines[i].color = Plain;
            }
        }

        private static void Put(TMP_Text text, Locale words, string key, Color ink)
        {
            if (text == null) return;

            text.text = Say(words, key);
            text.color = ink;
        }

        private static string Say(Locale words, string key)
        {
            return words != null ? words.Get(key) : key;
        }
    }
}
