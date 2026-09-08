using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using GameLift.Scene;
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

        /// <summary>The view this scene draws with, for whatever assembles a run around it.</summary>
        public CombatView View { get { return _view; } }
    }
}
