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
    /// What collecting a relic family is worth, and how far along it is.
    /// </summary>
    /// <remarks>
    /// One press from a relic's own card, which is where it earns its keep: a delver holding two
    /// Edge relics and offered a third can see what the third buys without leaving the draft.
    ///
    /// Opened from the relic book instead, nothing is held and every step reads as off. That is
    /// not a degraded case — it is the screen answering the other question it exists for, which
    /// is what a family would be worth before committing to one.
    /// </remarks>
    public sealed class SetPopup : PopupBase
    {
        /// <summary>What the popup service files this under.</summary>
        public const string Id = "set";

        public override string PopupId
        {
            get { return Id; }
        }

        [SerializeField] private TMP_Text _kicker;
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _count;

        [Tooltip("The steps. One is spawned per tier the family has.")]
        [SerializeField] private RectTransform _steps;

        [SerializeField] private TMP_Text _step;
        [SerializeField] private TMP_Text _members;
        [SerializeField] private Button _close;

        /// <summary>The line over the heading. English, from the source's own markup.</summary>
        public const string Kicker = "SET BONUS";

        /// <summary>What a step that has been reached is drawn in: the family's own colour.</summary>
        private static readonly Color Reached = new Color(0.90f, 0.87f, 0.80f);

        /// <summary>And one that has not: the same grey everything unearned is drawn in.</summary>
        private static readonly Color Short = new Color(0.40f, 0.36f, 0.31f);

        private readonly List<TMP_Text> _lines = new List<TMP_Text>();

        protected override void Awake()
        {
            base.Awake();

            if (_close != null) _close.onClick.AddListener(Disappear);
        }

        /// <summary>Puts one family's card on the screen.</summary>
        public void Show(SetCard card, Locale words)
        {
            Color family = Ink(card.Hex);

            if (_kicker != null) _kicker.text = Kicker;

            if (_name != null)
            {
                _name.text = card.Name;
                _name.color = family;
            }

            if (_count != null) _count.text = card.CountText;

            Steps(card, family);
            Members(card, words);
        }

        /// <summary>
        /// The steps, spawned once and refilled after.
        /// </summary>
        /// <remarks>
        /// Seven families have three and Curse has one, so the list is grown to whatever the
        /// family needs rather than assumed. A card that always drew three rows would invent two
        /// bonuses for Curse that no engine has heard of.
        /// </remarks>
        private void Steps(SetCard card, Color family)
        {
            if (_step == null || _steps == null || card.Steps == null) return;

            while (_lines.Count < card.Steps.Count)
            {
                TMP_Text made = Instantiate(_step, _steps);

                made.gameObject.SetActive(true);
                _lines.Add(made);
            }

            for (var i = 0; i < _lines.Count; i++)
            {
                bool used = i < card.Steps.Count;

                _lines[i].gameObject.SetActive(used);

                if (!used) continue;

                SetStep step = card.Steps[i];

                _lines[i].text = step.Tag + "  " + step.What;

                // A reached step is drawn in the family's own colour and an unreached one in the
                // grey everything unearned uses, so the card is read at a glance rather than by
                // counting brackets.
                _lines[i].color = step.On ? family : Short;
            }
        }

        /// <summary>Which relics of the family are in hand, named in the delver's language.</summary>
        private void Members(SetCard card, Locale words)
        {
            if (_members == null) return;

            var said = new StringBuilder();

            if (card.Members != null)
            {
                foreach (RelicId one in card.Members)
                {
                    if (said.Length > 0) said.Append(SetCards.Between);

                    RelicTextDef text = RelicText.Get(one);

                    if (text == null) continue;

                    said.Append(text.NameKey != null && words != null
                        ? words.Get(text.NameKey)
                        : text.Name);
                }
            }

            _members.text = said.ToString();
            _members.color = Reached;

            // Nothing held is a legitimate answer and an empty line under the steps is not, so
            // the row goes away rather than sitting there as a gap.
            _members.gameObject.SetActive(said.Length > 0);
        }

        /// <summary>A colour from the content tables, or a legible default.</summary>
        private static Color Ink(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Reached;

            Rgb rgb = Rgb.Parse(hex);

            return new Color(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f);
        }
    }
}
