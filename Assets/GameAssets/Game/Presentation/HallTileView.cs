using System;
using RelicRun.Core.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// One hall's square in the dungeon grid.
    /// </summary>
    /// <remarks>
    /// Four states and they are drawn with colour alone: sealed, open, cleared, and whichever one
    /// the delver is reading. No icons, because a lock and a tick at this size are four pixels of
    /// difference and the word underneath already says it.
    ///
    /// A BORDER and a fill, not one square. The first version used a single image for both and
    /// coloured it by state, which made the chosen tile gold — with its number, which is also
    /// gold, drawn on top of it and therefore invisible. Nothing in code could have said so; it
    /// took a screenshot of a running game. The source has it right: a dark tile with a coloured
    /// edge, so the edge can shout without swallowing what is written inside it.
    /// </remarks>
    public sealed class HallTileView : MonoBehaviour
    {
        [SerializeField] private Button _press;
        [SerializeField] private Image _frame;
        [SerializeField] private Image _fill;
        [SerializeField] private TMP_Text _number;
        [SerializeField] private TMP_Text _tag;

        /// <summary>The edge of the hall being read, which is the one that has to stand out.</summary>
        private static readonly Color ChosenEdge = new Color(0.89f, 0.70f, 0.25f);

        private static readonly Color OpenEdge = new Color(0.29f, 0.27f, 0.21f);

        private static readonly Color SealedEdge = new Color(0.17f, 0.15f, 0.11f);

        /// <summary>What is inside the edge. Always dark, so the number on it can be read.</summary>
        private static readonly Color ChosenFill = new Color(0.16f, 0.14f, 0.07f);

        private static readonly Color OpenFill = new Color(0.10f, 0.08f, 0.06f);

        private static readonly Color SealedFill = new Color(0.07f, 0.07f, 0.06f);

        /// <summary>A hall already beaten, in the green the game uses for something finished.</summary>
        private static readonly Color ClearedInk = new Color(0.49f, 0.60f, 0.42f);

        private static readonly Color OpenInk = new Color(0.90f, 0.87f, 0.80f);

        private static readonly Color SealedInk = new Color(0.29f, 0.27f, 0.21f);

        private int _tier;

        /// <summary>Raised with this tile's hall when it is pressed.</summary>
        public event Action<int> Picked;

        private void Awake()
        {
            if (_press == null) return;

            // Reads the tier at press time, because a tile is reused: the grid is built once and
            // redressed, so whatever was captured when the handler was attached is not what the
            // tile is showing now.
            _press.onClick.AddListener(() =>
            {
                Action<int> picked = Picked;
                if (picked != null) picked(_tier);
            });
        }

        /// <summary>Dresses this tile as one hall.</summary>
        public void Show(HallTile hall)
        {
            _tier = hall.Tier;

            // A sealed hall does not show its number. The delver knows how many halls there are
            // from the grid; what they do not get is which one this is, and the source keeps it
            // that way.
            if (_number != null)
            {
                _number.text = hall.Unlocked ? hall.Tier.ToString() : string.Empty;

                _number.color = hall.Chosen ? ChosenEdge
                    : hall.Cleared ? ClearedInk
                    : hall.Unlocked ? OpenInk : SealedInk;
            }

            if (_tag != null)
            {
                _tag.text = hall.Tag;
                _tag.color = hall.Cleared ? ClearedInk : SealedInk;
            }

            if (_frame != null)
            {
                _frame.color = hall.Chosen ? ChosenEdge : hall.Unlocked ? OpenEdge : SealedEdge;
            }

            if (_fill != null)
            {
                _fill.color = hall.Chosen ? ChosenFill : hall.Unlocked ? OpenFill : SealedFill;
            }

            // Pressable whether it is open or not: pressing a sealed hall is how a delver finds
            // out what would open it, and a tile that did nothing would read as broken.
            if (_press != null) _press.interactable = true;
        }
    }
}
