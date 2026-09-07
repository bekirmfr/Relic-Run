using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Run;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// Phase 6 gate: <c>Tools/corpus/synergy.json</c>.
    /// </summary>
    /// <remarks>
    /// How the game reads a build. It is shipped rather than harness — the rival delvers draft
    /// with it between rounds — and it is subtle enough to be worth its own contract: set tiers,
    /// the dominant kind, and a chain graph of what a loadout produces against what it consumes.
    ///
    /// Scores land on halves, so the corpus stores them DOUBLED and this compares integers. A
    /// tolerance would hide exactly the kind of drift the gate exists to catch.
    /// </remarks>
    [TestFixture]
    public class RelicSynergyTests
    {
        [Test]
        public void RecordedSynergiesScoreExactly()
        {
            JArray cases = Corpus.Array("synergy.json");
            Assert.That(cases.Count, Is.GreaterThan(0), "synergy corpus is empty");

            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                RelicId id;
                if (!RelicCatalog.TryParse(token["id"].Value<string>(), out id)) continue;

                var owned = new List<RelicId>();
                foreach (JToken t in (JArray)token["owned"])
                {
                    RelicId held;
                    if (RelicCatalog.TryParse(t.Value<string>(), out held)) owned.Add(held);
                }

                double score = RelicSynergy.Of(id, owned);
                int doubled = (int)Math.Round(score * 2);

                if (Math.Abs(doubled / 2.0 - score) > 1e-9)
                {
                    failures.Add(RelicCatalog.KeyOf(id) + ": scored " + score + ", which is not a half");
                    continue;
                }

                int want = token["score2"].Value<int>();
                if (doubled != want)
                {
                    failures.Add(RelicCatalog.KeyOf(id) + " with [" + Keys(owned) + "]: recorded " +
                                 (want / 2.0) + ", replayed " + (doubled / 2.0));
                }
            }

            Assert.That(failures, Is.Empty,
                failures.Count + " of " + cases.Count + " synergies diverge:\n  " +
                string.Join("\n  ", failures.GetRange(0, Math.Min(8, failures.Count))));
        }

        private static string Keys(IReadOnlyList<RelicId> owned)
        {
            var keys = new List<string>(owned.Count);
            for (int i = 0; i < owned.Count; i++) keys.Add(RelicCatalog.KeyOf(owned[i]));
            return string.Join(",", keys);
        }
    }
}
