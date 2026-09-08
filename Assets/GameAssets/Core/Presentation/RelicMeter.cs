using RelicRun.Core.Combat;
using RelicRun.Core.Content;

namespace RelicRun.Core.Presentation
{
    /// <summary>
    /// A gauge counting toward the next time something fires.
    /// </summary>
    /// <remarks>
    /// Drawn as pips rather than a bar, which is why <see cref="Total"/> matters as much as the
    /// fill: three of three is a different shape from ten of ten, and a delver reads the shape
    /// before they read the number.
    /// </remarks>
    public readonly struct Cadence
    {
        /// <summary>How many of the way there. Always less than <see cref="Total"/>.</summary>
        public readonly int Filled;

        public readonly int Total;

        /// <summary>What is being counted, in words: "strikes", "hits taken".</summary>
        public readonly string Counting;

        public Cadence(int filled, int total, string counting)
        {
            Filled = filled;
            Total = total;
            Counting = counting;
        }

        public bool Any { get { return Total > 0; } }

        public override string ToString()
        {
            return Any ? Filled + "/" + Total + " " + Counting : "none";
        }
    }

    /// <summary>
    /// What is left of a relic's budget for this run.
    /// </summary>
    /// <remarks>
    /// Some relics do a thing a fixed number of times and are then ornaments. A delver who cannot
    /// see that has no way to know whether the Anvil Heart on their shelf is still worth
    /// anything, and finds out by it not happening.
    /// </remarks>
    public readonly struct Budget
    {
        public readonly int Left;
        public readonly int Cap;

        /// <summary>
        /// Floors still owed, for a relic that lends rather than spends.
        /// </summary>
        /// <remarks>
        /// Negative means this is not a debt at all — which, in the shipped pool, it never is.
        /// The source draws this gauge for the Debtor's Chain, and the Debtor's Chain is one of
        /// the twenty-eight relics the draft cannot offer. The shape is kept because a gauge that
        /// counts UP toward a reckoning is not a gauge that counts down toward nothing, and
        /// showing one as the other would read as good news; the relic that needs it is cut.
        /// </remarks>
        public readonly int Owed;

        public Budget(int left, int cap, int owed = -1)
        {
            Left = left;
            Cap = cap;
            Owed = owed;
        }

        public bool Any { get { return Cap > 0; } }

        public bool IsDebt { get { return Owed >= 0; } }

        public bool Spent { get { return Any && Left <= 0; } }

        public override string ToString()
        {
            if (!Any) return "none";

            return IsDebt ? Owed + " owed" : Left + "/" + Cap + " left";
        }
    }

    /// <summary>
    /// What one copy of a relic has to show for itself.
    /// </summary>
    /// <remarks>
    /// One COPY, not one relic. A socket is attached to an inventory slot, so the second Whetstone
    /// on the shelf can be counting toward something the first is not — which is the whole reason
    /// inventory is an ordered list with duplicates rather than a tally.
    ///
    /// Two cadences can run at once and both are shown. A socketed trigger fires every third
    /// strike; an Anvil fires every tenth on its own account; a relic with both is counting two
    /// different things and a delver watching one gauge would be surprised by the other.
    /// </remarks>
    public sealed class RelicMeter
    {
        /// <summary>The socket bolted to this copy, if any.</summary>
        public readonly Cadence Attached;

        /// <summary>The relic's own rhythm, if it has one.</summary>
        public readonly Cadence Native;

        /// <summary>What is left of its budget, if it has one.</summary>
        public readonly Budget Uses;

        public RelicMeter(Cadence attached, Cadence native, Budget uses)
        {
            Attached = attached;
            Native = native;
            Uses = uses;
        }

        /// <summary>Whether this copy has anything to show at all. Most do not.</summary>
        public bool Any { get { return Attached.Any || Native.Any || Uses.Any; } }

        /// <summary>How often a socketed trigger fires.</summary>
        public const int SocketEvery = 3;

        /// <summary>How often the Sundering Anvil sharpens, and how many times it may.</summary>
        public const int AnvilEvery = 10;

        public const int AnvilCap = 10;

        /// <summary>An awakened Anvil gets two more.</summary>
        public const int AwokenAnvilCap = 12;

        public const int SentinelCap = 3;

        /// <summary>
        /// What one copy of a relic is counting, and what it has left.
        /// </summary>
        /// <param name="socket">The trigger bolted to this copy, or None.</param>
        /// <param name="awakened">Whether this copy has been woken.</param>
        /// <param name="versus">Whether this is a duel. One relic only counts there.</param>
        public static RelicMeter For(RelicId relic, SocketTrigger socket, CombatCounters counters,
            HeroState hero, bool awakened, bool versus)
        {
            return new RelicMeter(Socketed(socket, counters), Rhythm(relic, counters, hero, awakened, versus),
                Spending(relic, hero, counters));
        }

        /// <summary>
        /// A socketed trigger's progress, which is every third of whatever it listens to.
        /// </summary>
        /// <remarks>
        /// Kept apart from the relic's own rhythm rather than merged, because a relic can have
        /// both and they count different things. Merging them would show one number for two
        /// clocks.
        /// </remarks>
        private static Cadence Socketed(SocketTrigger socket, CombatCounters counters)
        {
            switch (socket)
            {
                case SocketTrigger.Attack:
                    return new Cadence(counters.Strikes % SocketEvery, SocketEvery, "strikes");

                case SocketTrigger.Hit:
                    return new Cadence(counters.Pain % SocketEvery, SocketEvery, "hits taken");

                case SocketTrigger.Gold:
                    return new Cadence(counters.Gold % SocketEvery, SocketEvery, "gold gains");

                default:
                    return default(Cadence);
            }
        }

        /// <summary>The relic's own rhythm, for the four that have one.</summary>
        private static Cadence Rhythm(RelicId relic, CombatCounters counters, HeroState hero,
            bool awakened, bool versus)
        {
            switch (relic)
            {
                case RelicId.AnvilHeart:
                    // Only while it still has sharpenings left. A gauge filling toward something
                    // that cannot happen is a promise the relic will not keep.
                    return Sharpenings(hero, awakened) > 0
                        ? new Cadence(counters.StrikeTotal % AnvilEvery, AnvilEvery, "strikes")
                        : default(Cadence);

                case RelicId.QuenchedBlade:
                    return new Cadence(counters.QuenchCount % SocketEvery, SocketEvery,
                        "heals → +1 ATK");

                case RelicId.MomentumBead:
                    return new Cadence(counters.MomentumCount % SocketEvery, SocketEvery,
                        "strikes → +1 SPD");

                case RelicId.RabbitsFoot:
                    // Only in a duel. In a delve the Rabbit answers luck without counting to
                    // anything, so a gauge there would fill and never do a thing.
                    return versus
                        ? new Cadence(counters.RabbitCount % SocketEvery, SocketEvery,
                            "luck signals → +LCK")
                        : default(Cadence);

                default:
                    return default(Cadence);
            }
        }

        /// <summary>What is left of the budget, for the six that have one.</summary>
        private static Budget Spending(RelicId relic, HeroState hero, CombatCounters counters)
        {
            switch (relic)
            {
                case RelicId.AnvilHeart:
                    return new Budget(Sharpenings(hero, false), AnvilCap);

                case RelicId.SentinelBell:
                    return new Budget(Max(SentinelCap - counters.SentinelBonus), SentinelCap);

                case RelicId.GravekeepersSoil:
                    return new Budget(hero.SoilUsed ? 0 : 1, 1);

                default:
                    return default(Budget);
            }
        }

        /// <summary>
        /// How many sharpenings the Anvil has left.
        /// </summary>
        /// <remarks>
        /// The cap moves when the relic is woken and the count does not, so the same eight
        /// sharpenings leave two left or four depending on whether somebody paid at the bazaar.
        /// </remarks>
        private static int Sharpenings(HeroState hero, bool awakened)
        {
            return Max((awakened ? AwokenAnvilCap : AnvilCap) - hero.AnvilBonus);
        }

        private static int Max(int value) { return value < 0 ? 0 : value; }
    }
}
