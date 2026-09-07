using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RelicRun.Core.Combat;
using RelicRun.Core.Content;
using RelicRun.Core.Run;
using RelicRun.Tests.Support;

namespace RelicRun.Tests
{
    /// <summary>
    /// Phase 5 gate: <c>Tools/corpus/runs.json</c>.
    /// </summary>
    /// <remarks>
    /// Each recorded run is replayed from its seed alone. The recording supplies only the
    /// DECISIONS a player would make — which event choice, how many rerolls, which relic, what
    /// the bazaar did — because choosing is not a rule. Everything else has to come out of the
    /// port: where the events land, what each choice does, the breath heal, the reroll ladder,
    /// the bazaar's prices, what a pickup does, and which relics an offer holds.
    ///
    /// An offer is compared as a SET. Which relics the draw produced is a rule; the order they
    /// were ranked in for a player is the recorder's, and is not ported.
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
            private readonly string _id;
            private int _at;
            private int _rerollsDone;

            public string Failure;

            public Replay(string id, JArray steps)
            {
                _id = id;
                _steps = steps;
            }

            public bool Exhausted { get { return _at >= _steps.Count; } }

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
                JObject step = Step("pick");
                return _rerollsDone < step["rerolls"].Value<int>();
            }

            RelicId IRunChoices.Draft(RunState run, IReadOnlyList<RelicId> offer)
            {
                JObject step = Step("pick");
                RelicId pick;
                RelicCatalog.TryParse(step["pick"].Value<string>(), out pick);
                return pick;
            }

            BazaarDeal IRunChoices.Bazaar(RunState run, IReadOnlyList<RelicId> offer)
            {
                JObject step = Step("shop");
                string deal = step["deal"].Value<string>();
                if (deal == "none") return BazaarDeal.Walk;

                RelicId id;
                RelicCatalog.TryParse(step["dealId"].Value<string>(), out id);
                return deal == "awaken" ? BazaarDeal.Awaken(id) : BazaarDeal.Buy(id);
            }

            // ---- and the checks ----

            void IRunObserver.Event(RunState run, int floor, int index, int choice)
            {
                Check("event", floor, run);
            }

            void IRunObserver.Bazaar(RunState run, int floor, IReadOnlyList<RelicId> offer, BazaarDeal deal)
            {
                CheckOffer(Step("shop"), offer, floor);
                Check("shop", floor, run);
            }

            void IRunObserver.Draft(RunState run, int floor, IReadOnlyList<RelicId> offer,
                int rerolls, RelicId pick)
            {
                JObject step = Step("pick");
                CheckOffer(step, offer, floor);

                int want = step["rerolls"].Value<int>();
                if (rerolls != want)
                {
                    Fail("floor " + floor + ": recorded " + want + " rerolls, replayed " + rerolls);
                }

                _rerollsDone = 0;
                Check("pick", floor, run);
            }

            void IRunObserver.Fight(RunState run, int floor, IReadOnlyList<EnemyState> pack,
                CombatResult result)
            {
                JObject step = Step("fight");

                int foes = step["foes"].Value<int>();
                if (pack.Count != foes)
                {
                    Fail("floor " + floor + ": recorded " + foes + " foes, replayed " + pack.Count);
                }

                int drop = 0, hp = 0;
                for (int i = 0; i < pack.Count; i++)
                {
                    drop += pack[i].Drop;

                    // Hp, not MaxHp: an awakened Vampire Tooth eats the foe's CEILING as the
                    // fight runs, and this is read afterwards. Starting health is what was
                    // recorded, and nothing writes to it.
                    hp += pack[i].Hp;
                }

                if (drop != step["drop"].Value<int>())
                {
                    Fail("floor " + floor + ": pack loot recorded " + step["drop"] + ", replayed " + drop);
                }

                if (hp != step["hp"].Value<int>())
                {
                    Fail("floor " + floor + ": pack health recorded " + step["hp"] +
                         ", replayed " + hp);
                }

                int events = step["events"].Value<int>();
                if (result.Events.Count != events)
                {
                    Fail("floor " + floor + ": recorded " + events + " fight events, replayed " +
                         result.Events.Count);
                }

                Check("fight", floor, run);
            }

            /// <summary>
            /// The recorded runs never revive: the source's own harness has no such notion, and
            /// a revive is bought with sparks or an advertisement. RunReviveTests covers it.
            /// </summary>
            bool IRunChoices.Revive(RunState run, int floor) { return false; }

            void IRunObserver.Revived(RunState run, int floor)
            {
                Fail("floor " + floor + ": revived, but the recording never does");
            }

            void IRunObserver.End(RunState run, bool dead, int floor) { }

            /// <summary>A reroll happened; count it so the next question answers correctly.</summary>
            public void CountReroll() { _rerollsDone++; }

            private void CheckOffer(JObject step, IReadOnlyList<RelicId> offer, int floor)
            {
                var got = new List<string>(offer.Count);
                for (int i = 0; i < offer.Count; i++) got.Add(RelicCatalog.KeyOf(offer[i]));
                got.Sort(System.StringComparer.Ordinal);

                var want = new List<string>();
                foreach (JToken t in (JArray)step["offer"]) want.Add(t.Value<string>());

                if (string.Join(",", want) != string.Join(",", got))
                {
                    Fail("floor " + floor + ": offer recorded [" + string.Join(",", want) +
                         "], replayed [" + string.Join(",", got) + "]");
                }
            }

            /// <summary>Diffs the run's whole state against what was recorded after this step.</summary>
            private void Check(string kind, int floor, RunState run)
            {
                JObject step = Step(kind);
                _at++;

                string where = "floor " + floor + " (" + kind + ") ";
                var s = (JObject)step["state"];

                string diff =
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

            private static string Awake(string where, JObject s, RunState run)
            {
                var want = new List<string>();
                foreach (JProperty p in ((JObject)s["awake"]).Properties())
                {
                    want.Add(p.Name + ":" + p.Value.Value<int>());
                }

                var got = new List<string>();
                foreach (KeyValuePair<RelicId, int> kv in run.Awakened)
                {
                    if (kv.Value > 0) got.Add(RelicCatalog.KeyOf(kv.Key) + ":" + kv.Value);
                }

                want.Sort(System.StringComparer.Ordinal);
                got.Sort(System.StringComparer.Ordinal);

                return string.Join(",", want) == string.Join(",", got)
                    ? null
                    : where + "awakenings: recorded [" + string.Join(",", want) + "], replayed [" +
                      string.Join(",", got) + "]";
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

        /// <summary>Counts rerolls as they are paid for, so the replay can stop at the right one.</summary>
        private sealed class RerollCounter : IRunChoices
        {
            private readonly Replay _inner;

            public RerollCounter(Replay inner) { _inner = inner; }

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

            BazaarDeal IRunChoices.Bazaar(RunState run, IReadOnlyList<RelicId> offer)
            {
                return ((IRunChoices)_inner).Bazaar(run, offer);
            }

            bool IRunChoices.Revive(RunState run, int floor)
            {
                return ((IRunChoices)_inner).Revive(run, floor);
            }
        }

        private static RunSetup SetupFrom(JObject p)
        {
            var setup = new RunSetup();
            if (p["baseHp"] != null) { setup.Hp = p["baseHp"].Value<int>(); }
            if (p["baseGold"] != null) setup.Gold = p["baseGold"].Value<int>();
            if (p["baseAtk"] != null) setup.Atk = p["baseAtk"].Value<int>();
            if (p["baseDef"] != null) setup.Def = p["baseDef"].Value<int>();
            if (p["baseSpd"] != null) setup.Spd = p["baseSpd"].Value<int>();
            if (p["baseLck"] != null) setup.Lck = p["baseLck"].Value<int>();
            if (p["breath"] != null) setup.Breath = p["breath"].Value<int>();
            // balanceRuns offers three where the shipped delve offers two, so an absent value
            // means the Lab's default rather than the game's.
            setup.DraftChoices = p["draftChoices"] != null ? p["draftChoices"].Value<int>() : 3;

            if (p["startKit"] != null)
            {
                var kit = new List<RelicId>();
                foreach (JToken t in (JArray)p["startKit"])
                {
                    RelicId id;
                    if (RelicCatalog.TryParse(t.Value<string>(), out id)) kit.Add(id);
                }

                setup.StartKit = kit;
            }

            return setup;
        }

        [Test]
        public void RecordedRunsReplayExactly()
        {
            JArray cases = Corpus.Array("runs.json");
            Assert.That(cases.Count, Is.GreaterThan(0), "run corpus is empty");

            var failures = new List<string>();

            foreach (JToken token in cases)
            {
                string id = token["id"].Value<string>();
                uint seed = token["seed"].Value<uint>();
                var replay = new Replay(id, (JArray)token["steps"]);

                try
                {
                    // The recording is the SOURCE's run, so it is replayed under the source's
                    // rules. Where the port deliberately differs — a Debt of Flesh stacks here
                    // and does not there — the difference is RunRules and nothing else.
                    DelveRun.Resolve(seed, SetupFrom((JObject)token["params"]),
                        new RerollCounter(replay), replay, RunRules.AsRecorded());
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
    }
}
