using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;

namespace RelicRun.Core.Run
{
    /// <summary>
    /// What a choice turned out to be, in the words the screen reads back.
    /// </summary>
    /// <remarks>
    /// An event is two beats and this is the second. A delver picks blind — the hint says what
    /// a choice COSTS, never what it does — and half the choices roll for it, so the sentence
    /// afterwards is the only place the run says what actually happened.
    ///
    /// The words are English and they live beside the logic they describe rather than in the
    /// generated table with the rest of an event's prose. A line saying "+25 gold" is a claim
    /// about a branch, and it belongs where somebody changing that branch will see it.
    /// </remarks>
    public struct EventOutcome
    {
        /// <summary>What happened. English, with <c>{relic}</c> where one was handed over.</summary>
        public string Text;

        /// <summary>Whether it went well, which is the colour the screen reads it in.</summary>
        public bool Good;

        /// <summary>The relic granted, or <see cref="RelicId.None"/>.</summary>
        public RelicId Relic;
    }

    /// <summary>One thing the hero can do about an event.</summary>
    public sealed class EventChoice
    {
        /// <summary>Gold it costs to take. Zero when it is free.</summary>
        public readonly int Cost;

        private readonly Func<RunState, Mulberry32, EventOutcome> _resolve;

        public EventChoice(int cost, Func<RunState, Mulberry32, EventOutcome> resolve)
        {
            Cost = cost;
            _resolve = resolve;
        }

        /// <summary>Takes it, and says what came of it.</summary>
        public EventOutcome Resolve(RunState run, Mulberry32 rng) { return _resolve(run, rng); }
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

        private static EventChoice Free(Func<RunState, Mulberry32, EventOutcome> resolve)
        {
            return new EventChoice(0, resolve);
        }

        private static EventChoice Costs(int gold, Func<RunState, Mulberry32, EventOutcome> resolve)
        {
            return new EventChoice(gold, resolve);
        }

        /// <summary>
        /// Nothing happens, and here is how the run says so.
        /// </summary>
        /// <remarks>
        /// Every event has a way out and each words it differently, which is most of what gives
        /// the twelve of them their character — so the sentence is the argument rather than the
        /// name of the helper.
        /// </remarks>
        private static EventChoice WalkAway(string said)
        {
            return new EventChoice(0, (run, rng) => Fine(said));
        }

        /// <summary>It went well.</summary>
        private static EventOutcome Fine(string said)
        {
            return new EventOutcome { Text = said, Good = true, Relic = RelicId.None };
        }

        /// <summary>And it did not.</summary>
        private static EventOutcome Ill(string said)
        {
            return new EventOutcome { Text = said, Good = false, Relic = RelicId.None };
        }

        /// <summary>It went well, and handed something over.</summary>
        /// <remarks>
        /// The relic is carried rather than named, because Core does not translate and nineteen
        /// of the fifty have names that are translated. The screen fills <c>{relic}</c>.
        /// </remarks>
        private static EventOutcome Gave(string said, RelicId relic)
        {
            return new EventOutcome { Text = said, Good = true, Relic = relic };
        }

        public static readonly IReadOnlyList<DungeonEvent> All = new[]
        {
            // 0 — The Pale Merchant: a relic, for coin or for blood.
            new DungeonEvent(0, "paleMerchant",
                Costs(60, (run, rng) =>
                {
                    run.Gold -= 60;
                    return Gave("It hands you {relic} and melts into the dark.",
                        GrantRelic(run, rng));
                }),
                Free((run, rng) =>
                {
                    run.Php -= 20;
                    return Gave("The knife stings. {relic} is yours.", GrantRelic(run, rng));
                }),
                WalkAway("You keep walking. The lantern fades behind you.")),

            // 1 — A Voice in the Dark: charity, repaid on the next floor.
            new DungeonEvent(1, "voiceInTheDark",
                Costs(10, (run, rng) =>
                {
                    run.Gold -= 10;
                    run.Pending = new PendingGold { Floor = run.Floor + 1, Gold = 30 };
                    return Fine("They limp off into the dark, whispering a promise.");
                }),
                new EventChoice(0, (run, rng) =>
                    Ill("Their curses follow you down the stair."))),

            // 2 — Scratching in the Walls: a coin flip, unweighted by luck.
            new DungeonEvent(2, "scratchingInTheWalls",
                Free((run, rng) =>
                {
                    if (rng.Next() < 0.5)
                    {
                        run.Gold += 25;
                        return Fine("A hidden cache! +25 gold.");
                    }

                    run.Php -= 6;
                    return Ill("Teeth. Many teeth. -6 HP.");
                }),
                WalkAway("Some walls are better left whole.")),

            // 3 — The Chained Ghost: armor for speed.
            new DungeonEvent(3, "chainedGhost",
                Free((run, rng) =>
                {
                    run.Hero.DefBonus += 5;
                    run.Hero.SpdBonus -= 5;
                    return Fine("Cold iron settles around you. +5 DEF, -5 SPD.");
                }),
                WalkAway("The ghost sighs and drifts through the wall.")),

            // 4 — Knife at Your Throat: pay the thief, or take them on.
            new DungeonEvent(4, "knifeAtYourThroat",
                Costs(15, (run, rng) =>
                {
                    run.Gold -= 15;
                    return Ill("The thief bows mockingly and vanishes.");
                }),
                Free((run, rng) =>
                {
                    if (LuckRoll(run, rng, 0.6))
                    {
                        run.Gold += 20;
                        return Fine("You win — and take their purse. +20 gold.");
                    }

                    run.Php -= 8;
                    run.Gold = Math.Max(0, run.Gold - 10);
                    return Ill("The blade finds you. -8 HP, -10 gold.");
                })),

            // 5 — The Glimmering Pool: a free drink.
            new DungeonEvent(5, "glimmeringPool",
                Free((run, rng) =>
                {
                    run.Php += 8;
                    return Fine("Warmth spreads through you. +8 HP.");
                }),
                WalkAway("You cup a handful, then let it fall.")),

            // 6 — Rusted Spike Trap: no good way across.
            new DungeonEvent(6, "rustedSpikeTrap",
                Free((run, rng) =>
                {
                    run.Hero.SpdBonus -= 5;
                    return Ill("You make it — but your boots are ruined. -5 SPD.");
                }),
                Free((run, rng) =>
                {
                    run.Php -= 6;
                    return Ill("Almost. A spike rakes your leg. -6 HP.");
                })),

            // 7 — The Imp's Dice: a straight bet.
            new DungeonEvent(7, "impsDice",
                Costs(15, (run, rng) =>
                {
                    if (LuckRoll(run, rng, 0.5))
                    {
                        run.Gold += 30;
                        return Fine("The dice smile on you. +30 gold.");
                    }

                    run.Gold -= 15;
                    return Ill("The imp cackles and pockets your coin. -15 gold.");
                }),
                WalkAway("The imp shrugs. \"Wise. Boring, but wise.\"")),

            // 8 — Altar of the Deep: blood for attack, or a quiet mercy.
            new DungeonEvent(8, "altarOfTheDeep",
                Free((run, rng) =>
                {
                    run.Php -= 5;
                    run.Hero.AtkBonus += 2;
                    return Fine("The altar drinks. Your arms feel like iron. +2 ATK.");
                }),
                Free((run, rng) =>
                {
                    run.Php += 3;
                    return Fine("A small mercy settles over you. +3 HP.");
                })),

            // 9 — The Glowing Mushroom: a bigger pool, or a smaller one.
            new DungeonEvent(9, "glowingMushroom",
                Free((run, rng) =>
                {
                    if (LuckRoll(run, rng, 0.5))
                    {
                        run.Pmax += 4;
                        run.Php += 4;
                        return Fine("Power blooms in your chest. +4 MAX HP.");
                    }

                    run.Php -= 4;
                    return Ill("Your stomach disagrees. Violently. -4 HP.");
                }),
                WalkAway("It pulses on, patient, for the next fool.")),

            // 10 — The Dead Miner's Map: gold below, bought or stolen.
            new DungeonEvent(10, "deadMinersMap",
                Costs(10, (run, rng) =>
                {
                    run.Gold -= 10;
                    run.Pending = new PendingGold { Floor = run.Floor + 1, Gold = 25 };
                    return Fine("Marked in a dead man's hand: where the gold hides below.");
                }),
                Free((run, rng) =>
                {
                    if (LuckRoll(run, rng, 0.5))
                    {
                        run.Pending = new PendingGold { Floor = run.Floor + 1, Gold = 25 };
                        return Fine("You pry the map from cold fingers. The dark stays " +
                                    "silent... this time.");
                    }

                    run.Php -= 6;
                    run.Hero.SpdBonus -= 3;
                    return Ill("The miner's grip tightens. A dead-man's curse: -6 HP, -3 SPD.");
                }),
                WalkAway("You leave the miner his map. Some bargains outlive the bargainer.")),

            // 11 — A Chest, Unclaimed: worse odds than they look, and unweighted by luck.
            new DungeonEvent(11, "chestUnclaimed",
                Free((run, rng) =>
                {
                    if (rng.Next() < 0.4)
                    {
                        run.Gold += 40;
                        return Fine("Actual treasure. Unbelievable. +40 gold.");
                    }

                    run.Hero.DefBonus -= 1;
                    run.Php -= 4;
                    return Ill("A curse of rot seeps out. -1 DEF, -4 HP.");
                }),
                WalkAway("You've read this story before. You walk on.")),
        };

        public static DungeonEvent Get(int index) { return All[index]; }
    }
}
