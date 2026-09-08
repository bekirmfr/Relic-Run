using RelicRun.Core.Presentation;
using TMPro;
using UnityEngine;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// One line of the combat log.
    /// </summary>
    /// <remarks>
    /// The colours live here rather than in <see cref="LineKind"/>, which names meanings instead.
    /// A view-model that handed out hex values would be choosing the game's palette from inside
    /// Core, and the palette is something a person changes by looking at it.
    /// </remarks>
    public sealed class LogLine : MonoBehaviour
    {
        [SerializeField] private TMP_Text _text;

        [Header("What each kind reads in")]
        [SerializeField] private Color _enter = new Color(0.85f, 0.80f, 0.70f);
        [SerializeField] private Color _you = new Color(0.90f, 0.88f, 0.82f);
        [SerializeField] private Color _chain = new Color(0.72f, 0.63f, 0.36f);
        [SerializeField] private Color _enemyChain = new Color(0.76f, 0.35f, 0.24f);
        [SerializeField] private Color _dim = new Color(0.47f, 0.44f, 0.38f);
        [SerializeField] private Color _hurt = new Color(0.77f, 0.35f, 0.24f);
        [SerializeField] private Color _good = new Color(0.45f, 0.70f, 0.45f);
        [SerializeField] private Color _gold = new Color(0.89f, 0.70f, 0.25f);
        [SerializeField] private Color _kill = new Color(0.89f, 0.83f, 0.60f);
        [SerializeField] private Color _death = new Color(0.70f, 0.20f, 0.20f);

        /// <summary>
        /// How many arrows a deeply chained line is prefixed with before it stops counting.
        /// </summary>
        /// <remarks>
        /// The source's number. A chain can reach depth forty at the cap, and forty arrows would
        /// be a line of arrows with a sentence after it.
        /// </remarks>
        private const int DeepestShown = 3;

        /// <summary>Draws a line.</summary>
        public void Read(CombatLine line)
        {
            if (_text == null) return;

            _text.text = Arrows(line.Depth) + line.Text;
            _text.color = Of(line.Kind);
        }

        private static string Arrows(int depth)
        {
            if (depth <= 0) return "";

            int shown = depth < DeepestShown ? depth : DeepestShown;
            return new string('\u21b3', shown) + " ";
        }

        private Color Of(LineKind kind)
        {
            switch (kind)
            {
                case LineKind.Enter: return _enter;
                case LineKind.You: return _you;
                case LineKind.Chain: return _chain;
                case LineKind.EnemyChain: return _enemyChain;
                case LineKind.Hurt: return _hurt;
                case LineKind.Good: return _good;
                case LineKind.Gold: return _gold;
                case LineKind.Kill: return _kill;
                case LineKind.Death: return _death;
                default: return _dim;
            }
        }
    }
}
