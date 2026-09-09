using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;
using RelicRun.Game.Data;
using UnityEngine;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// Resolves one fight and watches it. The smallest thing that is actually a game.
    /// </summary>
    /// <remarks>
    /// A scaffold, and honest about it: there is no run around this fight, no draft before it and
    /// nothing after it. What it proves is the whole chain — an engine resolving a floor, a
    /// pacing built from its length, a loop walking it, and a screen drawing what it is told —
    /// and that chain is the thing the rest of Phase 9 hangs from.
    ///
    /// The seed is fixed and shown. A fight nobody can reproduce is a fight nobody can
    /// investigate, and the first thing anybody asks about a strange-looking fight is what seed
    /// it was.
    /// </remarks>
    public sealed class FightHarness : MonoBehaviour
    {
        [SerializeField] private CombatView _view;
        [SerializeField] private GameContent _content;

        /// <summary>
        /// Which fight to show. An asset, so that editing it survives a rebuild.
        /// </summary>
        /// <remarks>
        /// These used to be fields right here, which lasted until somebody edited them: this
        /// component lives inside a generated hierarchy, and <c>Build Fight Scene</c> deletes the
        /// whole thing and adds it back from scratch. Every setting typed into the inspector was
        /// thrown away by the next rebuild, silently, with the harness then showing a fight
        /// nobody had asked for.
        /// </remarks>
        [SerializeField] private FightSettings _fight;

        private CombatPlaybackController _showing;
        private bool _fighting;

        /// <summary>
        /// Resolves a fight and shows it, start to end.
        /// </summary>
        /// <remarks>
        /// Started by <see cref="FightScene"/> and by nothing else. This used to also start
        /// itself from <c>Start</c>, which meant that once the scene was loaded through the
        /// service two fights ran over one view: every log line spawned twice, and the doubled
        /// list looked like a fight where every blow landed twice rather than like a bug in the
        /// wiring. Deleting the second caller is the fix; the guard below is so there can never
        /// be a third.
        ///
        /// The guard returns rather than queueing. Two fights on one screen is not a thing that
        /// can be done slightly — the second would draw over the first's widgets — so the honest
        /// answer to being asked twice is to say so and refuse.
        /// </remarks>
        public async UniTask Fight()
        {
            if (_fighting)
            {
                Debug.LogWarning("a fight is already on screen; ignoring the second", this);
                return;
            }

            if (_view == null || _content == null)
            {
                Debug.LogError("the harness has nothing to show or nothing to show it with", this);
                return;
            }

            if (_fight == null)
            {
                Debug.LogError("no fight is set up — run Tools > Relic Run > Build Fight Scene, " +
                               "which makes one and wires it", this);
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

            _fighting = true;

            try
            {
                IReadOnlyList<CombatEvent> events = Resolve();
                // The settings live in an asset now, so the log names the asset as well as the
                // fight. Otherwise the first question about a strange fight — what was it? — has
                // no answer visible on the object being watched.
                Debug.Log(_fight.Describe() + ": " + events.Count + " events", _fight);

                Pacing pacing = Pacing.For(events.Count, _fight.ReducedMotion, _fight.Speed,
                    _content.Presentation.ToPacing());

                _view.Begin(events, pacing, Reading(), Shelf.Of(_delver), false);
                _showing = new CombatPlaybackController(_content.Presentation);

                await _showing.Show(events, _view, _fight.SkipIntro);
            }
            finally
            {
                // In a finally, because a fight that is abandoned still ends. Left set, the
                // guard above would turn one cancelled fight into a screen that refuses to show
                // any more of them — quietly, which is the worst way to refuse.
                _fighting = false;
            }
        }

        /// <summary>
        /// The hero the last fight was resolved for, kept so the tray can read their shelf.
        /// </summary>
        /// <remarks>
        /// The shelf only. Nothing else about this object is safe to read afterwards — it is the
        /// engine's working copy and holds the state the fight ENDED in, which is exactly why
        /// every number the tray animates comes off the events instead.
        /// </remarks>
        private HeroState _delver;

        /// <summary>
        /// The fight itself, resolved before a single frame of it is drawn.
        /// </summary>
        /// <remarks>
        /// Invariant 2. Nothing in the presentation layer computes combat, so this returns a
        /// finished list and the screen's only job afterwards is to read it out.
        /// </remarks>
        private IReadOnlyList<CombatEvent> Resolve()
        {
            HeroState hero = _fight.Delver.Build(_fight.Floor);
            _delver = hero;

            // One stream for both, which is why an authored pack changes the whole fight and not
            // just who is standing in it: generating a pack CONSUMES draws, so skipping that
            // leaves every later roll reading a different part of the sequence. The same seed
            // then describes a different fight, which is fine until somebody compares the two and
            // concludes the engine moved.
            var rng = new Mulberry32(_fight.Seed);

            List<EnemyState> pack = _fight.Foes.Build(_fight.Floor, rng,
                RunSetup.ForLevel(_fight.Delver.Level).Dungeon);

            return new CombatEngine(CombatRules.Delve()).ResolveFloor(hero, pack, rng).Events;
        }

        /// <summary>English, or nothing at all if the content has not been imported.</summary>
        private CombatLog Reading()
        {
            if (_content.Locales == null) return new CombatLog(null);

            var strings = Strings(LocaleBook.Fallback);
            return new CombatLog(strings == null ? null : new Locale(LocaleBook.Fallback, strings));
        }

        /// <summary>
        /// Reads a language out of the content, synchronously.
        /// </summary>
        /// <remarks>
        /// Through the editor asset rather than through Addressables, which is a scaffold's
        /// shortcut and marked as one. A real loader awaits the address; this is a harness that
        /// wants to be pressed and watched.
        /// </remarks>
        private Dictionary<string, string> Strings(string language)
        {
#if UNITY_EDITOR
            var address = _content.Locales.For(language);
            var text = address == null ? null : address.editorAsset as TextAsset;
            if (text == null) return null;

            var table = new Dictionary<string, string>();
            foreach (var line in Newtonsoft.Json.Linq.JObject.Parse(text.text).Properties())
            {
                table[line.Name] = line.Value.ToString();
            }

            return table;
#else
            return null;
#endif
        }

        /// <summary>
        /// Stops whatever is on screen.
        /// </summary>
        /// <remarks>
        /// Called when the screen is taken down, not only when the object dies. A playback loop
        /// that outlived its screen would go on drawing into destroyed widgets — a null
        /// reference per beat, which reads as the NEXT screen being broken.
        /// </remarks>
        public void Abandon()
        {
            if (_showing != null) _showing.Abandon();
        }

        private void OnDestroy()
        {
            if (_showing != null) _showing.Dispose();
        }
    }
}
