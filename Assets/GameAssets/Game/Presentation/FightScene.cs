using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using GameLift.Audio;
using GameLift.Scene;
using VContainer;
using VContainer.Unity;
using UnityEngine;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The fight, as a scene.
    /// </summary>
    /// <remarks>
    /// A scene in this project is a PREFAB under <c>Assets/Scenes/</c>, not a <c>.unity</c> file.
    /// <c>Corescene.unity</c> is empty and stays empty; <see cref="ISceneObject"/> implementations
    /// are loaded into it by <c>SceneService</c> through a <c>SceneConfig</c> that addresses the
    /// prefab and names it with a key. That is the GameLift package's arrangement and this
    /// follows it rather than inventing a second one.
    ///
    /// What it means in practice is that a screen has a lifecycle rather than an Awake: it is
    /// built when somebody asks for it and taken down when somebody asks for the next one, and
    /// both of those are awaited. A fight abandoned halfway through has to stop when
    /// <see cref="Clear"/> is called, not whenever its own token happens to notice.
    /// </remarks>
    public sealed class FightScene : MonoBehaviour, ISceneObject
    {
        [SerializeField] private CombatView _view;
        [SerializeField] private FightHarness _harness;

        /// <summary>Built. Starts the fight the harness is set up for.</summary>
        public async Task Initialize()
        {
            if (_harness == null)
            {
                Debug.LogError("no harness — this scene has nothing to show", this);
                return;
            }

            Hear();

            await _harness.Fight().AsTask();
        }

        /// <summary>
        /// Taken down. Stops the fight rather than leaving it running into the next screen.
        /// </summary>
        /// <remarks>
        /// The one thing a scene owes the service. A playback loop that outlived its screen would
        /// go on drawing into destroyed widgets, which is a null reference per beat and reads as
        /// the next screen being broken.
        /// </remarks>
        public Task Clear()
        {
            if (_harness != null) _harness.Abandon();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Finds whoever makes the noises and hands them to the screen.
        /// </summary>
        /// <remarks>
        /// Resolved here because this is the object the service instantiates, and its
        /// <c>LifetimeScope</c> is parented to the application's — so the audio service
        /// registered up there is reachable from down here without the fight needing an installer
        /// of its own.
        ///
        /// Asked for rather than injected. Injection into a plain component only happens once a
        /// scope has been told to register it, and the fight has no installer doing that, so an
        /// attribute would have looked like wiring and done nothing at all.
        ///
        /// A fight with no sound is still a fight, so a missing service is said once and stepped
        /// over. It is the sort of thing that goes missing in a build and should not take the
        /// screen with it.
        /// </remarks>
        private void Hear()
        {
            if (_view == null) return;

            var scope = GetComponent<LifetimeScope>();

            if (scope == null || scope.Container == null)
            {
                Debug.LogWarning("no lifetime scope on the fight, so it plays silently", this);
                return;
            }

            IAudioService audio;

            if (!scope.Container.TryResolve(out audio))
            {
                Debug.LogWarning("nothing registered to play sounds; the fight will be silent", this);
                return;
            }

            _view.Hear(audio);
        }

        /// <summary>The view this scene draws with, for whatever assembles a run around it.</summary>
        public CombatView View { get { return _view; } }
    }
}
