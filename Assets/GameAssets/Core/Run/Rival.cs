using System.Collections.Generic;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;

namespace RelicRun.Core.Run
{
    /// <summary>A rival delver's fixed statline, rolled once when the lobby is made.</summary>
    public struct RivalBase
    {
        public int Hp;
        public int Atk;
        public int Def;
        public int Spd;
        public int Lck;
    }

    /// <summary>
    /// One of the seven delvers in a versus lobby.
    /// </summary>
    /// <remarks>
    /// A rival persists across the whole match. Their statline is fixed at match-make and only
    /// their relics evolve, drafted between rounds by <see cref="RivalDraft"/>. What a duel
    /// leaves behind — banked attack, a spent Gravekeeper's Soil, a shattered Glass Edge —
    /// survives on <see cref="Carry"/> into their next one, so a rival the hero meets twice is
    /// the same delver rather than a fresh copy.
    /// </remarks>
    public sealed class Rival
    {
        public string Name;

        /// <summary>Within one of the hero's own, which is what flavours the lobby.</summary>
        public int Level;

        /// <summary>Lobby lives. At zero the delver is out.</summary>
        public int Lives = 3;

        /// <summary>The hall they host in, which the hero visits on a rematch.</summary>
        public int Hall = 1;

        /// <summary>Their purse, spent on rerolling their own draft.</summary>
        public int Gold;

        public readonly List<RelicId> Relics = new List<RelicId>();

        public RivalBase Base;

        /// <summary>What their last duel left them carrying, or null before their first.</summary>
        public DuelSide Carry;
    }

    /// <summary>
    /// How a rival builds its deck between rounds.
    /// </summary>
    /// <remarks>
    /// This is shipped AI, not a test harness: it is how the opponents a player actually faces
    /// get their relics, and it draws from the match's own generator, so it belongs here rather
    /// than in a recording. It is the counterpart of the Balance Lab's pickScore — the same
    /// shape of code, but that one stands in for a human and this one is a character in the
    /// game.
    ///
    /// It reads the round to decide what it wants: the first three rounds build a body, and
    /// after that it builds a win condition.
    /// </remarks>
    public static class RivalDraft
    {
        /// <summary>Rounds up to and including this one count as the early game.</summary>
        public const int EarlyRounds = 3;

        /// <summary>What a reroll costs a rival, and the floor of gold they need for one.</summary>
        public const int RerollCost = 10;

        /// <summary>Below this, the bot considers both offers weak enough to pay to redraw.</summary>
        public const double WeakOffer = 5;

        private static readonly Dictionary<RelicId, double> EarlyValue = new Dictionary<RelicId, double>
        {
            { RelicId.OxHeart, 9 }, { RelicId.IronSkin, 8 }, { RelicId.VampireTooth, 8 },
            { RelicId.Whetstone, 8 }, { RelicId.AlchemistsVial, 6 }, { RelicId.ThornVest, 5 },
            { RelicId.SwiftBoots, 4 }, { RelicId.BerserkerCharm, 4 }, { RelicId.LuckyClover, 4 },
            { RelicId.WeightedDice, 3 }, { RelicId.BattleDash, 3 }, { RelicId.RabbitsFoot, 3 },
            { RelicId.AdrenalineGland, 5 },
        };

        private static readonly Dictionary<RelicId, double> LateValue = new Dictionary<RelicId, double>
        {
            { RelicId.WeightedDice, 9 }, { RelicId.SwiftBoots, 8 }, { RelicId.LuckyClover, 8 },
            { RelicId.BattleDash, 7 }, { RelicId.RabbitsFoot, 7 }, { RelicId.BloodAltar, 7 },
            { RelicId.ThornVest, 6 }, { RelicId.BerserkerCharm, 6 }, { RelicId.VampireTooth, 6 },
            { RelicId.AlchemistsVial, 6 }, { RelicId.Whetstone, 4 }, { RelicId.OxHeart, 4 },
            { RelicId.IronSkin, 4 }, { RelicId.AdrenalineGland, 3 },
        };

        /// <summary>What a rival thinks a relic is worth, given what they already hold.</summary>
        public static double Score(RelicId id, Rival rival, int round)
        {
            Dictionary<RelicId, double> table = round <= EarlyRounds ? EarlyValue : LateValue;

            double value;
            if (!table.TryGetValue(id, out value)) value = 3;

            // A unique they already own is a dead pick.
            int held = Count(rival.Relics, id);
            if (!RelicCatalog.Get(id).Stackable && held > 0) value -= 8;

            value += Conditional(id, rival);
            value += RelicSynergy.Of(id, rival.Relics);

            // Diminishing returns on stacking the same relic.
            return value - held * 1.5;
        }

        /// <summary>
        /// Relics that are worth little until the thing that switches them on is in the build.
        /// </summary>
        private static double Conditional(RelicId id, Rival rival)
        {
            if (id == RelicId.RabbitsFoot)
            {
                bool enabled = Count(rival.Relics, RelicId.LuckyClover) > 0
                            || Count(rival.Relics, RelicId.WeightedDice) > 0
                            || Count(rival.Relics, RelicId.CutpurseHook) > 0;
                return enabled ? 2 : -2;
            }

            if (id == RelicId.BloodAltar)
            {
                // It turns healing into damage, so it needs healing to turn.
                int healing = Count(rival.Relics, RelicId.VampireTooth)
                            + Count(rival.Relics, RelicId.AlchemistsVial)
                            + Count(rival.Relics, RelicId.OxHeart);
                return healing > 0 ? 2 : -5;
            }

            if (id == RelicId.AlchemistsVial)
            {
                return Count(rival.Relics, RelicId.CutpurseHook) > 0 ? 2 : 0;
            }

            if (id == RelicId.CoinMagnet)
            {
                return Count(rival.Relics, RelicId.CutpurseHook) > 0 ? 1 : -1;
            }

            return 0;
        }

        private static int Count(IReadOnlyList<RelicId> list, RelicId id)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == id) n++;
            }

            return n;
        }
    }
}
