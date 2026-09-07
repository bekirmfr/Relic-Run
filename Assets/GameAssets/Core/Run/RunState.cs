using System.Collections.Generic;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;

namespace RelicRun.Core.Run
{
    /// <summary>Gold that arrives on a later floor, promised by an event.</summary>
    public struct PendingGold
    {
        public int Floor;
        public int Gold;
    }

    /// <summary>
    /// A delve in progress: the hero, plus the few things only the run loop cares about.
    /// </summary>
    /// <remarks>
    /// The source keeps one state object for the whole run and hands it straight to the fight.
    /// This splits it: <see cref="Hero"/> is the part combat reads and writes, and the rest
    /// lives out here. Nothing is copied between them — the run passes <see cref="Hero"/> to
    /// the engine by reference — so the two cannot drift.
    ///
    /// Awakenings are held as counts rather than a set because the bazaar can awaken the same
    /// relic more than once. Nothing reads the count back; every rule asks only whether a relic
    /// is awake at all, which is what <see cref="Hero"/>'s Awakened collection carries.
    /// </remarks>
    public sealed class RunState
    {
        public readonly HeroState Hero = new HeroState();

        /// <summary>Inventory in draft order, duplicates included. Aliased into the hero.</summary>
        public readonly List<RelicId> Items = new List<RelicId>();

        /// <summary>How many copies of each relic have been awakened.</summary>
        public readonly Dictionary<RelicId, int> Awakened = new Dictionary<RelicId, int>();

        /// <summary>Rerolls bought across the whole run. The price ladder reads this.</summary>
        public int Rerolls;

        /// <summary>Gold an event promised on a later floor.</summary>
        public PendingGold? Pending;

        public int Floor
        {
            get { return Hero.Floor; }
            set { Hero.Floor = value; }
        }

        public int Php
        {
            get { return Hero.Php; }
            set { Hero.Php = value; }
        }

        public int Pmax
        {
            get { return Hero.Pmax; }
            set { Hero.Pmax = value; }
        }

        public int Gold
        {
            get { return Hero.Gold; }
            set { Hero.Gold = value; }
        }

        public RunState()
        {
            Hero.Items = Items;
            Hero.Awakened = Awakened.Keys;
        }

        public int Count(RelicId id)
        {
            int n = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i] == id) n++;
            }

            return n;
        }

        public bool Has(RelicId id) { return Count(id) > 0; }

        public bool IsAwake(RelicId id)
        {
            int n;
            return Awakened.TryGetValue(id, out n) && n > 0;
        }

        /// <summary>Awakens one more copy. The bazaar's only lasting purchase.</summary>
        public void Awaken(RelicId id)
        {
            int n;
            Awakened[id] = (Awakened.TryGetValue(id, out n) ? n : 0) + 1;
        }

        /// <summary>How many relics of a kind are in hand. A Hollow Idol counts toward every set.</summary>
        public int SetCount(RelicKind kind)
        {
            int n = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                if (RelicCatalog.KindOf(Items[i]) == kind) n++;
            }

            return n + Count(RelicId.HollowIdol);
        }
    }
}
