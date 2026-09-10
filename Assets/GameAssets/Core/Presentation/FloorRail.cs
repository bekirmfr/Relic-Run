using System.Collections.Generic;
using RelicRun.Core.Run;

namespace RelicRun.Core.Presentation
{
    /// <summary>What one floor's node on the rail is.</summary>
    public enum FloorMark
    {
        /// <summary>An ordinary floor with an ordinary fight on it.</summary>
        Plain = 0,

        /// <summary>The bazaar, which is walked through rather than fought.</summary>
        Bazaar = 1,

        /// <summary>The bottom, where the Hoard-King is.</summary>
        Crown = 2,
    }

    /// <summary>One floor, as the rail across the top of a run draws it.</summary>
    public struct FloorNode
    {
        /// <summary>Which floor, counting from one.</summary>
        public int Floor;

        /// <summary>What kind of floor it is.</summary>
        public FloorMark Mark;

        /// <summary>Whether it is behind the delver.</summary>
        public bool Walked;

        /// <summary>Whether it is the one being stood on.</summary>
        public bool Here;

        /// <summary>Whether an event waits in the gap before it.</summary>
        public bool Event;

        /// <summary>And whether that event has already been met.</summary>
        public bool EventPassed;

        /// <summary>
        /// Whether the delver may open its card.
        /// </summary>
        /// <remarks>
        /// One floor further than they have reached, and no further. Looking one step ahead is
        /// the whole point of the rail — it is how a delver decides whether to walk out — and
        /// looking further would hand them the rest of the run.
        /// </remarks>
        public bool Known;

        /// <summary>How big to draw it, in the source's own pixels.</summary>
        /// <remarks>
        /// Size IS the information here: a bazaar and a crown are bigger than a floor, and
        /// whichever one is underfoot is bigger again. Carried rather than left to the screen so
        /// that the two facts it encodes — what a floor is, and where the delver is — cannot
        /// come apart from the flags above them.
        /// </remarks>
        public int Side;
    }

    /// <summary>Everything the rail across the top of a run shows.</summary>
    public struct FloorRailCard
    {
        public IReadOnlyList<FloorNode> Floors;

        /// <summary>The line on the left: which floor of how many.</summary>
        public int Floor;

        public int Deepest;
    }

    /// <summary>
    /// The rail across the top of a run.
    /// </summary>
    /// <remarks>
    /// Thirteen nodes and a thread between them, and it is the only thing on the run screen that
    /// says where the delver IS. Everything else — the fight, the draft, the shelf — is about the
    /// floor underfoot; this is the one piece that shows the shape of the whole descent, which is
    /// what makes walking out at the ninth gate a decision rather than a guess.
    ///
    /// The bazaar is drawn round and the crown is drawn turned, because neither is a fight and a
    /// delver planning a run needs to see them coming. That is the source's own encoding.
    /// </remarks>
    public static class FloorRails
    {
        /// <summary>How wide a node is, by what it is and whether it is underfoot.</summary>
        /// <remarks>The source's numbers, which are pixels on a 390-wide shell.</remarks>
        public const int CrownHere = 15;

        public const int CrownAway = 12;

        public const int BazaarHere = 13;

        public const int BazaarAway = 10;

        public const int PlainHere = 11;

        public const int PlainAway = 7;

        /// <summary>The rail for a run standing on a floor, with events placed where they are.</summary>
        /// <param name="events">
        /// Which floors have an event in the gap BEFORE them, as <c>DelveRun.PlaceEvents</c>
        /// returns them. Null draws a rail with none, which is what a versus arena has.
        /// </param>
        public static FloorRailCard Of(int floor, IReadOnlyDictionary<int, int> events)
        {
            var nodes = new List<FloorNode>(DelveRun.MaxFloor);

            for (var at = 1; at <= DelveRun.MaxFloor; at++)
            {
                FloorMark mark = at == DelveRun.MaxFloor ? FloorMark.Crown
                    : at == DelveRun.BazaarFloor ? FloorMark.Bazaar
                    : FloorMark.Plain;

                bool here = at == floor;

                nodes.Add(new FloorNode
                {
                    Floor = at,
                    Mark = mark,
                    Walked = at < floor,
                    Here = here,

                    // An event sits in the gap BEFORE a floor, so the first floor never has one:
                    // there is no gap in front of the first step of a run.
                    Event = at > 1 && events != null && events.ContainsKey(at - 1),
                    EventPassed = at > 1 && floor > at - 1,

                    Known = at <= floor + 1,
                    Side = Side(mark, here),
                });
            }

            return new FloorRailCard
            {
                Floors = nodes,
                Floor = floor,
                Deepest = DelveRun.MaxFloor,
            };
        }

        /// <summary>How wide one node is drawn.</summary>
        public static int Side(FloorMark mark, bool here)
        {
            switch (mark)
            {
                case FloorMark.Crown: return here ? CrownHere : CrownAway;
                case FloorMark.Bazaar: return here ? BazaarHere : BazaarAway;
                default: return here ? PlainHere : PlainAway;
            }
        }
    }
}
