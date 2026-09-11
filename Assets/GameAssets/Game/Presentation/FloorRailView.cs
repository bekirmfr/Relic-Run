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
    /// It decides nothing. Which floor is which, which gaps hold an event and which of those have
    /// been met is <see cref="FloorRails"/>' answer; what lives here is the drawing of it.
    ///
    /// DRAWN FROM ART, not from rectangles. Every state has a sprite off one sheet — a plain
    /// floor, the bazaar's awning, the crown, an event's diamond, and the marker over wherever
    /// the delver is standing — each in the two or three states it can be in. That is why
    /// <see cref="FloorNode.Side"/> goes unused here: it records the SOURCE's sizing, which was
    /// CSS, and the port's rail is sized by the pixels somebody drew.
    /// </remarks>
    public sealed class FloorRailView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _line;

        [Tooltip("The nodes. One is spawned per floor.")]
        [SerializeField] private RectTransform _nodes;

        [SerializeField] private Image _node;

        [Tooltip("The marker over the floor underfoot. Slides rather than jumps.")]
        [SerializeField] private RectTransform _arrow;

        [Header("A floor")]
        [SerializeField] private Sprite _floorWalked;
        [SerializeField] private Sprite _floorHere;
        [SerializeField] private Sprite _floorAhead;

        [Header("The bazaar")]
        [SerializeField] private Sprite _shopWalked;
        [SerializeField] private Sprite _shopHere;
        [SerializeField] private Sprite _shopAhead;

        [Header("The crown")]
        [Tooltip("No walked crown: a run that reaches the bottom is over.")]
        [SerializeField] private Sprite _crownHere;

        [SerializeField] private Sprite _crownAhead;

        [Header("An event, in the gap before a floor")]
        [SerializeField] private Sprite _eventPassed;

        [Tooltip("The one the delver is about to walk into.")]
        [SerializeField] private Sprite _eventNext;

        [SerializeField] private Sprite _eventAhead;

        [Header("Size")]
        [Tooltip("How many screen units one drawn pixel takes.")]
        [SerializeField] private int _scale = 3;

        [Tooltip("The narrowest a gap between two floors may be squeezed to.")]
        [SerializeField] private float _spacing = 6f;

        [Tooltip("How much bare panel is left at each end of the rail.")]
        [SerializeField] private int _inset = 16;

        private readonly List<Image> _spawned = new List<Image>();

        private float _from;
        private float _to;
        private float _started;
        private float _over;

        /// <summary>Draws the rail for a run standing on a floor.</summary>
        public void Show(FloorRailCard card)
        {
            if (_line != null) _line.text = "FLOOR " + card.Floor + " / " + card.Deepest;

            if (_node == null || _nodes == null || card.Floors == null) return;

            Row();

            while (_spawned.Count < card.Floors.Count)
            {
                Image made = Instantiate(_node, _nodes);

                made.gameObject.SetActive(true);
                _spawned.Add(made);
            }

            var wide = 0f;

            for (var i = 0; i < _spawned.Count; i++)
            {
                bool used = i < card.Floors.Count;

                _spawned[i].gameObject.SetActive(used);

                if (!used) continue;

                wide += Draw(_spawned[i], card.Floors[i], card.Floor);
            }

            // The gaps are whatever is LEFT once the floors have had their pixels, which is what
            // lets the rail be as wide as the scene says without anything here knowing how wide
            // that is. Then the events are placed, because where an event sits is half a gap.
            float gap = Gap(wide, card.Floors.Count);

            for (var i = 0; i < _spawned.Count && i < card.Floors.Count; i++)
            {
                Place(_spawned[i], card.Floors[i], gap);
            }

            // Measured NOW, not at the end of the frame. The marker is placed from where the
            // nodes actually are, and a marker placed before the layout ran would sit over
            // whichever floor was underfoot last time.
            LayoutRebuilder.ForceRebuildLayoutImmediate(_nodes);

            Point(card.Floor, false);
        }

        /// <summary>
        /// Sets the row to space the nodes and to leave their SIZE alone.
        /// </summary>
        /// <remarks>
        /// Both halves matter. The spacing is authored rather than computed, and the diamond's
        /// offset is computed from it, so widening the rail in the scene moves the events with it
        /// instead of leaving them stranded between the wrong pair.
        ///
        /// And a layout group that controls its children's size will happily stretch a nine-pixel
        /// crown into a two-by-thirty smear — which is exactly what it did the first time this
        /// drew. The sizes here come from the ART, so the row is told to keep its hands off them
        /// every time the rail is drawn rather than trusting whatever the scene was last saved
        /// with.
        /// </remarks>
        private void Row()
        {
            var row = _nodes.GetComponent<HorizontalLayoutGroup>();

            if (row == null) return;

            row.childControlWidth = false;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.padding = new RectOffset(_inset, _inset, 0, 0);
        }

        /// <summary>
        /// How wide each gap between two floors comes out, given the room left over.
        /// </summary>
        /// <remarks>
        /// Computed rather than authored, which is the difference between a rail that fits the
        /// panel somebody drew it into and one that runs off the end of it. The floors take their
        /// pixels first — they are art and they are not negotiable — and the gaps divide whatever
        /// remains.
        ///
        /// Floored at <c>_spacing</c>, so a rail squeezed narrower than its own contents runs
        /// over the edge rather than stacking thirteen floors on top of each other. Overflowing
        /// is visible; overlapping looks like one floor.
        /// </remarks>
        private float Gap(float taken, int floors)
        {
            var row = _nodes.GetComponent<HorizontalLayoutGroup>();

            int gaps = floors - 1;

            if (gaps <= 0) return _spacing;

            float room = _nodes.rect.width - _inset * 2f - taken;

            // Never narrower than the mark that sits in it. A gap squeezed under an event's own
            // diamond draws the diamond over the two floors either side of it, which reads as
            // three things in one place rather than as one thing between two.
            float least = _eventAhead != null
                ? Mathf.Max(_spacing, _eventAhead.rect.width * _scale + 2f)
                : _spacing;

            float gap = Mathf.Max(least, room / gaps);

            if (row != null) row.spacing = gap;

            return gap;
        }

        /// <summary>One node: whichever sprite says what this floor is and where the delver is.</summary>
        /// <returns>How wide it came out, so the gaps can divide what is left.</returns>
        private float Draw(Image node, FloorNode floor, int standing)
        {
            Sprite drawn = Face(floor);

            node.sprite = drawn;
            node.enabled = drawn != null;
            node.color = Color.white;

            var rect = (RectTransform)node.transform;

            // The art's own size, scaled. A bazaar is wider than a floor and a crown is wider
            // than both, and that is the sheet's decision rather than this file's.
            rect.sizeDelta = drawn != null
                ? new Vector2(drawn.rect.width * _scale, drawn.rect.height * _scale)
                : new Vector2(_scale * 3f, _scale * 3f);

            // Nothing is turned on its corner any more. The crown is a crown.
            rect.localRotation = Quaternion.identity;

            Dot(node, floor, standing);

            return rect.sizeDelta.x;
        }

        /// <summary>Puts a floor's event mark in the middle of the gap before it.</summary>
        private void Place(Image node, FloorNode floor, float gap)
        {
            if (!floor.Event) return;

            Transform found = node.transform.childCount > 0 ? node.transform.GetChild(0) : null;

            if (found == null) return;

            var rect = (RectTransform)found;
            var mine = (RectTransform)node.transform;

            // Half a floor and half a gap to the left, which is the middle of the thread between
            // this floor and the one before it.
            rect.anchoredPosition = new Vector2(-(mine.sizeDelta.x + gap) * 0.5f, 0f);
        }

        /// <summary>Which face this floor wears.</summary>
        /// <remarks>
        /// Three kinds, and two or three states each. A crown has no walked state on purpose: a
        /// delver standing past floor thirteen has finished the run, so the rail is gone.
        /// </remarks>
        private Sprite Face(FloorNode floor)
        {
            switch (floor.Mark)
            {
                case FloorMark.Crown:
                    return floor.Here ? _crownHere : _crownAhead;

                case FloorMark.Bazaar:
                    return floor.Here ? _shopHere : floor.Walked ? _shopWalked : _shopAhead;

                default:
                    return floor.Here ? _floorHere : floor.Walked ? _floorWalked : _floorAhead;
            }
        }

        /// <summary>
        /// The event marker, in the GAP before a floor rather than on it.
        /// </summary>
        /// <remarks>
        /// The first child of a node, made once and then only re-sprited. It is drawn on the
        /// floor the delver arrives at rather than the one they leave, and then pushed back into
        /// the gap — because that is where the event happens, and hanging it on either node
        /// would claim it belongs to a floor.
        /// </remarks>
        private void Dot(Image node, FloorNode floor, int standing)
        {
            Transform found = node.transform.childCount > 0 ? node.transform.GetChild(0) : null;

            if (found == null) return;

            found.gameObject.SetActive(floor.Event);

            if (!floor.Event) return;

            var mark = found.GetComponent<Image>();

            if (mark == null) return;

            // The one in the gap the delver is about to walk into is picked out, because that is
            // the only event on the rail they are about to have to answer.
            bool next = !floor.EventPassed && floor.Floor == standing + 1;

            Sprite drawn = floor.EventPassed ? _eventPassed : next ? _eventNext : _eventAhead;

            mark.sprite = drawn;
            mark.enabled = drawn != null;
            mark.color = Color.white;

            var rect = (RectTransform)found;

            rect.sizeDelta = drawn != null
                ? new Vector2(drawn.rect.width * _scale, drawn.rect.height * _scale)
                : rect.sizeDelta;
        }

        /// <summary>
        /// Puts the marker over a floor at once, wherever it was.
        /// </summary>
        /// <remarks>
        /// The marker is placed from where the node ACTUALLY IS after the layout has run, rather
        /// than from arithmetic over the sizes — the nodes are different widths and the art is
        /// free to change them.
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
    }
}
