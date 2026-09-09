using RelicRun.Core.Presentation;
using RelicRun.Core.Stats;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// One stat on screen: an icon, what it is called, and what it reads.
    /// </summary>
    /// <remarks>
    /// One object for every stat on either side, which is the whole reason it exists. The foe's
    /// stats were a single string of rich text with the colours spliced into it — quick to write,
    /// and a thing nothing could lay out, align, animate or reuse. Four of these in a row is the
    /// same information as a widget rather than as a sentence.
    ///
    /// The icon is real and currently unfilled. The source labels its stats with words and draws
    /// no glyph for them, so there is no art to bind — but the slot is here because a three-letter
    /// label in a pixel face is a compromise, and the day somebody draws five little icons the
    /// row should take them without being rebuilt.
    /// </remarks>
    public sealed class StatChip : MonoBehaviour
    {
        [Tooltip("Optional. Hidden while nothing is bound, which is every stat today.")]
        [SerializeField] private Image _icon;

        [SerializeField] private TMP_Text _label;
        [SerializeField] private TMP_Text _value;

        [Header("What each stat reads in")]
        [SerializeField] private Color _name = new Color(0.55f, 0.51f, 0.45f);
        [SerializeField] private Color _attack = new Color(0.91f, 0.88f, 0.82f);
        [SerializeField] private Color _defence = new Color(0.68f, 0.71f, 0.75f);
        [SerializeField] private Color _speed = new Color(0.49f, 0.60f, 0.42f);
        [SerializeField] private Color _luck = new Color(0.89f, 0.70f, 0.25f);

        /// <summary>Draws one stat.</summary>
        public void Show(StatLine line)
        {
            if (_label != null)
            {
                _label.text = line.Label;
                _label.color = _name;
            }

            if (_value != null)
            {
                _value.text = line.Value.ToString();
                _value.color = Of(line.Stat);
            }

            // The label is dimmed and the value is not, in every stat. A row of five names as
            // loud as their numbers is a row where the numbers have to be hunted for, and the
            // numbers are the only part that changes.
            if (_icon != null) _icon.enabled = _icon.sprite != null;
        }

        /// <summary>Gives this stat a picture, if it ever has one.</summary>
        public void Wear(Sprite icon)
        {
            if (_icon == null) return;

            _icon.sprite = icon;
            _icon.enabled = icon != null;
        }

        private Color Of(Stat stat)
        {
            switch (stat)
            {
                case Stat.Atk: return _attack;
                case Stat.Def: return _defence;
                case Stat.Spd: return _speed;
                case Stat.Lck: return _luck;
                default: return _name;
            }
        }
    }
}
