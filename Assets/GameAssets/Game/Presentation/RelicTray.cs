using System.Collections.Generic;
using RelicRun.Core.Combat;
using RelicRun.Core.Presentation;
using RelicRun.Game.Data;
using UnityEngine;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The delver's shelf, along the bottom of the fight.
    /// </summary>
    /// <remarks>
    /// One slot per COPY, in inventory order, because that is the order sockets are keyed by and
    /// a tray that collapsed duplicates would have to pick which of two Whetstones to lie about.
    ///
    /// Laid out once and updated in place. The shelf is settled before the first tick — nothing
    /// is picked up or dropped mid-floor — so spawning is a <see cref="Begin"/> concern and the
    /// only thing that happens per event is that numbers move. A tray that rebuilt itself every
    /// beat would throw away and remake a dozen objects two hundred times a fight, and would lose
    /// the flash it was in the middle of drawing.
    /// </remarks>
    public sealed class RelicTray : MonoBehaviour
    {
        [SerializeField] private RectTransform _row;
        [SerializeField] private RelicSlot _slot;
        [SerializeField] private GameContent _content;

        private readonly List<RelicSlot> _slots = new List<RelicSlot>();
        private Shelf _shelf;
        private bool _versus;

        /// <summary>Lays out one slot per copy on the shelf.</summary>
        public void Begin(Shelf shelf, bool versus)
        {
            _shelf = shelf;
            _versus = versus;

            foreach (RelicSlot slot in _slots)
            {
                if (slot != null) Destroy(slot.gameObject);
            }

            _slots.Clear();

            // An empty shelf leaves an empty tray rather than a gap where one should be. A delve
            // starts with nothing, so this is the ordinary case for the first floor and not a
            // sign that anything went wrong.
            if (shelf == null || _row == null || _slot == null) return;

            for (int index = 0; index < shelf.Count; index++)
            {
                RelicSlot made = Instantiate(_slot, _row);
                made.gameObject.SetActive(true);

                RelicCopy copy = shelf[index];
                made.Bind(copy, Icon(copy));

                _slots.Add(made);
            }
        }

        /// <summary>Updates every slot to the moment this event happened in.</summary>
        public void Show(CombatEvent shown)
        {
            if (_shelf == null) return;

            for (int index = 0; index < _slots.Count && index < _shelf.Count; index++)
            {
                if (_slots[index] == null) continue;

                _slots[index].Show(RelicMeter.For(_shelf, index, shown.State, _versus));
            }

            // The copy that fired, not the relic. An event carries an inventory index precisely
            // so that one of two identical icons can light and the other stay dark.
            if (shown.RelicSlot.HasValue) Fire(shown.RelicSlot.Value);
        }

        private void Fire(int index)
        {
            if (index < 0 || index >= _slots.Count) return;
            if (_slots[index] == null) return;

            _slots[index].Fire();
        }

        private Sprite Icon(RelicCopy copy)
        {
            if (_content == null || _content.RelicIcons == null) return null;

            return _content.RelicIcons.For(copy.Relic);
        }
    }
}
