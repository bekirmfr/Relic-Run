using System;
using UnityEngine;
using UnityEngine.UI;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// What is left of something that has just been killed: less and less, and then nothing.
    /// </summary>
    /// <remarks>
    /// A corpse that stays in its frame is the thing this exists to stop. Without it the foe a
    /// delver has just beaten goes on standing there through the walk to the next one, through
    /// the last stretch to the door, and through the gate — which reads as the fight not having
    /// finished rather than as having been won.
    ///
    /// It clears the picture at the END rather than at the start, so the fade is something a
    /// delver watches happen rather than a thing that already happened while they were reading
    /// the log. The source spends a fifth of a second on a guard and a third on a boss, which is
    /// short enough to stay out of the way and long enough to be seen.
    ///
    /// NO white flash. The source brightens the sprite to three times before it fades it, and a
    /// UI graphic's colour can only ever darken what a sprite already has — brightening wants a
    /// material of its own, and a material of its own wants a shader, a draw call and a reason.
    /// The fade is the half that carries the meaning.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DeathFade : MonoBehaviour
    {
        private Graphic _actor;
        private Color _was;
        private float _started;
        private float _over;
        private Action _gone;

        /// <summary>Whether something is still on its way out.</summary>
        public bool Fading
        {
            get { return _over > 0f; }
        }

        /// <summary>
        /// Fades a graphic to nothing over this long, and then puts it out.
        /// </summary>
        /// <param name="gone">
        /// Run when it has finished, or at once when there is nothing to fade. Whoever clears the
        /// picture hangs off this, so it has to happen on every path — a frame left holding a
        /// dead foe is the bug this is here to fix.
        /// </param>
        public void Play(Graphic actor, float seconds, Action gone)
        {
            _actor = actor;

            if (actor == null || seconds <= 0f)
            {
                Done(gone);
                return;
            }

            _was = actor.color;
            _started = Time.unscaledTime;
            _over = seconds;
            _gone = gone;
        }

        /// <summary>
        /// Stops a fade without finishing it, and puts the colour back.
        /// </summary>
        /// <remarks>
        /// For the next foe stepping into the same frame. Left alone, the graphic would keep
        /// whatever alpha the interrupted fade had reached and the new foe would walk on half
        /// transparent.
        /// </remarks>
        public void Stop()
        {
            if (_over > 0f && _actor != null) _actor.color = _was;

            _over = 0f;
            _gone = null;
            _actor = null;
        }

        private void Update()
        {
            if (_over <= 0f) return;

            float over = Mathf.Clamp01((Time.unscaledTime - _started) / _over);

            if (_actor != null)
            {
                Color ink = _was;
                ink.a = _was.a * (1f - over);

                _actor.color = ink;
            }

            if (over < 1f) return;

            Action done = _gone;
            Graphic actor = _actor;
            Color was = _was;

            _over = 0f;
            _gone = null;
            _actor = null;

            // The colour goes back before the picture goes away, so whatever is drawn here next
            // starts opaque rather than inheriting the end of somebody else's death.
            if (actor != null) actor.color = was;

            Done(done);
        }

        private static void Done(Action gone)
        {
            if (gone != null) gone();
        }
    }
}
