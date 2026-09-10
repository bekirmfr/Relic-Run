using System;
using System.Collections.Generic;
using System.Text;
using GameLift.Popup;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// One relic's dossier: what it is, what it does, and what it would become.
    /// </summary>
    /// <remarks>
    /// The only screen in the game a DECISION is made on. Everywhere else reports something that
    /// has already happened; here a delver chooses between three relics and then plays fifty
    /// floors on the choice, so what this says has to be exactly what the engine will do.
    ///
    /// It is reachable from the relic book with nothing held, and from a run with a copy in hand.
    /// Both cases are one <see cref="RelicCard"/> — the difference is entirely in what Core was
    /// told, which is why this class has no idea whether there is a run.
    ///
    /// Its prose is English in every language. Two of its lines are not: the name and the
    /// description are translated for nineteen of the fifty relics, and the card carries the key
    /// rather than the word so that this can ask.
    /// </remarks>
    public sealed class RelicPopup : PopupBase
    {
        /// <summary>What the popup service files this under.</summary>
        public const string Id = "relic";

        public override string PopupId
        {
            get { return Id; }
        }

        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _flavour;
        [SerializeField] private TMP_Text _what;
        [SerializeField] private TMP_Text _promise;

        [Tooltip("The labelled rule rows. One is spawned per row the card has.")]
        [SerializeField] private RectTransform _rows;

        [SerializeField] private TMP_Text _row;

        [Tooltip("The family chip. Pressing it opens that family's set card.")]
        [SerializeField] private Button _family;

        [SerializeField] private TMP_Text _footer;
        [SerializeField] private Button _close;

        private readonly List<TMP_Text> _lines = new List<TMP_Text>();

        /// <summary>Raised with the family whose chip was pressed.</summary>
        public event Action<RelicKind> Opened;

        protected override void Awake()
        {
            base.Awake();

            if (_close != null) _close.onClick.AddListener(Disappear);
        }

        /// <summary>Puts one relic's card on the screen.</summary>
        /// <param name="icon">Its picture, or null — the card reads without one.</param>
        public void Show(RelicCard card, Locale words, Sprite icon)
        {
            if (_icon != null)
            {
                _icon.sprite = icon;
                _icon.enabled = icon != null;
            }

            Put(_name, card.Mark + Word(card.NameKey, card.Name, words));
            Put(_flavour, card.Flavour);
            Put(_what, Describes(card, words));
            Put(_promise, card.AwakeLine);

            Rows(card.Rows);
            Family(card);
            Put(_footer, Footer(card, words));
        }

        /// <summary>
        /// The description, with whatever awakening and socketing have added to it.
        /// </summary>
        /// <remarks>
        /// Joined here rather than in Core because the middle of it may be a KEY, and Core does
        /// not pick a language. The two ends are English whatever happens — nobody translated
        /// the awakening verbs or the socket catalogue — so this is one sentence in up to two
        /// languages, which is the source's behaviour and looks like exactly what it is.
        /// </remarks>
        private static string Describes(RelicCard card, Locale words)
        {
            var said = new StringBuilder();

            if (card.Awoke != null) said.Append(card.Awoke);

            said.Append(Word(card.WhatKey, card.What, words));

            if (card.SocketNote != null) said.Append(card.SocketNote);

            return said.ToString();
        }

        /// <summary>
        /// The last line: which modes it appears in, what it converts, how many are held.
        /// </summary>
        /// <remarks>
        /// Three small facts on one line rather than three rows, because none of them is worth a
        /// row of its own and all three are worth knowing. The count is the only one that is
        /// ever absent, and it is absent whenever it would say "1".
        /// </remarks>
        private static string Footer(RelicCard card, Locale words)
        {
            var said = new StringBuilder();

            if ((card.Modes & GameModes.Delve) != 0) said.Append(RelicCards.DelveWord);

            if ((card.Modes & GameModes.Versus) != 0)
            {
                if (said.Length > 0) said.Append("   ");
                said.Append(RelicCards.VersusWord);
            }

            if (card.Wheel != null)
            {
                said.Append("   ").Append(card.Wheel[0]).Append(" → ").Append(card.Wheel[1]);
            }

            if (card.ShowsOwned && words != null)
            {
                said.Append("   ").Append(words.Get("ownedTimes",
                    new Dictionary<string, string> { { "n", card.Owned.ToString() } }));
            }

            return said.ToString();
        }

        /// <summary>The rule rows, spawned once and refilled after.</summary>
        private void Rows(IReadOnlyList<RelicRow> rows)
        {
            if (_row == null || _rows == null || rows == null) return;

            while (_lines.Count < rows.Count)
            {
                TMP_Text made = Instantiate(_row, _rows);

                made.gameObject.SetActive(true);
                _lines.Add(made);
            }

            for (var i = 0; i < _lines.Count; i++)
            {
                bool used = i < rows.Count;

                _lines[i].gameObject.SetActive(used);

                if (!used) continue;

                // The label and the sentence on one line, because the label is two words and a
                // column of them would take a third of the card's width to say PASSIVE.
                _lines[i].text = RelicCards.Label(rows[i].Kind) + "  " + rows[i].Text;
                _lines[i].color = Ink(RelicCards.Hex(rows[i].Kind));
            }
        }

        /// <summary>The family chip, coloured for the family and pointing at its card.</summary>
        private void Family(RelicCard card)
        {
            if (_family == null) return;

            var label = _family.GetComponentInChildren<TMP_Text>(true);

            if (label != null)
            {
                label.text = card.FamilyName;
                label.color = Ink(card.FamilyHex);
            }

            RelicKind family = card.Family;

            _family.onClick.RemoveAllListeners();
            _family.onClick.AddListener(() =>
            {
                Action<RelicKind> opened = Opened;
                if (opened != null) opened(family);
            });
        }

        /// <summary>A translated word, or the English one for a relic nobody translated.</summary>
        private static string Word(string key, string english, Locale words)
        {
            if (key == null) return english ?? string.Empty;

            return words != null ? words.Get(key) : key;
        }

        /// <summary>
        /// A colour from the content tables, or a legible default.
        /// </summary>
        /// <remarks>
        /// The tables are hex strings because Core has no Unity types. Parsing here rather than
        /// keeping a second table of Colors is what stops the two drifting — a family whose
        /// colour changed in the source would change here and nowhere else.
        /// </remarks>
        private static Color Ink(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return new Color(0.90f, 0.87f, 0.80f);

            Rgb rgb = Rgb.Parse(hex);

            return new Color(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f);
        }

        private static void Put(TMP_Text text, string said)
        {
            if (text == null) return;

            text.text = said ?? string.Empty;

            // Hidden rather than blank, so the rows above and below close up. An empty line in
            // the middle of a card reads as something that failed to load.
            text.gameObject.SetActive(!string.IsNullOrEmpty(said));
        }
    }
}
