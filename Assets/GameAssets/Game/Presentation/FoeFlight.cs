using System;
using RelicRun.Core.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// Carries a foe from the card that announced it into the frame it will be fought in.
    /// </summary>
    /// <remarks>
    /// Half a second of pure joinery, and the only thing tying the announcement to the fight.
    /// Without it the card blinks out and a foe appears somewhere else at a quarter of the size,
    /// and a delver has to work out for themselves that they are the same creature.
    ///
    /// It draws a COPY rather than moving either of the two things it flies between, so neither
    /// the card nor the frame has to know this exists — and a flight interrupted halfway leaves
    /// both exactly where they were.
    /// </remarks>
    public sealed class FoeFlight : MonoBehaviour
    {
        [SerializeField] private Image _art;

        /// <summary>
        /// How much bigger than its landing size the foe swells on the way in.
        /// </summary>
        /// <remarks>
        /// The source's own overshoot: at sixty-two per cent of the way it is a third again too
        /// large, and it settles from there. A flight that only shrank would read as the foe
        /// retreating; swelling first reads as it coming at you.
        /// </remarks>
        private const float Swell = 1.35f;

        private const float Peak = 0.62f;

        private RectTransform _me;
        private Vector3 _from;
        private Vector3 _to;
        private float _big;
        private float _started;
        private float _over;
        private Action _landed;

        /// <summary>Whether something is in the air.</summary>
        public bool Flying
        {
            get { return _over > 0f; }
        }

        /// <summary>
        /// Sends a foe from one rect to another.
        /// </summary>
        /// <param name="landed">
        /// Run when it arrives, or at once when there is nothing to fly. Whoever is waiting to
        /// show the real foe hangs off this, so it must happen on every path or the frame stays
        /// empty for the rest of the fight.
        /// </param>
        public void Fly(Sprite drawn, RectTransform from, RectTransform to, float seconds,
            Action landed)
        {
            _me = (RectTransform)transform;

            bool possible = _art != null && drawn != null && from != null && to != null
                            && seconds > 0f && to.rect.width > 0f;

            if (!possible)
            {
                Stop();

                if (landed != null) landed();

                return;
            }

            _art.sprite = drawn;
            _art.enabled = true;

            _me.gameObject.SetActive(true);
            _me.sizeDelta = to.rect.size;

            _from = from.position;
            _to = to.position;

            // How much bigger the card's portrait is than the frame it is heading for, so the
            // copy leaves at exactly the size the delver was just looking at.
            _big = from.rect.width / to.rect.width;

            _started = Time.unscaledTime;
            _over = seconds;
            _landed = landed;

            Draw(0f);
        }

        /// <summary>Takes whatever is in the air out of it, without landing anything.</summary>
        public void Stop()
        {
            _over = 0f;
            _landed = null;

            if (_me == null) _me = (RectTransform)transform;

            _me.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_over <= 0f) return;

            float over = Mathf.Clamp01((Time.unscaledTime - _started) / _over);

            Draw(over);

            if (over < 1f) return;

            Action done = _landed;

            Stop();

            if (done != null) done();
        }

        /// <summary>
        /// Where it is, and how big, this far along.
        /// </summary>
        /// <remarks>
        /// The source's easing is a cubic that starts fast and settles slowly, which is what
        /// something thrown looks like. The size runs on its own curve so the swell can happen
        /// before the arrival rather than at it.
        /// </remarks>
        private void Draw(float over)
        {
            if (_me == null) return;

            float eased = 1f - Mathf.Pow(1f - over, 3f);

            _me.position = Vector3.Lerp(_from, _to, eased);

            float size = over < Peak
                ? Mathf.Lerp(_big, Swell, over / Peak)
                : Mathf.Lerp(Swell, 1f, (over - Peak) / (1f - Peak));

            _me.localScale = new Vector3(size, size, 1f);
        }
    }
}
