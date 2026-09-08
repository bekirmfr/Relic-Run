using System.Collections.Generic;
using System.Text;

namespace RelicRun.Core.Content
{
    /// <summary>One entry in a binding: an id, and whether anything is actually behind it.</summary>
    /// <remarks>
    /// Core never sees a sprite, a font or a texture — it sees whether the slot was filled. That
    /// is the whole of what the rule below needs, and it is what lets the rule be tested in a
    /// second by <c>dotnet test</c> rather than in a minute by the Editor.
    /// </remarks>
    public readonly struct Binding
    {
        public readonly string Id;

        /// <summary>Whether the asset reference resolves to something.</summary>
        public readonly bool Filled;

        public Binding(string id, bool filled)
        {
            Id = id ?? "";
            Filled = filled;
        }

        public override string ToString() { return Filled ? Id : Id + " (empty)"; }
    }

    /// <summary>
    /// What a binding of ids to assets got wrong.
    /// </summary>
    /// <remarks>
    /// The content itself is generated and gated in <c>Tools/extract/</c>, so the tables can be
    /// trusted. What cannot be is the join between them and the project's assets, because that
    /// join is made in the Editor by an importer that can only guess from filenames. A relic
    /// whose icon nobody bound draws as a hole in the shelf; an icon left behind by a relic that
    /// was renamed sits in the build forever, costing memory and telling nobody.
    ///
    /// So the rule is exact rather than lenient: every id the game will ask for is bound exactly
    /// once, to something, and nothing is bound that the game will never ask for. Five ways to
    /// break that, five lists, one remedy each.
    ///
    /// The excuse list is the pressure valve, and it is deliberately awkward to lean on. An id
    /// may be declared as having no art on purpose — <c>debtflesh</c> is one, drawn from a glyph
    /// rather than from the icon sheet — but an excuse that has stopped being true is itself a
    /// failure, so the list cannot quietly outlive the hole it was written for.
    /// </remarks>
    public sealed class BindingAudit
    {
        /// <summary>What was audited, for the message: "relics", "halls", "enemies".</summary>
        public readonly string What;

        /// <summary>Needed, bound by nobody, and not excused. Draws as a hole.</summary>
        public readonly IReadOnlyList<string> Missing;

        /// <summary>Bound, but nothing will ever ask for it. Ships for no reason.</summary>
        public readonly IReadOnlyList<string> Unknown;

        /// <summary>Bound more than once. Which copy wins is a lookup detail, not a decision.</summary>
        public readonly IReadOnlyList<string> Doubled;

        /// <summary>Bound to an empty slot, which is a hole that looks like it was handled.</summary>
        public readonly IReadOnlyList<string> Blank;

        /// <summary>Excused, but bound anyway or no longer needed. The excuse has expired.</summary>
        public readonly IReadOnlyList<string> Stale;

        public BindingAudit(string what, IReadOnlyList<string> missing, IReadOnlyList<string> unknown,
            IReadOnlyList<string> doubled, IReadOnlyList<string> blank, IReadOnlyList<string> stale)
        {
            What = what;
            Missing = missing;
            Unknown = unknown;
            Doubled = doubled;
            Blank = blank;
            Stale = stale;
        }

        public bool Passed
        {
            get
            {
                return Missing.Count == 0 && Unknown.Count == 0 && Doubled.Count == 0 &&
                       Blank.Count == 0 && Stale.Count == 0;
            }
        }

        /// <summary>
        /// Audits one binding.
        /// </summary>
        /// <param name="needed">Every id the game will ask for, in the order it is worth reading.</param>
        /// <param name="bound">What the binding actually holds, in its own order.</param>
        /// <param name="excused">Ids known to have nothing behind them, on purpose.</param>
        /// <remarks>
        /// Order is the caller's, not sorted, so a report reads down the catalog rather than down
        /// the alphabet — the fourth hall and the fifth are neighbours in the game and should be
        /// neighbours in the message about them.
        /// </remarks>
        public static BindingAudit Of(string what, IEnumerable<string> needed,
            IEnumerable<Binding> bound, IEnumerable<string> excused = null)
        {
            var wanted = new List<string>();
            var wantedSet = new HashSet<string>();
            if (needed != null)
            {
                foreach (string id in needed)
                {
                    // A catalog that named the same id twice would make every count below wrong,
                    // so the audit reads its own input as a set and reports each id once.
                    if (wantedSet.Add(id)) wanted.Add(id);
                }
            }

            var forgiven = new HashSet<string>();
            var forgivenOrder = new List<string>();
            if (excused != null)
            {
                foreach (string id in excused)
                {
                    if (forgiven.Add(id)) forgivenOrder.Add(id);
                }
            }

            var counts = new Dictionary<string, int>();
            var filled = new HashSet<string>();
            var unknown = new List<string>();
            var doubled = new List<string>();

            if (bound != null)
            {
                foreach (Binding entry in bound)
                {
                    int seen;
                    counts.TryGetValue(entry.Id, out seen);
                    counts[entry.Id] = seen + 1;

                    // Reported on the SECOND sighting, so a slot bound three times is named once.
                    if (seen == 1) doubled.Add(entry.Id);
                    if (entry.Filled) filled.Add(entry.Id);
                    if (seen == 0 && !wantedSet.Contains(entry.Id)) unknown.Add(entry.Id);
                }
            }

            var missing = new List<string>();
            var blank = new List<string>();

            foreach (string id in wanted)
            {
                if (forgiven.Contains(id)) continue;

                if (!counts.ContainsKey(id)) missing.Add(id);
                else if (!filled.Contains(id)) blank.Add(id);
            }

            var stale = new List<string>();
            foreach (string id in forgivenOrder)
            {
                // Either the art arrived and nobody deleted the excuse, or the id itself is gone.
                if (counts.ContainsKey(id) || !wantedSet.Contains(id)) stale.Add(id);
            }

            return new BindingAudit(what, missing, unknown, doubled, blank, stale);
        }

        /// <summary>The failure, written out. Empty when nothing is wrong.</summary>
        public string Report()
        {
            if (Passed) return "";

            var said = new StringBuilder(What);
            Line(said, "unbound", Missing);
            Line(said, "bound to nothing", Blank);
            Line(said, "bound twice", Doubled);
            Line(said, "bound but never asked for", Unknown);
            Line(said, "excused for no reason", Stale);

            return said.ToString();
        }

        private static void Line(StringBuilder said, string what, IReadOnlyList<string> ids)
        {
            if (ids.Count == 0) return;

            said.Append("\n  ").Append(ids.Count).Append(" ").Append(what).Append(": ");
            for (int i = 0; i < ids.Count && i < 12; i++)
            {
                if (i > 0) said.Append(", ");
                said.Append(ids[i]);
            }

            if (ids.Count > 12) said.Append(", and ").Append(ids.Count - 12).Append(" more");
        }
    }
}
