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

        [Header("The fight")]
        [Tooltip("Fixed, so the same fight can be watched twice and talked about.")]
        [SerializeField] private uint _seed = 0x5E1F00D;

        [Tooltip("Which floor of the first hall. Seven is the bazaar and has no fight.")]
        [Range(1, 13)] [SerializeField] private int _floor = 1;

        [Tooltip("The delver's level, which sets their opening stats.")]
        [Min(1)] [SerializeField] private int _level = 1;

        [Header("Watching")]
        [SerializeField] private bool _playOnStart = true;

        [Tooltip("Skips the walk down the hall, which is three and a half seconds of scenery.")]
        [SerializeField] private bool _skipIntro;

        private CombatPlaybackController _showing;

        private void Start()
        {
            if (_playOnStart) Fight().Forget();
        }

        /// <summary>Resolves a fight and shows it, start to end.</summary>
        public async UniTask Fight()
        {
            if (_view == null || _content == null)
            {
                Debug.LogError("the harness has nothing to show or nothing to show it with", this);
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

            IReadOnlyList<CombatEvent> events = Resolve();
            Debug.Log("seed " + _seed + ", floor " + _floor + ": " + events.Count + " events", this);

            Pacing pacing = Pacing.For(events.Count, false, 1, _content.Presentation.ToPacing());

            _view.Begin(events, pacing, Reading());
            _showing = new CombatPlaybackController(_content.Presentation);

            await _showing.Show(events, _view, _skipIntro);
        }

        /// <summary>
        /// The fight itself, resolved before a single frame of it is drawn.
        /// </summary>
        /// <remarks>
        /// Invariant 2. Nothing in the presentation layer computes combat, so this returns a
        /// finished list and the screen's only job afterwards is to read it out.
        /// </remarks>
        private IReadOnlyList<CombatEvent> Resolve()
        {
            RunSetup setup = RunSetup.ForLevel(_level);

            var hero = new HeroState
            {
                Floor = _floor,
                Php = setup.Hp,
                Pmax = setup.Hp,
                Gold = setup.Gold,
                BaseAtk = setup.Atk,
                BaseDef = setup.Def,
                BaseSpd = setup.Spd,
                BaseLck = setup.Lck,
            };

            var rng = new Mulberry32(_seed);
            List<EnemyState> pack = EnemyPackGenerator.Build(_floor, rng, setup.Dungeon);

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
