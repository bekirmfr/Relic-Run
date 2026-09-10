using System;
using System.Threading.Tasks;
using GameLift.Popup;
using GameLift.Scene;
using RelicRun.Core.Presentation;
using RelicRun.Game.Data;
using RelicRun.Game.Services;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// Everything that is not a run, as one scene.
    /// </summary>
    /// <remarks>
    /// The screen the game opens on, and the only one that reads a delver's save directly. A
    /// scene here is a PREFAB under <c>Assets/Scenes/</c> loaded by <c>SceneService</c> through a
    /// <c>SceneConfig</c> — the GameLift package's arrangement — so this has a lifecycle rather
    /// than an Awake, and the clock it starts is stopped in <see cref="Clear"/> rather than left
    /// ticking into the next screen.
    ///
    /// Eleven screens in one scene rather than eleven scenes, which is the source's arrangement
    /// and the right one: they are a few hundred objects between them, and loading each as its
    /// own addressable prefab would buy eleven waits and eleven flickers for nothing.
    ///
    /// The scope is REQUIRED rather than merely expected. <c>SceneService</c> instantiates inside
    /// <c>LifetimeScope.EnqueueParent</c>, so a scope on this prefab is parented to the
    /// application's and can resolve what was registered there — and without one the game still
    /// opens, showing a delver who has never played, every launch, with nothing but a warning to
    /// say why.
    /// </remarks>
    [RequireComponent(typeof(LifetimeScope))]
    public sealed class MetaScene : MonoBehaviour, ISceneObject
    {
        /// <summary>
        /// Every screen this scene can show, in whatever order the builder laid them out.
        /// </summary>
        /// <remarks>
        /// Listed rather than found by <c>GetComponentsInChildren</c>, because a hidden panel is
        /// an INACTIVE object and the cheap overload does not return those. A screen that worked
        /// until the first time you navigated away from it is exactly the sort of bug this whole
        /// port has been avoiding.
        /// </remarks>
        [SerializeField] private MetaPanel[] _panels = new MetaPanel[0];

        /// <summary>
        /// The book of languages, wired by the builder from the game's content.
        /// </summary>
        /// <remarks>
        /// Serialised rather than looked up at run time, because it IS content: the builder wires
        /// it from the same GameContent every other book comes out of, and a screen that went
        /// hunting for it would be a screen that fails differently in a build than in the editor.
        /// </remarks>
        [SerializeField] private LocaleBook _locales;

        /// <summary>
        /// The relic pictures, wired by the builder from the same content as everything else.
        /// </summary>
        /// <remarks>
        /// Only the relic card wants them, and it opens without them. Serialised here rather
        /// than reached for inside the popup because the popup is spawned by a service and has
        /// nowhere to reach from.
        /// </remarks>
        [SerializeField] private RelicIconBook _icons;

        /// <summary>The foe pictures, wired the same way and wanted by the same one card.</summary>
        [SerializeField] private EnemyBook _foes;

        /// <summary>
        /// How often a ticking panel is redrawn.
        /// </summary>
        /// <remarks>
        /// Once a second, which is the smallest unit any of them show. Faster redraws the same
        /// string; slower lets the seconds visibly skip, which on a countdown reads as a stutter
        /// rather than as a saving.
        /// </remarks>
        private const float Tick = 1f;

        private SaveVault _vault;
        private ISceneService _scenes;
        private Speech _speech;
        private Modals _modals;
        private MetaPanel _showing;
        private float _due;

        /// <summary>Where the board was opened from, which is where closing it goes back to.</summary>
        /// <remarks>
        /// The one piece of history this keeps, and it is kept because the board is reachable
        /// from two places. Everything else about navigation is a function of where you are.
        /// </remarks>
        private Page _cameFrom = Page.Title;

        /// <summary>
        /// Built. Fetches the delver's language, then opens on the title.
        /// </summary>
        /// <remarks>
        /// The strings are awaited rather than fetched in the background, and it is the one thing
        /// this screen waits for. A menu drawn before its words arrive shows a frame of raw keys
        /// — and the first frame of the game is the one screenshot everybody takes.
        /// </remarks>
        public async Task Initialize()
        {
            _vault = Vault();
            _scenes = Scenes();
            _speech = new Speech();

            foreach (MetaPanel panel in _panels)
            {
                if (panel == null) continue;

                panel.Wants += Asked;
                panel.Showing = false;
            }

            _modals = new Modals(Popups(), _vault, _speech, _icons, _foes);

            Aim();

            // Redraw whatever is showing when a modal changes something. A settings modal that
            // altered the language and left the screen behind it in the old one would be the
            // change appearing to have failed.
            _modals.Changed += () =>
            {
                foreach (MetaPanel panel in _panels)
                {
                    if (panel != null) panel.Words = _speech.Locale;
                }

                if (_showing != null) Draw(_showing);
            };

            foreach (MetaPanel panel in _panels)
            {
                if (panel != null) panel.Modals = _modals;
            }

            await Learn();

            Go(Page.Title);

            // After the title is up, so the delver sees what they are being welcomed to rather
            // than a modal over an empty screen.
            _modals.WelcomeIfNew(UnityEngine.Random.Range(0, 10000));
        }

        /// <summary>Fetches the language and hands it to every panel.</summary>
        /// <remarks>
        /// A failure here is a warning and a game in keys rather than a game that will not open.
        /// Every screen still draws, every button still works, and what is wrong is legible on
        /// the screen itself.
        /// </remarks>
        private async Task Learn()
        {
            try
            {
                await _speech.Learn(_locales, _vault != null ? _vault.Chosen : null,
                    Speech.Asked());
            }
            catch (System.Exception broken)
            {
                Debug.LogWarning("could not fetch the strings, so the game speaks in keys: " +
                                 broken.Message, this);
            }

            foreach (MetaPanel panel in _panels)
            {
                if (panel != null) panel.Words = _speech.Locale;
            }
        }

        /// <summary>Taken down. Stops the clock and lets go of the panels.</summary>
        /// <remarks>
        /// The handlers are removed rather than left. A panel outliving its scene would go on
        /// asking a destroyed router to navigate, which is a null reference per press and reads
        /// as the next screen being broken.
        /// </remarks>
        public Task Clear()
        {
            enabled = false;

            foreach (MetaPanel panel in _panels)
            {
                if (panel != null) panel.Wants -= Asked;
            }

            return Task.CompletedTask;
        }

        /// <summary>Shows one screen and hides the rest.</summary>
        /// <remarks>
        /// Drawn BEFORE it is shown, so a screen never appears holding whatever it had when the
        /// delver last left it. One frame of last week's gold is one frame too many.
        /// </remarks>
        public void Go(Page page)
        {
            MetaPanel wanted = Find(page);

            if (wanted == null)
            {
                // The rest of Phase 10. Said out loud rather than silently ignored, because a
                // button that does nothing and says nothing is indistinguishable from one that
                // is broken.
                Debug.Log("[MetaScene] " + page + " has no panel yet", this);
                return;
            }

            Draw(wanted);

            foreach (MetaPanel panel in _panels)
            {
                if (panel != null) panel.Showing = panel == wanted;
            }

            _showing = wanted;
            _due = Tick;
        }

        private void Update()
        {
            if (_showing == null || !_showing.Ticks) return;

            _due -= Time.unscaledDeltaTime;
            if (_due > 0f) return;

            _due = Tick;
            Draw(_showing);
        }

        /// <summary>
        /// What a panel's request actually does.
        /// </summary>
        /// <remarks>
        /// The routing rules are Core's — <see cref="Pages"/> answers where home goes and where
        /// the board closes to — and the reason they are asked HERE is that a panel does not know
        /// where it was opened from. The board is the case that proves it: the same screen, two
        /// ways in, and one of them goes back to the end of a run.
        /// </remarks>
        private void Asked(Page page)
        {
            // The run is not a panel. It is its own scene, because a fight is a different thing
            // from a menu: it owns the whole screen, it has a lifecycle of its own, and the
            // service tears the menu down as it loads rather than drawing one over the other.
            if (page == Page.Run)
            {
                Delve();
                return;
            }

            if (page == Page.Board && _showing != null) _cameFrom = _showing.Shows;

            if (page == Page.Title && _showing != null && _showing.Shows == Page.Board)
            {
                Go(Pages.CloseBoard(_cameFrom));
                return;
            }

            Go(page);
        }

        /// <summary>
        /// Hands over to the run.
        /// </summary>
        /// <remarks>
        /// What loads today is the fight SCAFFOLD from Phase 9 — one fight, set up from the
        /// Fight asset, rather than a delve into the hall the delver just chose. The hall IS
        /// remembered and committed before this is called, so nothing is lost by the run layer
        /// not existing yet; when it does, this line is what changes.
        ///
        /// Not awaited, and deliberately: the service tears this scene down as part of loading,
        /// so awaiting here would be awaiting on an object being destroyed. The task is dropped
        /// with its failure reported rather than left to disappear silently.
        /// </remarks>
        private void Delve()
        {
            if (_scenes == null)
            {
                Debug.LogWarning("nothing can load scenes, so the run cannot be reached", this);
                return;
            }

            Task loading = _scenes.LoadScene(SceneKeys.GameScene);

            loading.ContinueWith(
                done => Debug.LogError("could not load the run: " + done.Exception, this),
                TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously);
        }

        private void Draw(MetaPanel panel)
        {
            if (panel == null || _vault == null) return;

            // One instant for the whole draw, read here and passed down, so everything on a
            // screen is about the same moment rather than about whenever each piece asked. A
            // card assembled from three readings of the clock can say the day has rolled over
            // and still show yesterday's seed.
            panel.Draw(_vault, DateTimeOffset.UtcNow);
        }

        private MetaPanel Find(Page page)
        {
            foreach (MetaPanel panel in _panels)
            {
                if (panel != null && panel.Shows == page) return panel;
            }

            return null;
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
        /// A screen with no save shows a delver who has never played, which is wrong but legible.
        /// The alternative is a first screen that fails to open, and a first screen that fails to
        /// open is the whole game failing to open.
        /// </remarks>
        private SaveVault Vault()
        {
            var scope = GetComponent<LifetimeScope>();

            if (scope == null || scope.Container == null)
            {
                Debug.LogWarning("no lifetime scope, so the menu shows a fresh delver", this);
                return new SaveVault(null);
            }

            SaveVault vault;

            if (!scope.Container.TryResolve(out vault))
            {
                Debug.LogWarning("nothing registered to keep a save; the menu shows a fresh " +
                                 "delver and nothing will be remembered", this);
                return new SaveVault(null);
            }

            return vault;
        }

        /// <summary>
        /// Points the popup canvas at this scene's camera.
        /// </summary>
        /// <remarks>
        /// It cannot be wired by a builder. The popup canvas belongs to the application's root
        /// prefab, which exists before any scene is loaded and outlives all of them, so at the
        /// moment it is authored there is no camera in existence to point it at.
        ///
        /// Left alone it stays Screen Space - Overlay, and a modal drawn that way is composited
        /// outside every camera — which works on screen and cannot be captured, so the one part
        /// of the interface that appears ON TOP of everything would be the one part nobody could
        /// take a picture of. Its sorting order still keeps it above this scene's canvas.
        ///
        /// Nothing is restored when the scene goes: the next scene aims it at its own camera, and
        /// a canvas pointing at a destroyed camera between the two draws nothing for a frame that
        /// nobody is looking at.
        /// </remarks>
        private void Aim()
        {
            Camera eye = GetComponentInChildren<Camera>(true);

            if (eye == null) return;

            foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                // Only the ones this scene does not own. Its own are already aimed by the builder,
                // and re-aiming them here would hide a builder that had stopped doing it.
                if (canvas.transform.IsChildOf(transform)) continue;
                if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;

                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = eye;
                canvas.planeDistance = 10f;

                Grid(canvas);
            }
        }

        /// <summary>
        /// Puts an inherited canvas on the same pixel grid as the game's own.
        /// </summary>
        /// <remarks>
        /// The popup canvas belongs to the application's root prefab and arrives with the
        /// sample's scaler: ScaleWithScreenSize against a 1080-unit reference, which on this
        /// phone is 1.33 screen pixels per authored one. The game's canvas is a whole 3.
        ///
        /// So every modal was drawn at a third the size of the screen behind it and on a
        /// fractional grid besides — which is the exact fault <see cref="PixelCanvas"/> exists
        /// for, showing up on the one canvas no builder could reach. A modal is not fine print;
        /// it is the same interface with the screen dimmed behind it.
        ///
        /// Adopted rather than configured, so there is one place that decides the factor. The
        /// component finds its own scaler and refits itself whenever the screen changes.
        /// </remarks>
        private static void Grid(Canvas canvas)
        {
            if (canvas.GetComponent<PixelCanvas>() != null) return;
            if (canvas.GetComponent<CanvasScaler>() == null) return;

            canvas.gameObject.AddComponent<PixelCanvas>();
        }

        /// <summary>Finds whatever opens modals.</summary>
        /// <remarks>
        /// Missing the same way the save is, and survivable the same way: a menu that cannot open
        /// its settings is still a menu, and one warning beats a button that does nothing.
        /// </remarks>
        private IPopupService Popups()
        {
            var scope = GetComponent<LifetimeScope>();

            if (scope == null || scope.Container == null) return null;

            IPopupService popups;

            return scope.Container.TryResolve(out popups) ? popups : null;
        }

        /// <summary>Finds whatever loads scenes, so the menu can hand over to a run.</summary>
        /// <remarks>
        /// Asked for the same way the save is, and missing the same way: a menu that cannot
        /// reach the run is still a menu, and saying so once beats a button that does nothing.
        /// </remarks>
        private ISceneService Scenes()
        {
            var scope = GetComponent<LifetimeScope>();

            if (scope == null || scope.Container == null) return null;

            ISceneService scenes;

            return scope.Container.TryResolve(out scenes) ? scenes : null;
        }
    }
}
