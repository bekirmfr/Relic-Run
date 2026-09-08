using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using RelicRun.Core.Combat;
using RelicRun.Core.Presentation;
using RelicRun.Game.Data;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// Shows one fight at a time, and stops showing it the moment anything else starts.
    /// </summary>
    /// <remarks>
    /// All the deciding is in Core — which event, how long to hold, when to walk the hall — and
    /// what is left here is the two things Core cannot have: a clock made of frames, and the
    /// token that ends a fight nobody is watching any more.
    ///
    /// That token is the whole of what replaces the source's single-flight dance. There, every
    /// playback chain carried a number, and each callback compared its number to the current one
    /// and returned quietly if it had been superseded — because a browser timer, once set, will
    /// fire. Starting a new fight here cancels the old one instead, and the old loop stops inside
    /// whatever it was awaiting.
    /// </remarks>
    public sealed class CombatPlaybackController : IDisposable
    {
        private readonly PresentationSettings _settings;
        private CancellationTokenSource _showing;

        /// <summary>
        /// Whether the delver has asked for less motion.
        /// </summary>
        /// <remarks>
        /// A game setting rather than a system one, which is a real difference from the browser.
        /// The source asks the platform through <c>prefers-reduced-motion</c>; Unity has no
        /// cross-platform equivalent, and reading the accessibility setting on each platform
        /// separately is its own piece of work. Until then this is somewhere a delver can say so.
        /// </remarks>
        public bool ReducedMotion { get; set; }

        /// <summary>The fight being shown, or null between fights.</summary>
        public CombatPlayback Playing { get; private set; }

        public CombatPlaybackController(PresentationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            _settings = settings;
        }

        /// <summary>
        /// Shows a fight from its first event to its last.
        /// </summary>
        /// <remarks>
        /// Abandons whatever was on screen first. Two fights at once is not a state worth
        /// supporting: it would mean two loops writing to one screen, which is exactly the
        /// failure the source's chain numbering existed to prevent.
        /// </remarks>
        public async UniTask Show(IReadOnlyList<CombatEvent> events, IPlaybackScreen screen,
            bool skipIntro = false)
        {
            Abandon();

            _showing = new CancellationTokenSource();
            CancellationToken token = _showing.Token;

            Playing = CombatPlayback.Of(events, ReducedMotion, 1, _settings.ToPacing(), skipIntro);

            try
            {
                await PlaybackLoop.Play(Playing, screen, new Frames(), token);
            }
            catch (OperationCanceledException)
            {
                // Abandoned on purpose. A fight nobody is watching is not a failure, and the
                // caller that cancelled it already knows.
            }
        }

        /// <summary>Presses the speed control. Takes effect on the next hold.</summary>
        public int Faster()
        {
            return Playing == null ? 1 : Playing.Faster();
        }

        /// <summary>Throws the rest of the fight away and shows its end.</summary>
        public void Skip()
        {
            if (Playing != null) Playing.Skip();
        }

        /// <summary>Stops showing whatever is on screen.</summary>
        public void Abandon()
        {
            if (_showing == null) return;

            _showing.Cancel();
            _showing.Dispose();
            _showing = null;
        }

        public void Dispose() { Abandon(); }

        /// <summary>
        /// The clock, made of frames.
        /// </summary>
        /// <remarks>
        /// Unscaled on purpose. Pausing is something the delver does with a button and the loop
        /// answers with a gate; if playback also stopped when <c>Time.timeScale</c> did, a menu
        /// that froze time would freeze a fight halfway through with no way to say so.
        /// </remarks>
        private sealed class Frames : IPlaybackClock
        {
            public Task Wait(int ms, CancellationToken token)
            {
                return UniTask.Delay(ms, DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update,
                    token).AsTask();
            }

            public Task Until(Func<bool> ready, CancellationToken token)
            {
                return UniTask.WaitUntil(ready, PlayerLoopTiming.Update, token).AsTask();
            }
        }
    }
}
