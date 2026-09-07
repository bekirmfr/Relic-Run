using System.Collections.Generic;
using RelicRun.Core.Content;

namespace RelicRun.Core.Run
{
    /// <summary>
    /// Where the run's rules deliberately differ from the source's.
    /// </summary>
    /// <remarks>
    /// Structural facts about a relic — its kind, what it emits, whether a second copy is worth
    /// anything — are generated from the source into <see cref="RelicCatalog"/> and are not
    /// edited by hand. When the game wants a different answer, it goes here instead, so the
    /// generated table stays a faithful record of where the port came from and the change is
    /// readable in one place.
    ///
    /// <see cref="AsRecorded"/> is the source's answers, and only the corpus gate uses it. The
    /// difference between the two IS the design change.
    /// </remarks>
    public sealed class RunRules
    {
        private readonly IReadOnlyDictionary<RelicId, bool> _stacks;

        private RunRules(IReadOnlyDictionary<RelicId, bool> stacks)
        {
            _stacks = stacks;
        }

        /// <summary>
        /// Whether the bazaar rolls the pack for the floor beyond it.
        /// </summary>
        /// <remarks>
        /// A fight ends by rolling the next floor's pack, so the bazaar is handed one it will
        /// never fight. The source then rolls a replacement while the merchant's hall pans past
        /// — but only on the animated path; with reduced motion set, the pan is skipped and so
        /// is the roll, and the floor after the bazaar fights the pack the bazaar was given.
        /// A gentler eighth floor for anyone who asked for less animation.
        ///
        /// The corpus records the reduced path, because that is the branch where presentation
        /// does not reach into the seeded stream. This rule is what keeps the port from
        /// inheriting the rest of it.
        /// </remarks>
        public bool BazaarRollsItsOwnPack = true;

        /// <summary>
        /// Whether the Flesh set stays granted once a floor has granted it.
        /// </summary>
        /// <remarks>
        /// The set gives +3 max HP, once per run, and the source guards it with a flag — but a
        /// delve builds each floor's fight state without copying that flag in, and never copies
        /// it back out. So the guard is fresh every floor and a Flesh delver quietly gains +3
        /// max HP per floor for the rest of the run. Versus does copy it, and gets the rule as
        /// written; so does the Balance Lab, which hands the run state straight to the fight,
        /// which is why the harness could never see this.
        ///
        /// The pickup's own grant — a tenth of the pool the moment the third Flesh relic is
        /// taken — is genuinely once per run in both, because that one reads the RUN's flag.
        /// </remarks>
        public bool FleshSetSurvivesTheFloor = true;

        /// <summary>
        /// Whether an awakening stays with the copy it was bought for when the tray shortens.
        /// </summary>
        /// <remarks>
        /// The only thing that leaves a delver's tray mid-run is a Duelist's Oath, which
        /// shatters after four floors. The source drops it with a filter and never touches the
        /// awakening map, which is keyed by position — so every awakening behind the oath slides
        /// onto its neighbour, and sixty gold spent on one relic ends up spent on another.
        /// </remarks>
        public bool AwakeningsFollowTheirCopy = true;

        /// <summary>
        /// Whether a second copy of this relic is worth holding. This decides both what the
        /// draft may offer again and what the bazaar may awaken — the source lets neither
        /// happen for a relic that does not stack.
        /// </summary>
        public bool Stacks(RelicId id)
        {
            bool overridden;
            return _stacks.TryGetValue(id, out overridden)
                ? overridden
                : RelicCatalog.Get(id).Stackable;
        }

        /// <summary>The rules the game ships.</summary>
        /// <remarks>
        /// A Debt of Flesh stacks here and does not in the source. It pays out in health at
        /// every counter and twice as much once awakened, but the bazaar only ever awakens a
        /// relic that stacks — so in the source the awakened half is unreachable, a rule that
        /// exists and can never fire. Letting it stack is what makes it real.
        /// </remarks>
        public static RunRules Shipped()
        {
            return new RunRules(new Dictionary<RelicId, bool>
            {
                { RelicId.DebtOfFlesh, true },
            });

        }

        /// <summary>The source's own answers. Nothing but the corpus gate should ask for these.</summary>
        public static RunRules AsRecorded()
        {
            return new RunRules(new Dictionary<RelicId, bool>())
            {
                BazaarRollsItsOwnPack = false,
                FleshSetSurvivesTheFloor = false,
                AwakeningsFollowTheirCopy = false,
            };
        }
    }
}
