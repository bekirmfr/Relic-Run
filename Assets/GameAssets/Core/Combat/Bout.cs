using System;
using System.Collections.Generic;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Run;

namespace RelicRun.Core.Combat
{
    /// <summary>
    /// One floor's worth of fighting, and everything needed to resolve it.
    /// </summary>
    /// <remarks>
    /// A plan is what a caller KNOWS before the fight: who is descending, where, and from which
    /// seed. What they meet is normally not part of it — a floor's pack is rolled from the same
    /// stream the fight draws from — so <see cref="Pack"/> is left null and generated, and
    /// filling it in is for a caller that has one already, which today means a run resuming a
    /// floor and an authored fight in the editor.
    /// </remarks>
    public sealed class FightPlan
    {
        /// <summary>The stream everything is drawn from. Fixed, so a fight can be watched twice.</summary>
        public uint Seed;

        /// <summary>Which floor of the hall, counting from one.</summary>
        public int Floor = 1;

        /// <summary>
        /// Which hall, counting from one.
        /// </summary>
        /// <remarks>
        /// Two things at once, and they are the same thing: it picks the backdrop the delver
        /// walks down, and through <see cref="DungeonConfig.ForTier"/> it multiplies what waits
        /// at the bottom of it. A deeper hall looks different because it IS harder.
        /// </remarks>
        public int Tier = 1;

        /// <summary>Who is descending. The engine's working copy — see <see cref="Fight"/>.</summary>
        public HeroState Delver;

        /// <summary>
        /// What waits, or null to roll the floor's own.
        /// </summary>
        /// <remarks>
        /// Null is the normal case and is not the same as an empty list. Generating a pack
        /// CONSUMES draws from the fight's stream, so a caller that supplies one has moved every
        /// later roll — the same seed then describes a different fight, which is fine until
        /// somebody compares the two and concludes the engine moved.
        /// </remarks>
        public IReadOnlyList<EnemyState> Pack;
    }

    /// <summary>How a fight went, in the few numbers anything outside it needs.</summary>
    public struct FightSummary
    {
        /// <summary>Whether the delver walked out of it.</summary>
        public bool Won;

        /// <summary>What they had left, clamped to nothing.</summary>
        public int Left;

        /// <summary>And the ceiling that is measured against.</summary>
        public int Most;

        /// <summary>What is in the purse now.</summary>
        public int Gold;

        /// <summary>How many fell.</summary>
        public int Kills;

        /// <summary>How many events the fight produced, which is how long it takes to watch.</summary>
        public int Beats;
    }

    /// <summary>
    /// A fight, resolved.
    /// </summary>
    /// <remarks>
    /// The thin piece between a caller who knows WHAT fight and an engine that knows how. It
    /// exists because there were two callers doing it differently: the editor's harness, which
    /// built a pack and ran the engine inline, and a run, which does the same thing surrounded by
    /// draft and breather. Two spellings of one procedure is one chance for a screen to show a
    /// fight the run layer would not have fought.
    ///
    /// Everything is resolved before a single frame is drawn — the port's second invariant.
    /// Nothing in the presentation layer computes combat; it reads out a finished list.
    /// </remarks>
    public static class Bout
    {
        /// <summary>
        /// Resolves the plan, start to end.
        /// </summary>
        /// <remarks>
        /// The hero is MUTATED, deliberately: it is the engine's working copy, and what it holds
        /// afterwards is the state the fight ended in. A caller that wants the state it started
        /// with has to have kept one.
        ///
        /// One stream for the pack and the fight, which is why supplying a pack changes the whole
        /// fight rather than only who is standing in it.
        /// </remarks>
        public static CombatResult Fight(FightPlan plan, CombatRules rules = null)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (plan.Delver == null) throw new ArgumentNullException("plan.Delver");

            var rng = new Mulberry32(plan.Seed);

            IReadOnlyList<EnemyState> pack = plan.Pack ??
                EnemyPackGenerator.Build(plan.Floor, rng, DungeonConfig.ForTier(plan.Tier));

            plan.Delver.Floor = plan.Floor;

            return new CombatEngine(rules ?? CombatRules.Delve()).ResolveFloor(plan.Delver, pack, rng);
        }

        /// <summary>
        /// What the fight came to.
        /// </summary>
        /// <remarks>
        /// Read from the hero AFTER the fight, and clamped to the ceiling the floor opened with
        /// — which is what a run does at the same point, and matters: a Bottomless Chalice that
        /// grew the pool mid-fight raises the ceiling only once the fight is over, so the health
        /// it bought does not arrive with it. Reading the raw pool instead would show a delver
        /// leaving a floor with more health than they could hold.
        /// </remarks>
        /// <param name="ceiling">The delver's maximum as the floor OPENED, before any growth.</param>
        public static FightSummary Read(FightPlan plan, CombatResult result, int ceiling)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (plan.Delver == null) throw new ArgumentNullException("plan.Delver");

            HeroState hero = plan.Delver;

            int left = hero.Php < ceiling ? hero.Php : ceiling;

            return new FightSummary
            {
                // Above nothing, not above the clamp. A hero on exactly zero is dead, and the
                // engine leaves the pool below zero on a killing blow rather than at it.
                Won = left > 0,

                Left = left < 0 ? 0 : left,
                Most = hero.Pmax,
                Gold = hero.Gold,
                Kills = hero.Kills,
                Beats = result != null && result.Events != null ? result.Events.Count : 0,
            };
        }

        /// <summary>
        /// A delver at the start of a run, built from what their level buys.
        /// </summary>
        /// <remarks>
        /// The same seeding <see cref="DelveRun.Resolve"/> does, in one place both can be held
        /// to. A fight scene that built its own hero would drift from the run layer silently: the
        /// numbers are plausible either way, and the only symptom is that a delver's first floor
        /// plays differently depending on which screen started it.
        /// </remarks>
        public static HeroState Delver(RunSetup setup)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));

            var items = new List<RelicId>();

            if (setup.StartKit != null) items.AddRange(setup.StartKit);

            return new HeroState
            {
                Php = setup.Hp,
                Pmax = setup.Hp,
                Gold = setup.Gold,
                BaseAtk = setup.Atk,
                BaseDef = setup.Def,
                BaseSpd = setup.Spd,
                BaseLck = setup.Lck,
                Items = items,
                Floor = 1,
            };
        }
    }
}
