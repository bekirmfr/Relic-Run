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

        [Tooltip("The marker over the floor underfoot. Slides rather than jumps.")]
        [SerializeField] private RectTransform _arrow;

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

        private float _from;
        private float _to;
        private float _started;
        private float _over;

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

            // Measured NOW, not at the end of the frame. The marker is placed from where the
            // nodes actually are, and the nodes have just been resized — a marker placed before
            // the layout ran would sit over whichever floor was underfoot last time.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_nodes);

            Point(card.Floor, false);
        }

        /// <summary>
        /// Puts the marker over a floor at once, wherever it was.
        /// </summary>
        /// <remarks>
        /// The nodes resize as the delver walks — whichever is underfoot is drawn bigger — so
        /// every node's position moves whenever the rail is redrawn. The marker is therefore
        /// placed from where the node ACTUALLY IS after the layout has run, rather than from
        /// arithmetic over the sizes, which is what the source has to do in CSS.
        /// </remarks>
        public void Point(int floor, bool half)
        {
            _over = 0f;

            float x;

            if (!Marked(floor, half, out x)) return;

            Put(x);
        }

        /// <summary>
        /// Walks the marker to a floor over this long.
        /// </summary>
        /// <param name="half">
        /// Whether to stop in the GAP before it rather than on it. An event waits in the gap, and
        /// a delver walking into one has not arrived at the floor beyond it — so the marker stops
        /// between two nodes, which is exactly where the event's own mark is drawn.
        /// </param>
        public void Walk(int floor, bool half, float seconds)
        {
            float x;

            if (!Marked(floor, half, out x)) return;

            if (seconds <= 0f)
            {
                Point(floor, half);
                return;
            }

            _from = _arrow.position.x;
            _to = x;
            _started = Time.unscaledTime;
            _over = seconds;
        }

        /// <summary>Where the marker sits for a floor, in world x, or nothing to point at.</summary>
        private bool Marked(int floor, bool half, out float x)
        {
            x = 0f;

            if (_arrow == null) return false;

            int at = floor - 1;

            if (at < 0 || at >= _spawned.Count || !_spawned[at].gameObject.activeSelf) return false;

            x = _spawned[at].transform.position.x;

            if (!half) return true;

            // Halfway back toward the floor before it, which is where the gap is.
            int before = at - 1;

            if (before >= 0 && _spawned[before].gameObject.activeSelf)
            {
                x = (x + _spawned[before].transform.position.x) * 0.5f;
            }

            return true;
        }

        private void Put(float x)
        {
            if (_arrow == null) return;

            Vector3 at = _arrow.position;

            _arrow.position = new Vector3(x, at.y, at.z);
        }

        private void Update()
        {
            if (_over <= 0f) return;

            float over = Mathf.Clamp01((Time.unscaledTime - _started) / _over);

            Put(Mathf.Lerp(_from, _to, Ease(over)));

            if (over >= 1f) _over = 0f;
        }

        /// <summary>
        /// Ease in and out, the same cubic the hall walks on.
        /// </summary>
        /// <remarks>
        /// Deliberately the same shape and the same length as the descent behind it, because the
        /// two are one movement seen twice: the hall says a floor is being left and the marker
        /// says which one is being arrived at. Eased differently, they would drift apart in the
        /// middle and read as two things happening at once.
        /// </remarks>
        private static float Ease(float t)
        {
            return t < 0.5f
                ? 4f * t * t * t
                : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
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
