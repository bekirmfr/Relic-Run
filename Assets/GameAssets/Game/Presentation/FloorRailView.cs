using System.Collections.Generic;
using RelicRun.Core.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The rail across the top of a run: thirteen floors and where the delver is on them.
    /// </summary>
    /// <remarks>
    /// The only thing on the run screen that is about the DESCENT rather than about the floor
    /// underfoot. Everything else — the fight, the draft, the shelf — answers "what is happening
    /// now"; this answers "how much is left", which is the question a delver walks out on.
    ///
    /// It decides nothing. Which floors are marked and how big each is comes from
    /// <see cref="FloorRails"/>, so a node's size and what it means cannot come apart — what
    /// lives here is what the source keeps in CSS: which colours, and the thread between.
    /// </remarks>
    public sealed class FloorRailView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _line;

        [Tooltip("The nodes. One is spawned per floor.")]
        [SerializeField] private RectTransform _nodes;

        [SerializeField] private Image _node;

        /// <summary>Behind the delver: the thread and the floors they have walked.</summary>
        private static readonly Color Walked = new Color(0.55f, 0.51f, 0.45f);

        /// <summary>Underfoot.</summary>
        private static readonly Color Here = new Color(0.89f, 0.70f, 0.25f);

        /// <summary>And ahead, which is most of a run and is meant to look like nothing.</summary>
        private static readonly Color Ahead = new Color(0.14f, 0.12f, 0.09f);

        /// <summary>The violet an event in the gap is marked in.</summary>
        private static readonly Color Waiting = new Color(0.61f, 0.55f, 0.82f);

        private static readonly Color Met = new Color(0.29f, 0.27f, 0.38f);

        private readonly List<Image> _spawned = new List<Image>();

        /// <summary>Draws the rail for a run standing on a floor.</summary>
        public void Show(FloorRailCard card)
        {
            if (_line != null)
            {
                _line.text = "FLOOR " + card.Floor + " / " + card.Deepest;
                _line.color = Walked;
            }

            if (_node == null || _nodes == null || card.Floors == null) return;

            while (_spawned.Count < card.Floors.Count)
            {
                Image made = Instantiate(_node, _nodes);

                made.gameObject.SetActive(true);
                _spawned.Add(made);
            }

            for (var i = 0; i < _spawned.Count; i++)
            {
                bool used = i < card.Floors.Count;

                _spawned[i].gameObject.SetActive(used);

                if (!used) continue;

                Draw(_spawned[i], card.Floors[i]);
            }
        }

        /// <summary>One node, at the size and colour its floor has earned.</summary>
        private static void Draw(Image node, FloorNode floor)
        {
            var rect = (RectTransform)node.transform;

            rect.sizeDelta = new Vector2(floor.Side, floor.Side);

            node.color = floor.Here ? Here : floor.Walked ? Walked : Ahead;

            // The crown is turned on its corner, which is the source's way of saying the bottom
            // of a run does not look like the rest of it.
            rect.localRotation = floor.Mark == FloorMark.Crown
                ? Quaternion.Euler(0f, 0f, 45f)
                : Quaternion.identity;

            Dot(node, floor);
        }

        /// <summary>
        /// The event marker, which sits on the node AFTER the gap it waits in.
        /// </summary>
        /// <remarks>
        /// The first child of a node, made once and then only recoloured. It is drawn on the
        /// floor the delver arrives at rather than the one they leave, because that is where the
        /// gap is on the rail — the thread between two nodes — and putting it on the earlier one
        /// would show an event a floor from where it happens.
        /// </remarks>
        private static void Dot(Image node, FloorNode floor)
        {
            Transform found = node.transform.childCount > 0 ? node.transform.GetChild(0) : null;

            if (found == null) return;

            found.gameObject.SetActive(floor.Event);

            if (!floor.Event) return;

            var mark = found.GetComponent<Image>();

            if (mark != null) mark.color = floor.EventPassed ? Met : Waiting;
        }
    }
}
