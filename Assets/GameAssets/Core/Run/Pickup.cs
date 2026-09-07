using System;
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
        public static void Take(RunState run, RelicId id)
        {
            run.Items.Add(id);

            // An Ox Heart thickens the hero on the spot.
            if (id == RelicId.OxHeart)
            {
                run.Pmax += 13;
                run.Php = Math.Min(run.Pmax, run.Php + 13);
            }

            // The Midas Blade is sharpened once, by whatever is in the purse when it is taken.
            if (id == RelicId.MidasBlade)
            {
                run.Hero.MidasBonus = Math.Max(1, (int)Math.Floor(run.Gold / 50.0));
            }

            // A Hollow Idol counts toward every set, and costs a slice of the pool for it.
            if (id == RelicId.HollowIdol)
            {
                run.Pmax = Math.Max(10, run.Pmax - 15);
                run.Php = Math.Min(run.Php, run.Pmax);
            }

            // The Blood Pact's signing fee.
            if (id == RelicId.BloodPact) run.Php = Math.Max(1, run.Php - 20);

            // The source also pays out a Debtor's Chain here — 40 gold now against 60 owed —
            // but that relic is absent from the draft table and so is cut from the port. See
            // docs/relics-cut.md; reintroducing it means restoring this branch.

            // The Flesh set at three thickens the hero by a tenth, once per run.
            //
            // The flag is shared with the fight's own Flesh-set grant, exactly as the source
            // shares it: whichever fires first spends it. In a real run that is always this
            // one, because the first draft precedes the first fight.
            if (run.SetCount(RelicKind.Flesh) >= 3 && !run.Hero.FleshSetApplied)
            {
                run.Hero.FleshSetApplied = true;
                int bonus = JsMath.RoundToInt(run.Pmax * 0.10);
                run.Pmax += bonus;
                run.Php = Math.Min(run.Pmax, run.Php + bonus);
            }
        }
    }
}
