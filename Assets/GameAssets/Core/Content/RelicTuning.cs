using System.Collections.Generic;

namespace RelicRun.Core.Content
{
    /// <summary>Which mode a fight is being resolved under.</summary>
    public enum CombatMode
    {
        Delve = 0,
        Versus = 1,
    }

    /// <summary>
    /// How one relic behaves in one mode.
    /// </summary>
    /// <remarks>
    /// A handful of relics are deliberately tuned differently in versus: a duel runs long
    /// enough that a relic which is fine over thirteen floors can snowball over twenty rounds.
    /// Those differences belong to the relic, not to the engine — this is the table a designer
    /// would look in to answer "what does Rabbit's Foot do in versus?", and the engine reads it
    /// rather than branching on the mode itself.
    ///
    /// Defaults describe the common case, so a relic only appears in
    /// <see cref="RelicTuning.Overrides"/> if it actually differs somewhere.
    /// </remarks>
    public sealed class RelicTuning
    {
        /// <summary>Whether a socketed trigger can fire this relic's own effect at all.</summary>
        public bool HasActivation = true;

        /// <summary>
        /// Whether an awakened copy counts as an extra copy when this relic ANSWERS an event.
        /// Distinct from its passive strength, which always counts awakening.
        /// </summary>
        public bool ReactionCountsAwakened = true;

        /// <summary>Whether it still answers when there is nothing left to strike.</summary>
        public bool AnswersWithoutTarget;

        /// <summary>Answers every Nth signal. Above 1 throttles a relic that would snowball.</summary>
        public int SignalCadence = 1;

        /// <summary>Ceiling on what this relic can accumulate in one fight.</summary>
        public int Cap = int.MaxValue;

        /// <summary>Ceiling once awakened.</summary>
        public int CapAwakened = int.MaxValue;

        /// <summary>Whether it works on the opponent as well as its bearer.</summary>
        public bool AffectsOpponent;

        /// <summary>Whether it counts itself toward every relic set.</summary>
        public bool CountsTowardEverySet;

        private static readonly RelicTuning Default = new RelicTuning();

        /// <summary>
        /// Every relic that is tuned differently between the modes. Anything absent behaves the
        /// same in both, which is the overwhelming majority.
        /// </summary>
        public static readonly IReadOnlyDictionary<RelicId, RelicTuning[]> Overrides =
            new Dictionary<RelicId, RelicTuning[]>
            {
                // Blood Altar answers a heal with violence. A duel lets an awakened Altar take
                // its tithe even when the blow lands on a corpse; a delve stops at a dead foe.
                {
                    RelicId.BloodAltar, Pair(
                        delve: new RelicTuning { ReactionCountsAwakened = false },
                        versus: new RelicTuning { AnswersWithoutTarget = true })
                },

                // Rabbit's Foot answers luck with luck. Over twenty rounds answering every
                // signal snowballs, so a duel throttles it to every third.
                {
                    RelicId.RabbitsFoot, Pair(
                        delve: new RelicTuning { ReactionCountsAwakened = false },
                        versus: new RelicTuning { SignalCadence = 3 })
                },

                // Sentinel Bell rings defence on a dodge. A duel is long enough to let an
                // awakened Bell climb higher before it stops.
                {
                    RelicId.SentinelBell, Pair(
                        delve: new RelicTuning { Cap = 3, CapAwakened = 3 },
                        versus: new RelicTuning { Cap = 3, CapAwakened = 5 })
                },

                // The Famine Bell starves the table. In versus it reaches across and starves
                // the opponent too, which is most of why it is worth carrying there.
                {
                    RelicId.FamineBell, Pair(
                        delve: new RelicTuning(),
                        versus: new RelicTuning { AffectsOpponent = true })
                },

                // Hollow Idol counts itself toward every set — a delve-only bargain. A duel
                // gives it nothing, so its max-HP cost is pure downside there.
                {
                    RelicId.HollowIdol, Pair(
                        delve: new RelicTuning { CountsTowardEverySet = true },
                        versus: new RelicTuning())
                },

                // The greed relics have no activation of their own in a duel: there is no loot
                // to amplify, so a socket cannot make them pay out.
                { RelicId.GreedyCurse, NoVersusActivation() },
                { RelicId.PiggyBank, NoVersusActivation() },
                { RelicId.CoinMagnet, NoVersusActivation() },
            };

        private static RelicTuning[] Pair(RelicTuning delve, RelicTuning versus)
        {
            return new[] { delve, versus };
        }

        private static RelicTuning[] NoVersusActivation()
        {
            return Pair(new RelicTuning(), new RelicTuning { HasActivation = false });
        }

        /// <summary>How this relic behaves in this mode.</summary>
        public static RelicTuning For(RelicId id, CombatMode mode)
        {
            RelicTuning[] pair;
            if (Overrides.TryGetValue(id, out pair))
            {
                return pair[(int)mode];
            }

            return Default;
        }
    }
}
