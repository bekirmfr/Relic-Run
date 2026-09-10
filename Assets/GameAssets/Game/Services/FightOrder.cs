using System;
using System.Collections.Generic;
using RelicRun.Core.Presentation;

namespace RelicRun.Game.Services
{
    /// <summary>What delve the next screen is to walk.</summary>
    public struct RunOrder
    {
        /// <summary>The number the whole run is drawn from.</summary>
        public uint Seed;

        /// <summary>Which hall, which decides the backdrop and what waits in it.</summary>
        public int Tier;

        /// <summary>
        /// Whether this is today's Daily.
        /// </summary>
        /// <remarks>
        /// Carried past the seed because the save needs it too: a Daily is written down as done
        /// for the day, and a practice delve is not.
        /// </remarks>
        public bool Daily;

        /// <summary>The day it is, for a Daily. Meaningless otherwise.</summary>
        public uint Day;
    }

    /// <summary>What a finished delve came to, ready for the screens that show it.</summary>
    /// <remarks>
    /// Worked out at the moment the run ends and carried rather than recomputed, because the
    /// save has already been written by then: asking afterwards whether this run beat the record
    /// answers yes forever, and asking how much experience it paid means subtracting one number
    /// from another that has both already moved.
    /// </remarks>
    public struct RunReport
    {
        /// <summary>The end screen, whole.</summary>
        public OverCard Over;

        /// <summary>The level to open the experience bar on — the one held BEFORE the run.</summary>
        public int Level;

        /// <summary>And how full it was.</summary>
        public double Progress;

        /// <summary>What was unlocked on the way, if anything.</summary>
        public IReadOnlyList<string> Gains;
    }

    /// <summary>
    /// What the next screen is to do, and how the last one went.
    /// </summary>
    /// <remarks>
    /// A handoff, because the scene service has nowhere to put one. <c>LoadScene</c> takes a KEY
    /// and nothing else, and <c>ISceneObject.Initialize</c> takes nothing at all — so a screen
    /// cannot be handed its subject the way a method is handed an argument, and whatever it is to
    /// work on has to live somewhere that outlives the load.
    ///
    /// Registered at the application's root beside <see cref="SaveVault"/>, and for the same
    /// reason its comment gives: a second one built for a second scene would be a second opinion,
    /// and the menu would place an order the run never sees.
    ///
    /// It holds no history. What is here is the delve that has been ASKED for and the one that
    /// has just finished — a career's record of itself is the save's business.
    /// </remarks>
    public sealed class FightOrder
    {
        private RunOrder _order;
        private bool _placed;
        private RunReport _last;
        private bool _reported;

        /// <summary>Whether a delve is waiting to be walked.</summary>
        public bool Placed
        {
            get { return _placed; }
        }

        /// <summary>Whether a delve has finished and been read out.</summary>
        public bool Reported
        {
            get { return _reported; }
        }

        /// <summary>How the last one went. Meaningless until <see cref="Reported"/>.</summary>
        public RunReport Last
        {
            get { return _last; }
        }

        /// <summary>Says what is to be walked next.</summary>
        public void Place(RunOrder order)
        {
            if (order.Tier < 1) throw new ArgumentOutOfRangeException(nameof(order));

            _order = order;
            _placed = true;
            _reported = false;
        }

        /// <summary>
        /// Takes the order, leaving none.
        /// </summary>
        /// <remarks>
        /// Cleared on the way out, so a delve is walked once. Left in place, a screen that opened
        /// with nothing to do would silently replay the last run — which looks like a button that
        /// works and a career that never advances.
        /// </remarks>
        public bool Take(out RunOrder order)
        {
            order = _order;

            bool had = _placed;

            _placed = false;
            _order = new RunOrder();

            return had;
        }

        /// <summary>Says how it went, for whoever asked for it.</summary>
        public void Report(RunReport report)
        {
            _last = report;
            _reported = true;
        }

        /// <summary>
        /// Forgets the report, once it has been shown.
        /// </summary>
        /// <remarks>
        /// Otherwise the menu opens on the end of the same run every time it is loaded, which is
        /// a delver being told about a delve they finished an hour ago.
        /// </remarks>
        public void Shown()
        {
            _reported = false;
        }

        /// <summary>Throws away both halves.</summary>
        public void Forget()
        {
            _placed = false;
            _reported = false;
            _order = new RunOrder();
            _last = new RunReport();
        }
    }
}
