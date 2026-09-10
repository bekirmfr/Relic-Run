using System.Collections.Generic;
using RelicRun.Core.Presentation;
using RelicRun.Game.Data;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The row of foes on a floor: who is up, who is down, and how many are left.
    /// </summary>
    /// <remarks>
    /// The one thing on a fight screen that is about the FLOOR rather than about the blow being
    /// struck, and the reason a delver holds a relic charge instead of spending it. Which tile is
    /// which comes from <see cref="FoeQueues"/>; what lives here is the drawing.
    ///
    /// Hidden on a floor with one foe, because a row of one repeats the picture in the middle of
    /// the screen — and most floors of a run are exactly that, so the row appears when a floor is
    /// worth worrying about and not before.
    /// </remarks>
    public sealed class FoeQueueView : MonoBehaviour
    {
        [Tooltip("The word in front of the row. Hidden with it.")]
        [SerializeField] private GameObject _label;

        [Tooltip("The tiles. One is spawned per foe on the floor.")]
        [SerializeField] private RectTransform _tiles;

        [SerializeField] private Image _tile;

        [SerializeField] private GameContent _content;

        /// <summary>How dark a foe still ahead is drawn.</summary>
        /// <remarks>
        /// The source's <c>brightness(.45)</c>, which is not something a UI Image can do — so it
        /// is a multiply on the tint instead. Near enough on a dark ground, and it costs no
        /// material.
        /// </remarks>
        private const float Ahead = 0.45f;

        /// <summary>And how faded a fallen one is.</summary>
        private const float Fallen = 0.3f;

        /// <summary>The ground every tile is drawn on.</summary>
        private static readonly Color Ground = new Color(0.098f, 0.082f, 0.063f);

        /// <summary>The colour a dead foe is struck out in.</summary>
        private static readonly Color Struck = new Color(0.769f, 0.349f, 0.235f);

        private readonly List<Image> _spawned = new List<Image>();

        /// <summary>Draws the row for a pack this far along.</summary>
        public void Show(FoeQueueCard card)
        {
            gameObject.SetActive(card.Shown);

            if (!card.Shown || _tile == null || _tiles == null || card.Foes == null) return;

            if (_label != null) _label.SetActive(true);

            while (_spawned.Count < card.Foes.Count)
            {
                Image made = Instantiate(_tile, _tiles);

                made.gameObject.SetActive(true);
                _spawned.Add(made);
            }

            for (var i = 0; i < _spawned.Count; i++)
            {
                bool used = i < card.Foes.Count;

                _spawned[i].gameObject.SetActive(used);

                if (used) Draw(_spawned[i], card.Foes[i]);
            }
        }

        /// <summary>Takes the row off the screen, whatever it was showing.</summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// One tile: a ring, a foe, and a line through it if the foe is dead.
        /// </summary>
        /// <remarks>
        /// The ring is the tile's own Image and the two things inside it are its children, in the
        /// order the builder makes them — the picture first, the strike over it. Found by index
        /// rather than by name for the same reason the draft's card lines are: a name is a string
        /// somebody can rename in the editor without the code noticing.
        /// </remarks>
        private void Draw(Image tile, FoeTile foe)
        {
            var rect = (RectTransform)tile.transform;

            rect.sizeDelta = new Vector2(foe.Side, foe.Side);

            tile.color = Ground;

            Ring(tile, foe);

            Transform art = tile.transform.childCount > 0 ? tile.transform.GetChild(0) : null;
            Transform line = tile.transform.childCount > 1 ? tile.transform.GetChild(1) : null;

            if (art != null) Art(art.GetComponent<Image>(), foe);

            if (line != null)
            {
                line.gameObject.SetActive(foe.Struck);

                var ink = line.GetComponent<Image>();

                if (ink != null) ink.color = Struck;
            }
        }

        /// <summary>The tile's border, which is an outline component rather than a border.</summary>
        /// <remarks>
        /// A UI Image has no border. An <see cref="Outline"/> draws four offset copies of the
        /// graphic underneath it, which for a flat fill is indistinguishable from one — and, being
        /// on the tile itself, it scales with the tile rather than needing a second object.
        /// </remarks>
        private static void Ring(Image tile, FoeTile foe)
        {
            var edge = tile.GetComponent<Outline>();

            if (edge == null) return;

            Color drawn;

            edge.effectColor = ColorUtility.TryParseHtmlString(foe.Hex, out drawn) ? drawn : Ground;
            edge.effectDistance = new Vector2(foe.Edge, foe.Edge);
        }

        /// <summary>The foe on the tile, dimmed by how much of it is still to come.</summary>
        private void Art(Image art, FoeTile foe)
        {
            if (art == null) return;

            Sprite drawn = _content != null && _content.Enemies != null
                ? _content.Enemies.For(foe.Species, foe.Variant)
                : null;

            art.sprite = drawn;
            art.enabled = drawn != null;

            var rect = (RectTransform)art.transform;

            rect.sizeDelta = new Vector2(FoeQueues.Glyph, FoeQueues.Glyph);

            float lit = foe.Struck ? Fallen : foe.Dim ? Ahead : 1f;

            // Alpha for the fallen and a darker tint for what is still ahead. The two read
            // differently on purpose: something behind the delver is FADING, and something ahead
            // of them is in the dark.
            art.color = foe.Struck
                ? new Color(1f, 1f, 1f, lit)
                : new Color(lit, lit, lit, 1f);
        }
    }
}
