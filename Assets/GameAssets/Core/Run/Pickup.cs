using System;
using System.Collections.Generic;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;

namespace RelicRun.Core.Run
{
    /// <summary>
    /// Taking a relic into the run.
    /// </summary>
    /// <remarks>
    /// One place for every pickup side effect, because a relic is picked up from three
    /// directions — the draft, the bazaar, and an event that hands one over — and they must all
    /// do the same thing.
    /// </remarks>
    public static class Pickup
    {
        public static void Take(RunState run, RelicId id) { Take(run.Hero, run.Items, id); }

        /// <summary>
        /// Takes a relic into a hand. Written against the hero and the inventory rather than a
        /// run, because a delve and a versus match are different things that pick relics up in
        /// exactly the same way.
        /// </summary>
        public static void Take(HeroState hero, List<RelicId> items, RelicId id)
        {
            items.Add(id);

            // An Ox Heart thickens the hero on the spot.
            if (id == RelicId.OxHeart)
            {
                hero.Pmax += 13;
                hero.Php = Math.Min(hero.Pmax, hero.Php + 13);
            }

            // The Midas Blade is sharpened once, by whatever is in the purse when it is taken.
            if (id == RelicId.MidasBlade)
            {
                hero.MidasBonus = Math.Max(1, (int)Math.Floor(hero.Gold / 50.0));
            }

            // A Hollow Idol counts toward every set, and costs a slice of the pool for it.
            if (id == RelicId.HollowIdol)
            {
                hero.Pmax = Math.Max(10, hero.Pmax - 15);
                hero.Php = Math.Min(hero.Php, hero.Pmax);
            }

            // The Blood Pact's signing fee.
            if (id == RelicId.BloodPact) hero.Php = Math.Max(1, hero.Php - 20);

            // The source also pays out a Debtor's Chain here — 40 gold now against 60 owed —
            // but that relic is absent from the draft table and so is cut from the port. See
            // docs/relics-cut.md; reintroducing it means restoring this branch.

            // The Flesh set at three thickens the hero by a tenth, once per run.
            //
            // The flag is shared with the fight's own Flesh-set grant, exactly as the source
            // shares it: whichever fires first spends it. In a real run that is always this
            // one, because the first draft precedes the first fight.
            if (FleshCount(items) >= 3 && !hero.FleshSetApplied)
            {
                hero.FleshSetApplied = true;
                int bonus = JsMath.RoundToInt(hero.Pmax * 0.10);
                hero.Pmax += bonus;
                hero.Php = Math.Min(hero.Pmax, hero.Php + bonus);
            }
        }

        /// <summary>Relics of the Flesh kind in hand. A Hollow Idol counts toward every set.</summary>
        private static int FleshCount(List<RelicId> items)
        {
            int flesh = 0, idols = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (RelicCatalog.KindOf(items[i]) == RelicKind.Flesh) flesh++;
                if (items[i] == RelicId.HollowIdol) idols++;
            }

            return flesh + idols;
        }
    }
}
