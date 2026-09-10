using System.Collections.Generic;
using RelicRun.Core.Combat;
using RelicRun.Core.Stats;

namespace RelicRun.Core.Presentation
{
    /// <summary>One foe in the row along the bottom of a fight.</summary>
    public struct FoeTile
    {
        public int Species;

        /// <summary>Which column of the sheet draws it: guard, elite, or boss.</summary>
        public int Variant;

        public EnemyRank Rank;

        /// <summary>Whether it has already fallen.</summary>
        public bool Done;

        /// <summary>Whether it is the one being fought.</summary>
        public bool Here;

        /// <summary>The tile's border, as <c>#RRGGBB</c>.</summary>
        public string Hex;

        /// <summary>How thick that border is.</summary>
        /// <remarks>
        /// Two for the foe underfoot and for anything that outranks a guard, one for the rest.
        /// Thickness is the source's way of saying "this one is worth looking at" without
        /// spending a second colour on it.
        /// </remarks>
        public int Edge;

        /// <summary>Whether it glows. Only the one being fought does.</summary>
        public bool Lit;

        /// <summary>Whether it is drawn dark: still ahead, and not the one being fought.</summary>
        public bool Dim;

        /// <summary>Whether it is struck through, which is what a dead foe looks like.</summary>
        public bool Struck;

        /// <summary>How wide the tile is, in the source's own pixels.</summary>
        public int Side;
    }

    /// <summary>Everything the row of foes shows.</summary>
    public struct FoeQueueCard
    {
        public IReadOnlyList<FoeTile> Foes;

        /// <summary>How many have fallen.</summary>
        public int Done;

        public int Count;

        /// <summary>
        /// Whether to draw the row at all.
        /// </summary>
        /// <remarks>
        /// A floor with one foe on it has nothing to queue. The source hides the row there, and
        /// it is right: a row of one says only what the picture in the middle of the screen
        /// already says, and costs a line of the screen to say it.
        /// </remarks>
        public bool Shown;
    }

    /// <summary>
    /// The pack on a floor, and how far into it the fight has got.
    /// </summary>
    /// <remarks>
    /// The one thing on a fight screen that is about the FLOOR rather than about the blow being
    /// struck. A delver watching a fight can see what they are fighting and what it has left;
    /// what they cannot see, without this, is whether beating it ends the floor or buys them the
    /// next of four — which is the difference between spending a relic charge now and holding it.
    ///
    /// How far in is counted from the events rather than tracked. Playback can be skipped, sped
    /// up, or started from the middle of a list, and a counter that had been incremented along
    /// the way would be wrong in all three; the events themselves are the only thing that is
    /// true at any index.
    /// </remarks>
    public static class FoeQueues
    {
        /// <summary>How wide a tile is, and how big the foe drawn on it is.</summary>
        /// <remarks>The source's numbers, which are pixels on a 390-wide shell.</remarks>
        public const int Side = 26;

        public const int Glyph = 24;

        /// <summary>The gold a foe being fought is ringed in.</summary>
        public const string HereHex = "#E3B341";

        /// <summary>And the steel a floor boss is ringed in instead, so the two read apart.</summary>
        public const string BossHere = "#AEB6C0";

        public const string KingAway = "#7A5E1D";

        public const string BossAway = "#565B63";

        public const string PlainAway = "#2B261D";

        /// <summary>The row for a pack, with this many of them already fallen.</summary>
        public static FoeQueueCard Of(IReadOnlyList<EnemyState> pack, int done)
        {
            var tiles = new List<FoeTile>();

            if (pack != null)
            {
                for (var i = 0; i < pack.Count; i++)
                {
                    EnemyState foe = pack[i];

                    bool fallen = i < done;
                    bool here = i == done;

                    EnemyRank rank = foe != null ? foe.Rank : EnemyRank.Guard;

                    tiles.Add(new FoeTile
                    {
                        Species = foe != null ? foe.SpeciesIndex : 0,
                        Variant = foe != null ? foe.Variant : 0,
                        Rank = rank,
                        Done = fallen,
                        Here = here,
                        Hex = Ringed(rank, here),
                        Edge = here || rank == EnemyRank.Boss || rank == EnemyRank.King ? 2 : 1,
                        Lit = here,
                        Dim = !fallen && !here,
                        Struck = fallen,
                        Side = Side,
                    });
                }
            }

            return new FoeQueueCard
            {
                Foes = tiles,
                Done = done,
                Count = tiles.Count,
                Shown = tiles.Count > 1,
            };
        }

        /// <summary>
        /// What a tile is ringed in.
        /// </summary>
        /// <remarks>
        /// An elite is ringed like a guard here, which is not an oversight: the row is read at a
        /// glance and the source spends its two colours on the two things that change how a floor
        /// is fought — which foe is up, and whether one of them is a boss.
        /// </remarks>
        public static string Ringed(EnemyRank rank, bool here)
        {
            if (here) return rank == EnemyRank.Boss ? BossHere : HereHex;

            switch (rank)
            {
                case EnemyRank.King: return KingAway;
                case EnemyRank.Boss: return BossAway;
                default: return PlainAway;
            }
        }

        /// <summary>
        /// How many of the pack have fallen by the time an event is on screen.
        /// </summary>
        /// <remarks>
        /// Counted UP TO AND INCLUDING the event, because a kill is over by the time it is drawn:
        /// the source advances its counter on the kill itself, so a foe struck out one event
        /// later would still be standing in the row while its death was being read out.
        ///
        /// A hero dying is not a kill. That is <c>Death</c>, and it ends the floor with the pack
        /// exactly as far along as it was.
        /// </remarks>
        public static int Fallen(IReadOnlyList<CombatEvent> events, int upTo)
        {
            if (events == null) return 0;

            var many = 0;

            for (var i = 0; i <= upTo && i < events.Count; i++)
            {
                if (events[i].Type == CombatEventType.Kill) many++;
            }

            return many;
        }
    }
}
