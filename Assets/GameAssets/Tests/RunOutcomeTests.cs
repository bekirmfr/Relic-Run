using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Content;
using RelicRun.Core.Meta;
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

                // Recorded as inventory SLOTS, because a copy is what gets awakened, and that
                // is how the run holds them too.
                foreach (JToken slot in (JArray)input["awake"]) run.Awaken(slot.Value<int>());

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

        /// <summary>
        /// Phase 5 gate: what a finished run leaves in the save.
        /// </summary>
        /// <remarks>
        /// Scoring says what a run was worth; this is what happens to it afterwards. Both halves
        /// come out of the same recorded call, so the store either side of it is recorded with
        /// the arithmetic and replayed here against <see cref="RunBanking"/>.
        ///
        /// A leaderboard row's timestamp is not compared. The board sorts on score alone, so the
        /// clock orders nothing, and a clock is not a rule.
        /// </remarks>
        [Test]
        public void RecordedOutcomesBankExactly()
        {
            JArray cases = Corpus.Array("outcomes.json");
            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                string id = token["id"].Value<string>();
                var input = (JObject)token["input"];
                var before = (JObject)input["store"];
                var after = (JObject)token["store"];

                SaveState save = SaveFrom(before);
                bool daily = input["daily"].Value<bool>();
                uint seed = daily ? DailySeedOfTheRecording : 0u;

                Banked banked = RunBanking.Bank(save, new FinishedRun
                {
                    Ending = Endings[input["how"].Value<string>()],
                    Versus = input["mode"].Value<string>() == "versus",
                    Daily = daily,
                    Seed = seed,
                    Tier = input["tier"].Value<int>(),
                    Floor = input["floor"].Value<int>(),
                    Kills = input["kills"].Value<int>(),
                    Relics = ((JArray)input["items"]).Count,
                    Name = "you",
                }, new RunReward
                {
                    Banked = token["banked"].Value<int>(),
                    Score = token["score"].Value<int>(),
                    Xp = token["xpGain"].Value<int>(),
                    Stars = token["stars"].Value<int>(),
                });

                bool wasBest = token["isBest"].Value<bool>();
                if (banked.NewBest != wasBest)
                {
                    failures.Add(id + ": a new best was " + (wasBest ? "" : "not ") +
                                 "recorded, and the port says " + banked.NewBest);
                    continue;
                }

                string diff =
                    Int(id, "runs", after["dd.runs"].Value<int>(), save.Runs) ??
                    Int(id, "clears", after["dd.clears"].Value<int>(), save.Clears) ??
                    Int(id, "gold", after["dd.gold"].Value<int>(), save.Gold) ??
                    Int(id, "lifetime gold", after["dd.goldLife"].Value<int>(), save.GoldLife) ??
                    Int(id, "xp", after["dd.xp"].Value<int>(), save.Xp) ??
                    Int(id, "best", after["dd.best"].Value<int>(), save.Best) ??
                    Int(id, "unlocked", after["dd.unlocked"].Value<int>(), save.Unlocked) ??
                    Int(id, "crowns", after["dd.vsCrowns"].Value<int>(), save.VsCrowns) ??
                    Int(id, "daily best", after["dd.daily"].Value<int>(), save.DailyBestFor(seed)) ??
                    Arts(id, (JArray)after["dd.arts"], save) ??
                    Board(id, (JArray)after["dd.scores"], save);

                if (diff != null) failures.Add(diff);
            }

            Assert.That(failures, Is.Empty,
                failures.Count + " of " + cases.Count + " runs bank differently:\n  " +
                string.Join("\n  ", failures.GetRange(0, System.Math.Min(8, failures.Count))));
        }

        /// <summary>The day the recorded dailies are filed under. Fixed, so no clock is read.</summary>
        private const uint DailySeedOfTheRecording = 20260907u;

        private static SaveState SaveFrom(JObject store)
        {
            var save = new SaveState
            {
                Runs = store["dd.runs"].Value<int>(),
                Clears = store["dd.clears"].Value<int>(),
                GoldLife = store["dd.goldLife"].Value<int>(),
                Gold = store["dd.gold"].Value<int>(),
                Best = store["dd.best"].Value<int>(),
                Xp = store["dd.xp"].Value<int>(),
                Unlocked = store["dd.unlocked"].Value<int>(),
                VsCrowns = store["dd.vsCrowns"].Value<int>(),
            };

            foreach (JToken t in (JArray)store["dd.arts"]) save.Arts.Add(t.Value<string>());

            foreach (JToken t in (JArray)store["dd.scores"])
            {
                var row = (JObject)t;
                save.Scores.Add(new ScoreRow
                {
                    Score = row["score"].Value<int>(),
                    Floor = row["floor"].Value<int>(),
                    Kills = row["kills"].Value<int>(),
                    Relics = row["relics"].Value<int>(),
                    Name = row["name"].Value<string>(),
                });
            }

            int had = store["dd.daily"].Value<int>();
            if (had > 0) save.DailyBest[DailySeedOfTheRecording] = had;
            return save;
        }

        private static string Arts(string id, JArray want, SaveState save)
        {
            var theirs = new List<string>();
            foreach (JToken t in want) theirs.Add(t.Value<string>());

            return string.Join(",", theirs) == string.Join(",", save.Arts)
                ? null
                : id + ": backdrops recorded [" + string.Join(",", theirs) + "], banked [" +
                  string.Join(",", save.Arts) + "]";
        }

        private static string Board(string id, JArray want, SaveState save)
        {
            var theirs = new List<string>();
            foreach (JToken t in want)
            {
                var row = (JObject)t;
                theirs.Add(row["score"].Value<int>() + "/" + row["floor"].Value<int>() + "/" +
                           row["kills"].Value<int>() + "/" + row["relics"].Value<int>() + "/" +
                           row["name"].Value<string>());
            }

            var mine = new List<string>();
            for (int i = 0; i < save.Scores.Count; i++)
            {
                ScoreRow row = save.Scores[i];
                mine.Add(row.Score + "/" + row.Floor + "/" + row.Kills + "/" + row.Relics + "/" +
                         row.Name);
            }

            return string.Join(" | ", theirs) == string.Join(" | ", mine)
                ? null
                : id + ": board recorded [" + string.Join(" | ", theirs) + "], banked [" +
                  string.Join(" | ", mine) + "]";
        }

        private static string Int(string id, string field, int want, int got)
        {
            return want == got
                ? null
                : id + ": " + field + " recorded " + want + ", replayed " + got;
        }
    }
}
