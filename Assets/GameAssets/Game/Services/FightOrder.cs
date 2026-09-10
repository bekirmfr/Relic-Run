using System;
using RelicRun.Core.Combat;

namespace RelicRun.Game.Services
{
    /// <summary>
    /// What fight the next screen is to fight, and how the last one went.
    /// </summary>
    /// <remarks>
    /// A handoff, because the scene service has nowhere to put one. <c>LoadScene</c> takes a KEY
    /// and nothing else, and <c>ISceneObject.Initialize</c> takes nothing at all — so a screen
    /// cannot be handed its subject the way a method is handed an argument, and whatever it is to
    /// work on has to live somewhere that outlives the load.
    ///
    /// Registered at the application's root beside <see cref="SaveVault"/>, and for the same
    /// reason its comment gives: a second one built for a second scene would be a second opinion,
    /// and the menu would place an order the fight never sees. One order, at the root, outliving
    /// every scene that reads it.
    ///
    /// It holds no history. What is here is the fight that has been ASKED for and the one that
    /// has just been fought — a run's record of itself belongs to the run layer, which will keep
    /// far more than two numbers and will keep them somewhere a save can reach.
    /// </remarks>
    public sealed class FightOrder
    {
        private FightPlan _plan;
        private int _ceiling;
        private FightSummary _last;
        private bool _reported;

        /// <summary>Whether a fight is waiting to be fought.</summary>
        public bool Placed
        {
            get { return _plan != null; }
        }

        /// <summary>Whether a fight has been fought and read out.</summary>
        public bool Reported
        {
            get { return _reported; }
        }

        /// <summary>How the last fight went. Meaningless until <see cref="Reported"/>.</summary>
        public FightSummary Last
        {
            get { return _last; }
        }

        /// <summary>
        /// Says what is to be fought next.
        /// </summary>
        /// <param name="ceiling">
        /// The delver's maximum as the floor OPENS. Kept beside the plan because it is only true
        /// beforehand — the hero is the engine's working copy, and by the time anybody reads it
        /// the ceiling may have grown. Reading it afterwards would let a Bottomless Chalice's
        /// growth arrive on the floor that bought it.
        /// </param>
        public void Place(FightPlan plan, int ceiling)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            _plan = plan;
            _ceiling = ceiling;
            _reported = false;
        }

        /// <summary>
        /// Takes the order, leaving none.
        /// </summary>
        /// <remarks>
        /// Cleared on the way out, so a fight is fought once. Left in place, a screen that opened
        /// with nothing to fight would silently replay the last one — which looks like a button
        /// that works and a run that never advances.
        /// </remarks>
        public bool Take(out FightPlan plan, out int ceiling)
        {
            plan = _plan;
            ceiling = _ceiling;

            _plan = null;
            _ceiling = 0;

            return plan != null;
        }

        /// <summary>Says how it went, for whoever asked for it.</summary>
        public void Report(FightSummary summary)
        {
            _last = summary;
            _reported = true;
        }

        /// <summary>Throws away both halves.</summary>
        public void Forget()
        {
            _plan = null;
            _ceiling = 0;
            _last = new FightSummary();
            _reported = false;
        }
    }
}
