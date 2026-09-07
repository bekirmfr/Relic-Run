using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;

namespace RelicRun.Core.Run
{
    /// <summary>One thing the hero can do about an event.</summary>
    public sealed class EventChoice
    {
        /// <summary>Gold it costs to take. Zero when it is free.</summary>
        public readonly int Cost;

        private readonly Action<RunState, Mulberry32> _resolve;

        public EventChoice(int cost, Action<RunState, Mulberry32> resolve)
        {
            Cost = cost;
            _resolve = resolve;
        }

        public void Resolve(RunState run, Mulberry32 rng) { _resolve(run, rng); }
    }

    /// <summary>An event between two floors.</summary>
    public sealed class DungeonEvent
    {
        public readonly int Index;

        /// <summary>Stable identity. The prose the player reads lives in the locale files.</summary>
        public readonly string Key;

        public readonly IReadOnlyList<EventChoice> Choices;

        public DungeonEvent(int index, string key, params EventChoice[] choices)
        {
            Index = index;
            Key = key;
            Choices = choices;
        }
    }

    /// <summary>
    /// The twelve things that can happen between floors.
    /// </summary>
    /// <remarks>
    /// Only the outcomes are here. Which event lands where is <see cref="DelveRun"/>'s, and
    /// which choice is taken is the player's — the source's headless harness picks greedily by
    /// reading the hint text, but that is a stand-in for a player and is not a rule.
    ///
    /// Events draw from their own random stream, separate from the fights, so a run's events
    /// are the same whatever happens in its combat.
    /// </remarks>
    public static class DungeonEvents
    {
        /// <summary>
        /// A gamble's odds lean on the hero's luck: every point of luck banked this run is
        /// worth half a percent.
        /// </summary>
        public static bool LuckRoll(RunState run, Mulberry32 rng, double baseChance)
        {
            return rng.Next() < baseChance + (10 + run.Hero.LuckBonus) / 200.0;
        }

        /// <summary>
        /// Hands the hero a relic. Unlike the draft, this draws from the MODE's pool rather
        /// than the whole table.
        /// </summary>
        public static RelicId GrantRelic(RunState run, Mulberry32 rng)
        {
            List<RelicId> pool = RelicCatalog.PoolFor(
                run.Hero.IsVersus ? GameModes.Versus : GameModes.Delve);

            var avail = new List<RelicId>(pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                if (!run.Rules.Stacks(pool[i]) && run.Has(pool[i])) continue;
                if (!RelicDraft.IsLive(pool[i], run.Items)) continue;
                avail.Add(pool[i]);
            }

            RelicId id = RelicDraft.Weighted(avail, run.Items, rng);
            Pickup.Take(run, id);
            return id;
        }

        private static EventChoice Free(Action<RunState, Mulberry32> resolve)
        {
            return new EventChoice(0, resolve);
        }

        private static EventChoice Costs(int gold, Action<RunState, Mulberry32> resolve)
        {
            return new EventChoice(gold, resolve);
        }

        /// <summary>Nothing happens. The safe way out of an event.</summary>
        private static EventChoice WalkAway() { return new EventChoice(0, (run, rng) => { }); }

        public static readonly IReadOnlyList<DungeonEvent> All = new[]
        {
            // 0 — The Pale Merchant: a relic, for coin or for blood.
            new DungeonEvent(0, "paleMerchant",
                Costs(60, (run, rng) => { run.Gold -= 60; GrantRelic(run, rng); }),
                Free((run, rng) => { run.Php -= 20; GrantRelic(run, rng); }),
                WalkAway()),

            // 1 — A Voice in the Dark: charity, repaid on the next floor.
            new DungeonEvent(1, "voiceInTheDark",
                Costs(10, (run, rng) =>
                {
                    run.Gold -= 10;
                    run.Pending = new PendingGold { Floor = run.Floor + 1, Gold = 30 };
                }),
                WalkAway()),

            // 2 — Scratching in the Walls: a coin flip, unweighted by luck.
            new DungeonEvent(2, "scratchingInTheWalls",
                Free((run, rng) =>
                {
                    if (rng.Next() < 0.5) run.Gold += 25;
                    else run.Php -= 6;
                }),
                WalkAway()),

            // 3 — The Chained Ghost: armor for speed.
            new DungeonEvent(3, "chainedGhost",
                Free((run, rng) =>
                {
                    run.Hero.DefBonus += 5;
                    run.Hero.SpdBonus -= 5;
                }),
                WalkAway()),

            // 4 — Knife at Your Throat: pay the thief, or take them on.
            new DungeonEvent(4, "knifeAtYourThroat",
                Costs(15, (run, rng) => { run.Gold -= 15; }),
                Free((run, rng) =>
                {
                    if (LuckRoll(run, rng, 0.6))
                    {
                        run.Gold += 20;
                    }
                    else
                    {
                        run.Php -= 8;
                        run.Gold = Math.Max(0, run.Gold - 10);
                    }
                })),

            // 5 — The Glimmering Pool: a free drink.
            new DungeonEvent(5, "glimmeringPool",
                Free((run, rng) => { run.Php += 8; }),
                WalkAway()),

            // 6 — Rusted Spike Trap: no good way across.
            new DungeonEvent(6, "rustedSpikeTrap",
                Free((run, rng) => { run.Hero.SpdBonus -= 5; }),
                Free((run, rng) => { run.Php -= 6; })),

            // 7 — The Imp's Dice: a straight bet.
            new DungeonEvent(7, "impsDice",
                Costs(15, (run, rng) =>
                {
                    if (LuckRoll(run, rng, 0.5)) run.Gold += 30;
                    else run.Gold -= 15;
                }),
                WalkAway()),

            // 8 — Altar of the Deep: blood for attack, or a quiet mercy.
            new DungeonEvent(8, "altarOfTheDeep",
                Free((run, rng) =>
                {
                    run.Php -= 5;
                    run.Hero.AtkBonus += 2;
                }),
                Free((run, rng) => { run.Php += 3; })),

            // 9 — The Glowing Mushroom: a bigger pool, or a smaller one.
            new DungeonEvent(9, "glowingMushroom",
                Free((run, rng) =>
                {
                    if (LuckRoll(run, rng, 0.5))
                    {
                        run.Pmax += 4;
                        run.Php += 4;
                    }
                    else
                    {
                        run.Php -= 4;
                    }
                }),
                WalkAway()),

            // 10 — The Dead Miner's Map: gold below, bought or stolen.
            new DungeonEvent(10, "deadMinersMap",
                Costs(10, (run, rng) =>
                {
                    run.Gold -= 10;
                    run.Pending = new PendingGold { Floor = run.Floor + 1, Gold = 25 };
                }),
                Free((run, rng) =>
                {
                    if (LuckRoll(run, rng, 0.5))
                    {
                        run.Pending = new PendingGold { Floor = run.Floor + 1, Gold = 25 };
                    }
                    else
                    {
                        run.Php -= 6;
                        run.Hero.SpdBonus -= 3;
                    }
                }),
                WalkAway()),

            // 11 — A Chest, Unclaimed: worse odds than they look, and unweighted by luck.
            new DungeonEvent(11, "chestUnclaimed",
                Free((run, rng) =>
                {
                    if (rng.Next() < 0.4) run.Gold += 40;
                    else
                    {
                        run.Hero.DefBonus -= 1;
                        run.Php -= 4;
                    }
                }),
                WalkAway()),
        };

        public static DungeonEvent Get(int index) { return All[index]; }
    }
}
