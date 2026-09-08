using RelicRun.Core.Presentation;
using TMPro;
using UnityEngine;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// A number flying up off somebody, and then gone.
    /// </summary>
    /// <remarks>
    /// The scatter is here rather than in the view-model, deliberately. Two numbers landing in
    /// the same place would overlap and read as one, so each is nudged — and a view-model that
    /// rolled dice would make the same fight look different on replay, which is the one thing
    /// this whole layer exists to prevent.
    /// </remarks>
    public sealed class FlyingNumber : MonoBehaviour
    {
        [SerializeField] private TMP_Text _text;

        [Header("What each kind reads in")]
        [SerializeField] private Color _damage = new Color(0.92f, 0.90f, 0.84f);
        [SerializeField] private Color _crit = new Color(1f, 0.85f, 0.35f);
        [SerializeField] private Color _chain = new Color(0.72f, 0.63f, 0.36f);
        [SerializeField] private Color _miss = new Color(0.55f, 0.52f, 0.46f);
        [SerializeField] private Color _heal = new Color(0.45f, 0.72f, 0.45f);
        [SerializeField] private Color _buff = new Color(0.40f, 0.66f, 0.78f);
        [SerializeField] private Color _hurt = new Color(0.80f, 0.30f, 0.22f);
        [SerializeField] private Color _gold = new Color(0.89f, 0.70f, 0.25f);

        [Header("The flight")]
        [SerializeField] private float _rise = 34f;
        [SerializeField] private float _seconds = 1.3f;
        [SerializeField] private Vector2 _scatter = new Vector2(48f, 14f);

        private RectTransform _me;
        private Vector2 _from;
        private float _born;

        /// <summary>Says a number and sets it going.</summary>
        public void Say(FlierKind kind, string what)
        {
            _me = (RectTransform)transform;

            _from = new Vector2(
                Random.Range(-_scatter.x, _scatter.x) * 0.5f,
                Random.Range(-_scatter.y, _scatter.y) * 0.5f);

            _me.anchoredPosition = _from;
            _born = Time.unscaledTime;

            if (_text == null) return;

            _text.text = what;
            _text.color = Of(kind);
        }

        private void Update()
        {
            if (_me == null) return;

            float lived = (Time.unscaledTime - _born) / Mathf.Max(0.01f, _seconds);
            if (lived >= 1f) { Destroy(gameObject); return; }

            _me.anchoredPosition = _from + new Vector2(0f, _rise * lived);

            if (_text != null)
            {
                Color colour = _text.color;

                // Fades over the last third, so a number is legible for most of its flight and
                // gone before the next one lands on top of it.
                colour.a = lived < 0.66f ? 1f : 1f - (lived - 0.66f) / 0.34f;
                _text.color = colour;
            }
        }

        private Color Of(FlierKind kind)
        {
            switch (kind)
            {
                case FlierKind.Crit: return _crit;
                case FlierKind.Chain: return _chain;
                case FlierKind.Miss: return _miss;
                case FlierKind.Heal: return _heal;
                case FlierKind.Buff: return _buff;
                case FlierKind.Hurt: return _hurt;
                case FlierKind.Gold: return _gold;
                default: return _damage;
            }
        }
    }
}
