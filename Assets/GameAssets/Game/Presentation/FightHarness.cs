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
    /// the ability to open the scene on a chosen hall from a chosen seed is most of how anything
    /// in the combat layer has ever been looked at.
    ///
    /// It carries settings and nothing else now. Building a fight out of them was its job until
    /// the run loop could be walked a stop at a time; a delve rolls its own, so what is left here
    /// is a seed, a hall, and how fast to read it out.
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

        /// <summary>What this fight is, in one line, for the log.</summary>
        public string Describe()
        {
            return _fight != null ? _fight.Describe() : "no authored fight";
        }
    }
}
