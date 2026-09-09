using System.Collections.Generic;
using RelicRun.Core.Presentation;
using UnityEngine;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// A fighter's stats, one chip each.
    /// </summary>
    /// <remarks>
    /// Spawned once and updated in place, like the relic tray: the stats a fighter HAS do not
    /// change during a fight, only what they read. Rebuilding the row every event would remake
    /// four objects a couple of hundred times to draw the same four words.
    ///
    /// The hit points are not here. A pool is read as a proportion and belongs on a bar; these
    /// are read as figures and belong beside a label. That split is the arrangement rather than
    /// an omission, and it is why this takes <see cref="StatLine"/>s rather than a snapshot —
    /// what to show is Core's answer, and it does not include the pool.
    /// </remarks>
    public sealed class StatRow : MonoBehaviour
    {
        [SerializeField] private RectTransform _row;
        [SerializeField] private StatChip _chip;

        private readonly List<StatChip> _chips = new List<StatChip>();

        /// <summary>Draws these stats, making chips the first time and reusing them after.</summary>
        public void Show(IReadOnlyList<StatLine> lines)
        {
            if (_row == null || _chip == null || lines == null) return;

            while (_chips.Count < lines.Count)
            {
                StatChip made = Instantiate(_chip, _row);
                made.gameObject.SetActive(true);
                _chips.Add(made);
            }

            for (int i = 0; i < _chips.Count; i++)
            {
                if (_chips[i] == null) continue;

                // Hidden rather than destroyed. A fighter shows the same four stats all fight,
                // so the only way this list shrinks is a change nobody has made yet — and
                // destroying is the harder half to get right for a case that does not arise.
                _chips[i].gameObject.SetActive(i < lines.Count);

                if (i < lines.Count) _chips[i].Show(lines[i]);
            }
        }
    }
}
