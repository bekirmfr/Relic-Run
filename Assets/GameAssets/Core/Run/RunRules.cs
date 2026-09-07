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
            return new RunRules(new Dictionary<RelicId, bool>());
        }
    }
}
