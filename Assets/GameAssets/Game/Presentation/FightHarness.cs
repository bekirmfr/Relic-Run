using RelicRun.Core.Combat;
using RelicRun.Core.Determinism;
using RelicRun.Core.Run;
using RelicRun.Game.Data;
using UnityEngine;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// One fight, written down: the editor's way of asking for a particular one.
    /// </summary>
    /// <remarks>
    /// It used to BE the fight scene — resolving a floor, driving the playback and owning the
    /// screen — which was right while there was nothing else to fight. Now the scene fights what
    /// it is ORDERED to, and this is one of the two places an order can come from: the other is a
    /// delver pressing PLAY.
    ///
    /// So it is a fallback rather than the main road, and that is the point of keeping it. A
    /// fight that only exists when a run reaches it is a fight nobody can sit and stare at, and
    /// the ability to open the scene on a chosen floor of a chosen hall with a chosen shelf is
    /// most of how anything in the combat layer has ever been looked at.
    /// </remarks>
    public sealed class FightHarness : MonoBehaviour
    {
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

        /// <summary>Whether there is an authored fight to fall back on at all.</summary>
        public bool Ready
        {
            get { return _fight != null; }
        }

        /// <summary>How the authored fight is to be watched, rather than what it is.</summary>
        public FightSettings Watching
        {
            get { return _fight; }
        }

        /// <summary>
        /// The authored fight, as a plan.
        /// </summary>
        /// <remarks>
        /// The pack is built HERE rather than left to <see cref="Bout"/>, because the asset may
        /// override it — an authored pack is most of what this exists for. Building it costs the
        /// same draws the generator would have cost, from a generator seeded the same way, so an
        /// asset that overrides nothing produces exactly the fight the plan would have rolled.
        ///
        /// The hall it is scaled for is the hall it SAYS, which it was not before: the harness
        /// used to scale by whatever <c>RunSetup.ForLevel</c> carried, which is nothing at all,
        /// so every authored fight was fought at the first hall's difficulty whichever backdrop
        /// it was watched against. Authored fights therefore hit harder from the second hall
        /// down — which is what the setting always claimed.
        /// </remarks>
        public FightPlan Plan(out int ceiling)
        {
            ceiling = 0;

            if (_fight == null) return null;

            HeroState hero = _fight.Delver.Build(_fight.Floor);

            ceiling = hero.Pmax;

            // One stream for the pack and the fight, which is why an authored pack changes the
            // whole fight and not just who is standing in it: generating one CONSUMES draws, so
            // skipping that leaves every later roll reading a different part of the sequence.
            var rng = new Mulberry32(_fight.Seed);

            return new FightPlan
            {
                Seed = _fight.Seed,
                Floor = _fight.Floor,
                Tier = _fight.Hall,
                Delver = hero,
                Pack = _fight.Foes.Build(_fight.Floor, rng, DungeonConfig.ForTier(_fight.Hall)),
            };
        }

        /// <summary>What this fight is, in one line, for the log.</summary>
        public string Describe()
        {
            return _fight != null ? _fight.Describe() : "no authored fight";
        }
    }
}
