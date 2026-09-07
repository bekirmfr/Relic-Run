using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Run;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// Phase 5 gate: <c>Tools/corpus/outcomes.json</c>.
    /// </summary>
    /// <remarks>
    /// Scoring lives inside a React method in the source, wrapped in sound, telemetry and screen
    /// transitions, so it cannot be lifted whole the way a fight can. The recorder stubs the
    /// presentation and calls the real thing; what is recorded is the arithmetic that came out.
    ///
    /// The one fraction in the calculation, how relevant the dungeon still is, is not recorded.
    /// It exists only to scale XP, so pinning the resulting xpGain pins it exactly and keeps the
    /// corpus free of float literals.
    /// </remarks>
    [TestFixture]
    public class RunOutcomeTests
    {
        private static readonly Dictionary<string, RunEnding> Endings = new Dictionary<string, RunEnding>
        {
            { "died", RunEnding.Died },
            { "cashout", RunEnding.CashedOut },
            { "cleared", RunEnding.Cleared },
        };

        [Test]
        public void RecordedOutcomesScoreExactly()
        {
            JArray cases = Corpus.Array("outcomes.json");
            Assert.That(cases.Count, Is.GreaterThan(0), "outcome corpus is empty");

            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                string id = token["id"].Value<string>();
                var input = (JObject)token["input"];

                var run = new RunState();
                foreach (JToken t in (JArray)input["items"])
                {
                    RelicId relic;
                    if (RelicCatalog.TryParse(t.Value<string>(), out relic)) run.Items.Add(relic);
                }

                // Recorded as inventory SLOTS, because a copy is what gets awakened. RunState
                // keeps the per-id count the rules actually read, which is the same conversion
                // the source makes before anything downstream sees an awakening.
                foreach (JToken slot in (JArray)input["awake"]) run.Awaken(run.Items[slot.Value<int>()]);

                RunReward got = RunOutcome.Score(
                    Endings[input["how"].Value<string>()],
                    input["mode"].Value<string>() == "versus",
                    input["gold"].Value<int>(),
                    input["floor"].Value<int>(),
                    input["kills"].Value<int>(),
                    run,
                    input["tier"].Value<int>(),
                    input["unlocked"].Value<int>());

                string diff =
                    Int(id, "banked", token["banked"].Value<int>(), got.Banked) ??
                    Int(id, "score", token["score"].Value<int>(), got.Score) ??
                    Int(id, "stars", token["stars"].Value<int>(), got.Stars) ??
                    Int(id, "xp", token["xpGain"].Value<int>(), got.Xp);

                if (diff != null) failures.Add(diff);
            }

            Assert.That(failures, Is.Empty,
                failures.Count + " of " + cases.Count + " outcomes diverge:\n  " +
                string.Join("\n  ", failures.GetRange(0, System.Math.Min(8, failures.Count))));
        }

        private static string Int(string id, string field, int want, int got)
        {
            return want == got
                ? null
                : id + ": " + field + " recorded " + want + ", replayed " + got;
        }
    }
}
