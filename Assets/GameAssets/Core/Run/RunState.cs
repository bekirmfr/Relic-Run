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
    /// Awakenings are keyed by INVENTORY SLOT, because that is what the bazaar awakens: the
    /// copy in hand, not the relic. Two Ox Hearts can be held with only one of them awake. The
    /// rules never ask which copy, so <see cref="Hero"/> carries the ids the slots resolve to.
    /// </remarks>
    public sealed class RunState
    {
        public readonly HeroState Hero = new HeroState();

        /// <summary>Inventory in draft order, duplicates included. Aliased into the hero.</summary>
        public readonly List<RelicId> Items = new List<RelicId>();

        /// <summary>Inventory slots whose copy has been awakened.</summary>
        public readonly HashSet<int> AwakenedSlots = new HashSet<int>();

        /// <summary>Which relics have at least one awakened copy. Aliased into the hero.</summary>
        private readonly HashSet<RelicId> _awake = new HashSet<RelicId>();

        /// <summary>Rerolls bought across the whole run. The price ladder reads this.</summary>
        public int Rerolls;

        /// <summary>Gold an event promised on a later floor.</summary>
        public PendingGold? Pending;

        /// <summary>Whether the one revive this run allows has been spent.</summary>
        public bool Revived;

        /// <summary>
        /// Health the last breather gave back, until the next fight opens and spends it.
        /// </summary>
        /// <remarks>
        /// It is carried rather than applied and forgotten because the fight reports it: the
        /// breath lands in the log as the floor opens, which is also where a Second Stomach
        /// announces itself. The run corpus records it at the draft, before the fight clears it.
        /// </remarks>
        public int BreathHealed;

        /// <summary>Floors a Duelist's Oath has been carried. It shatters on the fourth.</summary>
        public int OathCarried;

        /// <summary>Where this run's rules differ from the source's.</summary>
        public RunRules Rules = RunRules.Shipped();

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
            Hero.Awakened = _awake;
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

        public bool IsAwake(RelicId id) { return _awake.Contains(id); }

        /// <summary>How many copies of this relic are awake.</summary>
        public int AwakenedCount(RelicId id)
        {
            int n = 0;
            foreach (int slot in AwakenedSlots)
            {
                if (slot >= 0 && slot < Items.Count && Items[slot] == id) n++;
            }

            return n;
        }

        /// <summary>Awakens the copy held in one inventory slot. The bazaar's lasting purchase.</summary>
        public void Awaken(int slot)
        {
            if (slot < 0 || slot >= Items.Count) return;
            AwakenedSlots.Add(slot);
            RefreshAwakened();
        }

        /// <summary>
        /// Which copies the bazaar would offer to awaken: one per relic, stacking, and not
        /// already awake. The shelf shows at most six.
        /// </summary>
        public List<int> Awakenable()
        {
            var slots = new List<int>();
            var seen = new HashSet<RelicId>();

            for (int slot = 0; slot < Items.Count; slot++)
            {
                if (!Rules.Stacks(Items[slot])) continue;
                if (AwakenedSlots.Contains(slot)) continue;
                if (!seen.Add(Items[slot])) continue;

                slots.Add(slot);
                if (slots.Count == BazaarShelf) break;
            }

            return slots;
        }

        /// <summary>How many awakenings the bazaar will ever put on its shelf at once.</summary>
        public const int BazaarShelf = 6;

        /// <summary>
        /// Drops every copy of a relic from the tray — what a Duelist's Oath shattering does.
        /// </summary>
        /// <remarks>
        /// Awakenings are keyed by position, so something leaving the middle of the tray moves
        /// every one behind it. The source does not notice: it filters the inventory and leaves
        /// the awakening map alone, so an oath shattering out of slot one hands slot three's
        /// awakening to whatever slid into slot two. Under the shipped rules an awakening
        /// follows the copy it was bought for; <see cref="RunRules.AwakeningsFollowTheirCopy"/>
        /// is what puts the source's answer back for the gate.
        /// </remarks>
        public void Drop(RelicId id)
        {
            if (!Rules.AwakeningsFollowTheirCopy)
            {
                Items.RemoveAll(held => held == id);
                RefreshAwakened();
                return;
            }

            for (int slot = Items.Count - 1; slot >= 0; slot--)
            {
                if (Items[slot] != id) continue;

                Items.RemoveAt(slot);

                var kept = new List<int>(AwakenedSlots.Count);
                foreach (int awake in AwakenedSlots)
                {
                    if (awake == slot) continue;
                    kept.Add(awake > slot ? awake - 1 : awake);
                }

                AwakenedSlots.Clear();
                for (int i = 0; i < kept.Count; i++) AwakenedSlots.Add(kept[i]);
            }

            RefreshAwakened();
        }

        /// <summary>
        /// Recomputes which relics are awake from the slots that are.
        /// </summary>
        /// <remarks>
        /// Called whenever the inventory or the awakened slots change, because slots are
        /// positional: a Duelist's Oath shattering out of the middle of the tray shifts every
        /// slot behind it, and the source lets it, so an awakening can land on a neighbour.
        /// </remarks>
        public void RefreshAwakened()
        {
            _awake.Clear();
            foreach (int slot in AwakenedSlots)
            {
                if (slot >= 0 && slot < Items.Count) _awake.Add(Items[slot]);
            }
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
