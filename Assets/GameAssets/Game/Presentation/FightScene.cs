using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using GameLift.Audio;
using GameLift.Scene;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Game.Data;
using RelicRun.Game.Services;
using VContainer;
using VContainer.Unity;
using UnityEngine;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The fight, as a scene: it fights what it is ordered to and says how it went.
    /// </summary>
    /// <remarks>
    /// A scene in this project is a PREFAB under <c>Assets/Scenes/</c>, not a <c>.unity</c> file.
    /// <c>Corescene.unity</c> is empty and stays empty; <see cref="ISceneObject"/>
    /// implementations are loaded into it by <c>SceneService</c> through a <c>SceneConfig</c>
    /// that addresses the prefab and names it with a key.
    ///
    /// What it means in practice is that a screen has a lifecycle rather than an Awake: it is
    /// built when somebody asks for it and taken down when somebody asks for the next one. It
    /// also means a screen cannot be handed its subject — <c>LoadScene</c> takes a key and
    /// <c>Initialize</c> takes nothing — which is what <see cref="FightOrder"/> is for.
    ///
    /// Three things it now does that the harness never did. It fights the fight the MENU asked
    /// for rather than one written into an asset. It reads the log in the delver's own language,
    /// where the harness read English through an editor-only path and would have shown a build
    /// nothing at all. And it ENDS: the fight finishes, the result is reported, and the delver is
    /// returned to the menu instead of left staring at a still frame.
    /// </remarks>
    [RequireComponent(typeof(LifetimeScope))]
    public sealed class FightScene : MonoBehaviour, ISceneObject
    {
        [SerializeField] private CombatView _view;
        [SerializeField] private GameContent _content;

        [Tooltip("The authored fight, used when nobody has ordered one.")]
        [SerializeField] private FightHarness _harness;

        private CombatPlaybackController _showing;
        private FightOrder _order;
        private ISceneService _scenes;
        private SaveVault _vault;
        private Speech _speech;
        private bool _fighting;
        private bool _abandoned;

        /// <summary>
        /// Built. Starts the fight without waiting for it to finish.
        /// </summary>
        /// <remarks>
        /// Not awaited, and that is a change: the harness ran the whole fight inside this call,
        /// which meant the service's own transition did not finish until the delver had died. A
        /// screen is BUILT when its widgets exist; what happens on it afterwards is the screen's
        /// business, and the service has a <see cref="Clear"/> for taking it away mid-sentence.
        /// </remarks>
        public Task Initialize()
        {
            // Everything from the container is fetched HERE, while this object is certainly
            // alive. Asking for it later cost a MissingReferenceException the first time a fight
            // outlived its screen: the playback finished, the scene had already been taken away,
            // and the line that wanted to go back to the menu was a GetComponent on a corpse.
            _order = Resolve<FightOrder>("an order to fight");
            _scenes = Resolve<ISceneService>("anything that loads scenes");
            _vault = Resolve<SaveVault>("a save");

            Hear();
            Watch();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Whether this screen is still the one on the screen.
        /// </summary>
        /// <remarks>
        /// Two ways it might not be, and they are not the same. <see cref="Clear"/> is the
        /// service saying so, politely, before it takes the scene away; being DESTROYED is the
        /// same thing having already happened, which is what a Unity object's null comparison
        /// answers. A fight is several seconds long and the game can move on during it.
        /// </remarks>
        private bool Gone
        {
            get { return _abandoned || this == null; }
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
            _abandoned = true;

            if (_showing != null) _showing.Abandon();

            return Task.CompletedTask;
        }

        /// <summary>The view this scene draws with, for whatever assembles a run around it.</summary>
        public CombatView View { get { return _view; } }

        /// <summary>
        /// Fights whatever was ordered, then hands back.
        /// </summary>
        /// <remarks>
        /// Void because nothing awaits it: this is the screen living its life, not a task
        /// somebody is holding. Everything that can go wrong inside it is caught, because an
        /// exception escaping here would be swallowed by the runtime and the screen would simply
        /// stop with no fight and no message.
        /// </remarks>
        private async void Watch()
        {
            if (_fighting)
            {
                Debug.LogWarning("a fight is already on screen; ignoring the second", this);
                return;
            }

            _fighting = true;

            try
            {
                await Fight();
            }
            catch (Exception broken)
            {
                Debug.LogError("the fight ended badly: " + broken, this);
            }
            finally
            {
                _fighting = false;
            }
        }

        private async UniTask Fight()
        {
            if (_view == null || _content == null)
            {
                Debug.LogError("the fight has nothing to show or nothing to show it with", this);
                return;
            }

            if (_content.Presentation == null)
            {
                // Everything else here has a sensible nothing to fall back on. The pacing does
                // not: with no numbers there is no beat, and a fight would either flash past or
                // never move.
                Debug.LogError("no pacing is authored — run Tools > Relic Run > Import Content", this);
                return;
            }

            int ceiling;
            FightPlan plan = Ordered(out ceiling);

            if (plan == null) return;

            // Before a single frame is drawn. Nothing in the presentation layer computes combat;
            // its whole job afterwards is to read out a finished list.
            CombatResult result = Bout.Fight(plan);

            Debug.Log("floor " + plan.Floor + " of hall " + plan.Tier + ", seed " + plan.Seed +
                      ": " + result.Events.Count + " events", this);

            await Say();

            Pacing pacing = Pacing.For(result.Events.Count, Reduced(), Speed(),
                _content.Presentation.ToPacing());

            // The screen can go away while the words are being fetched, which is a network
            // round trip in a build. Drawing into it afterwards is a null reference per widget.
            if (Gone) return;

            _view.Begin(result.Events, pacing, new CombatLog(_speech.Locale),
                Shelf.Of(plan.Delver), false, plan.Tier);

            _showing = new CombatPlaybackController(_content.Presentation);

            await _showing.Show(result.Events, _view, SkipsIntro());

            Done(plan, result, ceiling);
        }

        /// <summary>
        /// What was ordered, or the authored fight, or nothing.
        /// </summary>
        /// <remarks>
        /// The fallback is the whole reason the harness still exists: opening this scene on its
        /// own — pressing Play with the fight prefab as the startup scene — has to show a fight,
        /// or the combat layer becomes something you can only reach by playing the game up to
        /// it. It says which one it took, because a scene showing the wrong fight is otherwise a
        /// silent mystery.
        /// </remarks>
        private FightPlan Ordered(out int ceiling)
        {
            ceiling = 0;

            FightPlan plan;

            if (_order != null && _order.Take(out plan, out ceiling)) return plan;

            if (_harness == null || !_harness.Ready)
            {
                Debug.LogError("nothing ordered a fight and there is no authored one to fall " +
                               "back on — run Tools > Relic Run > Build Fight Scene", this);
                return null;
            }

            Debug.Log("nothing ordered a fight, so the authored one is being shown: " +
                      _harness.Describe(), this);

            return _harness.Plan(out ceiling);
        }

        /// <summary>
        /// Reports the result and hands the delver back to the menu.
        /// </summary>
        /// <remarks>
        /// Not when the screen was taken away underneath it. A fight abandoned halfway through
        /// has no result worth reporting, and loading the menu from here would be loading it
        /// twice — once for whoever abandoned this, and once for a fight that has not noticed.
        /// </remarks>
        private void Done(FightPlan plan, CombatResult result, int ceiling)
        {
            if (Gone) return;

            FightSummary summary = Bout.Read(plan, result, ceiling);

            if (_order != null) _order.Report(summary);

            Debug.Log(summary.Won
                ? "the delver walked out with " + summary.Left + " of " + summary.Most
                : "the delver fell on floor " + plan.Floor, this);

            Leave();
        }

        /// <summary>
        /// Back to the menu.
        /// </summary>
        /// <remarks>
        /// The run layer will put a result screen here, and then a draft, and then the next
        /// floor. Until it does, a fight that ends has to go SOMEWHERE — a screen that stops on
        /// its last frame and stays there is indistinguishable from one that crashed.
        ///
        /// Not awaited: the service tears this scene down as part of loading, so awaiting would
        /// be awaiting on an object being destroyed.
        /// </remarks>
        private void Leave()
        {
            if (_scenes == null)
            {
                Debug.LogWarning("the fight is over and nothing can load the menu");
                return;
            }

            // No context object on the failures below. Loading the menu DESTROYS this one as
            // part of the same call, so by the time anything is reported there may be nothing
            // left to point the message at — and handing Debug a destroyed object is itself an
            // error.
            //
            // A null result is checked as well as a fault, because the service catches its own
            // exceptions, logs them as ordinary messages and returns null: a load that failed
            // completes its task perfectly and goes nowhere.
            _scenes.LoadScene(SceneKeys.MenuScene).ContinueWith(done =>
            {
                if (done.IsFaulted)
                {
                    Debug.LogError("could not get back to the menu: " + done.Exception);
                    return;
                }

                if (done.Result == null)
                {
                    Debug.LogError("the fight is over and nothing loaded for the menu — the " +
                                   "scene service logged the reason as an ordinary message.");
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <summary>
        /// Fetches the delver's language.
        /// </summary>
        /// <remarks>
        /// The fight's log is the one place in the game a delver reads sentences rather than
        /// labels, and until now it read them in English — through the editor's asset database,
        /// behind a <c>UNITY_EDITOR</c> guard, which in a build returned nothing at all. A
        /// shipped fight would have narrated itself in raw keys.
        ///
        /// Awaited, because a fight drawn before its words arrive spends its first beats saying
        /// nothing. A failure is a warning and a fight narrated in keys, which is ugly and
        /// diagnosable — where a blank log is neither.
        /// </remarks>
        private async UniTask Say()
        {
            _speech = new Speech();

            if (_content.Locales == null)
            {
                Debug.LogWarning("no locale book, so the fight is narrated in keys", this);
                return;
            }

            try
            {
                await _speech.Learn(_content.Locales, _vault != null ? _vault.Chosen : null,
                    Speech.Asked());
            }
            catch (Exception broken)
            {
                Debug.LogWarning("could not fetch the strings, so the fight speaks in keys: " +
                                 broken.Message, this);
            }
        }

        /// <summary>How fast to read it out, from the authored settings when there are any.</summary>
        /// <remarks>
        /// Watching preferences, not fight ones — how fast and how still are about the person
        /// holding the phone. They live on the authored asset today because that is where the
        /// only dial is; when there is a settings screen for them, this is the line that changes.
        /// </remarks>
        private int Speed()
        {
            FightSettings watching = _harness != null ? _harness.Watching : null;

            return watching != null ? watching.Speed : 1;
        }

        private bool Reduced()
        {
            FightSettings watching = _harness != null ? _harness.Watching : null;

            return watching != null && watching.ReducedMotion;
        }

        private bool SkipsIntro()
        {
            FightSettings watching = _harness != null ? _harness.Watching : null;

            return watching != null && watching.SkipIntro;
        }

        /// <summary>
        /// Finds whoever makes the noises and hands them to the screen.
        /// </summary>
        /// <remarks>
        /// A fight with no sound is still a fight, so a missing service is said once and stepped
        /// over. It is the sort of thing that goes missing in a build and should not take the
        /// screen with it.
        /// </remarks>
        private void Hear()
        {
            if (_view == null) return;

            var audio = Resolve<IAudioService>("anything that plays sounds");

            if (audio == null) return;

            _view.Hear(audio);
        }

        /// <summary>
        /// Asks the application's container for something, or says what is missing.
        /// </summary>
        /// <remarks>
        /// Asked for rather than injected. Injection into a plain component only happens once a
        /// scope has been told to register it, and the fight has no installer doing that, so an
        /// attribute would have looked like wiring and done nothing at all.
        ///
        /// The scope on this object is parented to the application's, which is what makes
        /// everything registered up there reachable from down here.
        /// </remarks>
        private T Resolve<T>(string what) where T : class
        {
            var scope = GetComponent<LifetimeScope>();

            if (scope == null || scope.Container == null)
            {
                Debug.LogWarning("no lifetime scope on the fight, so there is no " + what, this);
                return null;
            }

            T found;

            if (!scope.Container.TryResolve(out found))
            {
                Debug.LogWarning("nothing registered as " + what, this);
                return null;
            }

            return found;
        }

        private void OnDestroy()
        {
            if (_showing != null) _showing.Dispose();
        }
    }
}
