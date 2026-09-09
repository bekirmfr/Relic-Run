using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// An attack gauge, filling over exactly the time until its owner strikes.
    /// </summary>
    /// <remarks>
    /// The bar arriving full and the blow landing are meant to be the same moment. That is not
    /// decoration: it is the only thing on screen that tells a delver whose turn is coming, and a
    /// bar that fills at its own pace would be a clock showing the wrong time in the one place
    /// somebody is reading it.
    ///
    /// So the duration comes from <see cref="Core.Presentation.FightFrame"/>, which reads it off
    /// the fight's own events. This only draws it.
    /// </remarks>
    [RequireComponent(typeof(Image))]
    public sealed class WindingGauge : MonoBehaviour
    {
        private Image _bar;
        private float _started;
        private float _over;

        /// <summary>
        /// Fills from nothing to full over this many milliseconds.
        /// </summary>
        /// <remarks>
        /// A duration of nothing means the blow is already landing, so the bar goes straight to
        /// full rather than flickering through a fill nobody would see.
        /// </remarks>
        public void Over(int ms)
        {
            _over = ms / 1000f;
            _started = Time.unscaledTime;

            if (_over <= 0f) Draw(1f);
        }

        /// <summary>
        /// Starts again at whatever duration it last used.
        /// </summary>
        /// <remarks>
        /// For a unit that never strikes again — it dies first. Freezing the bar would tell the
        /// delver the ending a second before the game does, and that is not the screen's news
        /// to break.
        /// </remarks>
        public void Again()
        {
            if (_over <= 0f) return;

            _started = Time.unscaledTime;
        }

        /// <summary>
        /// Forgets whatever it was winding, so the bar can be emptied and stay empty.
        /// </summary>
        /// <remarks>
        /// Without this, setting fillAmount from outside lasts exactly one frame: Update is still
        /// holding a duration and draws straight over it. Which is the correct behaviour during a
        /// fight and the wrong one between two.
        /// </remarks>
        public void Stop()
        {
            _over = 0f;
        }

        private void Awake() { _bar = GetComponent<Image>(); }

        private void Update()
        {
            if (_over <= 0f) return;

            // Unscaled, to match the clock the playback loop waits on. A gauge measured in
            // scaled time would drift away from the fight the moment anything touched timeScale.
            Draw(Mathf.Clamp01((Time.unscaledTime - _started) / _over));
        }

        private void Draw(float filled)
        {
            if (_bar != null) _bar.fillAmount = filled;
        }
    }
}
