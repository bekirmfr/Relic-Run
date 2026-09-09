using RelicRun.Core.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// One speck thrown off a body: grit from a blow, blood from a death.
    /// </summary>
    /// <remarks>
    /// A spawned Image rather than a particle system, and that is a departure from this project's
    /// own earlier note, which said UI-Particle. The reason is arithmetic: a blow throws four of
    /// these and a death throws eight. A particle system earns its keep in the hundreds, where
    /// batching and pooling are the difference between a frame and a stutter; at eight it is a
    /// material, a renderer and a simulation to configure for less work than the damage numbers
    /// already do beside it — and those are spawned Images, so this is the pattern already here
    /// rather than a second one.
    ///
    /// The scatter lives here for the same reason it lives in the flying numbers: motes landing
    /// in the same place read as one mote, and a view-model that rolled dice would make the same
    /// fight look different on replay, which is the one thing this whole layer exists to prevent.
    ///
    /// The ranges are the source's. Blood goes up and outward — it is thrown by the blow that
    /// landed — and dust only ever goes UP, because it is knocked off a body rather than out of
    /// it.
    /// </remarks>
    public sealed class Mote : MonoBehaviour
    {
        [SerializeField] private Image _speck;

        [Header("What each kind reads in")]
        [SerializeField] private Color _blood = new Color(0.68f, 0.16f, 0.14f);
        [SerializeField] private Color _dust = new Color(0.56f, 0.51f, 0.43f);

        [Header("The throw")]
        [Tooltip("Blood: the source scatters it 72 across and 56 up, offset to fall mostly upward.")]
        [SerializeField] private Vector2 _bloodSpread = new Vector2(72f, 56f);

        [SerializeField] private float _bloodRise = 16f;

        [Tooltip("Dust: narrower, and it only ever goes up.")]
        [SerializeField] private Vector2 _dustSpread = new Vector2(56f, 26f);

        [SerializeField] private float _dustRise = 6f;

        [Tooltip("How far a mote falls back before it is gone. Blood is heavier than grit.")]
        [SerializeField] private float _fall = 90f;

        [SerializeField] private float _seconds = 0.9f;

        /// <summary>How long a mote may wait before it sets off, so a burst is not one shape.</summary>
        [SerializeField] private float _stagger = 0.15f;

        private RectTransform _me;
        private Vector2 _from;
        private Vector2 _to;
        private float _born;
        private float _waits;

        /// <summary>Throws one speck and leaves it to get on with it.</summary>
        public void Throw(SprayKind kind, bool shattering)
        {
            _me = (RectTransform)transform;
            _from = _me.anchoredPosition;
            _born = Time.unscaledTime;

            bool blood = kind == SprayKind.Blood;

            Vector2 spread = blood ? _bloodSpread : _dustSpread;
            float rise = blood ? _bloodRise : _dustRise;

            // Blood is staggered and dust is not. A death is one moment and reads better as a
            // ragged one; a blow is already brief and grit that trickled would look like a leak.
            _waits = blood ? Random.value * _stagger : 0f;

            _to = new Vector2(
                _from.x + Random.Range(-spread.x, spread.x) * 0.5f,
                _from.y + rise + Random.Range(0f, spread.y));

            if (_speck != null)
            {
                _speck.color = blood ? _blood : _dust;

                // A shattering body throws bigger pieces of itself than a bleeding one throws
                // drops. It is the whole of what is left of the source's pixel pile.
                _me.localScale = Vector3.one * (shattering ? 2f : 1f);
            }
        }

        private void Update()
        {
            if (_me == null) return;

            float over = Time.unscaledTime - _born - _waits;

            if (over < 0f)
            {
                // Still waiting its turn, and not yet visible: a mote drawn at its start point
                // before it moves is a dot sitting on a body for a tenth of a second.
                if (_speck != null) _speck.enabled = false;
                return;
            }

            if (_speck != null) _speck.enabled = true;

            float part = Mathf.Clamp01(over / _seconds);

            // Out and up, then down. The arc is what makes it read as thrown rather than as
            // faded — a mote that only faded would look like the screen forgetting it.
            _me.anchoredPosition = Vector2.Lerp(_from, _to, part) +
                                   new Vector2(0f, -_fall * part * part);

            if (_speck != null)
            {
                Color shown = _speck.color;
                shown.a = 1f - part;
                _speck.color = shown;
            }

            if (part >= 1f) Destroy(gameObject);
        }
    }
}
