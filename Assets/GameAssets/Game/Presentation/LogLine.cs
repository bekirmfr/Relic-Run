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
        /// How far a chained line is pushed right, per level of depth.
        /// </summary>
        /// <remarks>
        /// The source indents by 14 pixels in a 390-wide shell. Sixteen units in a canvas near
        /// 440 wide is the same 3.6% of a line, and it is one em of the ui face, so a chained
        /// line starts exactly two characters in rather than at a measurement nobody can see
        /// the reasoning for.
        /// </remarks>
        private const float Step = 16f;

        /// <summary>
        /// How many levels of chain the indent distinguishes before it stops moving.
        /// </summary>
        /// <remarks>
        /// A delve caps chains at FORTY. Forty steps would be six hundred units of indent on a
        /// line four hundred wide, and the text would simply be gone. The corpus says the
        /// deepest chain any recorded fight actually reaches is three, so three is where the
        /// indent stops: it costs nothing in practice, and it means a pathological chain
        /// produces a crowded log rather than an empty one.
        /// </remarks>
        private const int Deepest = 3;

        /// <summary>Draws a line.</summary>
        public void Read(CombatLine line)
        {
            if (_text == null) return;

            _text.text = Arrow(line.Depth) + line.Text;
            _text.color = Of(line.Kind);

            // The indent carries the depth; the arrow only says there is one. TMP's margin
            // rather than the transform, because the log's layout group owns the width and
            // overwrites anything set on the rect.
            _text.margin = new Vector4(Indent(line.Depth), 0f, 0f, 0f);
        }

        /// <summary>
        /// One arrow, marking a line as chained, however deep the chain goes.
        /// </summary>
        /// <remarks>
        /// This used to repeat the arrow up to three times, which was ported from the wrong
        /// place: the source has two log renderers, and that one is the flat text export,
        /// where there is no indent to be had and the arrows are the only depth signal there
        /// is. The fight screen shows a SINGLE arrow and says the depth with padding.
        ///
        /// Doing both would encode the same fact twice — three arrows and three steps of
        /// indent, free to disagree the moment either cap moved.
        /// </remarks>
        private static string Arrow(int depth)
        {
            return depth > 0 ? "\u21b3 " : "";
        }

        private static float Indent(int depth)
        {
            if (depth <= 0) return 0f;

            return (depth < Deepest ? depth : Deepest) * Step;
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
