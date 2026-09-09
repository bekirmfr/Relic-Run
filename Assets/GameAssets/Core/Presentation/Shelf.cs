using System.Collections.Generic;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;

namespace RelicRun.Core.Presentation
{
    /// <summary>
    /// One relic on the shelf, with whatever is bolted to it.
    /// </summary>
    /// <remarks>
    /// One COPY. Invariant 6: sockets key by inventory index, so the second Whetstone can be
    /// counting toward something the first is not, and a tray that showed one row per relic
    /// would have to pick which of them to lie about.
    ///
    /// Awakening is the exception and goes the other way — the source wakes by ID, so both
    /// Whetstones wake together. Kept as a field on the copy anyway, because that is where the
    /// tray reads it, and a copy that had to consult a second list to know its own cap is how
    /// the Anvil's two gauges came to disagree.
    /// </remarks>
    public readonly struct RelicCopy
    {
        public readonly RelicId Relic;
        public readonly SocketTrigger Trigger;
        public readonly SocketEmitter Emitter;
        public readonly bool Awakened;

        public RelicCopy(RelicId relic, SocketTrigger trigger = SocketTrigger.None,
            SocketEmitter emitter = SocketEmitter.None, bool awakened = false)
        {
            Relic = relic;
            Trigger = trigger;
            Emitter = emitter;
            Awakened = awakened;
        }

        public RelicKind Kind { get { return RelicCatalog.KindOf(Relic); } }

        public override string ToString()
        {
            return Relic + (Awakened ? " (awake)" : "") +
                   (Trigger != SocketTrigger.None ? " +" + Trigger : "") +
                   (Emitter != SocketEmitter.None ? " +" + Emitter : "");
        }
    }

    /// <summary>
    /// The delver's shelf, in order, for the length of one fight.
    /// </summary>
    /// <remarks>
    /// Static for the fight, which is why it is not in <see cref="CombatSnapshot"/>. Nothing
    /// picks up or drops a relic mid-floor: the shelf is settled before the first tick and the
    /// only thing that moves during playback is the counters. So the view is handed this once
    /// and the snapshot per event, and between them the tray needs nothing else — no live hero,
    /// no engine, no second source of truth to drift from the first.
    ///
    /// <see cref="OfKind"/> exists for the set bonuses, which are the one thing about a copy
    /// that depends on its NEIGHBOURS: a fifth Edge relic changes what the other four show.
    /// </remarks>
    public sealed class Shelf
    {
        private readonly IReadOnlyList<RelicCopy> _copies;
        private readonly Dictionary<RelicKind, int> _kinds = new Dictionary<RelicKind, int>();

        public Shelf(IReadOnlyList<RelicCopy> copies)
        {
            _copies = copies ?? new List<RelicCopy>();

            foreach (RelicCopy copy in _copies)
            {
                int already;
                _kinds.TryGetValue(copy.Kind, out already);
                _kinds[copy.Kind] = already + 1;
            }
        }

        public int Count { get { return _copies.Count; } }

        public RelicCopy this[int index] { get { return _copies[index]; } }

        /// <summary>How many copies on the shelf share a kind, which is what a set counts.</summary>
        public int OfKind(RelicKind kind)
        {
            int many;
            return _kinds.TryGetValue(kind, out many) ? many : 0;
        }

        /// <summary>
        /// The shelf a hero is carrying.
        /// </summary>
        /// <remarks>
        /// The one place the three separate lists on <see cref="HeroState"/> — items, sockets by
        /// index, awakenings by id — are folded into one thing per copy. Doing it anywhere else
        /// means every caller has to remember that sockets key by index and awakenings do not.
        /// </remarks>
        public static Shelf Of(HeroState hero)
        {
            var copies = new List<RelicCopy>();
            if (hero == null || hero.Items == null) return new Shelf(copies);

            for (int index = 0; index < hero.Items.Count; index++)
            {
                RelicId relic = hero.Items[index];

                SocketTrigger trigger;
                if (hero.SocketTriggers == null ||
                    !hero.SocketTriggers.TryGetValue(index, out trigger))
                {
                    trigger = SocketTrigger.None;
                }

                SocketEmitter emitter;
                if (hero.SocketEmitters == null ||
                    !hero.SocketEmitters.TryGetValue(index, out emitter))
                {
                    emitter = SocketEmitter.None;
                }

                bool awake = hero.Awakened != null && Contains(hero.Awakened, relic);

                copies.Add(new RelicCopy(relic, trigger, emitter, awake));
            }

            return new Shelf(copies);
        }

        private static bool Contains(IReadOnlyCollection<RelicId> awakened, RelicId relic)
        {
            foreach (RelicId each in awakened)
            {
                if (each == relic) return true;
            }

            return false;
        }
    }
}
