using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using GameLift.Audio;
using GameLift.Scene;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Meta;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;
using RelicRun.Game.Data;
using RelicRun.Game.Services;
using VContainer;
using VContainer.Unity;
using UnityEngine;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// The delve, as a scene: it walks the run it was ordered to and says what it came to.
    /// </summary>
    /// <remarks>
    /// A scene in this project is a PREFAB under <c>Assets/Scenes/</c>, not a <c>.unity</c> file.
    /// <c>Corescene.unity</c> is empty and stays empty; <see cref="ISceneObject"/>
    /// implementations are loaded into it by <c>SceneService</c> through a <c>SceneConfig</c>
    /// that addresses the prefab and names it with a key. It also means a screen cannot be handed
    /// its subject — <c>LoadScene</c> takes a key and <c>Initialize</c> takes nothing — which is
    /// what <see cref="FightOrder"/> is for.
    ///
    /// It walks a whole <see cref="Delve"/>, floor after floor, and plays every fight it is
    /// handed. What it does NOT have yet is a screen for any of the other stops, so it answers
    /// them itself and says so once: the first relic of every offer, the first choice of every
    /// event, no rerolls, no deals, no revive. That is a scaffold and it is labelled as one —
    /// but it is a scaffold around the REAL run loop, so replacing an answer with a screen is a
    /// stage at a time rather than a rewrite.
    ///
    /// When the run ends it is settled into the save through <c>Career.Settle</c>, which is the
    /// same scoring and banking the corpus gates, and the delver is handed back to the menu with
    /// an end screen to read.
    /// </remarks>
    [RequireComponent(typeof(LifetimeScope))]
    public sealed class FightScene : MonoBehaviour, ISceneObject
    {
        [SerializeField] private CombatView _view;
        [SerializeField] private GameContent _content;

        [Tooltip("The authored fight, used when nobody has ordered a delve.")]
        [SerializeField] private FightHarness _harness;

        private CombatPlaybackController _showing;
        private FightOrder _order;
        private ISceneService _scenes;
        private SaveVault _vault;
        private Speech _speech;
        private bool _walking;
        private bool _abandoned;

        /// <summary>Built. Starts the delve without waiting for it to finish.</summary>
        /// <remarks>
        /// Everything from the container is fetched HERE, while this object is certainly alive.
        /// Asking for it later cost a MissingReferenceException the first time a fight outlived
        /// its screen: the playback finished, the scene had already been taken away, and the line
        /// that wanted to go back to the menu was a GetComponent on a corpse.
        /// </remarks>
        public Task Initialize()
        {
            _order = Resolve<FightOrder>("an order to delve");
            _scenes = Resolve<ISceneService>("anything that loads scenes");
            _vault = Resolve<SaveVault>("a save");

            Hear();
            Walk();

            return Task.CompletedTask;
        }

        /// <summary>
        /// Whether this screen is still the one on the screen.
        /// </summary>
        /// <remarks>
        /// Two ways it might not be, and they are not the same. <see cref="Clear"/> is the
        /// service saying so, politely, before it takes the scene away; being DESTROYED is the
        /// same thing having already happened, which is what a Unity object's null comparison
        /// answers. A delve is minutes long and the game can move on during it.
        /// </remarks>
        private bool Gone
        {
            get { return _abandoned || this == null; }
        }

        /// <summary>Taken down. Stops the run rather than leaving it playing into the next screen.</summary>
        public Task Clear()
        {
            _abandoned = true;

            if (_showing != null) _showing.Abandon();

            return Task.CompletedTask;
        }

        /// <summary>The view this scene draws with, for whatever assembles a run around it.</summary>
        public CombatView View { get { return _view; } }

        /// <summary>
        /// Walks the delve that was ordered, then hands back.
        /// </summary>
        /// <remarks>
        /// Void because nothing awaits it: this is the screen living its life, not a task
        /// somebody is holding. Everything that can go wrong inside it is caught, because an
        /// exception escaping here would be swallowed by the runtime and the screen would simply
        /// stop with no run and no message.
        /// </remarks>
        private async void Walk()
        {
            if (_walking)
            {
                Debug.LogWarning("a delve is already on screen; ignoring the second", this);
                return;
            }

            _walking = true;

            try
            {
                await Delving();
            }
            catch (Exception broken)
            {
                Debug.LogError("the delve ended badly: " + broken, this);
            }
            finally
            {
                _walking = false;
            }
        }

        private async UniTask Delving()
        {
            if (_view == null || _content == null)
            {
                Debug.LogError("the delve has nothing to show or nothing to show it with", this);
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

            RunOrder order;

            if (!Ordered(out order)) return;

            SaveState earned = _vault != null ? _vault.Earned : new SaveState();

            var delve = new Delve(order.Seed, Career.SetupFor(earned, order.Tier));

            Debug.Log("delving into hall " + order.Tier + ", seed " + order.Seed +
                      (order.Daily ? " (today's Daily)" : string.Empty), this);

            await Say();

            var told = false;

            while (!delve.Finished)
            {
                if (Gone) return;

                if (delve.Pending.Kind == AskKind.Fought)
                {
                    Met(earned, delve.Pending.Pack);

                    await Read(delve.Pending, order.Tier, delve.State);

                    if (Gone) return;
                }
                else if (!told)
                {
                    told = true;

                    Debug.Log("no screen answers a " + delve.Pending.Kind + " yet, so this delve " +
                              "answers its own — the first relic, the first choice, and no deals",
                        this);
                }

                delve.Pending.Answer = Plainly(delve.Pending);
                delve.Answer();
            }

            Settle(delve, order, earned);
        }

        /// <summary>
        /// Reads one floor's fight out, at the pace the content says.
        /// </summary>
        /// <remarks>
        /// The whole reason the engine stops for a fought floor. Everything about the fight is
        /// already decided — the port's second invariant — and this is the part that takes
        /// twelve seconds.
        /// </remarks>
        private async UniTask Read(Ask fought, int tier, RunState run)
        {
            Pacing pacing = Pacing.For(fought.Result.Events.Count, Reduced(), Speed(),
                _content.Presentation.ToPacing());

            _view.Begin(fought.Result.Events, pacing, new CombatLog(_speech.Locale),
                Shelf.Of(run.Hero), false, tier);

            if (_showing == null) _showing = new CombatPlaybackController(_content.Presentation);

            await _showing.Show(fought.Result.Events, _view, SkipsIntro());
        }

        /// <summary>
        /// Writes down every species the delver has now met.
        /// </summary>
        /// <remarks>
        /// The bestiary's fog is lifted here rather than by the run layer, because meeting
        /// something is a fact about the DELVER and not about the run: it survives the run
        /// ending badly, which is most of the point of a bestiary.
        /// </remarks>
        private static void Met(SaveState earned, IReadOnlyList<EnemyState> pack)
        {
            if (earned == null || pack == null) return;

            foreach (EnemyState foe in pack)
            {
                if (!earned.Seen.Contains(foe.SpeciesIndex)) earned.Seen.Add(foe.SpeciesIndex);
            }
        }

        /// <summary>
        /// What a delver with no screen to press would do.
        /// </summary>
        /// <remarks>
        /// One answer per stop, and every one of them is the dullest available: it takes what it
        /// is shown, spends nothing, and does not ask to be brought back. That is deliberate — a
        /// scaffold that made INTERESTING choices would be a scaffold somebody mistook for the
        /// game, and every one of these is a line that a screen will delete.
        /// </remarks>
        private static Answer Plainly(Ask ask)
        {
            switch (ask.Kind)
            {
                case AskKind.Draft:
                    return new Answer { Pick = ask.Offer[0] };

                case AskKind.Event:
                    return new Answer { Choice = 0 };

                case AskKind.Bazaar:
                    return new Answer { Deal = BazaarDeal.Walk };

                default:
                    return new Answer();
            }
        }

        /// <summary>
        /// Puts the finished run into the save, and works out what to tell the delver.
        /// </summary>
        /// <remarks>
        /// The level and the progress are read BEFORE settling, because they are what the
        /// experience bar opens on: reading them afterwards would fill a bar that is already
        /// full. Everything else the end screen shows comes out of <c>Settled</c>, which is the
        /// same scoring and banking the corpus gates.
        /// </remarks>
        private void Settle(Delve delve, RunOrder order, SaveState earned)
        {
            if (Gone) return;

            int level = earned.Level;
            double progress = Progression.Progress(earned.Xp);
            List<string> before = Career.NewlyEarned(new List<string>(), earned);

            double rate = Progression.RewardMultiplier(order.Tier, Career.Frontier(earned));

            string name = _vault != null && _vault.Chosen != null ? _vault.Chosen.Name : null;

            Settled settled = Career.Settle(earned, delve.State, delve.Ending, order.Tier,
                order.Daily, order.Day, name, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            if (_vault != null) _vault.CommitProgress();

            Debug.Log("the delve ended on floor " + delve.EndedOn + " (" + delve.Ending +
                      "): " + settled.Reward.Score + " points, " + settled.Reward.Xp +
                      " experience, " + settled.Reward.Banked + " banked", this);

            if (_order != null)
            {
                _order.Report(new RunReport
                {
                    Over = OverCards.Of(settled.Reward, delve.Ending, delve.EndedOn,
                        delve.State.Hero.Kills, settled.Banked.NewBest, null, rate),

                    Level = level,
                    Progress = progress,
                    Gains = Career.NewlyEarned(before, earned),
                });
            }

            Leave();
        }

        /// <summary>
        /// What was ordered, or the authored fight's hall, or nothing.
        /// </summary>
        /// <remarks>
        /// The fallback is the whole reason the harness still exists: opening this scene on its
        /// own — pressing Play with the fight prefab as the startup scene — has to show
        /// something, or the combat layer becomes a place you can only reach by playing the game
        /// up to it. It says which one it took, because a scene showing the wrong run is
        /// otherwise a silent mystery.
        /// </remarks>
        private bool Ordered(out RunOrder order)
        {
            if (_order != null && _order.Take(out order)) return true;

            order = new RunOrder { Seed = 1u, Tier = 1, Daily = false };

            if (_harness == null || !_harness.Ready)
            {
                Debug.LogError("nothing ordered a delve and there is no authored fight to fall " +
                               "back on — run Tools > Relic Run > Build Fight Scene", this);
                return false;
            }

            FightSettings watching = _harness.Watching;

            order.Seed = watching.Seed;
            order.Tier = watching.Hall;

            Debug.Log("nothing ordered a delve, so the authored fight's seed and hall are being " +
                      "walked instead: " + _harness.Describe(), this);

            return true;
        }

        /// <summary>Back to the menu, which opens on the end of the run.</summary>
        /// <remarks>
        /// The run layer will put a draft and a floor rail in this scene rather than sending the
        /// delver away between floors. What a FINISHED run does is go home, and it has to go
        /// somewhere — a screen that stops on its last frame is indistinguishable from one that
        /// crashed.
        /// </remarks>
        private void Leave()
        {
            if (_scenes == null)
            {
                Debug.LogWarning("the delve is over and nothing can load the menu");
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
                    Debug.LogError("the delve is over and nothing loaded for the menu — the " +
                                   "scene service logged the reason as an ordinary message.");
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <summary>
        /// Fetches the delver's language.
        /// </summary>
        /// <remarks>
        /// The fight's log is the one place in the game a delver reads sentences rather than
        /// labels. A failure is a warning and a run narrated in keys, which is ugly and
        /// diagnosable — where a blank log is neither.
        /// </remarks>
        private async UniTask Say()
        {
            _speech = new Speech();

            if (_content.Locales == null)
            {
                Debug.LogWarning("no locale book, so the delve is narrated in keys", this);
                return;
            }

            try
            {
                await _speech.Learn(_content.Locales, _vault != null ? _vault.Chosen : null,
                    Speech.Asked());
            }
            catch (Exception broken)
            {
                Debug.LogWarning("could not fetch the strings, so the delve speaks in keys: " +
                                 broken.Message, this);
            }
        }

        /// <summary>How fast to read it out, from the authored settings when there are any.</summary>
        /// <remarks>
        /// Watching preferences, not run ones — how fast and how still are about the person
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

        /// <summary>Finds whoever makes the noises and hands them to the screen.</summary>
        /// <remarks>
        /// A delve with no sound is still a delve, so a missing service is said once and stepped
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
        /// scope has been told to register it, and this scene has no installer doing that, so an
        /// attribute would have looked like wiring and done nothing at all.
        /// </remarks>
        private T Resolve<T>(string what) where T : class
        {
            var scope = GetComponent<LifetimeScope>();

            if (scope == null || scope.Container == null)
            {
                Debug.LogWarning("no lifetime scope on the delve, so there is no " + what, this);
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
