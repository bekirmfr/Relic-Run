using System;
using System.Collections.Generic;
using System.Globalization;
using RelicRun.Core.Content;
using RelicRun.Core.Presentation;
using RelicRun.Core.Run;
using UnityEngine;

namespace RelicRun.Game.Presentation
{
    /// <summary>
    /// One thing a run stops to do.
    /// </summary>
    /// <remarks>
    /// The run screen is not a screen, it is a stage machine — the source's IN RUN section is a
    /// floor rail and then draft, combat, event, shop, merchant, travel, decide and revive, and
    /// which one is up is a fact about what the engine has stopped to ask. So a stage is a widget
    /// that knows one <see cref="AskKind"/>, draws it, and raises the answer.
    ///
    /// Deliberately shaped like <see cref="MetaPanel"/>, because it is the same idea one layer
    /// down: the scene owns the routing and a stage owns its own drawing, which is what lets a
    /// stage be built and looked at without a run existing.
    ///
    /// A stage never advances the run. It answers, and the scene decides when to pump — because
    /// the one stop that is not a question, a floor that has been fought, takes twelve seconds
    /// of reading out and the engine must not run ahead of it.
    /// </remarks>
    public abstract class RunStage : MonoBehaviour
    {
        /// <summary>Which stop this stage is for.</summary>
        public abstract AskKind Answers { get; }

        /// <summary>
        /// Whether this stage is the one for a stop.
        /// </summary>
        /// <remarks>
        /// Usually just <see cref="Answers"/>, and virtual for the one case where it is not: a
        /// draft and a reroll are the same table with a different question over it, and the
        /// source draws them as one screen. Two stages for that would be two copies of the same
        /// layout that could drift apart.
        /// </remarks>
        public virtual bool Handles(AskKind kind)
        {
            return kind == Answers;
        }

        /// <summary>Whether it is the one on screen.</summary>
        public bool Showing
        {
            get { return gameObject.activeSelf; }
            set { gameObject.SetActive(value); }
        }

        /// <summary>
        /// What the game says, in the delver's language.
        /// </summary>
        /// <remarks>
        /// Handed down by the scene rather than fetched, because fetching it is asynchronous and
        /// a stage drawing itself is not. Never null: an unfetched one answers every key with the
        /// key, which is ugly on screen and immediately diagnosable — unlike a blank.
        /// </remarks>
        public Locale Words { get; set; }

        /// <summary>Raised when the delver has decided.</summary>
        public event Action<Answer> Decided;

        /// <summary>Puts the stop on the screen.</summary>
        /// <param name="ask">What the run wants to know.</param>
        /// <param name="run">The run as it stands, for everything the stop does not carry.</param>
        public abstract void Draw(Ask ask, RunState run);

        /// <summary>
        /// Whether this stage has something to say once the run has answered.
        /// </summary>
        /// <remarks>
        /// One stage does: an event, whose choice is made blind and whose outcome is the only
        /// place the run explains what it did. The scene waits for a second press when this is
        /// true, and pumps straight on when it is not.
        /// </remarks>
        public virtual bool Tells
        {
            get { return false; }
        }

        /// <summary>
        /// Reads out what the answer turned out to mean.
        /// </summary>
        /// <remarks>
        /// Called after the run has been pumped, so whatever the stop carries about its outcome
        /// is already written. Raise <see cref="Decided"/> when the delver has read it — the
        /// answer is ignored, because the run has already had the only one it needed.
        /// </remarks>
        public virtual void Tell(Ask ask, RunState run) { }

        /// <summary>Says what the delver chose. For subclasses to call from their own buttons.</summary>
        protected void Decide(Answer answer)
        {
            Action<Answer> decided = Decided;

            if (decided != null) decided(answer);
        }

        /// <summary>What a stat chip is painted with, on both sides of the word.</summary>
        /// <remarks>
        /// The source's own treatment: smaller, letter-spaced, and in the dull gold it uses for
        /// numbers that come off the delver rather than off the relic. TextMeshPro markup, which
        /// is why it lives here and not in Core.
        /// </remarks>
        private const string ChipOpen = "<size=82%><color=#B9A05C>";

        private const string ChipClose = "</color></size>";

        /// <summary>
        /// A relic's description, with its live number filled and its stat chips painted.
        /// </summary>
        /// <remarks>
        /// Two of the fifty relics need this and both reached a delver as raw markup before it
        /// existed — <c>(now +{n})</c> on the Midas Blade and <c>[[LCK]]</c> on the Weighted
        /// Dice. On <see cref="RunStage"/> rather than on either screen, because the draft and
        /// the shelf both describe relics and describing them differently is exactly the bug
        /// this is fixing.
        /// </remarks>
        protected string Describes(RelicId relic, string key, string english, int gold)
        {
            string said;

            if (key == null)
            {
                said = english ?? string.Empty;
            }
            else if (RelicWords.Lives(relic))
            {
                said = Say(key, "n", RelicWords.Live(relic, gold)
                    .ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                said = Say(key);
            }

            return RelicWords.Chips(said, ChipOpen, ChipClose);
        }

        /// <summary>A word from the delver's own language, or the key when there is none.</summary>
        protected string Say(string key)
        {
            return Words != null ? Words.Get(key) : key;
        }

        /// <summary>
        /// The same, with its placeholders filled — name and value, in pairs.
        /// </summary>
        /// <remarks>
        /// Pairs rather than a dictionary, which is how <c>CombatLog</c> says the same thing:
        /// filling one number is the common case and building a dictionary for it at every call
        /// site would bury the string being said.
        ///
        /// A whole sentence per language rather than a number appended to a word. Where the
        /// number goes is part of a translation — Japanese wants it before the noun and Arabic
        /// reads the other way — so a screen that concatenated would be deciding word order for
        /// eight languages it cannot read.
        /// </remarks>
        protected string Say(string key, params string[] values)
        {
            if (Words == null) return key;

            if (values == null || values.Length == 0) return Words.Get(key);

            var filled = new Dictionary<string, string>(values.Length / 2);

            for (int i = 0; i + 1 < values.Length; i += 2) filled[values[i]] = values[i + 1];

            return Words.Get(key, filled);
        }
    }
}
