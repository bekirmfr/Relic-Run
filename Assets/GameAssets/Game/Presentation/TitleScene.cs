using System;
using System.Threading.Tasks;
using GameLift.Scene;
using RelicRun.Core.Determinism;
using RelicRun.Core.Meta;
using RelicRun.Core.Presentation;
using RelicRun.Game.Services;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The title, as a scene.
    /// </summary>
    /// <remarks>
    /// The first screen the game opens on, and the first one that reads a delver's save. A scene
    /// here is a PREFAB under <c>Assets/Scenes/</c> loaded by <c>SceneService</c> through a
    /// <c>SceneConfig</c>, which is the GameLift package's arrangement — so this has a lifecycle
    /// rather than an Awake, and the clock it starts has to be stopped in <see cref="Clear"/>
    /// rather than left ticking into the next screen.
    ///
    /// The scope is REQUIRED rather than merely expected. <c>SceneService</c> instantiates inside
    /// <c>LifetimeScope.EnqueueParent</c>, so a scope on this prefab is parented to the
    /// application's and can resolve what was registered there — and without one the title still
    /// opens, showing a delver who has never played, every launch, with nothing but a warning to
    /// say why. That is the kind of failure worth making structurally impossible rather than
    /// leaving to whichever tool happened to build the prefab.
    /// </remarks>
    [RequireComponent(typeof(LifetimeScope))]
    public sealed class TitleScene : MonoBehaviour, ISceneObject
    {
        [SerializeField] private TitleView _view;

        /// <summary>
        /// How often the countdown is redrawn.
        /// </summary>
        /// <remarks>
        /// Once a second, which is the smallest unit the clock shows. Faster would redraw the
        /// same string; slower would let the seconds visibly skip, which on a countdown reads as
        /// a stutter rather than as a saving.
        /// </remarks>
        private const float Tick = 1f;

        private SaveVault _vault;
        private float _due;

        public Task Initialize()
        {
            _vault = Vault();

            Listen();
            Draw();

            return Task.CompletedTask;
        }

        /// <summary>Taken down. Stops the clock.</summary>
        public Task Clear()
        {
            enabled = false;

            return Task.CompletedTask;
        }

        private void Update()
        {
            _due -= Time.unscaledDeltaTime;
            if (_due > 0f) return;

            _due = Tick;
            Draw();
        }

        /// <summary>
        /// Builds the card and hands it over.
        /// </summary>
        /// <remarks>
        /// The whole card each time, not just the clock. A screen that redrew only the parts it
        /// believed had changed would eventually be wrong about one of them, and a card is a
        /// struct and some strings.
        /// </remarks>
        private void Draw()
        {
            if (_view == null) return;

            _view.Show(Card());
        }

        private void Listen()
        {
            if (_view == null)
            {
                Debug.LogError("no view — the title has nothing to draw on", this);
                return;
            }

            // Nothing to go to yet: the screens these open are the rest of Phase 10. Said out
            // loud rather than left silent, because a button that does nothing and says nothing
            // is indistinguishable from one that is broken.
            _view.Played += () => Debug.Log("[Title] play → " + Card().Play.Goes);
            _view.ChoseDaily += () => Debug.Log("[Title] daily banner");
            _view.ChoseVersus += () => Debug.Log("[Title] versus banner");
            _view.AskedHow += () => Debug.Log("[Title] how to play");
        }

        /// <summary>
        /// The card as it stands right now.
        /// </summary>
        /// <remarks>
        /// The instant is read once, here, and passed down — so the countdown, the day's seed and
        /// whether today's Daily is done are all about ONE moment rather than about whenever each
        /// piece happened to ask. A card assembled from three readings of the clock can say the
        /// day has rolled over and still show yesterday's seed.
        /// </remarks>
        private TitleCard Card()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            SaveState earned = _vault != null ? _vault.Earned : new SaveState();
            Preferences chosen = _vault != null ? _vault.Chosen : null;

            bool done = earned.DailyDone.Contains(DailySeed.For(now));

            return TitleCards.Of(earned, chosen, done, false, now);
        }

        /// <summary>
        /// Finds the delver's save.
        /// </summary>
        /// <remarks>
        /// Asked for rather than injected, the same way the fight asks for its audio service.
        /// Injection into a plain component only happens once a scope has been told to register
        /// it, and this screen has no installer doing that — an attribute would have looked like
        /// wiring and done nothing.
        ///
        /// A title with no save shows a delver who has never played, which is wrong but legible.
        /// The alternative is a first screen that fails to open, and a first screen that fails to
        /// open is the whole game failing to open.
        /// </remarks>
        private SaveVault Vault()
        {
            var scope = GetComponent<LifetimeScope>();

            if (scope == null || scope.Container == null)
            {
                Debug.LogWarning("no lifetime scope on the title, so it shows a fresh delver", this);
                return null;
            }

            SaveVault vault;

            if (!scope.Container.TryResolve(out vault))
            {
                Debug.LogWarning("nothing registered to keep a save; the title shows a fresh " +
                                 "delver and nothing will be remembered", this);
                return null;
            }

            return vault;
        }
    }
}
