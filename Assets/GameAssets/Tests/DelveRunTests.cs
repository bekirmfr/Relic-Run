using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Determinism;
using RelicRun.Core.Run;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// Phase 5 gate: <c>Tools/corpus/delve.json</c>.
    /// </summary>
    /// <remarks>
    /// Each recorded run is replayed from its seed and the delver's level alone. The recording
    /// supplies only the DECISIONS a player would make — which event choice, how many rerolls,
    /// which relic, what the bazaar was asked for, whether a revive was taken — because choosing
    /// is not a rule. Everything else has to come out of the port: where the events land, what
    /// each choice does, the breather, the reroll ladder, the bazaar's prices and its shelf,
    /// what a pickup does, which relics an offer holds, and which pack a floor fights.
    ///
    /// This replaces a gate recorded from the Balance Lab's own run loop, which was the only
    /// loop that could be driven headlessly before the UI was drivable. The Lab walks a floor in
    /// a different order and keeps a sketch of the bazaar, so what it gated was never quite the
    /// game. <c>Tools/capture/delve.mjs</c> drives the shipped path instead.
    ///
    /// An offer is compared as a SET. Which relics the draw produced is a rule; the order they
    /// were shown in is presentation.
    /// </remarks>
    [TestFixture]
    public class DelveRunTests
    {
        /// <summary>
        /// Stops a replay the moment it diverges, so the reported failure is the FIRST one
        /// rather than whatever the run collapsed into afterwards.
        /// </summary>
        private sealed class ReplayEnded : System.Exception
        {
        }

        /// <summary>Replays one recorded run, and reports the first place it diverges.</summary>
        private sealed class Replay : IRunChoices, IRunObserver
        {
            private readonly JArray _steps;
            private readonly JObject _end;
            private readonly string _id;
            private int _at;
            private int _rerollsDone;
            private int _dealAt;

            public string Failure;

            public Replay(string id, JArray steps, JObject end)
            {
                _id = id;
                _steps = steps;
                _end = end;
            }

            public bool Exhausted { get { return _at >= _steps.Count; } }

            /// <summary>The step the run is standing on, without consuming it.</summary>
            private JObject Step(string kind)
            {
                if (_at >= _steps.Count)
                {
                    Fail("the recording ended, but the run went on and wanted a " + kind);
                }

                var step = (JObject)_steps[_at];
                string got = step["kind"].Value<string>();
                if (got != kind) Fail("step " + _at + ": recorded " + got + ", replayed " + kind);
                return step;
            }

            private string Kind()
            {
                return _at < _steps.Count ? ((JObject)_steps[_at])["kind"].Value<string>() : null;
            }

            /// <summary>Records the first divergence and abandons the run.</summary>
            private void Fail(string why)
            {
                if (Failure == null) Failure = _id + ": " + why;
                throw new ReplayEnded();
            }

            // ---- the decisions, read straight off the recording ----

            int IRunChoices.Event(RunState run, DungeonEvent ev)
            {
                JObject step = Step("event");
                int index = step["ev"].Value<int>();
                if (index != ev.Index)
                {
                    Fail("floor " + step["f"] + ": recorded event " + index + ", replayed " + ev.Index);
                }

                return step["choice"].Value<int>();
            }

            bool IRunChoices.Reroll(RunState run, IReadOnlyList<RelicId> offer, int price)
            {
                return _rerollsDone < Step("draft")["rerolls"].Value<int>();
            }

            RelicId IRunChoices.Draft(RunState run, IReadOnlyList<RelicId> offer)
            {
                JObject step = Step("draft");
                RelicId pick;
                RelicCatalog.TryParse(step["pick"].Value<string>(), out pick);
                return pick;
            }

            BazaarDeal IRunChoices.Bazaar(RunState run, IReadOnlyList<RelicId> offer,
                IReadOnlyList<int> awakenable)
            {
                var deals = (JArray)Step("shop")["deals"];
                if (_dealAt >= deals.Count)
                {
                    // The recorded delver stopped either because they ran out of gold or
                    // patience, or because the bazaar closed the visit. Only the second says
                    // anything about the rules — and it says a port that asks again is wrong.
                    if (deals.Count > 0 && ((JObject)deals[deals.Count - 1])["done"].Value<bool>())
                    {
                        Fail("floor " + run.Floor + ": the bazaar served deal " + (_dealAt + 1) +
                             ", but the recorded visit closed after " + deals.Count);
                    }

                    return BazaarDeal.Walk;
                }

                var visit = (JObject)deals[_dealAt];

                // The shelf and the awakening list are checked HERE, before the deal changes
                // them, which is also where the recorder read them.
                CheckSet("floor " + run.Floor + " (shop deal " + _dealAt + "): offer",
                    (JArray)visit["offer"], offer);

                var wantSlots = new List<string>();
                foreach (JToken t in (JArray)visit["awakenable"])
                {
                    wantSlots.Add(t["slot"].Value<int>() + ":" + t["id"].Value<string>());
                }

                var gotSlots = new List<string>();
                for (int i = 0; i < awakenable.Count; i++)
                {
                    gotSlots.Add(awakenable[i] + ":" + RelicCatalog.KeyOf(run.Items[awakenable[i]]));
                }

                if (string.Join(",", wantSlots) != string.Join(",", gotSlots))
                {
                    Fail("floor " + run.Floor + ": awakenings on the shelf recorded [" +
                         string.Join(",", wantSlots) + "], replayed [" + string.Join(",", gotSlots) + "]");
                }

                var deal = (JObject)visit["deal"];
                if (deal["kind"].Value<string>() == "awaken") return BazaarDeal.Awaken(deal["slot"].Value<int>());

                RelicId bought;
                RelicCatalog.TryParse(deal["relic"].Value<string>(), out bought);
                return BazaarDeal.Buy(bought);
            }

            bool IRunChoices.Revive(RunState run, int floor) { return Kind() == "revive"; }

            bool IRunChoices.CashOut(RunState run, int floor)
            {
                return _end["how"].Value<string>() == "cashout" && _end["floor"].Value<int>() == floor;
            }

            // ---- and the checks ----

            void IRunObserver.Event(RunState run, int floor, int index, int choice)
            {
                Check("event", floor, run);
            }

            void IRunObserver.Deal(RunState run, int floor, IReadOnlyList<RelicId> offer,
                IReadOnlyList<int> awakenable, BazaarDeal deal)
            {
                if (deal.Kind == DealKind.None)
                {
                    _dealAt = 0;
                    Check("shop", floor, run);
                    return;
                }

                var visit = (JObject)((JArray)Step("shop")["deals"])[_dealAt];
                _dealAt++;
                Diff("floor " + floor + " (shop deal " + (_dealAt - 1) + ") ", (JObject)visit["state"], run);
            }

            void IRunObserver.Draft(RunState run, int floor, IReadOnlyList<RelicId> offer,
                int rerolls, RelicId pick)
            {
                JObject step = Step("draft");
                CheckSet("floor " + floor + ": offer", (JArray)step["offer"], offer);

                int want = step["rerolls"].Value<int>();
                if (rerolls != want)
                {
                    Fail("floor " + floor + ": recorded " + want + " rerolls, replayed " + rerolls);
                }

                int breath = step["breath"].Value<int>();
                if (run.BreathHealed != breath)
                {
                    Fail("floor " + floor + ": breather recorded " + breath + ", replayed " +
                         run.BreathHealed);
                }

                _rerollsDone = 0;
                Check("draft", floor, run);
            }

            void IRunObserver.Fight(RunState run, int floor, IReadOnlyList<EnemyState> pack,
                CombatResult result)
            {
                // A revive fights on in the same floor, so the second fight of a floor lands on
                // the recording's revive step rather than another fight step.
                string kind = Kind() == "revive" ? "revive" : "fight";
                var want = (JObject)Step(kind)["pack"];

                int foes = 0, hp = 0, atk = 0, drop = 0;
                for (int i = 0; i < pack.Count; i++)
                {
                    foes++;
                    drop += pack[i].Drop;

                    // Hp, not MaxHp: an awakened Vampire Tooth eats the foe's CEILING as the
                    // fight runs, and this is read afterwards.
                    hp += pack[i].Hp;
                    atk += pack[i].Atk;
                }

                string why =
                    Was("foes", want, foes) ?? Was("hp", want, hp) ??
                    Was("atk", want, atk) ?? Was("drop", want, drop);

                if (why != null) Fail("floor " + floor + " (" + kind + ") pack " + why);

                Check(kind, floor, run);
            }

            void IRunObserver.Revived(RunState run, int floor) { }

            void IRunObserver.End(RunState run, RunEnding ending, int floor)
            {
                string how = ending == RunEnding.Cleared ? "cleared"
                    : ending == RunEnding.CashedOut ? "cashout" : "died";

                if (how != _end["how"].Value<string>())
                {
                    Fail("run ended " + how + ", recorded " + _end["how"]);
                }

                if (floor != _end["floor"].Value<int>())
                {
                    Fail("run ended on floor " + floor + ", recorded " + _end["floor"]);
                }

                Diff("the end ", (JObject)_end["state"], run);
            }

            private static string Was(string key, JObject want, int got)
            {
                return want[key].Value<int>() == got
                    ? null
                    : key + " recorded " + want[key] + ", replayed " + got;
            }

            private void CheckSet(string where, JArray want, IReadOnlyList<RelicId> got)
            {
                var mine = new List<string>(got.Count);
                for (int i = 0; i < got.Count; i++) mine.Add(RelicCatalog.KeyOf(got[i]));
                mine.Sort(System.StringComparer.Ordinal);

                var theirs = new List<string>();
                foreach (JToken t in want) theirs.Add(t.Value<string>());
                theirs.Sort(System.StringComparer.Ordinal);

                if (string.Join(",", theirs) != string.Join(",", mine))
                {
                    Fail(where + " recorded [" + string.Join(",", theirs) + "], replayed [" +
                         string.Join(",", mine) + "]");
                }
            }

            /// <summary>Consumes the step, then diffs the run's whole state against it.</summary>
            private void Check(string kind, int floor, RunState run)
            {
                JObject step = Step(kind);
                _at++;
                Diff("floor " + floor + " (" + kind + ") ", (JObject)step["state"], run);
            }

            public void CountReroll() { _rerollsDone++; }

            private void Diff(string where, JObject s, RunState run)
            {
                string diff =
                    Int(where, s, "floor", run.Floor) ??
                    Int(where, s, "php", run.Php) ??
                    Int(where, s, "pmax", run.Pmax) ??
                    Int(where, s, "gold", run.Gold) ??
                    Int(where, s, "atkB", run.Hero.AtkBonus) ??
                    Int(where, s, "defB", run.Hero.DefBonus) ??
                    Int(where, s, "spdB", run.Hero.SpdBonus) ??
                    Int(where, s, "luckB", run.Hero.LuckBonus) ??
                    Int(where, s, "adrenaline", run.Hero.Adrenaline) ??
                    Int(where, s, "midasBonus", run.Hero.MidasBonus) ??
                    Int(where, s, "rerolls", run.Rerolls) ??
                    Int(where, s, "kills", run.Hero.Kills) ??
                    Int(where, s, "strikeTot", run.Hero.StrikeTotal) ??
                    Int(where, s, "anvilB", run.Hero.AnvilBonus) ??
                    Int(where, s, "chaliceG", run.Hero.ChaliceGain) ??
                    Int(where, s, "debtLeft", run.Hero.DebtLeft) ??
                    Int(where, s, "duelF", run.OathCarried) ??
                    Bool(where, s, "revived", run.Revived) ??
                    Items(where, s, run) ??
                    Awake(where, s, run) ??
                    Pending(where, s, run);

                if (diff != null) Fail(diff);
            }

            private static string Int(string where, JObject s, string key, int got)
            {
                int want = s[key].Value<int>();
                return want == got ? null : where + key + ": recorded " + want + ", replayed " + got;
            }

            private static string Bool(string where, JObject s, string key, bool got)
            {
                bool want = s[key].Value<bool>();
                return want == got ? null : where + key + ": recorded " + want + ", replayed " + got;
            }

            private static string Items(string where, JObject s, RunState run)
            {
                var want = new List<string>();
                foreach (JToken t in (JArray)s["items"]) want.Add(t.Value<string>());

                var got = new List<string>(run.Items.Count);
                for (int i = 0; i < run.Items.Count; i++) got.Add(RelicCatalog.KeyOf(run.Items[i]));

                return string.Join(",", want) == string.Join(",", got)
                    ? null
                    : where + "items: recorded [" + string.Join(",", want) + "], replayed [" +
                      string.Join(",", got) + "]";
            }

            /// <summary>
            /// Awakenings are compared as SLOTS, because that is what the bazaar sells: the copy
            /// in hand. Comparing the relics they resolve to would let a run wake the wrong copy
            /// of a pair and still pass.
            /// </summary>
            private static string Awake(string where, JObject s, RunState run)
            {
                var want = new List<int>();
                foreach (JToken t in (JArray)s["awake"]) want.Add(t.Value<int>());
                want.Sort();

                var got = new List<int>(run.AwakenedSlots);
                got.Sort();

                return string.Join(",", want) == string.Join(",", got)
                    ? null
                    : where + "awakened slots: recorded [" + string.Join(",", want) +
                      "], replayed [" + string.Join(",", got) + "]";
            }

            private static string Pending(string where, JObject s, RunState run)
            {
                JToken p = s["pending"];
                bool wantSome = p != null && p.Type != JTokenType.Null;

                if (wantSome != run.Pending.HasValue)
                {
                    return where + "pending gold: recorded " + (wantSome ? "some" : "none") +
                           ", replayed " + (run.Pending.HasValue ? "some" : "none");
                }

                if (!wantSome) return null;

                PendingGold got = run.Pending.Value;
                int floor = p["floor"].Value<int>(), gold = p["gold"].Value<int>();
                return floor == got.Floor && gold == got.Gold
                    ? null
                    : where + "pending gold: recorded " + gold + " on floor " + floor +
                      ", replayed " + got.Gold + " on floor " + got.Floor;
            }
        }

        /// <summary>
        /// Counts rerolls as they are paid for.
        /// </summary>
        /// <remarks>
        /// The run asks whether to reroll again without saying that the last one happened, so a
        /// replay reading "three rerolls" off a recording has nowhere else to keep the count.
        /// </remarks>
        private sealed class Driver : IRunChoices
        {
            private readonly Replay _inner;

            public Driver(Replay inner) { _inner = inner; }

            int IRunChoices.Event(RunState run, DungeonEvent ev)
            {
                return ((IRunChoices)_inner).Event(run, ev);
            }

            bool IRunChoices.Reroll(RunState run, IReadOnlyList<RelicId> offer, int price)
            {
                bool again = ((IRunChoices)_inner).Reroll(run, offer, price);
                if (again) _inner.CountReroll();
                return again;
            }

            RelicId IRunChoices.Draft(RunState run, IReadOnlyList<RelicId> offer)
            {
                return ((IRunChoices)_inner).Draft(run, offer);
            }

            BazaarDeal IRunChoices.Bazaar(RunState run, IReadOnlyList<RelicId> offer,
                IReadOnlyList<int> awakenable)
            {
                return ((IRunChoices)_inner).Bazaar(run, offer, awakenable);
            }

            bool IRunChoices.Revive(RunState run, int floor)
            {
                return ((IRunChoices)_inner).Revive(run, floor);
            }

            bool IRunChoices.CashOut(RunState run, int floor)
            {
                return ((IRunChoices)_inner).CashOut(run, floor);
            }
        }

        [Test]
        public void RecordedRunsReplayExactly()
        {
            JArray cases = Corpus.Array("delve.json");
            Assert.That(cases.Count, Is.GreaterThan(0), "delve corpus is empty");

            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                string id = token["id"].Value<string>();
                uint seed = token["seed"].Value<uint>();
                var replay = new Replay(id, (JArray)token["steps"], (JObject)token["end"]);

                try
                {
                    // The recording is the SOURCE's run, so it is replayed under the source's
                    // rules. Where the port deliberately differs — a Debt of Flesh stacks here,
                    // and the bazaar rolls the pack for the floor beyond it — the difference is
                    // RunRules and nothing else.
                    RunSetup setup = RunSetup.ForLevel(token["level"].Value<int>());
                    setup.Dungeon = DungeonConfig.ForTier(token["tier"].Value<int>());

                    DelveRun.Resolve(seed, setup, new Driver(replay), replay, RunRules.AsRecorded());
                }
                catch (ReplayEnded)
                {
                    // The first divergence is already recorded; nothing after it is meaningful.
                }

                if (replay.Failure != null) failures.Add(replay.Failure);
                else if (!replay.Exhausted) failures.Add(id + ": the run ended early");
            }

            Assert.That(failures, Is.Empty,
                failures.Count + " of " + cases.Count + " runs diverge:\n  " +
                string.Join("\n  ", failures.GetRange(0, System.Math.Min(8, failures.Count))));
        }

        /// <summary>Where the events land is a rule, and it is settled before a floor is walked.</summary>
        [Test]
        public void RecordedEventsLandWhereTheyWerePlaced()
        {
            JArray cases = Corpus.Array("delve.json");
            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                uint seed = token["seed"].Value<uint>();
                Dictionary<int, int> placed =
                    DelveRun.PlaceEvents(new Mulberry32(seed ^ DelveRun.EventSeedMix));

                var want = new List<string>();
                foreach (JProperty p in ((JObject)token["eventFloors"]).Properties())
                {
                    want.Add(p.Name + "=" + p.Value.Value<int>());
                }

                var got = new List<string>();
                foreach (KeyValuePair<int, int> kv in placed) got.Add(kv.Key + "=" + kv.Value);

                want.Sort(System.StringComparer.Ordinal);
                got.Sort(System.StringComparer.Ordinal);

                if (string.Join(",", want) != string.Join(",", got))
                {
                    failures.Add(token["id"] + ": recorded [" + string.Join(",", want) +
                                 "], replayed [" + string.Join(",", got) + "]");
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n  ", failures));
        }
    }
}
