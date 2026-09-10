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
    /// A foe's card: what it is, what it has left, and what it is carrying.
    /// </summary>
    /// <remarks>
    /// Opened from the bestiary as a species dossier, and from a fight as this particular foe.
    /// The two share a shape and share nothing else, and the difference is entirely in what Core
    /// was handed — so this class does not know which it is showing, and does not need to.
    ///
    /// The name is translated. Everything else on it — the rank, the four stat labels, the line
    /// of lore — is English in all eight languages, because the source writes all of it as
    /// literals its translator never sees.
    /// </remarks>
    public sealed class EnemyPopup : PopupBase
    {
        /// <summary>What the popup service files this under.</summary>
        public const string Id = "enemy";

        public override string PopupId
        {
            get { return Id; }
        }

        [SerializeField] private TMP_Text _kicker;
        [SerializeField] private Image _art;
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _lore;

        [Tooltip("The four numbers along the top. One is spawned per stat.")]
        [SerializeField] private RectTransform _stats;

        [SerializeField] private TMP_Text _stat;

        [SerializeField] private TMP_Text _abilities;
        [SerializeField] private TMP_Text _relics;
        [SerializeField] private Button _close;

        /// <summary>What the abilities row is labelled. English, from the source.</summary>
        public const string AbilitiesLabel = "ABILITIES";

        /// <summary>And the row under it.</summary>
        public const string RelicsLabel = "RELICS";

        /// <summary>The red a foe's name is written in.</summary>
        private static readonly Color Blood = new Color(0.77f, 0.35f, 0.24f);

        private static readonly Color Faint = new Color(0.40f, 0.36f, 0.31f);

        private static readonly Color Plain = new Color(0.73f, 0.69f, 0.63f);

        private readonly List<TMP_Text> _numbers = new List<TMP_Text>();

        protected override void Awake()
        {
            base.Awake();

            if (_close != null) _close.onClick.AddListener(Disappear);
        }

        /// <summary>Puts one foe's card on the screen.</summary>
        /// <param name="art">Its picture, or null — the card reads without one.</param>
        public void Show(EnemyCard card, Locale words, Sprite art)
        {
            if (_art != null)
            {
                _art.sprite = art;
                _art.enabled = art != null;
            }

            if (_kicker != null)
            {
                _kicker.text = card.Kicker;
                _kicker.color = Ink(card.KickerHex, Faint);
            }

            if (_name != null)
            {
                _name.text = words != null ? words.Get(card.NameKey) : card.NameKey;
                _name.color = Blood;
            }

            if (_lore != null)
            {
                _lore.text = card.Lore ?? string.Empty;
                _lore.color = Faint;
            }

            Numbers(card.Stats);

            Put(_abilities, AbilitiesLabel, card.Abilities);
            Put(_relics, RelicsLabel, Carrying(card, words));
        }

        /// <summary>
        /// Which relics it carries, named in the delver's own language.
        /// </summary>
        /// <remarks>
        /// Nineteen of the fifty are translated and the rest are not, so the two are asked about
        /// separately — the same split the book and the relic card carry, for the same reason.
        /// </remarks>
        private static string Carrying(EnemyCard card, Locale words)
        {
            if (card.Relics == null || card.Relics.Count == 0) return card.NoRelics;

            var said = new StringBuilder();

            foreach (RelicId one in card.Relics)
            {
                RelicTextDef text = RelicText.Get(one);

                if (text == null) continue;

                if (said.Length > 0) said.Append(EnemyCards.Between);

                said.Append(text.NameKey != null && words != null
                    ? words.Get(text.NameKey)
                    : text.Name);
            }

            return said.Length > 0 ? said.ToString() : card.NoRelics;
        }

        /// <summary>The four numbers, spawned once and refilled after.</summary>
        private void Numbers(IReadOnlyList<FoeStat> stats)
        {
            if (_stat == null || _stats == null || stats == null) return;

            while (_numbers.Count < stats.Count)
            {
                TMP_Text made = Instantiate(_stat, _stats);

                made.gameObject.SetActive(true);
                _numbers.Add(made);
            }

            for (var i = 0; i < _numbers.Count; i++)
            {
                bool used = i < stats.Count;

                _numbers[i].gameObject.SetActive(used);

                if (!used) continue;

                // The label over the number, on one line, because four of these share the width
                // of the card and a stacked pair would be four columns two characters wide.
                _numbers[i].text = stats[i].Label + " " + stats[i].Value;
                _numbers[i].color = Plain;
            }
        }

        private static void Put(TMP_Text row, string label, string said)
        {
            if (row == null) return;

            row.text = label + "  " + (said ?? string.Empty);
        }

        /// <summary>A colour from the content tables, or a legible default.</summary>
        private static Color Ink(string hex, Color otherwise)
        {
            if (string.IsNullOrEmpty(hex)) return otherwise;

            Rgb rgb = Rgb.Parse(hex);

            return new Color(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f);
        }
    }
}
